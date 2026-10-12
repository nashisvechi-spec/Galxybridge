package com.galaxybridge.lan;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.media.AudioAttributes;
import android.media.AudioManager;
import android.provider.DocumentsContract;
import android.provider.Settings;

final class ReceivedFiles {
    static final String CHANNEL="received_files";
    static final String SOUND_CHANNEL="received_files_sound_v1";
    static final int NOTIFICATION=62; // Separate from the connection's foreground notification (61).
    static final int PERMISSION_REQUEST=8;
    static void ensureChannel(Context context) {
        NotificationManager manager=context.getSystemService(NotificationManager.class);
        if(manager!=null && manager.getNotificationChannel(channelId(manager))==null)
            manager.createNotificationChannel(audibleChannel(CHANNEL));
    }
    private static String channelId(NotificationManager manager) {
        return manager.getNotificationChannel(SOUND_CHANNEL)!=null?SOUND_CHANNEL:CHANNEL;
    }
    private static NotificationChannel audibleChannel(String id) {
        NotificationChannel channel=new NotificationChannel(id,"Полученные файлы",NotificationManager.IMPORTANCE_HIGH);
        channel.setDescription("Получение файлов с ПК и проверка уведомлений");
        channel.setSound(Settings.System.DEFAULT_NOTIFICATION_URI,new AudioAttributes.Builder()
            .setUsage(AudioAttributes.USAGE_NOTIFICATION).setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build());
        return channel;
    }
    static void enableSound(Activity activity) {
        ensureChannel(activity);
        if(activity.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED) {
            enable(activity,false);return;
        }
        NotificationManager manager=activity.getSystemService(NotificationManager.class);
        if(manager==null) return;
        if(!manager.areNotificationsEnabled()) { enable(activity,false);return; }
        NotificationChannel channel=manager.getNotificationChannel(channelId(manager));
        // Existing channel behavior is immutable. Switch only after an explicit
        // tap; never reset a user's quiet/blocked category during an app update.
        if(channel.getId().equals(CHANNEL) && (channel.getSound()==null || channel.getImportance()<NotificationManager.IMPORTANCE_DEFAULT)) {
            manager.createNotificationChannel(audibleChannel(SOUND_CHANNEL));
            channel=manager.getNotificationChannel(SOUND_CHANNEL);
        }
        if(channel.getSound()==null || channel.getImportance()<NotificationManager.IMPORTANCE_DEFAULT) {
            enable(activity,false);return;
        }
        test(activity);
    }
    static String status(Context context) {
        NotificationManager manager=context.getSystemService(NotificationManager.class);
        if(manager==null || !manager.areNotificationsEnabled()) return "Уведомления: выключены";
        if(context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED) return "Уведомления: нужно разрешение";
        NotificationChannel channel=manager.getNotificationChannel(channelId(manager));
        if(channel!=null && channel.getImportance()==NotificationManager.IMPORTANCE_NONE) return "Уведомления: категория «Полученные файлы» выключена";
        if(channel!=null && (channel.getSound()==null || channel.getImportance()<NotificationManager.IMPORTANCE_DEFAULT)) return "Уведомления: включены, без звука";
        String enabled=channel!=null && channel.getImportance()<NotificationManager.IMPORTANCE_HIGH
            ?"Уведомления: включены, без всплывающего окна":"Уведомления: включены";
        AudioManager audio=context.getSystemService(AudioManager.class);
        if(audio!=null && audio.getRingerMode()!=AudioManager.RINGER_MODE_NORMAL) return enabled+"; телефон в беззвучном режиме или виброрежиме";
        if(audio!=null && audio.getStreamVolume(AudioManager.STREAM_NOTIFICATION)==0) return enabled+"; громкость уведомлений равна нулю";
        int filter=manager.getCurrentInterruptionFilter();
        if(filter==NotificationManager.INTERRUPTION_FILTER_NONE || filter==NotificationManager.INTERRUPTION_FILTER_ALARMS || filter==NotificationManager.INTERRUPTION_FILTER_PRIORITY)
            return enabled+"; действует режим «Не беспокоить»";
        return enabled;
    }
    static void enable(Activity activity,boolean automatic) {
        ensureChannel(activity);
        SharedPreferences prefs=activity.getSharedPreferences("notifications",Context.MODE_PRIVATE);
        boolean asked=prefs.getBoolean("permissionAsked",false);
        boolean granted=activity.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)==PackageManager.PERMISSION_GRANTED;
        if(automatic && (asked || granted)) return;
        if(!granted && (!asked || activity.shouldShowRequestPermissionRationale(Manifest.permission.POST_NOTIFICATIONS))) {
            prefs.edit().putBoolean("permissionAsked",true).apply();
            activity.requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS},PERMISSION_REQUEST);
            return;
        }
        NotificationManager manager=activity.getSystemService(NotificationManager.class);
        boolean category=granted && manager!=null && manager.areNotificationsEnabled();
        Intent settings=new Intent(category?Settings.ACTION_CHANNEL_NOTIFICATION_SETTINGS:Settings.ACTION_APP_NOTIFICATION_SETTINGS)
            .putExtra(Settings.EXTRA_APP_PACKAGE,activity.getPackageName());
        if(category) settings.putExtra(Settings.EXTRA_CHANNEL_ID,channelId(manager));
        activity.startActivity(settings);
    }
    static Uri folder() {
        return DocumentsContract.buildDocumentUri("com.android.externalstorage.documents","primary:Download/GalaxyBridge");
    }
    static Intent viewFolder() {
        return new Intent(Intent.ACTION_VIEW).setDataAndType(folder(),DocumentsContract.Document.MIME_TYPE_DIR);
    }
    static void notifySaved(Context context,String name) {
        post(context,"Файл получен с ПК",name+" · Нажмите, чтобы открыть Download/GalaxyBridge",NOTIFICATION);
    }
    private static void post(Context context,String title,String text,int id) {
        // Notification settings/UI failures must never turn a saved file into a failed transfer.
        try {
            if(context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED) return;
            NotificationManager manager=context.getSystemService(NotificationManager.class);
            if(manager==null || !manager.areNotificationsEnabled()) return;
            ensureChannel(context);
            String channel=channelId(manager);
            if(manager.getNotificationChannel(channel).getImportance()==NotificationManager.IMPORTANCE_NONE) return;
            PendingIntent open=PendingIntent.getActivity(context,NOTIFICATION,new Intent(context,ReceivedFolderActivity.class),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
            manager.notify(id,new Notification.Builder(context,channel)
                .setSmallIcon(android.R.drawable.stat_sys_download_done).setContentTitle(title)
                .setContentText(text).setStyle(new Notification.BigTextStyle().bigText(text))
                .setCategory(Notification.CATEGORY_STATUS).setContentIntent(open).setAutoCancel(true)
                .setOnlyAlertOnce(false).build());
        } catch(RuntimeException ignored) { }
    }
    static void test(Activity activity) {
        enable(activity,true);
        if(activity.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED) {
            android.widget.Toast.makeText(activity,"Разрешите уведомления, затем повторите проверку.",android.widget.Toast.LENGTH_LONG).show();return;
        }
        post(activity,"Проверка уведомлений","Galaxy Bridge · уведомления работают. Файл не создаётся.",63);
        android.widget.Toast.makeText(activity,status(activity),android.widget.Toast.LENGTH_LONG).show();
    }
}
