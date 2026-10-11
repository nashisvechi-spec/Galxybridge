package com.galaxybridge.lan;

import android.app.Activity;
import android.content.ClipData;
import android.content.Intent;
import android.content.ClipboardManager;
import android.content.res.AssetFileDescriptor;
import android.database.Cursor;
import android.graphics.Insets;
import android.provider.Settings;
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

/** Visible receiver with an explicitly enabled overlay target for global file drags. */
public final class DropActivity extends Activity {
    private static DropActivity edgeOwner;
    private final EdgeFileDrag edgeDrag=new EdgeFileDrag();
    private final FileDropContact edgeContact=new FileDropContact();
    private FileEdgeReceiver edgeReceiver;
    private boolean edgeEnabled,edgeDropSeen;
    private String edgeBatch="";
    private long edgeDropDeadline;
    private String edgeTrace="";
    private boolean locationLogged,rejectedLocationLogged;
    private final Runnable edgeTick=new Runnable() { public void run() {
        if(destroyed || !edgeEnabled) return;
        EdgeFileDrag.Pointer pointer=ControlService.filePointer();
        if(!Settings.canDrawOverlays(DropActivity.this)) { stopEdge("Разрешите «Поверх других приложений» и включите перенос через край снова.");return; }
        if(edgeDrag.waiting()) {
            if(!edgeDropSeen && SystemClock.elapsedRealtime()>edgeDropDeadline) {
                cancelEdge("Android не передал сброс за 3 секунды. Повторите перенос или используйте приёмник в разделении экрана.");
            } else if(!ConnectionService.edgeFilesReady() || !edgeDrag.valid(pointer,SystemClock.elapsedRealtime())) {
                cancelEdge("Перенос отменён: кнопка отпущена, курсор ушёл с края или связь прервалась.");
            } else if(!edgeBatch.isEmpty() && edgeBatch.equals(ConnectionService.dragReadyBatch)) {
                String batch=edgeBatch,line=edgeDrag.consume(pointer,SystemClock.elapsedRealtime());
                edgeBatch="";edgeContact.clear();edgeReceiver.showTarget(false);
                if(line==null || !ConnectionService.startFileDrag(batch,line)) {
                    ConnectionService.discardEdgeFiles(batch);report.setText("Не удалось начать перенос. Перетащите файл к краю ещё раз.");
                } else report.setText("Файл подготовлен. Перенесите курсор в папку Windows и отпустите кнопку.");
                files.clear();releasePermissions();
            } else if(!edgeBatch.isEmpty() && !ConnectionService.uploading()) {
                cancelEdge(ConnectionService.fileStatus);
            }
        } else if(pointer!=null && !edgeReceiver.matches(pointer)) {
            edgeContact.clear();edgeReceiver.close();
            if(!edgeReceiver.install(pointer.side)) { stopEdge("Не удалось перестроить зону. Включите её снова после изменения экрана.");return; }
        } else if(ConnectionService.edgeFilesReady() && edgeContact.ready(pointer,edgeReceiver.bounds(),SystemClock.elapsedRealtime())) {
            EdgeStrip zone=edgeReceiver.bounds();
            // A LOCATION dwell alone is insufficient: wait for the completed
            // accessibility continuation to end inside the measured target.
            if(ControlService.dropFileTouch(pointer,zone) && edgeDrag.begin(pointer,zone,SystemClock.elapsedRealtime())) {
                edgeContact.clear();edgeDropDeadline=SystemClock.elapsedRealtime()+3000;
                edgeLog("Touch up: внутри зоны после подтверждённого LOCATION");
                edgeReceiver.status("Сброс…");report.setText("Ожидаем файл от Android. Держите кнопку мыши.");
            }
        }
        if(!edgeBatch.isEmpty()) report.setText(ConnectionService.fileStatus+"\n\nДержите кнопку мыши зажатой у края до перехода на ПК.");
        main.postDelayed(this,100);
    }};
    private static String preparationTicket="";
    private static ArrayList<Uri> preparationFiles;
    private static long preparationDeadline;
    private final Handler main=new Handler(Looper.getMainLooper());
    private final AtomicReference<InputStream> activeInput=new AtomicReference<>();
    private final ArrayList<Uri> files=new ArrayList<>();
    private TextView target,report,format;
    private Button prepare,cancel,copy;
    private String dropDiagnostic="";
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
        TextView title=new TextView(this);title.setText("Приём из «Моих файлов» · 0.7.21");title.setTextSize(22);page.addView(title);
        report=new TextView(this);report.setTextSize(16);report.setMaxLines(5);page.addView(report);
        report.setText(saved==null?"Ожидаем перетаскивание из файлового приложения.":"Окно было пересоздано. Перетащите файлы ещё раз: временное разрешение на чтение не сохраняется.");
        copy=new Button(this);copy.setText("Скопировать диагностику сброса");copy.setEnabled(false);
        copy.setOnClickListener(v -> { getSystemService(ClipboardManager.class).setPrimaryClip(ClipData.newPlainText("Galaxy Bridge drop",dropDiagnostic));Toast.makeText(this,"Диагностика скопирована",Toast.LENGTH_SHORT).show(); });page.addView(copy);
        Button edgeOn=new Button(this);edgeOn.setText("Включить зону «На ПК» (тест)");edgeOn.setOnClickListener(v -> enableEdge());page.addView(edgeOn);
        LinearLayout actions=new LinearLayout(this);page.addView(actions);
        Button edgeOff=new Button(this);edgeOff.setText("Выключить зону");edgeOff.setOnClickListener(v -> stopEdge("Зона «На ПК» выключена."));actions.addView(edgeOff,new LinearLayout.LayoutParams(0,-2,1));
        Button minimize=new Button(this);minimize.setText("Свернуть окно");minimize.setOnClickListener(v -> { if(edgeEnabled) moveTaskToBack(true); });actions.addView(minimize,new LinearLayout.LayoutParams(0,-2,1));
        target=new TextView(this);target.setText("Перетащите файл сюда\nи отпустите кнопку");target.setTextSize(21);target.setGravity(Gravity.CENTER);
        target.setMinHeight(p*9);target.setPadding(p,p,p,p);target.setBackgroundColor(0xffdcecff);page.addView(target);
        target.setOnDragListener((v,event) -> drag(event));
        prepare=new Button(this);prepare.setText("Подготовить для переноса на ПК");prepare.setEnabled(false);prepare.setOnClickListener(v -> forward());page.addView(prepare);
        cancel=new Button(this);cancel.setText("Отменить проверку");cancel.setEnabled(false);cancel.setOnClickListener(v -> { cancellationText="Проверка отменена. Перетащите файлы ещё раз.";cancelProbe(); });page.addView(cancel);
        format=new TextView(this);format.setTextSize(14);page.addView(format);
        TextView help=new TextView(this);help.setText("Откройте Galaxy Bridge и «Мои файлы» рядом: через меню значка приложения в «Недавних» выберите разделение экрана или всплывающее окно. Зажмите файл, дождитесь начала перетаскивания и перенесите его в синюю область. Сначала проверьте пальцем, затем мышью по Wi-Fi.\n\nЕсли область не реагирует, файловое приложение могло не начать межприложное перетаскивание. Используйте «Поделиться → Galaxy Bridge Wi-Fi».\n\nПосле проверки нажмите кнопку подготовки. Пока идёт подготовка, держите окно отправки открытым. Затем перетащите его синюю карточку в папку Windows. Оригиналы сохраняются.");page.addView(help);
        ScrollView scroll=new ScrollView(this);scroll.addView(page);setContentView(scroll);
        edgeReceiver=new FileEdgeReceiver(this,(event,zone) -> edgeEvent(event,zone));
        if(saved==null && getIntent().getBooleanExtra("edgeMode",false)) enableEdge();
    }
    private void enableEdge() {
        if(!Settings.canDrawOverlays(this)) {
            report.setText("Разрешите «Поверх других приложений», затем нажмите включение переноса снова.");
            startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,Uri.parse("package:"+getPackageName())));return;
        }
        if(!ConnectionService.edgeFilesReady()) { report.setText("Подключите ноутбук и обновите Windows Galaxy Bridge до 0.7.20.");return; }
        if(edgeOwner!=null && edgeOwner!=this) edgeOwner.stopEdge("Полоса переноса включена в другом окне.");
        EdgeFileDrag.Pointer pointer=ControlService.filePointer();
        int side=pointer==null?0:pointer.side;
        if(!edgeReceiver.install(side)) { report.setText("Не удалось открыть полосу переноса. Проверьте разрешение поверх приложений.");return; }
        edgeEnabled=true;edgeOwner=this;main.removeCallbacks(edgeTick);main.post(edgeTick);
        report.setText("Зона включена. Сверните окно и откройте «Мои файлы» на весь экран. При перетаскивании появится «На ПК» внутри края со стороны ноутбука. Наведите туда файл и задержитесь, держа кнопку до перехода в Windows. Затем отпустите в папке.");
        Toast.makeText(this,"Зона «На ПК» включена. Сверните окно и откройте «Мои файлы».",Toast.LENGTH_LONG).show();
    }
    private boolean edgeEvent(DragEvent event,EdgeStrip zone) {
        if(!edgeEnabled) return false;
        switch(event.getAction()) {
            case DragEvent.ACTION_DRAG_STARTED:
                if(busy || edgeDrag.waiting() || ConnectionService.uploading() || !ConnectionService.edgeFilesReady()) return false;
                edgeDropSeen=false;edgeContact.clear();locationLogged=rejectedLocationLogged=false;edgeTrace="Режим: зона «На ПК»\n";
                edgeLog("Drag started: получено");
                if(!edgeReceiver.showTarget(true)) { stopEdge("Не удалось показать зону «На ПК».");return false; }
                report.setText("Тяните файл в «На ПК» и задержитесь внутри зоны, удерживая кнопку мыши.");return true;
            case DragEvent.ACTION_DRAG_ENTERED:
                edgeLog("Target entered: ожидаем LOCATION и задержку");return true;
            case DragEvent.ACTION_DRAG_LOCATION:
                if(edgeDrag.waiting()) return true;
                EdgeFileDrag.Pointer pointer=ControlService.filePointer();
                boolean contact=edgeReceiver.matches(pointer) && edgeContact.location(pointer,edgeReceiver.bounds(),event.getX(),event.getY(),SystemClock.elapsedRealtime());
                edgeReceiver.highlight(contact);
                if(contact && !locationLogged) {
                    locationLogged=true;EdgeStrip measured=edgeReceiver.bounds();
                    edgeLog("Location: внутри зоны "+measured.x+","+measured.y+","+measured.width+","+measured.height+"; capture="+pointer.epoch+"; side="+pointer.side);
                } else if(!contact && !rejectedLocationLogged) {
                    rejectedLocationLogged=true;edgeLog("Location: контакт не подтверждён; bounds="+(edgeReceiver.bounds()!=null)+
                        "; pointer="+(pointer!=null)+"; stroke="+(pointer!=null && pointer.stroke)+"; buttons="+(pointer==null?-1:pointer.buttons));
                }
                return true;
            case DragEvent.ACTION_DRAG_EXITED:
                edgeContact.clear();edgeReceiver.highlight(false);locationLogged=rejectedLocationLogged=false;
                edgeLog("Target exited: задержка сброшена");
                // After touch-up was dispatched, DROP/ENDED decides delivery.
                // An EXIT notification alone must not hide that pending target.
                return true;
            case DragEvent.ACTION_DROP:
                if(zone==null || !zone.contains(zone.x+event.getX(),zone.y+event.getY()) ||
                    !edgeDrag.valid(ControlService.filePointer(),SystemClock.elapsedRealtime())) return false;
                edgeDropSeen=true;
                edgeLog("Drop: получено внутри зоны");edgeReceiver.status("Проверка");
                boolean accepted=accept(event);
                if(!accepted) cancelEdge(report.getText().toString());
                return accepted;
            case DragEvent.ACTION_DRAG_ENDED:
                edgeContact.clear();
                edgeLog("Drag ended: результат Android "+event.getResult());
                if(edgeDrag.waiting() && !edgeDropSeen) cancelEdge("Файловое приложение не передало файл полосе. Используйте приёмник в разделении экрана.");
                if(!edgeDrag.waiting()) {
                    edgeReceiver.showTarget(false);
                    if(!edgeDropSeen) report.setText("Файл не получен зоной «На ПК». Повторите, задержав файл внутри неё, или используйте приёмник в разделении экрана. Диагностику можно скопировать выше.");
                }
                return true;
            default:return true;
        }
    }
    private void edgeLog(String line) {
        if(edgeTrace.length()<4096) edgeTrace+=line+"\n";
        showDiagnostic(edgeDropSeen?dropDiagnostic+line+"\n":edgeTrace);
    }
    static EdgeFileDrag.Pointer edgePointer(EdgeFileDrag.Pointer pointer) {
        DropActivity owner=edgeOwner;
        if(owner==null || !owner.edgeDrag.waiting()) return null;
        EdgeFileDrag.Pointer clamped=owner.edgeDrag.clamp(pointer);
        if(!owner.edgeDrag.valid(clamped,SystemClock.elapsedRealtime())) {
            owner.cancelEdge("Перенос отменён: держите кнопку мыши и оставайтесь у края до подготовки файла.");return pointer;
        }
        return clamped; // Hold at the measured boundary while the PC stages bytes.
    }
    private void cancelEdge(String reason) {
        boolean pending=edgeDrag.waiting();edgeDrag.cancel();edgeContact.clear();
        if(edgeReceiver!=null) edgeReceiver.showTarget(false);
        if(!edgeBatch.isEmpty()) {
            ConnectionService.cancelFiles();ConnectionService.discardEdgeFiles(edgeBatch);edgeBatch="";
        }
        if(pending && busy) { cancellationText=reason;cancelProbe(); }
        if(!busy) releasePermissions();files.clear();prepare.setEnabled(false);
        report.setText(reason);
        if(!edgeTrace.isEmpty()) showDiagnostic(dropDiagnostic+"Завершение: "+reason+"\n");
    }
    private void stopEdge(String reason) {
        edgeEnabled=false;if(edgeOwner==this) edgeOwner=null;main.removeCallbacks(edgeTick);
        cancelEdge(reason);if(edgeReceiver!=null) edgeReceiver.close();
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
        DropPayload payload=new DropPayload();ArrayList<Uri> incoming;
        try { incoming=payload.extract(clip); }
        catch(IOException e) { showDiagnostic((edgeDropSeen && edgeDrag.waiting()?edgeTrace:"")+payload.diagnostic());report.setText(e.getMessage());return false; }
        catch(RuntimeException e) { showDiagnostic((edgeDropSeen && edgeDrag.waiting()?edgeTrace:"")+payload.diagnostic()+"\nОшибка разбора: "+e.getClass().getSimpleName());report.setText("Файловое приложение передало неподдерживаемые данные. Скопируйте диагностику ниже.");return false; }
        showDiagnostic((edgeDropSeen && edgeDrag.waiting()?edgeTrace:"")+payload.diagnostic());
        try { permissions=requestDragAndDropPermissions(event); }
        catch(RuntimeException e) { report.setText("Не удалось получить временное разрешение на чтение. Повторите сброс.");return false; }
        // A null grant is not proof of denial: an existing grant or a public
        // provider may already permit reading. The real read probe decides.
        showDiagnostic(dropDiagnostic+"Drag permission: "+(permissions==null?"нет; проверяем фактический доступ":"получено")+"\n");
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
        return true; // Edge mode prepares after validation; the normal receiver waits for its button.
    }
    private void showDiagnostic(String text) {
        dropDiagnostic="Galaxy Bridge Android 0.7.21\n"+text.replace("Galaxy Bridge Android 0.7.21\n","");
        format.setText("Формат сброса:\n"+dropDiagnostic);copy.setEnabled(true);
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
            showDiagnostic(dropDiagnostic+"Read probe: "+(cancelled.get()?"отменено":"ошибка")+"\n");
            String reason=cancelled.get()?cancellationText:failure+"\nФайлы не отправлены.";
            if(edgeDrag.waiting()) cancelEdge(reason);
            else { releasePermissions();report.setText(reason); }
            return;
        }
        files.addAll(incoming);StringBuilder text=new StringBuilder("Доступ к чтению подтверждён: ").append(files.size()).append(".\n");
        showDiagnostic(dropDiagnostic+"Read probe: подтверждено; файлов "+files.size()+"\n");
        for(DropProbe.Entry entry:entries) text.append("\n").append(entry.name).append(" · ").append(entry.empty?"пустой файл":entry.size<0?"размер неизвестен":entry.size+" байт");
        text.append("\n\nПроверено открытие и чтение первого байта каждого файла. Передача на ПК ещё не началась.");
        report.setText(text);prepare.setEnabled(true);
        if(edgeDrag.waiting()) {
            if(!edgeDrag.valid(ControlService.filePointer(),SystemClock.elapsedRealtime())) { cancelEdge("Кнопка отпущена до завершения проверки. Перетащите файл ещё раз.");return; }
            edgeBatch=UUID.randomUUID().toString().replace("-","");prepare.setEnabled(false);
            if(!ConnectionService.prepareEdgeFiles(files,edgeBatch)) cancelEdge("Не удалось начать подготовку. Проверьте связь и повторите перетаскивание.");
            else { edgeReceiver.status("Приём…");Toast.makeText(this,"Файл получен. Держите кнопку: готовим перенос на ПК…",Toast.LENGTH_LONG).show(); }
        }
    }
    private void forward() {
        if(busy || edgeDrag.waiting() || files.isEmpty()) return;
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
        destroyed=true;stopEdge("Окно приёма закрыто. Перенос через край выключен.");main.removeCallbacksAndMessages(null);cancelProbe();releasePermissions();super.onDestroy();
    }
}
