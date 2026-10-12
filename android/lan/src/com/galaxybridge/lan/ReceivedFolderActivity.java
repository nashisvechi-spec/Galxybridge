package com.galaxybridge.lan;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.provider.DocumentsContract;
import android.app.DownloadManager;
import android.widget.Toast;

// An activity PendingIntent lets Android open the folder from a notification tap.
public final class ReceivedFolderActivity extends Activity {
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        if(saved!=null) return; // A fallback picker may already be above this activity.
        try { startActivity(ReceivedFiles.viewFolder());finish();return; }
        catch(RuntimeException ignored) { }
        // Some OEM file managers do not implement directory VIEW. The system file
        // chooser can still display this exact folder without requesting tree access.
        Intent picker=new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("*/*").addCategory(Intent.CATEGORY_OPENABLE)
            .putExtra(DocumentsContract.EXTRA_INITIAL_URI,ReceivedFiles.folder());
        try { startActivityForResult(picker,1);return; }
        catch(RuntimeException ignored) { }
        try { startActivity(new Intent(DownloadManager.ACTION_VIEW_DOWNLOADS)); }
        catch(RuntimeException ignored) { Toast.makeText(this,"Файлы сохранены в Download/GalaxyBridge. Откройте папку в «Моих файлах».",Toast.LENGTH_LONG).show(); }
        finish();
    }
    @Override protected void onActivityResult(int request,int result,Intent data) {
        super.onActivityResult(request,result,data);
        if(request==1) {
            if(result==RESULT_OK && data!=null && data.getData()!=null) {
                try { startActivity(new Intent(Intent.ACTION_VIEW).setDataAndType(data.getData(),getContentResolver().getType(data.getData()))
                    .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)); }
                catch(RuntimeException ignored) { Toast.makeText(this,"Нет приложения для открытия этого файла.",Toast.LENGTH_LONG).show(); }
            }
            finish();
        }
    }
}
