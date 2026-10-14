package com.galaxybridge.lan;

import android.content.*;
import android.app.*;
import android.net.Uri;
import java.io.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;
import org.json.*;
import org.junit.*;
import org.junit.runner.RunWith;
import org.robolectric.*;
import org.robolectric.annotation.*;
import org.robolectric.shadows.ShadowContentResolver;
import static org.junit.Assert.*;
import static org.robolectric.Shadows.shadowOf;

@RunWith(RobolectricTestRunner.class)
@Config(manifest=Config.NONE,sdk=35)
@LooperMode(LooperMode.Mode.PAUSED)
public class NewFeaturesTest {
    private final String id="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private org.robolectric.android.controller.ServiceController<ConnectionService> connection;
    private Object screenOwner;
    private Object pairedLink() throws Exception {
        connection=Robolectric.buildService(ConnectionService.class).create();
        Class<?> type=Class.forName("com.galaxybridge.lan.ConnectionService$Link");
        java.lang.reflect.Constructor<?> constructor=type.getDeclaredConstructors()[0];constructor.setAccessible(true);
        screenOwner=constructor.newInstance(new java.net.Socket(),new DataOutputStream(new ByteArrayOutputStream()),true,true,true,true);
        java.lang.reflect.Field screen=type.getDeclaredField("screen");screen.setAccessible(true);screen.setBoolean(screenOwner,true);
        java.lang.reflect.Field link=ConnectionService.class.getDeclaredField("link");link.setAccessible(true);link.set(connection.get(),screenOwner);
        shadowOf(RuntimeEnvironment.getApplication()).grantPermissions(android.Manifest.permission.POST_NOTIFICATIONS);
        return screenOwner;
    }
    @After public void stopConnection() {
        if(screenOwner!=null) ScreenShareService.lost(RuntimeEnvironment.getApplication(),screenOwner);
        if(connection!=null) connection.destroy();
    }
    @Test public void senderResumesAtReceiverOffsetAndHashesOriginalBytes() throws Exception {
        byte[] data={1,2,3,4,5,6};AtomicBoolean cancel=new AtomicBoolean();
        ResumableUpload.Source source=() -> new ByteArrayInputStream(data);
        ResumableUpload.Identity identity=ResumableUpload.identify(source,cancel);
        ByteArrayOutputStream sent=new ByteArrayOutputStream();List<String> commands=new ArrayList<>();
        String saved=ResumableUpload.send(source,"file.bin",identity,id,(message,timeout,c) -> {
            String type=message.getString("type");commands.add(type);
            if(type.equals("uploadBegin")) return new JSONObject().put("offset",2).put("done",false);
            if(type.equals("uploadChunk")) { sent.write(java.util.Base64.getDecoder().decode(message.getString("data")));return new JSONObject(); }
            assertEquals(identity.hash,message.getString("sha256"));return new JSONObject().put("name","file.bin");
        },cancel,(a,b) -> {});
        assertEquals("file.bin",saved);assertArrayEquals(new byte[]{3,4,5,6},sent.toByteArray());assertEquals(3,commands.size());
    }
    @Test public void completedReceiptAvoidsResendingAfterLostFinalAck() throws Exception {
        byte[] data={1,2};AtomicBoolean cancel=new AtomicBoolean();ResumableUpload.Identity identity=ResumableUpload.identify(() -> new ByteArrayInputStream(data),cancel);
        int[] calls={0};String saved=ResumableUpload.send(() -> { throw new IOException("Must not open source again"); },"file",identity,id,(m,t,c) -> { calls[0]++;return new JSONObject().put("offset",2).put("done",true).put("name","saved"); },cancel,(a,b) -> {});
        assertEquals("saved",saved);assertEquals(1,calls[0]);
    }
    @Test public void invalidOffsetDoesNotSendAnyChunks() throws Exception {
        AtomicBoolean cancel=new AtomicBoolean();int[] chunks={0};
        try { ResumableUpload.send(() -> new ByteArrayInputStream(new byte[2]),"file",new ResumableUpload.Identity(2,new String(new char[64]).replace('\0','a')),id,(m,t,c) -> { if(m.getString("type").equals("uploadChunk")) chunks[0]++;return new JSONObject().put("offset",3); },cancel,(a,b) -> {});fail(); }
        catch(IOException expected) { }assertEquals(0,chunks[0]);
    }
    @Test public void partialDownloadSurvivesDisconnectAndPublishesOnce() throws Exception {
        Context c=RuntimeEnvironment.getApplication();shadowOf(RuntimeEnvironment.getApplication()).grantPermissions(android.Manifest.permission.POST_NOTIFICATIONS);
        ReceivedFilesTest.DownloadProvider provider=new ReceivedFilesTest.DownloadProvider();android.content.pm.ProviderInfo info=new android.content.pm.ProviderInfo();info.authority="media";provider.attachInfo(c,info);provider.name="resumed (1).pdf";ShadowContentResolver.registerProviderInternal("media",provider);
        byte[] bytes={1,2,3,4};String hash=Wire.hex(java.security.MessageDigest.getInstance("SHA-256").digest(bytes));
        JSONObject begin=Wire.message("fileBegin","transfer",id,"name","resumed.pdf","size",4,"sha256",hash);
        try {
            try(ResumableDownloads first=new ResumableDownloads(c)) { first.accept(begin);first.accept(Wire.message("fileChunk","transfer",id,"seq",0,"data",java.util.Base64.getEncoder().encodeToString(new byte[]{1,2}))); }
            try(ResumableDownloads second=new ResumableDownloads(c)) {
                second.accept(begin);assertEquals(2,second.offset);assertNull(provider.file);
                File cache=new File(c.getCacheDir(),"resume-paired");
                assertTrue(new File(cache,id+".json").isFile());assertFalse(new File(cache,id+".json.tmp").exists());
                second.accept(Wire.message("fileChunk","transfer",id,"seq",0,"data",java.util.Base64.getEncoder().encodeToString(new byte[]{3,4})));
                assertEquals("resumed (1).pdf",second.accept(Wire.message("fileEnd","transfer",id,"sha256",hash)));assertTrue(provider.published);
            }
            File saved=provider.file;try(ResumableDownloads third=new ResumableDownloads(c)) { assertEquals("resumed (1).pdf",third.accept(begin));assertTrue(third.done);assertSame(saved,provider.file); }
            assertFalse(new File(c.getCacheDir(),"resume-paired/"+id+".json.tmp").exists());
        } finally { if(provider.file!=null) provider.file.delete(); }
    }
    @Test public void historyPersistsErrorsAndClearingDoesNotLaunchActivities() throws Exception {
        Context c=RuntimeEnvironment.getApplication();TransferHistory.clear(c);
        for(int i=0;i<105;i++) TransferHistory.add(c,"Телефон → ПК","file"+i,"Ошибка","content://files/"+i,"offline");
        JSONArray entries=TransferHistory.read(c);assertEquals(100,entries.length());assertEquals("file104",entries.getJSONObject(0).getString("name"));
        TransferHistory.clear(c);assertEquals(0,TransferHistory.read(c).length());assertNull(shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity());
    }
    @Test public void diagnosticsReportsConnectionAndPermissionsWithoutChangingPairing() {
        Context c=RuntimeEnvironment.getApplication();c.getSharedPreferences("lan",Context.MODE_PRIVATE).edit().putString("token","private-token").commit();
        String report=Diagnostics.report(c);assertTrue(report.contains("Связь:"));assertTrue(report.contains("Специальные возможности:"));assertFalse(report.contains("private-token"));
        assertEquals("private-token",c.getSharedPreferences("lan",Context.MODE_PRIVATE).getString("token",""));
    }
    @Test public void openingCaptureWithoutPcRequestDoesNotLaunchSystemConsentOrService() {
        try(org.robolectric.android.controller.ActivityController<ScreenShareActivity> activity=Robolectric.buildActivity(ScreenShareActivity.class).setup()) {
            assertNull(shadowOf(activity.get()).getNextStartedActivityForResult());assertNull(shadowOf(RuntimeEnvironment.getApplication()).getNextStartedService());
        }
        assertFalse(ScreenShareService.allowed(id));
    }
    @Test public void forgedOrExpiredScreenRequestCannotStartProjection() {
        assertFalse(ScreenShareService.allowed(null));assertFalse(ScreenShareService.allowed(id));
        ScreenShareService.request(RuntimeEnvironment.getApplication(),new Object(),id);assertFalse(ScreenShareService.allowed(id));
        assertNull(shadowOf(RuntimeEnvironment.getApplication().getSystemService(NotificationManager.class)).getNotification(112));
    }
    @Test public void pcScreenRequestOnlyShowsConsentNotificationUntilUserActs() throws Exception {
        Context c=RuntimeEnvironment.getApplication();Object owner=pairedLink();ScreenShareService.request(c,owner,id);
        assertTrue(ScreenShareService.allowed(id));assertNotNull(shadowOf(c.getSystemService(NotificationManager.class)).getNotification(112));
        assertNull(shadowOf(RuntimeEnvironment.getApplication()).getNextStartedService());
        ScreenShareService.stop(c,new Object(),id);assertTrue(ScreenShareService.allowed(id));
    }
    @Test public void decliningSystemConsentNeverStartsProjectionService() throws Exception {
        Context c=RuntimeEnvironment.getApplication();ScreenShareService.request(c,pairedLink(),id);
        try(org.robolectric.android.controller.ActivityController<ScreenShareActivity> activity=Robolectric.buildActivity(ScreenShareActivity.class).setup()) {
            assertNotNull(shadowOf(activity.get()).getNextStartedActivityForResult());
            activity.get().onActivityResult(41,Activity.RESULT_CANCELED,null);
            assertFalse(ScreenShareService.allowed(id));assertNull(shadowOf(RuntimeEnvironment.getApplication()).getNextStartedService());
        }
    }
    @Test public void lateConsentAfterWindowsClosedRequestCannotStartCapture() throws Exception {
        Context c=RuntimeEnvironment.getApplication();Object owner=pairedLink();ScreenShareService.request(c,owner,id);
        try(org.robolectric.android.controller.ActivityController<ScreenShareActivity> activity=Robolectric.buildActivity(ScreenShareActivity.class).setup()) {
            ScreenShareService.stop(c,owner,id);activity.get().onActivityResult(41,Activity.RESULT_OK,new Intent("grant"));
            assertNull(shadowOf(RuntimeEnvironment.getApplication()).getNextStartedService());assertFalse(ScreenShareService.allowed(id));
        }
    }
    @Test public void acceptedConsentStartsPrivateForegroundServiceExactlyOnce() throws Exception {
        Context c=RuntimeEnvironment.getApplication();ScreenShareService.request(c,pairedLink(),id);
        try(org.robolectric.android.controller.ActivityController<ScreenShareActivity> activity=Robolectric.buildActivity(ScreenShareActivity.class).setup()) {
            activity.get().onActivityResult(41,Activity.RESULT_OK,new Intent("grant"));
            Intent started=shadowOf(RuntimeEnvironment.getApplication()).getNextStartedService();assertNotNull(started);
            assertEquals(ScreenShareService.class.getName(),started.getComponent().getClassName());assertEquals(id,started.getStringExtra("request"));
            activity.get().onActivityResult(41,Activity.RESULT_OK,new Intent("grant"));assertNull(shadowOf(RuntimeEnvironment.getApplication()).getNextStartedService());
        }
    }
    @Test public void screenEditingReplacesSelectionAndDeletesWholeUnicodeCharacters() {
        ScreenEditing.Result replaced=ScreenEditing.apply("Привет мир",7,10,"S25","");assertEquals("Привет S25",replaced.text);assertEquals(10,replaced.start);
        ScreenEditing.Result deleted=ScreenEditing.apply("A🌍Б",3,3,null,"backspace");assertEquals("AБ",deleted.text);assertEquals(1,deleted.start);
        ScreenEditing.Result forward=ScreenEditing.apply("A🌍Б",1,1,null,"delete");assertEquals("AБ",forward.text);
    }
    @Test public void screenEditingArrowsCollapseSelectionAndUnsupportedKeysDoNothing() {
        ScreenEditing.Result left=ScreenEditing.apply("abcd",1,3,null,"left");assertEquals(1,left.start);assertEquals(1,left.end);
        ScreenEditing.Result right=ScreenEditing.apply("abcd",1,3,null,"right");assertEquals(3,right.start);assertEquals(3,right.end);
        ScreenEditing.Result selected=ScreenEditing.apply("abcd",2,2,null,"selectAll");assertEquals(0,selected.start);assertEquals(4,selected.end);
        assertNull(ScreenEditing.apply("abcd",2,2,null,"unsupported"));
    }
}
