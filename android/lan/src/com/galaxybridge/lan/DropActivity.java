package com.galaxybridge.lan;

import android.app.Activity;
import android.content.ClipData;
import android.content.Intent;
import android.content.res.AssetFileDescriptor;
import android.database.Cursor;
import android.graphics.Insets;
import android.net.Uri;
import android.os.Bundle;
import android.os.CancellationSignal;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.provider.OpenableColumns;
import android.view.DragAndDropPermissions;
import android.view.DragEvent;
import android.view.Gravity;
import android.view.WindowInsets;
import android.widget.*;
import java.io.IOException;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicReference;

/** Visible multi-window receiver; it deliberately does not intercept other apps. */
public final class DropActivity extends Activity {
    private static String preparationTicket="";
    private static ArrayList<Uri> preparationFiles;
    private static long preparationDeadline;
    private final Handler main=new Handler(Looper.getMainLooper());
    private final AtomicReference<InputStream> activeInput=new AtomicReference<>();
    private final ArrayList<Uri> files=new ArrayList<>();
    private TextView target,report;
    private Button prepare,cancel;
    private DragAndDropPermissions permissions;
    private AtomicBoolean cancelled=new AtomicBoolean();
    private CancellationSignal signal;
    private boolean busy,destroyed,dropSeen;
    private String cancellationText="Проверка отменена. Перетащите файлы ещё раз.";
    private final Runnable timeout=() -> {
        cancellationText="Файловое приложение не ответило за 20 секунд. Повторите сброс или используйте «Поделиться».";
        cancelProbe();
    };
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        LinearLayout page=new LinearLayout(this);page.setOrientation(LinearLayout.VERTICAL);
        int p=(int)(16*getResources().getDisplayMetrics().density);page.setPadding(p,p,p,p);
        page.setOnApplyWindowInsetsListener((v,i) -> { Insets b=i.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());v.setPadding(p+b.left,p+b.top,p+b.right,p+b.bottom);return i; });
        TextView title=new TextView(this);title.setText("Приём из «Моих файлов» · 0.7.14");title.setTextSize(22);page.addView(title);
        target=new TextView(this);target.setText("Перетащите файл сюда\nи отпустите кнопку");target.setTextSize(21);target.setGravity(Gravity.CENTER);
        target.setMinHeight(p*9);target.setPadding(p,p,p,p);target.setBackgroundColor(0xffdcecff);page.addView(target);
        target.setOnDragListener((v,event) -> drag(event));
        report=new TextView(this);report.setTextSize(16);page.addView(report);
        report.setText(saved==null?"Ожидаем перетаскивание из файлового приложения.":"Окно было пересоздано. Перетащите файлы ещё раз: временное разрешение на чтение не сохраняется.");
        prepare=new Button(this);prepare.setText("Подготовить для переноса на ПК");prepare.setEnabled(false);prepare.setOnClickListener(v -> forward());page.addView(prepare);
        cancel=new Button(this);cancel.setText("Отменить проверку");cancel.setEnabled(false);cancel.setOnClickListener(v -> { cancellationText="Проверка отменена. Перетащите файлы ещё раз.";cancelProbe(); });page.addView(cancel);
        TextView help=new TextView(this);help.setText("Откройте Galaxy Bridge и «Мои файлы» рядом: через меню значка приложения в «Недавних» выберите разделение экрана или всплывающее окно. Зажмите файл, дождитесь начала перетаскивания и перенесите его в синюю область. Сначала проверьте пальцем, затем мышью по Wi-Fi.\n\nЕсли область не реагирует, файловое приложение могло не начать межприложное перетаскивание. Используйте «Поделиться → Galaxy Bridge Wi-Fi».\n\nПосле проверки нажмите кнопку подготовки. Пока идёт подготовка, держите окно отправки открытым. Затем перетащите его синюю карточку в папку Windows. Оригиналы сохраняются.");page.addView(help);
        ScrollView scroll=new ScrollView(this);scroll.addView(page);setContentView(scroll);
    }
    private boolean drag(DragEvent event) {
        switch(event.getAction()) {
            case DragEvent.ACTION_DRAG_STARTED:
                if(busy || ConnectionService.uploading()) return false;
                dropSeen=false;target.setText("Перетащите сюда\nи отпустите кнопку");
                report.setText("Перетаскивание обнаружено. Ожидаем сброс файла.");return true;
            case DragEvent.ACTION_DRAG_ENTERED:
                target.setBackgroundColor(0xffc9efd7);return true;
            case DragEvent.ACTION_DRAG_LOCATION: return true;
            case DragEvent.ACTION_DRAG_EXITED:
                target.setBackgroundColor(0xffdcecff);return true;
            case DragEvent.ACTION_DROP:
                dropSeen=true;return accept(event);
            case DragEvent.ACTION_DRAG_ENDED:
                target.setBackgroundColor(0xffdcecff);
                if(!dropSeen) report.setText("Файл не сброшен в область приёма. Попробуйте ещё раз.");return true;
            default: return false;
        }
    }
    private boolean accept(DragEvent event) {
        if(busy || ConnectionService.uploading()) { report.setText("Дождитесь завершения текущей передачи.");return false; }
        files.clear();prepare.setEnabled(false);releasePermissions();
        ClipData clip=event.getClipData(); // Actual URIs are valid only at ACTION_DROP.
        if(clip==null || clip.getItemCount()<1 || clip.getItemCount()>DropProbe.MAX_FILES) {
            report.setText("Файловое приложение должно передать от 1 до 100 файлов.");return false;
        }
        ArrayList<Uri> incoming=new ArrayList<>();
        for(int i=0;i<clip.getItemCount();i++) {
            Uri uri=clip.getItemAt(i).getUri();
            if(uri==null || !"content".equals(uri.getScheme())) {
                report.setText("Получено название, текст или неподдерживаемая ссылка вместо доступного файла. Используйте «Поделиться».");return false;
            }
            if(!incoming.contains(uri)) incoming.add(uri);
        }
        try { permissions=requestDragAndDropPermissions(event); }
        catch(RuntimeException e) { report.setText("Не удалось получить временное разрешение на чтение. Повторите сброс.");return false; }
        if(permissions==null) { report.setText("Файловое приложение не предоставило разрешение на чтение. Используйте «Поделиться».");return false; }
        busy=true;cancelled=new AtomicBoolean();signal=new CancellationSignal();cancel.setEnabled(true);
        cancellationText="Проверка отменена. Перетащите файлы ещё раз.";
        report.setText("Получено файлов: "+incoming.size()+". Проверяем доступ к содержимому…");
        main.postDelayed(timeout,20000);
        new Thread(() -> {
            List<DropProbe.Entry> entries=null;String error=null;
            try { entries=DropProbe.inspect(incoming,uri -> open(uri),cancelled); }
            catch(SecurityException e) { error="Ссылка получена, но чтение запрещено файловым приложением. Используйте «Поделиться»."; }
            catch(IOException e) { error=e.getMessage(); }
            catch(RuntimeException e) { error="Не удалось прочитать файл. Повторите сброс или используйте «Поделиться»."; }
            finally { activeInput.set(null); }
            final List<DropProbe.Entry> checked=entries;final String failure=error;
            main.post(() -> finishProbe(incoming,checked,failure));
        },"GalaxyBridge-drop-probe").start();
        return true; // Accepted for asynchronous validation; no upload until the button is pressed.
    }
    private DropProbe.Source open(Uri uri) throws IOException {
        signal.throwIfCanceled();
        String name=null;long size=-1;
        try(Cursor cursor=getContentResolver().query(uri,new String[]{OpenableColumns.DISPLAY_NAME,OpenableColumns.SIZE},null,null,null,signal)) {
            if(cursor!=null && cursor.moveToFirst()) {
                int n=cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME),s=cursor.getColumnIndex(OpenableColumns.SIZE);
                if(n>=0 && !cursor.isNull(n)) name=cursor.getString(n);
                if(s>=0 && !cursor.isNull(s)) size=cursor.getLong(s);
            }
        }
        AssetFileDescriptor descriptor=getContentResolver().openAssetFileDescriptor(uri,"r",signal);
        if(descriptor==null) throw new IOException("Файловое приложение не открыло содержимое файла.");
        InputStream input;
        try { input=descriptor.createInputStream(); }
        catch(IOException | RuntimeException e) { descriptor.close();throw e; }
        activeInput.set(input);
        if(cancelled.get()) { activeInput.compareAndSet(input,null);input.close();throw new IOException("Проверка отменена."); }
        return new DropProbe.Source(name,size,input);
    }
    private void finishProbe(ArrayList<Uri> incoming,List<DropProbe.Entry> entries,String failure) {
        if(destroyed) return;
        main.removeCallbacks(timeout);busy=false;cancel.setEnabled(false);
        if(cancelled.get() || entries==null) {
            releasePermissions();report.setText(cancelled.get()?cancellationText:failure+"\nФайлы не отправлены.");return;
        }
        files.addAll(incoming);StringBuilder text=new StringBuilder("Доступ к чтению подтверждён: ").append(files.size()).append(".\n");
        for(DropProbe.Entry entry:entries) text.append("\n").append(entry.name).append(" · ").append(entry.empty?"пустой файл":entry.size<0?"размер неизвестен":entry.size+" байт");
        text.append("\n\nПроверено открытие и чтение первого байта каждого файла. Передача на ПК ещё не началась.");
        report.setText(text);prepare.setEnabled(true);
    }
    private void forward() {
        if(busy || files.isEmpty()) return;
        if(ConnectionService.uploading()) { report.setText("Дождитесь завершения текущей передачи, затем повторите подготовку.");return; }
        Intent intent=new Intent(this,SendActivity.class).setAction(Intent.ACTION_SEND_MULTIPLE).setType("*/*");
        preparationTicket=UUID.randomUUID().toString();preparationFiles=new ArrayList<>(files);
        preparationDeadline=SystemClock.elapsedRealtime()+30000;
        intent.putParcelableArrayListExtra(Intent.EXTRA_STREAM,new ArrayList<>(files));intent.putExtra("prepareDropTicket",preparationTicket);
        ClipData clip=ClipData.newRawUri("Galaxy Bridge",files.get(0));
        for(int i=1;i<files.size();i++) clip.addItem(new ClipData.Item(files.get(i)));
        intent.setClipData(clip);intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        try { startActivity(intent);finish(); } // Forward the grant before destroying its original owner.
        catch(RuntimeException e) { preparationTicket="";preparationFiles=null;report.setText("Не удалось передать доступ окну отправки. Повторите сброс."); }
    }
    // SendActivity is exported for Android sharing. Only this in-process user
    // action may automatically prepare files; external extras cannot opt in.
    static boolean consumePreparation(String ticket,ArrayList<Uri> selected) {
        if(ticket==null || !ticket.equals(preparationTicket) || preparationFiles==null || !selected.equals(preparationFiles)) return false;
        boolean fresh=SystemClock.elapsedRealtime()<=preparationDeadline;
        preparationTicket="";preparationFiles=null;return fresh;
    }
    private void cancelProbe() {
        if(!busy) return;
        cancelled.set(true);if(signal!=null) signal.cancel();
        InputStream input=activeInput.getAndSet(null);if(input!=null) try { input.close(); } catch(IOException ignored) { }
        if(!destroyed) { report.setText(cancellationText);cancel.setEnabled(false); }
    }
    private void releasePermissions() {
        if(permissions!=null) { permissions.release();permissions=null; }
    }
    @Override protected void onDestroy() {
        destroyed=true;main.removeCallbacksAndMessages(null);cancelProbe();releasePermissions();super.onDestroy();
    }
}
