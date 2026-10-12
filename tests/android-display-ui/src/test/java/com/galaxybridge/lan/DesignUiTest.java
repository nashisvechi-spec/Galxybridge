package com.galaxybridge.lan;

import android.app.Activity;
import android.content.*;
import android.graphics.*;
import android.os.Bundle;
import android.view.*;
import android.widget.*;
import java.io.*;
import java.util.*;
import org.junit.*;
import org.junit.runner.RunWith;
import org.robolectric.*;
import org.robolectric.annotation.*;
import org.robolectric.android.controller.ActivityController;
import static org.junit.Assert.*;
import static org.robolectric.Shadows.shadowOf;

@RunWith(RobolectricTestRunner.class)
@Config(manifest=Config.NONE,sdk=35,qualifiers="w420dp-h900dp-mdpi")
@LooperMode(LooperMode.Mode.PAUSED)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
public class DesignUiTest {
    @Before public void setup() {
        // These tests isolate navigation/layout after the onboarding permission.
        shadowOf(RuntimeEnvironment.getApplication()).grantPermissions(android.Manifest.permission.POST_NOTIFICATIONS);
        RuntimeEnvironment.getApplication().getSharedPreferences("appearance",Context.MODE_PRIVATE).edit().clear().commit();
        RuntimeEnvironment.getApplication().getSharedPreferences("lan",Context.MODE_PRIVATE).edit().clear().commit();
        ControlService.instance=null;
    }
    private static List<View> views(View view) {
        List<View> all=new ArrayList<>();all.add(view);
        if(view instanceof ViewGroup) for(int i=0;i<((ViewGroup)view).getChildCount();i++) all.addAll(views(((ViewGroup)view).getChildAt(i)));
        return all;
    }
    private static View root(Activity activity) { return ((ViewGroup)activity.findViewById(android.R.id.content)).getChildAt(0); }
    private static Button button(Activity activity,String text) {
        for(View view:views(root(activity))) if(view instanceof Button && text.contentEquals(((Button)view).getText())) return (Button)view;
        throw new AssertionError("Missing action: "+text);
    }
    private static void layout(Activity activity,int width,int height) {
        View root=root(activity);root.measure(View.MeasureSpec.makeMeasureSpec(width,View.MeasureSpec.EXACTLY),View.MeasureSpec.makeMeasureSpec(height,View.MeasureSpec.EXACTLY));root.layout(0,0,width,height);
    }
    private static void preview(Activity activity,String name) throws Exception {
        String directory=System.getProperty("galaxybridge.preview.dir");if(directory==null) return;
        layout(activity,420,900);View view=root(activity);
        // Load the real production icon for the resource-free Robolectric fixture.
        File icon=new File("../../android/lan/res/mipmap-mdpi/ic_launcher.png");
        for(View child:views(view)) if(child instanceof ImageView && ((ImageView)child).getDrawable()==null && icon.isFile())
            ((ImageView)child).setImageBitmap(BitmapFactory.decodeFile(icon.getAbsolutePath()));
        Bitmap image=Bitmap.createBitmap(420,900,Bitmap.Config.ARGB_8888);view.draw(new Canvas(image));
        File output=new File(directory,name+".png");output.getParentFile().mkdirs();
        try(FileOutputStream stream=new FileOutputStream(output)) { assertTrue(image.compress(Bitmap.CompressFormat.PNG,100,stream)); }
        image.recycle();
    }
    @Test public void bottomNavigationRestoresSelectedTabAfterRecreation() {
        Bundle saved=new Bundle();
        try(ActivityController<SetupActivity> controller=Robolectric.buildActivity(SetupActivity.class).setup()) {
            SetupActivity activity=controller.get();button(activity,"Файлы").performClick();assertNotNull(button(activity,"Выбрать файлы"));
            controller.saveInstanceState(saved);
        }
        try(ActivityController<SetupActivity> controller=Robolectric.buildActivity(SetupActivity.class).create(saved).start().resume().visible()) {
            assertTrue(button(controller.get(),"Файлы").isSelected());assertNotNull(button(controller.get(),"Открыть папку"));
            button(controller.get(),"Связь").performClick();assertNotNull(button(controller.get(),"Сканировать QR"));
        }
    }
    @Test public void navigationAndAppearanceDoNotStartConnectionsOrUploads() {
        try(ActivityController<SetupActivity> controller=Robolectric.buildActivity(SetupActivity.class).setup()) {
            SetupActivity activity=controller.get();button(activity,"Настройки").performClick();button(activity,"Файлы").performClick();
            assertNull(shadowOf(RuntimeEnvironment.getApplication()).getNextStartedService());assertNull(shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity());
        }
    }
    @Test public void fileAndTextActionsOpenTheirRealActivities() {
        try(ActivityController<SetupActivity> controller=Robolectric.buildActivity(SetupActivity.class).setup()) {
            SetupActivity activity=controller.get();button(activity,"Отправить файлы на ПК").performClick();
            assertEquals(SendActivity.class.getName(),shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity().getComponent().getClassName());
            button(activity,"Текст и ссылки").performClick();assertEquals(ClipboardActivity.class.getName(),shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity().getComponent().getClassName());
            button(activity,"Файлы").performClick();button(activity,"Открыть папку").performClick();
            assertEquals(ReceivedFolderActivity.class.getName(),shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity().getComponent().getClassName());
        }
    }
    @Test public void themeChoicePersistsWithoutChangingPairing() {
        Context context=RuntimeEnvironment.getApplication();context.getSharedPreferences("lan",Context.MODE_PRIVATE).edit().putString("token","saved-token").commit();
        try(ActivityController<SetupActivity> controller=Robolectric.buildActivity(SetupActivity.class).setup()) {
            SetupActivity activity=controller.get();button(activity,"Настройки").performClick();Spinner spinner=null;
            for(View view:views(root(activity))) if(view instanceof Spinner) spinner=(Spinner)view;
            assertNotNull(spinner);spinner.getOnItemSelectedListener().onItemSelected(spinner,null,2,2);
            assertEquals("dark",context.getSharedPreferences("appearance",Context.MODE_PRIVATE).getString("theme",""));
            assertEquals("saved-token",context.getSharedPreferences("lan",Context.MODE_PRIVATE).getString("token",""));assertTrue(BridgeUi.palette(activity).dark);
        }
    }
    @Test @Config(qualifiers="w420dp-h900dp-night-mdpi") public void systemThemeFollowsNightModeAndManualLightOverridesIt() {
        Context context=RuntimeEnvironment.getApplication();assertTrue(BridgeUi.palette(context).dark);
        context.getSharedPreferences("appearance",Context.MODE_PRIVATE).edit().putString("theme","light").commit();assertFalse(BridgeUi.palette(context).dark);
    }
    @Test public void largeFontKeepsNavigationOutsideScrollableContent() {
        RuntimeEnvironment.setFontScale(2f);
        try(ActivityController<SetupActivity> controller=Robolectric.buildActivity(SetupActivity.class).setup()) {
            SetupActivity activity=controller.get();layout(activity,420,780);
            LinearLayout main=(LinearLayout)root(activity);View body=main.getChildAt(0),nav=main.getChildAt(1);
            assertTrue(body.getBottom()<=nav.getTop());assertTrue(nav.getBottom()<=780);
            for(String tab:new String[]{"Связь","Файлы","Настройки"}) {
                Button button=button(activity,tab);assertTrue(button.getHeight()>=48);assertTrue(button.getWidth()>0);button.performClick();layout(activity,420,780);
            }
        } finally { RuntimeEnvironment.setFontScale(1f); }
    }
    @Test public void clipboardDraftSurvivesRecreationWithoutAutomaticSending() {
        Bundle saved=new Bundle();Intent intent=new Intent(RuntimeEnvironment.getApplication(),ClipboardActivity.class).putExtra("sharedText","Текст и ссылка https://example.com");
        try(ActivityController<ClipboardActivity> controller=Robolectric.buildActivity(ClipboardActivity.class,intent).setup()) { controller.saveInstanceState(saved); }
        try(ActivityController<ClipboardActivity> controller=Robolectric.buildActivity(ClipboardActivity.class,intent).create(saved).start().resume().visible()) {
            EditText text=null;for(View view:views(root(controller.get()))) if(view instanceof EditText) text=(EditText)view;
            assertNotNull(text);assertEquals("Текст и ссылка https://example.com",text.getText().toString());assertNotNull(button(controller.get(),"Отправить в буфер ПК"));
            assertNull(shadowOf(RuntimeEnvironment.getApplication()).getNextStartedService());
        }
    }
    @Test public void notificationPermissionStatusRefreshesOnSettingsScreen() {
        try(ActivityController<SetupActivity> controller=Robolectric.buildActivity(SetupActivity.class).setup()) {
            SetupActivity activity=controller.get();button(activity,"Настройки").performClick();
            shadowOf(activity.getSystemService(android.app.NotificationManager.class)).setNotificationsEnabled(false);
            shadowOf(android.os.Looper.getMainLooper()).idleFor(java.time.Duration.ofMillis(1100));
            boolean shown=false;for(View view:views(root(activity))) if(view instanceof TextView && "Уведомления: выключены".contentEquals(((TextView)view).getText())) shown=true;
            assertTrue(shown);
        }
    }
    @Test public void keyboardInsetsLeaveClipboardContentScrollableAboveKeyboard() {
        try(ActivityController<ClipboardActivity> controller=Robolectric.buildActivity(ClipboardActivity.class).setup()) {
            Activity activity=controller.get();View root=root(activity);
            WindowInsets insets=new WindowInsets.Builder().setInsets(WindowInsets.Type.systemBars(),Insets.of(0,24,0,16))
                .setInsets(WindowInsets.Type.ime(),Insets.of(0,0,0,300)).build();root.dispatchApplyWindowInsets(insets);layout(activity,420,780);
            assertEquals(24,root.getPaddingTop());assertEquals(300,root.getPaddingBottom());
            ScrollView scroll=(ScrollView)((ViewGroup)root).getChildAt(0);assertTrue(scroll.getHeight()>0);assertTrue(scroll.getBottom()<=480);
            assertNotNull(button(activity,"Отправить в буфер ПК"));
        }
    }
    @Test public void renderMainTabsAndDarkTheme() throws Exception {
        try(ActivityController<SetupActivity> controller=Robolectric.buildActivity(SetupActivity.class).setup()) {
            SetupActivity activity=controller.get();preview(activity,"android-connection-light");button(activity,"Файлы").performClick();preview(activity,"android-files-light");
            button(activity,"Настройки").performClick();preview(activity,"android-settings-light");
        }
        RuntimeEnvironment.getApplication().getSharedPreferences("appearance",Context.MODE_PRIVATE).edit().putString("theme","dark").commit();
        try(ActivityController<SetupActivity> controller=Robolectric.buildActivity(SetupActivity.class).setup()) { preview(controller.get(),"android-connection-dark"); }
    }
}
