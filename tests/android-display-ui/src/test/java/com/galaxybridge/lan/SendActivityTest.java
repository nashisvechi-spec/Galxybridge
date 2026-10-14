package com.galaxybridge.lan;

import android.content.*;
import android.net.Uri;
import android.os.*;
import org.junit.*;
import org.junit.runner.RunWith;
import org.robolectric.*;
import org.robolectric.android.controller.*;
import org.robolectric.annotation.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.time.Duration;
import static org.junit.Assert.*;
import static org.robolectric.Shadows.shadowOf;

@RunWith(RobolectricTestRunner.class)
@Config(manifest=Config.NONE,sdk=35)
@LooperMode(LooperMode.Mode.PAUSED)
public class SendActivityTest {
    private ActivityController<ShareActivity> activity;
    private ServiceController<ConnectionService> service;
    private final Uri first=Uri.parse("content://example.files/first.pdf"),second=Uri.parse("content://example.files/second.jpg");
    @Before public void setup() {
        RuntimeEnvironment.getApplication().getSharedPreferences("lan",Context.MODE_PRIVATE).edit()
            .putString("token","aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa").commit();
        ConnectionService.fileStatus="Ready";
    }
    @After public void close() {
        if(activity!=null) activity.pause().stop().destroy();
        if(service!=null) service.destroy();
        shadowOf(Looper.getMainLooper()).idle();
    }
    private Intent shared(Uri uri) { return new Intent(Intent.ACTION_SEND).setType("application/pdf").putExtra(Intent.EXTRA_STREAM,uri).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION); }
    private void open(Intent intent,Bundle saved) {
        activity=Robolectric.buildActivity(ShareActivity.class,intent);
        if(saved==null) activity.create();else activity.create(saved);
        activity.start().resume().visible();shadowOf(Looper.getMainLooper()).idle();
    }
    private Intent nextService() { return shadowOf(RuntimeEnvironment.getApplication()).getNextStartedService(); }
    private ArrayList<Uri> files(Intent intent) { return intent.getParcelableArrayListExtra("files",Uri.class); }
    private Object field(ConnectionService owner,String name) throws Exception {
        Field field=ConnectionService.class.getDeclaredField(name);field.setAccessible(true);return field.get(owner);
    }
    private ConnectionService busyService() throws Exception {
        service=Robolectric.buildService(ConnectionService.class).create();ConnectionService owner=service.get();
        ((AtomicBoolean)field(owner,"uploadBusy")).set(true);return owner;
    }
    @SuppressWarnings("unchecked") private ArrayDeque<ArrayList<Uri>> queue(ConnectionService owner) throws Exception { return (ArrayDeque<ArrayList<Uri>>)field(owner,"uploadQueue"); }
    private void enqueue(ConnectionService owner,ArrayList<Uri> files) throws Exception {
        Method method=ConnectionService.class.getDeclaredMethod("enqueueUpload",ArrayList.class);method.setAccessible(true);method.invoke(owner,files);
    }
    @Test public void singleFileShareAutomaticallyStartsForegroundUploadWithReadGrant() {
        open(shared(first),null);Intent request=nextService();assertNotNull(request);
        assertEquals(ConnectionService.class.getName(),request.getComponent().getClassName());assertEquals(Arrays.asList(first),files(request));
        assertTrue((request.getFlags()&Intent.FLAG_GRANT_READ_URI_PERMISSION)!=0);
        assertEquals(first,request.getClipData().getItemAt(0).getUri());
        assertTrue(activity.get().isFinishing());
        assertNull(shadowOf(activity.get()).getNextStartedActivity());
        assertEquals(0,((android.view.ViewGroup)activity.get().findViewById(android.R.id.content)).getChildCount());
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofSeconds(1));assertNull(nextService());
    }
    @Test public void multipleShareDeduplicatesUrisAndForwardsEveryGrant() {
        Intent intent=new Intent(Intent.ACTION_SEND_MULTIPLE).setType("*/*");
        intent.putParcelableArrayListExtra(Intent.EXTRA_STREAM,new ArrayList<>(Arrays.asList(first,second)));
        ClipData clip=ClipData.newRawUri("files",first);clip.addItem(new ClipData.Item(second));intent.setClipData(clip);
        open(intent,null);Intent request=nextService();assertNotNull(request);assertEquals(Arrays.asList(first,second),files(request));
        assertEquals(2,request.getClipData().getItemCount());assertEquals(second,request.getClipData().getItemAt(1).getUri());
    }
    @Test public void clipOnlyFileShareIsAutomatic() {
        Intent intent=new Intent(Intent.ACTION_SEND).setType("*/*");intent.setClipData(ClipData.newRawUri("file",first));open(intent,null);
        assertEquals(Arrays.asList(first),files(nextService()));
    }
    @Test public void rotationDoesNotReplayAnAlreadySubmittedShare() {
        Intent intent=shared(first);open(intent,null);assertNotNull(nextService());
        Bundle saved=new Bundle();activity.saveInstanceState(saved);activity.pause().stop().destroy();activity=null;
        open(intent,saved);assertNull(nextService());
    }
    @Test public void recreationBeforeFirstResumePreservesOnePendingShare() {
        Intent intent=shared(first);activity=Robolectric.buildActivity(ShareActivity.class,intent).create().start();
        Bundle saved=new Bundle();activity.saveInstanceState(saved);activity.stop().destroy();activity=null;
        open(intent,saved);assertNotNull(nextService());shadowOf(Looper.getMainLooper()).idleFor(Duration.ofSeconds(1));assertNull(nextService());
    }
    @Test public void returningToShareWindowDoesNotSendAgain() {
        open(shared(first),null);assertNotNull(nextService());activity.pause().resume();shadowOf(Looper.getMainLooper()).idle();assertNull(nextService());
    }
    @Test public void internalFileSelectionRequiresExplicitSend() {
        Intent intent=new Intent();intent.setClipData(ClipData.newRawUri("file",first));
        try(ActivityController<SendActivity> manual=Robolectric.buildActivity(SendActivity.class,intent).setup()) {
            shadowOf(Looper.getMainLooper()).idle();assertNull(nextService());assertFalse(manual.get().isFinishing());
        }
    }
    @Test public void invalidFileShareFinishesWithoutOpeningAnAppWindow() {
        open(shared(Uri.parse("https://example.com/file")),null);
        assertNull(nextService());assertTrue(activity.get().isFinishing());
        assertNull(shadowOf(activity.get()).getNextStartedActivity());
    }
    @Test public void mixedTextAndFileShareOnlyStartsBackgroundFileUpload() {
        open(shared(first).putExtra(Intent.EXTRA_TEXT,"caption"),null);
        assertNotNull(nextService());assertTrue(activity.get().isFinishing());
        assertNull(shadowOf(activity.get()).getNextStartedActivity());
    }
    @Test public void missingPairingRejectsAutomaticFileUpload() {
        RuntimeEnvironment.getApplication().getSharedPreferences("lan",Context.MODE_PRIVATE).edit().clear().commit();
        open(shared(first),null);assertNull(nextService());assertTrue(ConnectionService.fileStatus.contains("QR"));
        assertTrue(activity.get().isFinishing());assertNull(shadowOf(activity.get()).getNextStartedActivity());
    }
    @Test public void textSharingKeepsTheClipboardWindow() {
        open(new Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT,"example text"),null);
        assertNull(nextService());Intent opened=shadowOf(activity.get()).getNextStartedActivity();assertNotNull(opened);
        assertEquals(ClipboardActivity.class.getName(),opened.getComponent().getClassName());
    }
    @Test public void activeTransferDoesNotBlockSubmissionOfAnotherShare() throws Exception {
        busyService();open(shared(second),null);assertEquals(Arrays.asList(second),files(nextService()));
    }
    @Test public void serviceQueueCopiesFilesPreservesOrderAndHasALimit() throws Exception {
        ConnectionService owner=busyService();ArrayList<Uri> input=new ArrayList<>(Arrays.asList(first));enqueue(owner,input);input.clear();
        enqueue(owner,new ArrayList<>(Arrays.asList(second)));assertEquals(Arrays.asList(first),queue(owner).peekFirst());
        assertEquals(Arrays.asList(second),queue(owner).peekLast());
        for(int i=0;i<6;i++) enqueue(owner,new ArrayList<>(Arrays.asList(first)));
        enqueue(owner,new ArrayList<>(Arrays.asList(second)));assertEquals(8,queue(owner).size());
        assertTrue(ConnectionService.fileStatus.contains("заполнена"));
    }
    @Test public void cancelClearsWaitingSharesAndStopsTheCurrentUpload() throws Exception {
        ConnectionService owner=busyService();enqueue(owner,new ArrayList<>(Arrays.asList(first)));
        ConnectionService.cancelFiles();assertTrue(queue(owner).isEmpty());assertTrue(((AtomicBoolean)field(owner,"uploadCancel")).get());
    }
    @Test public void queueRejectsNonContentUris() throws Exception {
        ConnectionService owner=busyService();enqueue(owner,new ArrayList<>(Arrays.asList(Uri.parse("https://example.com/file"))));
        assertTrue(queue(owner).isEmpty());
    }
    @Test public void selectionOverLimitRejectsWholeShareInsteadOfSendingOnlyFirstHundred() {
        ArrayList<Uri> selection=new ArrayList<>();
        for(int i=0;i<101;i++) selection.add(Uri.parse("content://example.files/"+i));
        open(new Intent(Intent.ACTION_SEND_MULTIPLE).putParcelableArrayListExtra(Intent.EXTRA_STREAM,selection),null);
        assertNull(nextService());assertTrue(ConnectionService.fileStatus.contains("Ничего не отправлено"));
        assertTrue(activity.get().isFinishing());assertNull(shadowOf(activity.get()).getNextStartedActivity());
    }
    @Test public void exactlyOneHundredFilesStillSendTogether() {
        ArrayList<Uri> selection=new ArrayList<>();
        for(int i=0;i<100;i++) selection.add(Uri.parse("content://example.files/"+i));
        open(new Intent(Intent.ACTION_SEND_MULTIPLE).putParcelableArrayListExtra(Intent.EXTRA_STREAM,selection),null);
        assertEquals(selection,files(nextService()));
    }
    @Test public void duplicateClipItemsDoNotHideALaterUniqueFile() {
        ClipData clip=ClipData.newRawUri("files",first);
        for(int i=1;i<100;i++) clip.addItem(new ClipData.Item(first));
        clip.addItem(new ClipData.Item(second));
        Intent intent=new Intent(Intent.ACTION_SEND_MULTIPLE);intent.setClipData(clip);open(intent,null);
        assertEquals(Arrays.asList(first,second),files(nextService()));
    }
}
