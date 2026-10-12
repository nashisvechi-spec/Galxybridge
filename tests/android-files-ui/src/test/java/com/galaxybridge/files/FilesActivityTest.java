package com.galaxybridge.files;

import android.content.Intent;
import android.content.ComponentName;
import android.database.Cursor;
import android.net.Uri;
import android.provider.OpenableColumns;
import android.os.*;
import android.view.*;
import android.widget.*;
import android.webkit.MimeTypeMap;
import org.junit.*;
import org.junit.runner.RunWith;
import org.robolectric.*;
import org.robolectric.android.controller.ActivityController;
import org.robolectric.annotation.*;
import org.robolectric.shadows.*;
import java.io.*;
import java.lang.reflect.*;
import java.nio.file.Files;
import java.util.*;
import java.util.concurrent.*;
import java.time.Duration;
import static org.junit.Assert.*;
import static org.robolectric.Shadows.shadowOf;

/** Exercise the real ListView/touch dispatch path, not just its callback. */
@RunWith(RobolectricTestRunner.class)
@Config(manifest=Config.NONE,sdk=35,shadows={FilesActivityTest.StorageAccess.class,FilesActivityTest.MouseCoordinates.class})
@LooperMode(LooperMode.Mode.PAUSED)
public class FilesActivityTest {
    // Robolectric has no physical storage volume; mock only the permission check.
    @Implements(Environment.class)
    public static class StorageAccess extends ShadowEnvironment {
        @Implementation(minSdk=30) protected static boolean isExternalStorageManager() { return true; }
    }
    // Robolectric 4.14 omits the native cursor getters used by ViewGroup.
    // A single-pointer mouse dispatches at its pointer coordinates on Android.
    @Implements(MotionEvent.class)
    public static class MouseCoordinates extends ShadowMotionEvent {
        @Implementation protected static float nativeGetXCursorPosition(long pointer) { return nativeGetAxisValue(pointer,MotionEvent.AXIS_X,0,Integer.MIN_VALUE); }
        @Implementation protected static float nativeGetYCursorPosition(long pointer) { return nativeGetAxisValue(pointer,MotionEvent.AXIS_Y,0,Integer.MIN_VALUE); }
    }
    private ActivityController<FilesActivity> controller;
    private FilesActivity activity;
    private ListView list;
    private File root,directory,document;
    @Before public void setup() throws Exception {
        shadowOf(RuntimeEnvironment.getApplication()).declareComponentUnbindable(new ComponentName("com.galaxybridge.lan","com.galaxybridge.lan.FilesBridgeService"));
        shadowOf(MimeTypeMap.getSingleton()).addExtensionMimeTypeMapping("pdf","application/pdf");
        root=Environment.getExternalStorageDirectory().getCanonicalFile();root.mkdirs();
        // Keep aliases in the fixtures so comparisons must normalize paths.
        // Windows runners also supply 8.3 aliases such as RUNNER~1 in java.io.tmpdir.
        directory=new File(root,"."+File.separator+"Documents");directory.mkdir();
        document=new File(root,"."+File.separator+"example.pdf");Files.write(document.toPath(),new byte[]{1,2,3});
        controller=Robolectric.buildActivity(FilesActivity.class);controller.get().setTheme(android.R.style.Theme_Material_Light_NoActionBar);controller.setup().visible();activity=controller.get();
        list=(ListView)field("list");settle();tapView(activity.getWindow().getDecorView().findViewWithTag("storage"));
    }
    @After public void close() { dismissDialog();if(controller!=null) controller.pause().stop().destroy();shadowOf(Looper.getMainLooper()).idle(); }
    private void dismissDialog() { android.app.AlertDialog dialog=ShadowAlertDialog.getLatestAlertDialog();if(dialog!=null && dialog.isShowing()) dialog.dismiss(); }
    private Object field(String name) throws Exception { Field f=FilesActivity.class.getDeclaredField(name);f.setAccessible(true);return f.get(activity); }
    private void settle() throws Exception {
        ((ExecutorService)field("worker")).submit(() -> {}).get(5,TimeUnit.SECONDS);
        shadowOf(Looper.getMainLooper()).idle();
        View decor=activity.getWindow().getDecorView();decor.measure(View.MeasureSpec.makeMeasureSpec(1080,View.MeasureSpec.EXACTLY),View.MeasureSpec.makeMeasureSpec(1920,View.MeasureSpec.EXACTLY));decor.layout(0,0,1080,1920);
        shadowOf(Looper.getMainLooper()).idle();
    }
    private View row(File file) throws IOException {
        File canonical=file.getCanonicalFile();
        for(int i=0;i<list.getAdapter().getCount();i++) if(canonical.equals(list.getAdapter().getItem(i))) return list.getChildAt(i-list.getFirstVisiblePosition());
        throw new AssertionError("Missing row: "+canonical+"; adapter items="+list.getAdapter().getCount());
    }
    private void tap(File file) throws Exception {
        tapView(row(file));
    }
    private void tapView(View row) throws Exception {
        if(row instanceof CheckBox && row.getVisibility()!=View.VISIBLE) {
            activity.getWindow().getDecorView().findViewWithTag("select").performClick();settle();
            File current=(File)((View)row.getParent()).getTag();row=((LinearLayout)row(current)).getChildAt(0);
        }
        assertNotNull(row);int[] position=new int[2];row.getLocationOnScreen(position);
        View decor=activity.getWindow().getDecorView();int[] origin=new int[2];decor.getLocationOnScreen(origin);
        float x=position[0]+row.getWidth()*0.75f-origin[0],y=position[1]+row.getHeight()/2f-origin[1];
        long down=SystemClock.uptimeMillis();
        MotionEvent press=MotionEvent.obtain(down,down,MotionEvent.ACTION_DOWN,x,y,0);press.setSource(InputDevice.SOURCE_TOUCHSCREEN);activity.dispatchTouchEvent(press);press.recycle();
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(60));
        MotionEvent release=MotionEvent.obtain(down,SystemClock.uptimeMillis(),MotionEvent.ACTION_UP,x,y,0);release.setSource(InputDevice.SOURCE_TOUCHSCREEN);activity.dispatchTouchEvent(release);release.recycle();
        shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(200));settle();
    }
    @Test public void fingerTapOpensFolderThroughListView() throws Exception {
        tap(directory);assertEquals(directory.getCanonicalFile(),field("folder"));
    }
    @Test public void fingerTapOpensFileWithReadGrant() throws Exception {
        tap(document);Intent opened=shadowOf(activity).getNextStartedActivity();assertNotNull("File tap must launch viewer",opened);
        assertEquals(Intent.ACTION_VIEW,opened.getAction());assertEquals("application/pdf",opened.getType());
        assertEquals("com.galaxybridge.files.shared",opened.getData().getAuthority());
        assertTrue((opened.getFlags()&Intent.FLAG_GRANT_READ_URI_PERMISSION)!=0);assertNotNull(opened.getClipData());
    }
    @Test public void checkboxSelectsWithoutOpening() throws Exception {
        CheckBox check=(CheckBox)((LinearLayout)row(document)).getChildAt(0);tapView(check);
        assertTrue(((Set<?>)field("selected")).contains(document.getCanonicalFile()));assertNull(shadowOf(activity).getNextStartedActivity());
        assertEquals(root.getCanonicalFile(),field("folder"));
    }
    @Test public void homeCategoriesAndStorageAreRealNavigationTargets() throws Exception {
        activity.onBackPressed();settle();assertTrue((Boolean)field("home"));
        View documents=activity.getWindow().getDecorView().findViewWithTag("category:DOCUMENT");tapView(documents);
        assertEquals(FileKind.DOCUMENT,field("category"));assertFalse((Boolean)field("home"));assertNotNull(row(document));
    }
    @Test public void gridTapOpensFileAndReturnsToHome() throws Exception {
        activity.getWindow().getDecorView().findViewWithTag("view").performClick();
        android.app.AlertDialog dialog=ShadowAlertDialog.getLatestAlertDialog();dialog.getListView().performItemClick(dialog.getListView().getChildAt(1),1,1);settle();
        GridView tiles=(GridView)field("grid");assertEquals(View.VISIBLE,tiles.getVisibility());
        View cell=null;for(int i=0;i<tiles.getAdapter().getCount();i++) if(document.getCanonicalFile().equals(tiles.getAdapter().getItem(i))) cell=tiles.getChildAt(i-tiles.getFirstVisiblePosition());
        tapView(cell);assertEquals(Intent.ACTION_VIEW,shadowOf(activity).getNextStartedActivity().getAction());
    }
    @Test public void selectedItemsDoNotBlockFolderNavigation() throws Exception {
        tapView(((LinearLayout)row(document)).getChildAt(0));
        tap(directory);assertEquals(directory.getCanonicalFile(),field("folder"));
    }
    @Test public void rowHasNoFocusableDescendantBlockingListClick() throws Exception {
        assertFalse("Checkbox must not block the ListView click",row(directory).hasExplicitFocusable());
    }
    @Test public void sharedProviderReadsNestedUnicodeAndSpaceNames() throws Exception {
        File file=new File(directory,"пример с пробелом.pdf");byte[] data={7,8,9};Files.write(file.toPath(),data);
        SharedFilesProvider provider=Robolectric.buildContentProvider(SharedFilesProvider.class).create().get();Uri uri=SharedFilesProvider.uri(file);
        try(Cursor cursor=provider.query(uri,null,null,null,null)) {
            assertNotNull(cursor);assertTrue(cursor.moveToFirst());assertEquals(file.getName(),cursor.getString(cursor.getColumnIndexOrThrow(OpenableColumns.DISPLAY_NAME)));
            assertEquals(data.length,cursor.getLong(cursor.getColumnIndexOrThrow(OpenableColumns.SIZE)));
        }
        try(InputStream input=new ParcelFileDescriptor.AutoCloseInputStream(provider.openFile(uri,"r"))) { assertEquals(7,input.read());assertEquals(8,input.read());assertEquals(9,input.read());assertEquals(-1,input.read()); }
    }
    @Test public void mouseDoubleClickOpensAfterSelectionRebuildsRows() throws Exception {
        mouseClick(document);assertTrue(((Set<?>)field("selected")).contains(document.getCanonicalFile()));assertNull(shadowOf(activity).getNextStartedActivity());
        mouseClick(document);assertEquals(Intent.ACTION_VIEW,shadowOf(activity).getNextStartedActivity().getAction());
    }
    @Test public void selectedItemsDoNotBlockFileOpening() throws Exception {
        tapView(((LinearLayout)row(directory)).getChildAt(0));tap(document);
        assertEquals(Intent.ACTION_VIEW,shadowOf(activity).getNextStartedActivity().getAction());
    }
    @Test public void accessibilityClickOpensFolder() throws Exception {
        assertTrue(row(directory).performAccessibilityAction(android.view.accessibility.AccessibilityNodeInfo.ACTION_CLICK,null));settle();
        assertEquals(directory.getCanonicalFile(),field("folder"));
    }
    @Test public void wifiMouseDoubleClickOpensFolder() throws Exception {
        remoteClick(directory);assertTrue(((Set<?>)field("selected")).contains(directory.getCanonicalFile()));
        remoteClick(directory);assertEquals(directory.getCanonicalFile(),field("folder"));
    }
    private void remoteClick(File file) throws Exception {
        View target=row(file);int[] position=new int[2];target.getLocationOnScreen(position);
        Method method=FilesActivity.class.getDeclaredMethod("remote",Bundle.class);method.setAccessible(true);
        for(int next:new int[]{1,0}) {
            Bundle message=new Bundle();message.putFloat("x",position[0]+target.getWidth()*0.75f);message.putFloat("y",position[1]+target.getHeight()/2f);
            message.putInt("buttons",next);message.putInt("previous",1-next);method.invoke(activity,message);
            shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(20));
        }
        settle();
    }
    private void mouseClick(File file) throws Exception {
        View target=row(file);int[] position=new int[2];target.getLocationOnScreen(position);int[] origin=new int[2];activity.getWindow().getDecorView().getLocationOnScreen(origin);
        float x=position[0]+target.getWidth()*0.75f-origin[0],y=position[1]+target.getHeight()/2f-origin[1];long down=SystemClock.uptimeMillis();
        for(int action:new int[]{MotionEvent.ACTION_DOWN,MotionEvent.ACTION_UP}) {
            MotionEvent.PointerProperties property=new MotionEvent.PointerProperties();property.id=0;property.toolType=MotionEvent.TOOL_TYPE_MOUSE;
            MotionEvent.PointerCoords point=new MotionEvent.PointerCoords();point.x=x;point.y=y;point.pressure=1;point.size=1;
            MotionEvent event=MotionEvent.obtain(down,SystemClock.uptimeMillis(),action,1,new MotionEvent.PointerProperties[]{property},new MotionEvent.PointerCoords[]{point},0,
                action==MotionEvent.ACTION_DOWN?MotionEvent.BUTTON_PRIMARY:0,1,1,0,0,InputDevice.SOURCE_MOUSE,0);
            activity.dispatchTouchEvent(event);event.recycle();
            shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(20));
        }
        settle();
    }
}
