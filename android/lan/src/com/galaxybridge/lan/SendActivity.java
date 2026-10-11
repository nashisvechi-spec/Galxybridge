package com.galaxybridge.lan;

import android.app.Activity;
import android.content.ClipData;
import android.content.Intent;
import android.content.ActivityNotFoundException;
import android.graphics.Insets;
import android.graphics.Rect;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.WindowInsets;
import android.view.ViewConfiguration;
import android.widget.*;
import java.util.ArrayList;
import java.util.Locale;
import java.util.UUID;

public final class SendActivity extends Activity {
    private final ArrayList<Uri> files=new ArrayList<>();
    private final Handler handler=new Handler(Looper.getMainLooper());
    private TextView selection, progress;
    private Button send, cancel;
    private Button prepare;
    private TextView dragCard;
    private String preparedBatch="";
    private static SendActivity visible;
    private boolean remoteDrag;
    private float pressX,pressY;
    private final Runnable update=new Runnable() { public void run() {
        progress.setText(ConnectionService.status+"\n\n"+ConnectionService.fileStatus);
        send.setEnabled(!files.isEmpty() && !ConnectionService.uploading()); cancel.setEnabled(ConnectionService.uploading());
        prepare.setEnabled(!files.isEmpty() && !ConnectionService.uploading());
        boolean ready=!preparedBatch.isEmpty() && preparedBatch.equals(ConnectionService.dragReadyBatch);
        dragCard.setAlpha(ready?1f:0.45f);
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
        TextView title=new TextView(this); title.setText("Отправить на ПК · 0.7.14"); title.setTextSize(24); page.addView(title);
        TextView help=new TextView(this); help.setText("Файлы получит сохранённый ноутбук. Откройте на нём режим Wi-Fi без отладки. Оба устройства должны быть в одной сети. Папку приёма можно изменить в окне файлов Windows."); page.addView(help);
        selection=new TextView(this); selection.setTextSize(18); page.addView(selection);
        Button choose=new Button(this); choose.setText("Выбрать файлы…"); choose.setOnClickListener(v -> choose(false)); page.addView(choose);
        Button other=new Button(this); other.setText("Выбрать через файловое приложение…"); other.setOnClickListener(v -> choose(true)); page.addView(other);
        TextView hint=new TextView(this); hint.setText("Щёлкните по названию файла, чтобы выбрать его. Для нескольких файлов используйте режим выделения файлового приложения и подтвердите выбор в файловом окне. Также можно отправить через «Поделиться → Galaxy Bridge Wi-Fi»."); page.addView(hint);
        send=new Button(this); send.setText("Отправить на ноутбук"); send.setOnClickListener(v -> send()); page.addView(send);
        prepare=new Button(this);prepare.setText("Подготовить для перетаскивания на ПК");
        prepare.setOnClickListener(v -> { preparedBatch=UUID.randomUUID().toString().replace("-","");send(preparedBatch); });page.addView(prepare);
        dragCard=new TextView(this);dragCard.setText("▣  Перетащить выбранные файлы на ПК\n\nПосле подготовки зажмите эту карточку мышью и тяните к краю экрана со стороны ПК. Кнопку отпускайте уже в папке Windows.");
        dragCard.setTextSize(18);dragCard.setPadding(p,p,p,p);dragCard.setBackgroundColor(0xffdcecff);page.addView(dragCard);
        TextView dragHint=new TextView(this);dragHint.setText("Тестовый режим: мышь по Wi-Fi, из этой карточки. Сначала файлы копируются во временный кэш ПК. Подборка — до 2 ГБ. Оригиналы на телефоне сохраняются.");page.addView(dragHint);
        cancel=new Button(this); cancel.setText("Отменить отправку"); cancel.setOnClickListener(v -> ConnectionService.cancelFiles()); page.addView(cancel);
        progress=new TextView(this); progress.setTextSize(16); page.addView(progress);
        ScrollView scroll=new ScrollView(this); scroll.addView(page); setContentView(scroll);
        if(saved!=null) {
            ArrayList<Uri> restored=saved.getParcelableArrayList("files",Uri.class); if(restored!=null) files.addAll(restored);
        } else collect(getIntent());
        describe();
        if(saved==null && DropActivity.consumePreparation(getIntent().getStringExtra("prepareDropTicket"),files) && !files.isEmpty()) {
            preparedBatch=UUID.randomUUID().toString().replace("-","");send(preparedBatch);
        }
    }
    private void collect(Intent intent) {
        preparedBatch="";remoteDrag=false;
        files.clear();
        if(intent==null) return;
        String action=intent.getAction();
        if(Intent.ACTION_SEND.equals(action)) add(intent.getParcelableExtra(Intent.EXTRA_STREAM,Uri.class));
        else if(Intent.ACTION_SEND_MULTIPLE.equals(action)) {
            ArrayList<Uri> shared=intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM,Uri.class);
            if(shared!=null) for(Uri uri:shared) add(uri);
        }
        ClipData clip=intent.getClipData();
        if(clip!=null) for(int i=0;i<clip.getItemCount() && i<100;i++) add(clip.getItemAt(i).getUri());
        if(intent.getData()!=null) add(intent.getData());
    }
    private void add(Uri uri) { if(uri!=null && "content".equals(uri.getScheme()) && files.size()<100 && !files.contains(uri)) files.add(uri); }
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
        send(null);
    }
    private void send(String dragBatch) {
        if(files.isEmpty() || ConnectionService.uploading()) return;
        if(!Wire.hex(getSharedPreferences("lan",MODE_PRIVATE).getString("token",""),64)) {
            Toast.makeText(this,"Сначала выполните QR-сопряжение в Galaxy Bridge Wi-Fi.",Toast.LENGTH_LONG).show(); return;
        }
        Intent service=new Intent(this,ConnectionService.class); service.putParcelableArrayListExtra("files",new ArrayList<>(files));
        if(dragBatch!=null) service.putExtra("dragBatch",dragBatch);
        ClipData clip=ClipData.newRawUri("Galaxy Bridge",files.get(0));
        for(int i=1;i<files.size();i++) clip.addItem(new ClipData.Item(files.get(i)));
        service.setClipData(clip); service.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        try { startForegroundService(service); }
        catch(SecurityException | IllegalStateException e) {
            ConnectionService.fileStatus="Не удалось начать отправку. Выберите файлы заново и разрешите работу приложения.";
            Toast.makeText(this,ConnectionService.fileStatus,Toast.LENGTH_LONG).show();
        }
    }
    @Override protected void onActivityResult(int request,int result,Intent data) {
        super.onActivityResult(request,result,data);
        if(request==11 && result==RESULT_OK && data!=null) { collect(data); describe(); }
    }
    @Override protected void onSaveInstanceState(Bundle out) { out.putParcelableArrayList("files",files); super.onSaveInstanceState(out); }
    static boolean remotePointer(float x,float y,int buttons,int previous,int side,int epoch,int width,int height) {
        SendActivity activity=visible;
        return activity!=null && activity.pointer(x,y,buttons,previous,side,epoch,width,height);
    }
    private boolean pointer(float x,float y,int buttons,int previous,int side,int epoch,int width,int height) {
        if((buttons&1)!=0 && (previous&1)==0) {
            Rect bounds=new Rect();int[] location=new int[2];dragCard.getLocationOnScreen(location);
            if(dragCard.getLocalVisibleRect(bounds)) bounds.offset(location[0],location[1]);else bounds.setEmpty();
            remoteDrag=preparedBatch.equals(ConnectionService.dragReadyBatch) && !preparedBatch.isEmpty() && bounds.contains((int)x,(int)y);
            if(remoteDrag) { pressX=x;pressY=y; }
        }
        if(!remoteDrag) return false;
        if(buttons!=1) { remoteDrag=false;return true; }
        int slop=ViewConfiguration.get(this).getScaledTouchSlop();
        boolean moved=Math.abs(x-pressX)+Math.abs(y-pressY)>slop;
        boolean edge=side==0?x<16:side==1?x>=width-16:side==2?y>=height-16:y<16;
        if(moved && edge) {
            remoteDrag=false;
            String line=String.format(Locale.ROOT,"GB_EDGE %d 1 %d %d %.2f %.2f 1",epoch,width,height,x,y);
            if(!ConnectionService.startFileDrag(preparedBatch,line)) ConnectionService.fileStatus="Связь прервалась. Подготовьте файлы заново.";
        }
        return true;
    }
    static void cancelRemoteDrag() { if(visible!=null) visible.remoteDrag=false; }
    @Override protected void onResume() { super.onResume();visible=this; handler.post(update); }
    @Override protected void onPause() { if(visible==this) visible=null;remoteDrag=false;handler.removeCallbacks(update); super.onPause(); }
}
