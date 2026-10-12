package com.galaxybridge.lan;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.provider.Settings;
import org.junit.*;
import org.junit.runner.RunWith;
import org.robolectric.*;
import org.robolectric.annotation.*;
import org.robolectric.android.controller.ActivityController;
import static org.junit.Assert.*;
import static org.robolectric.Shadows.shadowOf;

@RunWith(RobolectricTestRunner.class)
@Config(manifest=Config.NONE,sdk=35)
@LooperMode(LooperMode.Mode.PAUSED)
public class NotificationSettingsTest {
    private Context context;
    @Before public void setup() {
        context=RuntimeEnvironment.getApplication();
        context.getSharedPreferences("notifications",Context.MODE_PRIVATE).edit().clear().commit();
        shadowOf(RuntimeEnvironment.getApplication()).denyPermissions(Manifest.permission.POST_NOTIFICATIONS);
    }
    @Test public void firstVisibleSetupRequestsPermissionButReopeningDoesNot() {
        try(ActivityController<SetupActivity> first=Robolectric.buildActivity(SetupActivity.class).setup()) {
            assertNotNull(shadowOf(first.get()).getLastRequestedPermission());
            assertEquals(ReceivedFiles.PERMISSION_REQUEST,shadowOf(first.get()).getLastRequestedPermission().requestCode);
            assertArrayEquals(new String[]{Manifest.permission.POST_NOTIFICATIONS},shadowOf(first.get()).getLastRequestedPermission().requestedPermissions);
            assertTrue(context.getSharedPreferences("notifications",Context.MODE_PRIVATE).getBoolean("permissionAsked",false));
            assertNotNull(shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity());
            assertNull(shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity());
        }
        try(ActivityController<SetupActivity> second=Robolectric.buildActivity(SetupActivity.class).setup()) {
            assertNull(shadowOf(second.get()).getLastRequestedPermission());
            assertNull(shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity());
        }
    }
    @Test public void previouslyDeniedPermissionButtonOpensApplicationNotificationSettings() {
        context.getSharedPreferences("notifications",Context.MODE_PRIVATE).edit().putBoolean("permissionAsked",true).commit();
        try(ActivityController<Activity> controller=Robolectric.buildActivity(Activity.class).setup()) {
            ReceivedFiles.enable(controller.get(),false);
            Intent settings=shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity();
            assertEquals(Settings.ACTION_APP_NOTIFICATION_SETTINGS,settings.getAction());
            assertEquals(context.getPackageName(),settings.getStringExtra(Settings.EXTRA_APP_PACKAGE));
            assertNull(shadowOf(controller.get()).getLastRequestedPermission());
        }
    }
    @Test public void grantedPermissionButtonOpensExactFileCategoryWithoutChangingIt() {
        shadowOf(RuntimeEnvironment.getApplication()).grantPermissions(Manifest.permission.POST_NOTIFICATIONS);
        NotificationManager manager=context.getSystemService(NotificationManager.class);
        manager.createNotificationChannel(new NotificationChannel(ReceivedFiles.CHANNEL,"Полученные файлы",NotificationManager.IMPORTANCE_NONE));
        try(ActivityController<Activity> controller=Robolectric.buildActivity(Activity.class).setup()) {
            ReceivedFiles.enable(controller.get(),false);
            Intent settings=shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity();
            assertEquals(Settings.ACTION_CHANNEL_NOTIFICATION_SETTINGS,settings.getAction());
            assertEquals(ReceivedFiles.CHANNEL,settings.getStringExtra(Settings.EXTRA_CHANNEL_ID));
            assertEquals(NotificationManager.IMPORTANCE_NONE,manager.getNotificationChannel(ReceivedFiles.CHANNEL).getImportance());
        }
    }
    @Test public void testNotificationDoesNotReplaceActualFileReceiptOrConnectionNotice() {
        shadowOf(RuntimeEnvironment.getApplication()).grantPermissions(Manifest.permission.POST_NOTIFICATIONS);
        NotificationManager manager=context.getSystemService(NotificationManager.class);
        ReceivedFiles.notifySaved(context,"real.pdf");
        manager.createNotificationChannel(new NotificationChannel("lan","Connection",NotificationManager.IMPORTANCE_LOW));
        manager.notify(61,new Notification.Builder(context,"lan").setSmallIcon(android.R.drawable.ic_menu_share).build());
        try(ActivityController<Activity> controller=Robolectric.buildActivity(Activity.class).setup()) {
            ReceivedFiles.test(controller.get());
            assertEquals("Проверка уведомлений",shadowOf(manager).getNotification(63).extras.getString(Notification.EXTRA_TITLE));
            assertTrue(shadowOf(manager).getNotification(ReceivedFiles.NOTIFICATION).extras.getString(Notification.EXTRA_TEXT).contains("real.pdf"));
            assertNotNull(shadowOf(manager).getNotification(61));assertEquals(3,shadowOf(manager).size());
        }
    }
}
