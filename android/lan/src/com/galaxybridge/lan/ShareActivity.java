package com.galaxybridge.lan;

import android.app.Activity;
import android.content.ClipData;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.widget.Toast;
import java.util.ArrayList;

// A transparent share target only hands the URI grants to the service.
// No transfer window is created, and the sender's screen remains underneath.
public final class ShareActivity extends Activity {
    private boolean submitted;
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        submitted=saved!=null && saved.getBoolean("submitted",false);
    }
    @Override protected void onResume() {
        super.onResume();
        if(!submitted) {
            submitted=true;
            ArrayList<Uri> files=collect(getIntent());
            if(!files.isEmpty()) submit(this,files);
            else {
                CharSequence text=getIntent().getCharSequenceExtra(Intent.EXTRA_TEXT);
                if(Intent.ACTION_SEND.equals(getIntent().getAction()) && text!=null) {
                    try { startActivity(new Intent(this,ClipboardActivity.class).putExtra("sharedText",ClipText.validate(text))); }
                    catch(IllegalArgumentException e) { Toast.makeText(this,e.getMessage(),Toast.LENGTH_LONG).show(); }
                } else Toast.makeText(this,"Не удалось получить файлы. Поделитесь ими снова из файлового приложения.",Toast.LENGTH_LONG).show();
            }
        }
        // Start the service while resumed, then leave the source task intact.
        finish();
    }
    @Override protected void onSaveInstanceState(Bundle out) {
        out.putBoolean("submitted",submitted);super.onSaveInstanceState(out);
    }
    static ArrayList<Uri> collect(Intent intent) {
        ArrayList<Uri> files=new ArrayList<>();
        if(intent==null) return files;
        String action=intent.getAction();
        if(Intent.ACTION_SEND.equals(action)) add(files,intent.getParcelableExtra(Intent.EXTRA_STREAM,Uri.class));
        else if(Intent.ACTION_SEND_MULTIPLE.equals(action)) {
            ArrayList<Uri> shared=intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM,Uri.class);
            if(shared!=null) for(Uri uri:shared) add(files,uri);
        }
        ClipData clip=intent.getClipData();
        if(clip!=null) for(int i=0;i<clip.getItemCount() && i<100;i++) add(files,clip.getItemAt(i).getUri());
        add(files,intent.getData());
        return files;
    }
    private static void add(ArrayList<Uri> files,Uri uri) {
        if(uri!=null && "content".equals(uri.getScheme()) && files.size()<100 && !files.contains(uri)) files.add(uri);
    }
    static void submit(Activity activity,ArrayList<Uri> files) {
        if(files.isEmpty()) return;
        if(!Wire.hex(activity.getSharedPreferences("lan",MODE_PRIVATE).getString("token",""),64)) {
            ConnectionService.fileStatus="Сначала выполните QR-сопряжение в Galaxy Bridge Wi-Fi, затем поделитесь файлами снова.";
            Toast.makeText(activity,ConnectionService.fileStatus,Toast.LENGTH_LONG).show();return;
        }
        Intent service=new Intent(activity,ConnectionService.class);
        service.putParcelableArrayListExtra("files",new ArrayList<>(files));
        ClipData clip=ClipData.newRawUri("Galaxy Bridge",files.get(0));
        for(int i=1;i<files.size();i++) clip.addItem(new ClipData.Item(files.get(i)));
        service.setClipData(clip);service.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        try { activity.startForegroundService(service); }
        catch(SecurityException | IllegalStateException e) {
            ConnectionService.fileStatus="Не удалось начать отправку. Выберите файлы заново и разрешите работу приложения.";
            Toast.makeText(activity,ConnectionService.fileStatus,Toast.LENGTH_LONG).show();
        }
    }
}
