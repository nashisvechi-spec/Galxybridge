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
    private TextView selection, progress,sendHint;
    private Button send, cancel;
    private final Runnable update=new Runnable() { public void run() {
        if(isFinishing() || progress==null) return;
        BridgeUi.updateText(progress,ConnectionService.status+"\n\n"+ConnectionService.fileStatus);
        updateActions();
        handler.postDelayed(this,500);
    }};
    @Override public void onCreate(Bundle saved) {
        BridgeUi.prepare(this);super.onCreate(saved);
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
        LinearLayout root=BridgeUi.root(this);LinearLayout page=BridgeUi.scrollBody(root);
        BridgeUi.header(page,"Отправить на ПК","Получатель: "+getSharedPreferences("lan",MODE_PRIVATE).getString("name","компьютер не сопряжён"),this::finish);
        LinearLayout selected=BridgeUi.card(page,"Готово к отправке");
        selection=BridgeUi.text(this,"",18,true);selected.addView(selection);
        BridgeUi.button(selected,"Выбрать файлы",false,() -> choose(false));
        BridgeUi.button(selected,"Выбрать через файловое приложение",false,() -> choose(true));
        send=BridgeUi.button(selected,"Отправить на ноутбук",true,this::send);
        cancel=BridgeUi.button(selected,"Отменить отправку",false,() -> ConnectionService.cancelFiles());
        sendHint=BridgeUi.text(this,"",14,false);selected.addView(sendHint);
        LinearLayout activity=BridgeUi.card(page,"Передача");progress=BridgeUi.text(this,"",16,false);activity.addView(progress);
        LinearLayout help=BridgeUi.card(page,"Быстрая отправка");help.addView(BridgeUi.text(this,"Из «Моих файлов» или Галереи: Поделиться → Galaxy Bridge Wi-Fi. Файлы отправляются в фоне, без открытия окна приложения.",14,false));
        page.addView(BridgeUi.text(this,"До 100 файлов, до 2 ГБ каждый. Для нескольких файлов подтвердите выбор в файловом окне. Папки не поддерживаются.",13,false));
        BridgeUi.show(this,root);
        if(saved!=null) {
            ArrayList<Uri> restored=saved.getParcelableArrayList("files",Uri.class); if(restored!=null) files.addAll(restored);
        }
        describe();updateActions();
    }
    private void collect(Intent intent) { files.clear();files.addAll(ShareActivity.collect(intent)); }
    private void describe() {
        selection.setText("Ноутбук: "+getSharedPreferences("lan",MODE_PRIVATE).getString("name","ещё не сопряжён")+"\nВыбрано файлов: "+(files.size()>100?"больше 100":String.valueOf(files.size()))+" (до 100, до 2 ГБ каждый)");
    }
    private void updateActions() {
        boolean paired=Wire.hex(getSharedPreferences("lan",MODE_PRIVATE).getString("token",""),64),busy=ConnectionService.uploading();
        send.setEnabled(paired && !files.isEmpty() && files.size()<=100 && !busy);cancel.setEnabled(busy);
        BridgeUi.updateText(sendHint,!paired?"Сначала подключите ПК по QR на вкладке «Связь».":files.size()>100?"Выбрано больше 100 файлов. Выберите меньшую подборку. Ничего не отправлено.":busy?"Идёт отправка. Кнопка отмены остановит её и очистит очередь.":files.isEmpty()?"Выберите файлы, затем нажмите «Отправить на ноутбук».":"При отсутствии связи подождём подключения до 30 секунд. Результат появится ниже.");
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
        if(request==11 && result==RESULT_OK && data!=null) {
            collect(data);
            if((data.getFlags()&Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION)!=0) for(Uri uri:files) try { getContentResolver().takePersistableUriPermission(uri,Intent.FLAG_GRANT_READ_URI_PERMISSION); } catch(SecurityException ignored) { }
            describe();updateActions();
        }
    }
    @Override protected void onSaveInstanceState(Bundle out) {
        out.putParcelableArrayList("files",files);super.onSaveInstanceState(out);
    }
    @Override protected void onResume() { super.onResume();handler.removeCallbacks(update);if(progress!=null && !isFinishing()) handler.post(update); }
    @Override protected void onPause() { handler.removeCallbacks(update); super.onPause(); }
}
