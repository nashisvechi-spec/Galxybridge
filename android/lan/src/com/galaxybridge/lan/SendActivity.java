package com.galaxybridge.lan;

import android.app.Activity;
import android.content.Intent;
import android.content.ActivityNotFoundException;
import android.graphics.Insets;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.WindowInsets;
import android.widget.*;
import java.util.ArrayList;

public final class SendActivity extends Activity {
    private final ArrayList<Uri> files=new ArrayList<>();
    private final Handler handler=new Handler(Looper.getMainLooper());
    private TextView selection, progress;
    private Button send, cancel;
    private final Runnable update=new Runnable() { public void run() {
        if(isFinishing() || progress==null) return;
        progress.setText(ConnectionService.status+"\n\n"+ConnectionService.fileStatus);
        send.setEnabled(!files.isEmpty() && !ConnectionService.uploading()); cancel.setEnabled(ConnectionService.uploading());
        handler.postDelayed(this,500);
    }};
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        if(saved==null) {
            collect(getIntent());
            CharSequence shared=getIntent().getCharSequenceExtra(Intent.EXTRA_TEXT);
            if(files.isEmpty() && Intent.ACTION_SEND.equals(getIntent().getAction()) && shared!=null) {
                try {
                    String value=ClipText.validate(shared);
                    startActivity(new Intent(this,ClipboardActivity.class).putExtra("sharedText",value));finish();return;
                } catch(IllegalArgumentException e) { ConnectionService.fileStatus=e.getMessage(); }
            }
        }
        LinearLayout page=new LinearLayout(this); page.setOrientation(LinearLayout.VERTICAL);
        int p=(int)(20*getResources().getDisplayMetrics().density); page.setPadding(p,p,p,p);
        page.setOnApplyWindowInsetsListener((v,i) -> { Insets b=i.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout()); v.setPadding(p+b.left,p+b.top,p+b.right,p+b.bottom); return i; });
        TextView title=new TextView(this); title.setText("Отправить на ПК · 0.7.30"); title.setTextSize(24); page.addView(title);
        TextView help=new TextView(this); help.setText("Файлы получит сохранённый ноутбук. Откройте на нём режим Wi-Fi без отладки. Оба устройства должны быть в одной сети. Папку приёма можно изменить в окне файлов Windows."); page.addView(help);
        selection=new TextView(this); selection.setTextSize(18); page.addView(selection);
        Button choose=new Button(this); choose.setText("Выбрать файлы…"); choose.setOnClickListener(v -> choose(false)); page.addView(choose);
        Button other=new Button(this); other.setText("Выбрать через файловое приложение…"); other.setOnClickListener(v -> choose(true)); page.addView(other);
        TextView hint=new TextView(this); hint.setText("Щёлкните по названию файла, чтобы выбрать его. Для нескольких файлов используйте режим выделения файлового приложения и подтвердите выбор в файловом окне. Через «Поделиться → Galaxy Bridge Wi-Fi» отправка проходит в фоне, без открытия окна приложения."); page.addView(hint);
        send=new Button(this); send.setText("Отправить на ноутбук"); send.setOnClickListener(v -> send()); page.addView(send);
        cancel=new Button(this); cancel.setText("Отменить отправку"); cancel.setOnClickListener(v -> ConnectionService.cancelFiles()); page.addView(cancel);
        progress=new TextView(this); progress.setTextSize(16); page.addView(progress);
        ScrollView scroll=new ScrollView(this); scroll.addView(page); setContentView(scroll);
        if(saved!=null) {
            ArrayList<Uri> restored=saved.getParcelableArrayList("files",Uri.class); if(restored!=null) files.addAll(restored);
        }
        describe();
    }
    private void collect(Intent intent) { files.clear();files.addAll(ShareActivity.collect(intent)); }
    private void describe() {
        selection.setText("Ноутбук: "+getSharedPreferences("lan",MODE_PRIVATE).getString("name","ещё не сопряжён")+"\nВыбрано файлов: "+files.size()+" (до 100, до 2 ГБ каждый)");
    }
    private void choose(boolean alternative) {
        Intent picker=new Intent(alternative?Intent.ACTION_GET_CONTENT:Intent.ACTION_OPEN_DOCUMENT).setType("*/*").addCategory(Intent.CATEGORY_OPENABLE);
        picker.putExtra(Intent.EXTRA_ALLOW_MULTIPLE,true);
        try { startActivityForResult(alternative?Intent.createChooser(picker,"Выберите файловое приложение"):picker,11); }
        catch(ActivityNotFoundException e) { Toast.makeText(this,"Приложение для выбора файлов не найдено. Используйте «Поделиться» из «Моих файлов».",Toast.LENGTH_LONG).show(); }
    }
    private void send() {
        if(!ConnectionService.uploading()) ShareActivity.submit(this,files);
    }
    @Override protected void onActivityResult(int request,int result,Intent data) {
        super.onActivityResult(request,result,data);
        if(request==11 && result==RESULT_OK && data!=null) { collect(data); describe(); }
    }
    @Override protected void onSaveInstanceState(Bundle out) {
        out.putParcelableArrayList("files",files);super.onSaveInstanceState(out);
    }
    @Override protected void onResume() { super.onResume();if(progress!=null && !isFinishing()) handler.post(update); }
    @Override protected void onPause() { handler.removeCallbacks(update); super.onPause(); }
}
