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
    @Test public void newReceiptChannelUsesSystemNotificationSoundAndAudioUsage() {
        ReceivedFiles.ensureChannel(context);
        NotificationChannel channel=context.getSystemService(NotificationManager.class).getNotificationChannel(ReceivedFiles.CHANNEL);
        assertEquals(Settings.System.DEFAULT_NOTIFICATION_URI,channel.getSound());
        assertEquals(android.media.AudioAttributes.USAGE_NOTIFICATION,channel.getAudioAttributes().getUsage());
        assertEquals(android.media.AudioAttributes.CONTENT_TYPE_SONIFICATION,channel.getAudioAttributes().getContentType());
    }
    private NotificationManager silentChannel(int importance) {
        shadowOf(RuntimeEnvironment.getApplication()).grantPermissions(Manifest.permission.POST_NOTIFICATIONS);
        NotificationManager manager=context.getSystemService(NotificationManager.class);
        NotificationChannel channel=new NotificationChannel(ReceivedFiles.CHANNEL,"Полученные файлы",importance);
        channel.setSound(null,null);manager.createNotificationChannel(channel);return manager;
    }
    @Test public void updatePreservesExistingSilenceAndReportsIt() {
        NotificationManager manager=silentChannel(NotificationManager.IMPORTANCE_HIGH);
        ReceivedFiles.ensureChannel(context);ReceivedFiles.notifySaved(context,"quiet.pdf");
        assertNull(manager.getNotificationChannel(ReceivedFiles.SOUND_CHANNEL));
        assertNull(manager.getNotificationChannel(ReceivedFiles.CHANNEL).getSound());
        assertTrue(ReceivedFiles.status(context).contains("без звука"));
        assertEquals(ReceivedFiles.CHANNEL,shadowOf(manager).getNotification(ReceivedFiles.NOTIFICATION).getChannelId());
    }
    @Test public void explicitSoundButtonSwitchesSilentLegacyChannelAndTestsNewCategory() {
        NotificationManager manager=silentChannel(NotificationManager.IMPORTANCE_HIGH);
        try(ActivityController<Activity> controller=Robolectric.buildActivity(Activity.class).setup()) {
            ReceivedFiles.enableSound(controller.get());
            assertEquals(Settings.System.DEFAULT_NOTIFICATION_URI,manager.getNotificationChannel(ReceivedFiles.SOUND_CHANNEL).getSound());
            assertEquals(ReceivedFiles.SOUND_CHANNEL,shadowOf(manager).getNotification(63).getChannelId());
            ReceivedFiles.notifySaved(context,"audible.pdf");
            assertEquals(ReceivedFiles.SOUND_CHANNEL,shadowOf(manager).getNotification(ReceivedFiles.NOTIFICATION).getChannelId());
            ReceivedFiles.enable(controller.get(),false);
            assertEquals(ReceivedFiles.SOUND_CHANNEL,shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity().getStringExtra(Settings.EXTRA_CHANNEL_ID));
        }
    }
    @Test public void lowImportanceIsSilentEvenWithSoundUriAndExplicitButtonRepairsIt() {
        shadowOf(RuntimeEnvironment.getApplication()).grantPermissions(Manifest.permission.POST_NOTIFICATIONS);
        NotificationManager manager=context.getSystemService(NotificationManager.class);
        NotificationChannel low=new NotificationChannel(ReceivedFiles.CHANNEL,"Полученные файлы",NotificationManager.IMPORTANCE_LOW);
        low.setSound(Settings.System.DEFAULT_NOTIFICATION_URI,null);manager.createNotificationChannel(low);
        assertTrue(ReceivedFiles.status(context).contains("без звука"));
        try(ActivityController<Activity> controller=Robolectric.buildActivity(Activity.class).setup()) {
            ReceivedFiles.enableSound(controller.get());
            assertEquals(NotificationManager.IMPORTANCE_HIGH,manager.getNotificationChannel(ReceivedFiles.SOUND_CHANNEL).getImportance());
        }
    }
    @Test public void mutedReplacementIsNotRecreatedAndButtonOpensItsSettings() {
        NotificationManager manager=silentChannel(NotificationManager.IMPORTANCE_HIGH);
        NotificationChannel muted=new NotificationChannel(ReceivedFiles.SOUND_CHANNEL,"Полученные файлы",NotificationManager.IMPORTANCE_HIGH);
        muted.setSound(null,null);manager.createNotificationChannel(muted);
        try(ActivityController<Activity> controller=Robolectric.buildActivity(Activity.class).setup()) {
            ReceivedFiles.enableSound(controller.get());
            assertNull(manager.getNotificationChannel(ReceivedFiles.SOUND_CHANNEL).getSound());
            assertNull(shadowOf(manager).getNotification(63));
            assertEquals(ReceivedFiles.SOUND_CHANNEL,shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity().getStringExtra(Settings.EXTRA_CHANNEL_ID));
        }
    }
    @Test public void globallyBlockedNotificationsOpenAppSettingsWithoutCreatingReplacement() {
        NotificationManager manager=silentChannel(NotificationManager.IMPORTANCE_HIGH);
        shadowOf(manager).setNotificationsEnabled(false);
        try(ActivityController<Activity> controller=Robolectric.buildActivity(Activity.class).setup()) {
            ReceivedFiles.enableSound(controller.get());
            assertNull(manager.getNotificationChannel(ReceivedFiles.SOUND_CHANNEL));
            assertEquals(Settings.ACTION_APP_NOTIFICATION_SETTINGS,shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity().getAction());
        }
    }
    @Test public void silentPhoneAndZeroNotificationVolumeAreReportedWithoutChangingSystemSettings() {
        shadowOf(RuntimeEnvironment.getApplication()).grantPermissions(Manifest.permission.POST_NOTIFICATIONS);
        ReceivedFiles.ensureChannel(context);
        android.media.AudioManager audio=context.getSystemService(android.media.AudioManager.class);
        audio.setRingerMode(android.media.AudioManager.RINGER_MODE_VIBRATE);
        assertTrue(ReceivedFiles.status(context).contains("виброрежиме"));
        assertEquals(android.media.AudioManager.RINGER_MODE_VIBRATE,audio.getRingerMode());
        audio.setRingerMode(android.media.AudioManager.RINGER_MODE_NORMAL);
        audio.setStreamVolume(android.media.AudioManager.STREAM_NOTIFICATION,0,0);
        assertTrue(ReceivedFiles.status(context).contains("равна нулю"));
        assertEquals(0,audio.getStreamVolume(android.media.AudioManager.STREAM_NOTIFICATION));
    }
    @Test public void soundButtonWithDeniedPermissionRequestsPermissionWithoutSwitchingCategory() {
        ReceivedFiles.ensureChannel(context);
        try(ActivityController<Activity> controller=Robolectric.buildActivity(Activity.class).setup()) {
            ReceivedFiles.enableSound(controller.get());
            assertEquals(ReceivedFiles.PERMISSION_REQUEST,shadowOf(controller.get()).getLastRequestedPermission().requestCode);
            assertNull(context.getSystemService(NotificationManager.class).getNotificationChannel(ReceivedFiles.SOUND_CHANNEL));
        }
    }
}
