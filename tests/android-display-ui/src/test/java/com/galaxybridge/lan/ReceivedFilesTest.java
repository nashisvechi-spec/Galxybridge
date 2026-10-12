package com.galaxybridge.lan;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.database.*;
import android.net.Uri;
import android.os.*;
import android.provider.*;
import android.util.Base64;
import org.json.JSONObject;
import org.junit.*;
import org.junit.runner.RunWith;
import org.robolectric.*;
import org.robolectric.annotation.*;
import org.robolectric.shadows.ShadowContentResolver;
import java.io.*;
import java.security.MessageDigest;
import static org.junit.Assert.*;
import static org.robolectric.Shadows.shadowOf;

@RunWith(RobolectricTestRunner.class)
@Config(manifest=Config.NONE,sdk=35)
@LooperMode(LooperMode.Mode.PAUSED)
public class ReceivedFilesTest {
    private Context context;
    private NotificationManager manager;
    private DownloadProvider provider;
    private Downloads downloads;
    private final String transfer="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private final byte[] bytes={1,2,3,4};
    @Before public void setup() throws Exception {
        context=RuntimeEnvironment.getApplication();
        shadowOf(RuntimeEnvironment.getApplication()).grantPermissions(Manifest.permission.POST_NOTIFICATIONS);
        manager=context.getSystemService(NotificationManager.class);
        provider=new DownloadProvider();android.content.pm.ProviderInfo info=new android.content.pm.ProviderInfo();info.authority="media";provider.attachInfo(context,info);
        ShadowContentResolver.registerProviderInternal("media",provider);
        downloads=new Downloads(context);
    }
    @After public void close() { downloads.close();if(provider.file!=null) provider.file.delete(); }
    private JSONObject command(String type) throws Exception { return Wire.message(type,"transfer",transfer); }
    private void begin() throws Exception { downloads.accept(command("fileBegin").put("name","report.pdf").put("size",bytes.length)); }
    private void chunk() throws Exception { downloads.accept(command("fileChunk").put("seq",0).put("data",Base64.encodeToString(bytes,Base64.NO_WRAP))); }
    private String end() throws Exception { return downloads.accept(command("fileEnd").put("sha256",Wire.hex(MessageDigest.getInstance("SHA-256").digest(bytes)))); }
    private Notification notification() { return shadowOf(manager).getNotification(ReceivedFiles.NOTIFICATION); }
    @Test public void repeatedReceiptsAlertAgain() {
        ReceivedFiles.notifySaved(context,"first.pdf");ReceivedFiles.notifySaved(context,"second.pdf");
        assertTrue(notification().extras.getString(Notification.EXTRA_TEXT).contains("second.pdf"));
        assertEquals(0,notification().flags&Notification.FLAG_ONLY_ALERT_ONCE);
    }
    @Test public void blockedReceiptChannelStillCompletesVerifiedFile() throws Exception {
        manager.createNotificationChannel(new NotificationChannel(ReceivedFiles.CHANNEL,"Полученные файлы",NotificationManager.IMPORTANCE_NONE));
        begin();chunk();assertEquals("report.pdf",end());assertTrue(provider.published);assertNull(notification());
        assertTrue(ReceivedFiles.status(context).contains("категория"));
        assertEquals(NotificationManager.IMPORTANCE_NONE,manager.getNotificationChannel(ReceivedFiles.CHANNEL).getImportance());
    }
    @Test public void existingDefaultChannelRemainsUserControlledAndIsReported() {
        manager.createNotificationChannel(new NotificationChannel(ReceivedFiles.CHANNEL,"Полученные файлы",NotificationManager.IMPORTANCE_DEFAULT));
        ReceivedFiles.ensureChannel(context);
        assertEquals(NotificationManager.IMPORTANCE_DEFAULT,manager.getNotificationChannel(ReceivedFiles.CHANNEL).getImportance());
        assertTrue(ReceivedFiles.status(context).contains("без всплывающего окна"));
    }
    @Test public void verifiedPublishedFileNotifiesWithProviderNameAndFolderTap() throws Exception {
        begin();assertNull(notification());chunk();assertNull(notification());
        provider.name="report (1).pdf";assertEquals("report (1).pdf",end());
        Notification notice=notification();assertNotNull(notice);assertTrue(provider.published);
        assertEquals("Файл получен с ПК",notice.extras.getString(Notification.EXTRA_TITLE));
        assertTrue(notice.extras.getString(Notification.EXTRA_TEXT).contains("report (1).pdf"));
        assertEquals(ReceivedFiles.CHANNEL,notice.getChannelId());
        assertEquals(NotificationManager.IMPORTANCE_HIGH,manager.getNotificationChannel(ReceivedFiles.CHANNEL).getImportance());
        assertTrue((notice.flags&Notification.FLAG_AUTO_CANCEL)!=0);assertTrue(notice.contentIntent.isImmutable());
        Intent tap=shadowOf(notice.contentIntent).getSavedIntent();
        assertEquals(ReceivedFolderActivity.class.getName(),tap.getComponent().getClassName());
        notice.contentIntent.send();assertEquals(ReceivedFolderActivity.class.getName(),shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity().getComponent().getClassName());
        downloads.close();assertFalse(provider.deleted);
    }
    @Test public void abortedFileIsDeletedWithoutSuccessNotification() throws Exception {
        begin();chunk();downloads.accept(command("fileAbort"));assertNull(notification());assertFalse(provider.published);assertTrue(provider.deleted);
    }
    @Test public void checksumFailureDoesNotNotifyOrPublish() throws Exception {
        begin();chunk();
        try { downloads.accept(command("fileEnd").put("sha256",new String(new char[64]).replace('\0','0')));fail("Checksum must fail"); }
        catch(IOException expected) { }
        downloads.close();assertNull(notification());assertFalse(provider.published);assertTrue(provider.deleted);
    }
    @Test public void truncatedFileDoesNotNotifyOrPublish() throws Exception {
        begin();try { end();fail("Incomplete file must fail"); }catch(IOException expected) { }
        downloads.close();assertNull(notification());assertFalse(provider.published);assertTrue(provider.deleted);
    }
    @Test public void publishFailureDoesNotNotify() throws Exception {
        begin();chunk();provider.failPublish=true;
        try { end();fail("Publish must fail"); }catch(IOException expected) { }
        downloads.close();assertNull(notification());assertTrue(provider.deleted);
    }
    @Test public void deniedNotificationPermissionStillCompletesReceipt() throws Exception {
        shadowOf(RuntimeEnvironment.getApplication()).denyPermissions(Manifest.permission.POST_NOTIFICATIONS);
        begin();chunk();assertEquals("report.pdf",end());downloads.close();assertTrue(provider.published);assertFalse(provider.deleted);assertNull(notification());
    }
    @Test public void disabledNotificationsStillCompleteReceipt() throws Exception {
        shadowOf(manager).setNotificationsEnabled(false);
        begin();chunk();assertEquals("report.pdf",end());assertTrue(provider.published);assertNull(notification());
    }
    @Test public void notificationServiceFailureCannotUndoSavedFile() throws Exception {
        downloads.close();downloads=new Downloads(new ContextWrapper(context) {
            @Override public Object getSystemService(String name) {
                if(Context.NOTIFICATION_SERVICE.equals(name)) throw new IllegalStateException("Notification service unavailable");
                return super.getSystemService(name);
            }
        });
        begin();chunk();assertEquals("report.pdf",end());downloads.close();assertTrue(provider.published);assertFalse(provider.deleted);
    }
    @Test public void missingNameQueryUsesOriginalFilename() throws Exception {
        begin();chunk();provider.emptyQuery=true;assertEquals("report.pdf",end());assertTrue(notification().extras.getString(Notification.EXTRA_TEXT).contains("report.pdf"));
    }
    @Test public void latestReceiptReplacesFileNoticeAndKeepsConnectionNotice() throws Exception {
        Notification ongoing=new Notification.Builder(context,"lan").setSmallIcon(android.R.drawable.ic_menu_share).build();
        manager.notify(61,ongoing);begin();chunk();end();
        begin();chunk();provider.name="second.pdf";end();
        assertEquals(2,shadowOf(manager).size());assertSame(ongoing,shadowOf(manager).getNotification(61));
        assertTrue(notification().extras.getString(Notification.EXTRA_TEXT).contains("second.pdf"));
    }
    @Test public void notificationActivityOpensExactDirectoryAndFinishes() {
        try(org.robolectric.android.controller.ActivityController<ReceivedFolderActivity> activity=Robolectric.buildActivity(ReceivedFolderActivity.class).create()) {
            Intent opened=shadowOf(activity.get()).getNextStartedActivity();assertNotNull(opened);
            assertEquals(Intent.ACTION_VIEW,opened.getAction());assertEquals(DocumentsContract.Document.MIME_TYPE_DIR,opened.getType());
            assertEquals("primary:Download/GalaxyBridge",DocumentsContract.getDocumentId(opened.getData()));
            assertTrue(activity.get().isFinishing());
        }
    }
    private void handler(Intent intent) {
        android.content.pm.ResolveInfo info=new android.content.pm.ResolveInfo();info.activityInfo=new android.content.pm.ActivityInfo();
        info.activityInfo.name="example.Files";info.activityInfo.packageName="example";info.activityInfo.exported=true;
        shadowOf(context.getPackageManager()).addResolveInfoForIntent(intent,info);
    }
    @Test public void missingDirectoryHandlerFallsBackToChooserAtReceivedFolder() {
        shadowOf(RuntimeEnvironment.getApplication()).checkActivities(true);
        Intent picker=new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("*/*").addCategory(Intent.CATEGORY_OPENABLE);handler(picker);
        try(org.robolectric.android.controller.ActivityController<ReceivedFolderActivity> activity=Robolectric.buildActivity(ReceivedFolderActivity.class).create()) {
            org.robolectric.shadows.ShadowActivity.IntentForResult opened=shadowOf(activity.get()).getNextStartedActivityForResult();
            assertEquals(Intent.ACTION_OPEN_DOCUMENT,opened.intent.getAction());assertEquals(1,opened.requestCode);
            assertEquals(ReceivedFiles.folder(),opened.intent.getParcelableExtra(DocumentsContract.EXTRA_INITIAL_URI,Uri.class));
            assertFalse(activity.get().isFinishing());
            Uri file=Uri.parse("content://media/external/downloads/17");handler(new Intent(Intent.ACTION_VIEW).setDataAndType(file,"application/pdf"));
            shadowOf(activity.get()).callOnActivityResult(1,Activity.RESULT_OK,new Intent().setData(file));
            Intent view=shadowOf(activity.get()).getNextStartedActivity();assertEquals(file,view.getData());
            assertEquals(Intent.ACTION_VIEW,view.getAction());assertTrue((view.getFlags()&Intent.FLAG_GRANT_READ_URI_PERMISSION)!=0);
            assertTrue(activity.get().isFinishing());
        }
    }
    @Test public void missingDirectoryAndPickerHandlersOpenDownloads() {
        shadowOf(RuntimeEnvironment.getApplication()).checkActivities(true);handler(new Intent(DownloadManager.ACTION_VIEW_DOWNLOADS));
        try(org.robolectric.android.controller.ActivityController<ReceivedFolderActivity> activity=Robolectric.buildActivity(ReceivedFolderActivity.class).create()) {
            assertEquals(DownloadManager.ACTION_VIEW_DOWNLOADS,shadowOf(activity.get()).getNextStartedActivity().getAction());
            assertTrue(activity.get().isFinishing());
        }
    }
    @Test public void absentFileManagersShowSavedPathWithoutCrashing() {
        shadowOf(RuntimeEnvironment.getApplication()).checkActivities(true);
        try(org.robolectric.android.controller.ActivityController<ReceivedFolderActivity> activity=Robolectric.buildActivity(ReceivedFolderActivity.class).create()) {
            assertTrue(activity.get().isFinishing());assertTrue(org.robolectric.shadows.ShadowToast.getTextOfLatestToast().contains("Download/GalaxyBridge"));
        }
    }
    public static final class DownloadProvider extends ContentProvider {
        File file;String name="report.pdf";boolean published,deleted,failPublish,emptyQuery;
        @Override public boolean onCreate() { return true; }
        @Override public Uri insert(Uri uri,ContentValues values) {
            assertEquals("Download/GalaxyBridge/",values.getAsString(MediaStore.Downloads.RELATIVE_PATH));
            assertEquals(Integer.valueOf(1),values.getAsInteger(MediaStore.Downloads.IS_PENDING));
            try { file=File.createTempFile("received",".bin",getContext().getCacheDir()); }
            catch(IOException e) { throw new IllegalStateException(e); }
            published=false;deleted=false;return Uri.parse("content://media/external/downloads/17");
        }
        @Override public ParcelFileDescriptor openFile(Uri uri,String mode) throws FileNotFoundException { return ParcelFileDescriptor.open(file,ParcelFileDescriptor.MODE_READ_WRITE|ParcelFileDescriptor.MODE_TRUNCATE); }
        @Override public Cursor query(Uri uri,String[] projection,String selection,String[] args,String order) {
            MatrixCursor cursor=new MatrixCursor(new String[]{MediaStore.Downloads.DISPLAY_NAME});if(!emptyQuery) cursor.addRow(new Object[]{name});return cursor;
        }
        @Override public int update(Uri uri,ContentValues values,String selection,String[] args) {
            if(failPublish) return 0;assertEquals(Integer.valueOf(0),values.getAsInteger(MediaStore.Downloads.IS_PENDING));published=true;return 1;
        }
        @Override public int delete(Uri uri,String selection,String[] args) { deleted=true;file.delete();return 1; }
        @Override public String getType(Uri uri) { return "application/pdf"; }
    }
}
