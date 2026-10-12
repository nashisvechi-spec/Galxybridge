package com.galaxybridge.lan;

import android.Manifest;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.provider.DocumentsContract;

final class ReceivedFiles {
    static final String CHANNEL="received_files";
    static final int NOTIFICATION=62; // Separate from the connection's foreground notification (61).
    static Uri folder() {
        return DocumentsContract.buildDocumentUri("com.android.externalstorage.documents","primary:Download/GalaxyBridge");
    }
    static Intent viewFolder() {
        return new Intent(Intent.ACTION_VIEW).setDataAndType(folder(),DocumentsContract.Document.MIME_TYPE_DIR);
    }
    static void notifySaved(Context context,String name) {
        // Notification settings/UI failures must never turn a saved file into a failed transfer.
        try {
            if(context.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED) return;
            NotificationManager manager=context.getSystemService(NotificationManager.class);
            if(manager==null || !manager.areNotificationsEnabled()) return;
            manager.createNotificationChannel(new NotificationChannel(CHANNEL,"Полученные файлы",NotificationManager.IMPORTANCE_DEFAULT));
            PendingIntent open=PendingIntent.getActivity(context,NOTIFICATION,new Intent(context,ReceivedFolderActivity.class),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
            String text=name+" · Нажмите, чтобы открыть Download/GalaxyBridge";
            manager.notify(NOTIFICATION,new Notification.Builder(context,CHANNEL)
                .setSmallIcon(android.R.drawable.stat_sys_download_done).setContentTitle("Файл получен с ПК")
                .setContentText(text).setStyle(new Notification.BigTextStyle().bigText(text))
                .setCategory(Notification.CATEGORY_STATUS).setContentIntent(open).setAutoCancel(true)
                .setOnlyAlertOnce(true).build());
        } catch(RuntimeException ignored) { }
    }
}
