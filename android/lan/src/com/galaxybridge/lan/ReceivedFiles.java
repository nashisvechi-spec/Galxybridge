package com.galaxybridge.lan;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.provider.DocumentsContract;
import android.provider.Settings;

final class ReceivedFiles {
    static final String CHANNEL="received_files";
    static final int NOTIFICATION=62; // Separate from the connection's foreground notification (61).
    static final int PERMISSION_REQUEST=8;
    static void ensureChannel(Context context) {
        NotificationManager manager=context.getSystemService(NotificationManager.class);
        if(manager!=null && manager.getNotificationChannel(CHANNEL)==null)
            manager.createNotificationChannel(new NotificationChannel(CHANNEL,"Полученные файлы",NotificationManager.IMPORTANCE_HIGH));
    }
    static String status(Context context) {
        NotificationManager manager=context.getSystemService(NotificationManager.class);
        if(manager==null || !manager.areNotificationsEnabled()) return "Уведомления: выключены";
        if(context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED) return "Уведомления: нужно разрешение";
        NotificationChannel channel=manager.getNotificationChannel(CHANNEL);
        if(channel!=null && channel.getImportance()==NotificationManager.IMPORTANCE_NONE) return "Уведомления: категория «Полученные файлы» выключена";
        if(channel!=null && channel.getImportance()<NotificationManager.IMPORTANCE_HIGH) return "Уведомления: включены, без всплывающего окна";
        return "Уведомления: включены";
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
        Intent settings=new Intent(granted?Settings.ACTION_CHANNEL_NOTIFICATION_SETTINGS:Settings.ACTION_APP_NOTIFICATION_SETTINGS)
            .putExtra(Settings.EXTRA_APP_PACKAGE,activity.getPackageName());
        if(granted) settings.putExtra(Settings.EXTRA_CHANNEL_ID,CHANNEL);
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
            if(manager.getNotificationChannel(CHANNEL).getImportance()==NotificationManager.IMPORTANCE_NONE) return;
            PendingIntent open=PendingIntent.getActivity(context,NOTIFICATION,new Intent(context,ReceivedFolderActivity.class),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
            manager.notify(id,new Notification.Builder(context,CHANNEL)
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
