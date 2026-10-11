package com.galaxybridge.files;

import android.app.*;
import android.content.*;
import android.graphics.Insets;
import android.graphics.Typeface;
import android.net.Uri;
import android.os.*;
import android.provider.Settings;
import android.text.*;
import android.text.format.Formatter;
import android.view.*;
import android.widget.*;
import java.io.*;
import java.util.*;
import java.util.concurrent.*;

/** Standalone file manager; the companion is an optional, separately installed app. */
public final class FilesActivity extends Activity {
    private static final String BRIDGE="com.galaxybridge.lan";
    private static final int INK=0xff132b46,BLUE=0xff1879a7;
    private FileOps ops;
    private File folder;
    private final ArrayList<File> entries=new ArrayList<>(),clipboard=new ArrayList<>();
    private final LinkedHashSet<File> selected=new LinkedHashSet<>();
    private boolean cut,hidden,loading,busy,resumed,bound,dialogOpen,remoteTouch;
    private int loadGeneration;
    private final ExecutorService worker=Executors.newSingleThreadExecutor();
    private final Handler main=new Handler(Looper.getMainLooper());
    private TextView path,summary,status;
    private EditText search;
    private ListView list;
    private Rows adapter;
    private Button paste;
    private Messenger bridge;
    private final FilePointer remotePointer=new FilePointer();
    private final FilePointer nativePointer=new FilePointer();
    private long remoteDownTime;
    private long remoteHold;
    private final Messenger inbox=new Messenger(new Handler(Looper.getMainLooper(),message -> {
        if(!resumed || dialogOpen) return true;
        if(message.what==101) remote(message.getData());
        else if(message.what==102) { status.setText(message.getData().getString("status","")); }
        else if(message.what==103) { remotePointer.cancel();remoteTouch=false; }
        return true;
    }));
    private final ServiceConnection connection=new ServiceConnection() {
        @Override public void onServiceConnected(ComponentName name,IBinder service) { bridge=new Messenger(service);register(); }
        @Override public void onServiceDisconnected(ComponentName name) { bridge=null;remotePointer.cancel();status.setText("Связь с GalaxyBridge прервалась. Файловый менеджер доступен."); }
        @Override public void onBindingDied(ComponentName name) { onServiceDisconnected(name);if(bound) { unbindService(this);bound=false; }if(resumed) bindBridge(); }
    };
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        try { ops=new FileOps(Environment.getExternalStorageDirectory());folder=ops.root;
            String remembered=saved!=null?saved.getString("folder",ops.root.getPath()):getPreferences(MODE_PRIVATE).getString("folder",ops.root.getPath());
            try { File restored=ops.checked(new File(remembered));if(restored.isDirectory()) folder=restored; }catch(IOException ignored) { }
            hidden=saved!=null?saved.getBoolean("hidden"):getPreferences(MODE_PRIVATE).getBoolean("hidden",false);
        } catch(IOException e) { toast(e.getMessage());finish();return; }
        LinearLayout page=new LinearLayout(this);page.setOrientation(LinearLayout.VERTICAL);page.setBackgroundColor(0xfff5f7fa);
        page.setOnApplyWindowInsetsListener((v,insets) -> {
            Insets bars=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout()|WindowInsets.Type.ime());
            v.setPadding(bars.left,bars.top,bars.right,bars.bottom);return insets;
        });
        LinearLayout header=new LinearLayout(this);header.setPadding(dp(16),dp(12),dp(12),dp(12));header.setGravity(Gravity.CENTER_VERTICAL);header.setBackgroundColor(INK);
        TextView title=text("Galaxy Files",24);title.setTextColor(0xffffffff);title.setTypeface(null,Typeface.BOLD);header.addView(title,new LinearLayout.LayoutParams(0,-2,1));
        Button menu=button("⋮",this::mainMenu);menu.setContentDescription("Меню файлового менеджера");header.addView(menu);page.addView(header);
        LinearLayout shortcuts=new LinearLayout(this);
        shortcut(shortcuts,"Память",ops.root);shortcut(shortcuts,"Загрузки",new File(ops.root,"Download"));
        shortcut(shortcuts,"Фото",new File(ops.root,"DCIM"));shortcut(shortcuts,"Документы",new File(ops.root,"Documents"));
        shortcut(shortcuts,"С ПК",new File(ops.root,"Download/GalaxyBridge"));
        page.addView(horizontal(shortcuts));
        LinearLayout navigation=new LinearLayout(this);navigation.setGravity(Gravity.CENTER_VERTICAL);
        navigation.addView(button("↑",this::up));path=text("",14);path.setTextColor(INK);navigation.addView(path,new LinearLayout.LayoutParams(0,-2,1));page.addView(navigation);
        search=new EditText(this);search.setSingleLine(true);search.setHint("Поиск в этой папке");search.setTextSize(16);search.setPadding(dp(16),dp(8),dp(16),dp(8));page.addView(search);
        search.addTextChangedListener(new TextWatcher() { public void beforeTextChanged(CharSequence s,int st,int c,int a){} public void onTextChanged(CharSequence s,int st,int b,int c){ main.removeCallbacks(reloadSearch);main.postDelayed(reloadSearch,200); } public void afterTextChanged(Editable e){} });
        summary=text("",13);summary.setPadding(dp(16),dp(4),dp(16),dp(4));page.addView(summary);
        list=new ListView(this);list.setDividerHeight(dp(1));adapter=new Rows();list.setAdapter(adapter);
        list.setEmptyView(null);list.setOnItemClickListener((parent,v,position,id) -> { if(busy) return;File file=entries.get(position);if(selected.isEmpty()) open(file);else toggle(file); });
        list.setOnItemLongClickListener((parent,v,position,id) -> { if(!busy) context(entries.get(position),v);return true; });
        page.addView(list,new LinearLayout.LayoutParams(-1,0,1));
        LinearLayout actions=new LinearLayout(this);
        actions.addView(button("На ПК",() -> sendToPc()));actions.addView(button("Копия",() -> copySelection(false)));
        actions.addView(button("Перенести",() -> copySelection(true)));paste=button("Вставить",this::paste);actions.addView(paste);
        actions.addView(button("Ещё",this::selectionMenu));page.addView(horizontal(actions));
        status=text("Работает самостоятельно. Для передачи установите GalaxyBridge Wi-Fi и подключите ПК.",13);
        status.setPadding(dp(16),dp(8),dp(16),dp(12));status.setTextColor(BLUE);page.addView(status);
        setContentView(page);updateSelection();
    }
    private final Runnable reloadSearch=() -> reload();
    private int dp(int n) { return Math.round(n*getResources().getDisplayMetrics().density); }
    private TextView text(String value,int size) { TextView view=new TextView(this);view.setText(value);view.setTextSize(size);return view; }
    private Button button(String label,Runnable action) { Button b=new Button(this);b.setText(label);b.setAllCaps(false);b.setTextSize(13);b.setOnClickListener(v -> action.run());return b; }
    private HorizontalScrollView horizontal(LinearLayout row) { HorizontalScrollView view=new HorizontalScrollView(this);view.setHorizontalScrollBarEnabled(false);view.addView(row);return view; }
    private void shortcut(LinearLayout row,String label,File destination) { row.addView(button(label,() -> navigate(destination))); }
    private void toast(String message) { Toast.makeText(this,message==null?"Операция не выполнена.":message,Toast.LENGTH_LONG).show(); }
    private void navigate(File destination) {
        if(busy) return;
        try { destination=ops.checked(destination);if(!destination.isDirectory()) { toast("Папка пока не создана.");return; }folder=destination;selected.clear();search.setText("");reload(); }
        catch(IOException e) { toast(e.getMessage()); }
    }
    private void up() { if(!folder.equals(ops.root)) navigate(folder.getParentFile()); }
    private void reload() {
        if(ops==null || !resumed) return;
        if(!Environment.isExternalStorageManager()) { entries.clear();adapter.notifyDataSetChanged();summary.setText("Нужен доступ к общей памяти — откройте меню ⋮ → Доступ к файлам.");return; }
        final int generation=++loadGeneration;File target=folder;String query=search.getText().toString();boolean showHidden=hidden;
        path.setText(folder.equals(ops.root)?"Внутренняя память":ops.root.toPath().relativize(folder.toPath()).toString());
        loading=true;summary.setText("Загрузка…");
        worker.execute(() -> {
            try { List<File> result=ops.list(target,query,showHidden);main.post(() -> {
                if(generation!=loadGeneration || isDestroyed()) return;loading=false;entries.clear();entries.addAll(result);
                selected.retainAll(entries);adapter.notifyDataSetChanged();updateSelection();
            }); } catch(IOException e) { main.post(() -> { if(generation!=loadGeneration || isDestroyed()) return;loading=false;entries.clear();selected.clear();adapter.notifyDataSetChanged();summary.setText(e.getMessage()); }); }
        });
    }
    private void toggle(File file) { if(!selected.remove(file)) selected.add(file);adapter.notifyDataSetChanged();updateSelection(); }
    private void updateSelection() {
        if(summary!=null && !loading) summary.setText(selected.isEmpty()?(entries.isEmpty()?"Папка пуста или нет совпадений":"Элементов: "+entries.size()):"Выбрано: "+selected.size());
        if(paste!=null) { paste.setEnabled(!clipboard.isEmpty() && !busy);paste.setText(clipboard.isEmpty()?"Вставить":"Вставить ("+clipboard.size()+")"); }
    }
    private ArrayList<File> selection() { if(selected.isEmpty()) toast("Сначала выделите файлы: галочка рядом с именем или один клик мышью.");return new ArrayList<>(selected); }
    private void open(File file) {
        if(busy) return;if(file.isDirectory()) { navigate(file);return; }
        try { Uri uri=SharedFilesProvider.uri(file);Intent intent=new Intent(Intent.ACTION_VIEW).setDataAndType(uri,SharedFilesProvider.mime(file)).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);intent.setClipData(ClipData.newRawUri("Файл",uri));startActivity(intent); }
        catch(ActivityNotFoundException e) { toast("Нет приложения для открытия этого файла."); } catch(IOException | SecurityException e) { toast(e.getMessage()); }
    }
    private ArrayList<Uri> uris(Collection<File> files) throws IOException {
        if(files.isEmpty() || files.size()>100) throw new IOException("Выберите от 1 до 100 файлов.");
        ArrayList<Uri> result=new ArrayList<>();for(File file:files) result.add(SharedFilesProvider.uri(file));return result;
    }
    private Intent shareIntent(ArrayList<Uri> files) {
        Intent intent=new Intent(files.size()==1?Intent.ACTION_SEND:Intent.ACTION_SEND_MULTIPLE).setType("*/*");
        if(files.size()==1) intent.putExtra(Intent.EXTRA_STREAM,files.get(0));else intent.putParcelableArrayListExtra(Intent.EXTRA_STREAM,files);
        ClipData clip=ClipData.newRawUri("Galaxy Files",files.get(0));for(int i=1;i<files.size();i++) clip.addItem(new ClipData.Item(files.get(i)));
        intent.setClipData(clip);return intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
    }
    private void sendToPc() {
        if(busy) return;
        try { ArrayList<Uri> files=uris(selection());Intent intent=shareIntent(files).setComponent(new ComponentName(BRIDGE,BRIDGE+".SendActivity"));startActivity(intent); }
        catch(ActivityNotFoundException e) { toast("Установите GalaxyBridge Wi-Fi из того же комплекта и выполните сопряжение с ПК."); }
        catch(IOException | SecurityException e) { toast(e.getMessage()); }
    }
    private void share() { try { startActivity(Intent.createChooser(shareIntent(uris(selection())),"Поделиться файлами")); } catch(IOException | ActivityNotFoundException e) { toast(e.getMessage()); } }
    private void copySelection(boolean moving) {
        if(busy) return;ArrayList<File> files=selection();if(files.isEmpty()) return;
        clipboard.clear();clipboard.addAll(files);cut=moving;updateSelection();status.setText("Выберите папку назначения и нажмите «Вставить». "+(cut?"Файлы будут перемещены.":"Оригиналы сохранятся."));
    }
    private void paste() {
        if(busy || clipboard.isEmpty()) return;ArrayList<File> files=new ArrayList<>(clipboard);File destination=folder;boolean moving=cut;
        perform(moving?"Перемещение":"Копирование",progress -> {
            int done=0;
            try { for(File file:files) { ops.transfer(file,destination,moving,progress);done++; } }
            catch(IOException e) { throw new IOException("Завершено: "+done+" из "+files.size()+". "+e.getMessage(),e); }
        },() -> { if(moving) clipboard.clear();selected.clear(); });
    }
    private interface Task { void run(FileOps.Progress progress) throws IOException; }
    private void perform(String title,Task task,Runnable success) {
        if(busy) return;busy=true;updateSelection();
        ProgressDialog progress=new ProgressDialog(this);progress.setTitle(title);progress.setMessage("Подождите…");progress.setIndeterminate(true);progress.setCancelable(false);showDialog(progress);
        worker.execute(() -> {
            String error=null;
            try { final long[] shown={0};task.run((name,bytes) -> { long now=SystemClock.elapsedRealtime();if(now-shown[0]>150) { shown[0]=now;main.post(() -> { if(!isDestroyed()) progress.setMessage(name+"\n"+Formatter.formatFileSize(this,bytes)); }); } }); }
            catch(IOException | SecurityException e) { error=e.getMessage(); }
            final String result=error;main.post(() -> {
                busy=false;if(isDestroyed()) return;progress.dismiss();if(result==null) { success.run();status.setText(title+": готово."); }else { status.setText(result);toast(result); }reload();updateSelection();
            });
        });
    }
    private void askName(String title,String initial,java.util.function.Consumer<String> action) {
        EditText input=new EditText(this);input.setSingleLine(true);input.setText(initial);input.selectAll();
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle(title).setView(input).setNegativeButton("Отмена",null).setPositiveButton("Готово",(d,w) -> action.accept(input.getText().toString())).create();showDialog(dialog);
    }
    private void rename(File file) { askName("Переименовать",file.getName(),name -> perform("Переименование",p -> ops.rename(file,name),() -> selected.clear())); }
    private void newFolder() { File target=folder;askName("Новая папка","",name -> perform("Создание папки",p -> ops.mkdir(target,name),() -> {})); }
    private void deleteSelection() {
        ArrayList<File> files=selection();if(files.isEmpty() || busy) return;
        AlertDialog confirm=new AlertDialog.Builder(this).setTitle("Удалить выбранное?").setMessage("Элементов: "+files.size()+". Папки удаляются вместе с содержимым. Корзины пока нет; отменить удаление нельзя.")
            .setNegativeButton("Отмена",null).setPositiveButton("Удалить",(d,w) -> perform("Удаление",p -> { int done=0;try { for(File file:files) { p.update(file.getName(),0);ops.delete(file);done++; } }catch(IOException e) { throw new IOException("Удалено элементов: "+done+". "+e.getMessage(),e); } },() -> selected.clear())).create();showDialog(confirm);
    }
    private void showDialog(Dialog dialog) {
        dialogOpen=true;message(2,null);remotePointer.cancel();
        dialog.setOnDismissListener(d -> { dialogOpen=false;register(); });dialog.show();
    }
    private void context(File file,View anchor) {
        if(!selected.contains(file)) { selected.clear();selected.add(file);adapter.notifyDataSetChanged();updateSelection(); }
        // Use a dialog so Wi-Fi input can fall back to standard accessibility while it is open.
        String[] labels={"Открыть","Отправить на ПК","Копировать","Переместить","Переименовать","Поделиться","Перетащить в Android","Удалить"};
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle(file.getName()).setItems(labels,(d,index) -> {
            // Post after dismiss; prevents a new operation dialog losing its IPC suspension.
            main.post(() -> { switch(index) { case 0:open(file);break;case 1:sendToPc();break;case 2:copySelection(false);break;case 3:copySelection(true);break;case 4:rename(file);break;case 5:share();break;case 6:nativeDrag(anchor,file);break;case 7:deleteSelection();break; } });
        }).create();showDialog(dialog);
    }
    private void mainMenu() {
        String[] labels={"Новая папка","Обновить",hidden?"Скрыть скрытые файлы":"Показать скрытые файлы","Доступ к файлам","Как передавать на ПК"};
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle("Galaxy Files · 0.1.0").setItems(labels,(d,index) -> main.post(() -> {
            switch(index) { case 0:newFolder();break;case 1:reload();break;case 2:hidden=!hidden;reload();break;case 3:permission();break;case 4:help();break; }
        })).create();showDialog(dialog);
    }
    private void selectionMenu() {
        String[] labels={"Выделить всё","Снять выделение","Переименовать","Поделиться","Удалить","Новая папка"};
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle("Действия").setItems(labels,(d,index) -> main.post(() -> {
            switch(index) { case 0:if(!busy) selected.addAll(entries);adapter.notifyDataSetChanged();updateSelection();break;case 1:selected.clear();adapter.notifyDataSetChanged();updateSelection();break;
                case 2:if(selected.size()==1) rename(selected.iterator().next());else toast("Для переименования выберите один элемент.");break;case 3:share();break;case 4:deleteSelection();break;case 5:newFolder();break; }
        })).create();showDialog(dialog);
    }
    private void help() { showDialog(new AlertDialog.Builder(this).setTitle("Телефон → ПК").setMessage("Кнопка «На ПК» отправляет выделенные файлы через GalaxyBridge Wi-Fi.\n\nМышь по Wi-Fi: один клик выделяет, двойной открывает, правый показывает действия. Зажмите файл и тяните к краю со стороны ПК. Дождитесь подготовки, удерживая кнопку, затем отпустите файл в папке Windows.\n\nДля обычной Bluetooth-мыши доступно перетаскивание Android; включите приём файлов у края в GalaxyBridge.\n\nПередача папок на ПК пока не поддерживается. Оригиналы файлов на телефоне сохраняются.").setPositiveButton("Понятно",null).create()); }
    private void permission() {
        try { startActivity(new Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,Uri.parse("package:"+getPackageName()))); }
        catch(ActivityNotFoundException e) { startActivity(new Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION)); }
    }
    private void nativeDrag(View view,File file) {
        try {
            if(!selected.contains(file)) { selected.clear();selected.add(file); }
            ArrayList<Uri> files=uris(selected);ClipData data=ClipData.newRawUri("Galaxy Files",files.get(0));for(int i=1;i<files.size();i++) data.addItem(new ClipData.Item(files.get(i)));
            if(!view.startDragAndDrop(data,new View.DragShadowBuilder(view),null,View.DRAG_FLAG_GLOBAL|View.DRAG_FLAG_GLOBAL_URI_READ)) toast("Не удалось начать перетаскивание.");
        } catch(IOException e) { toast(e.getMessage()); }
    }
    private void register() { if(resumed && !dialogOpen && bridge!=null) message(1,null); }
    private void message(int what,Bundle data) {
        if(bridge==null) return;
        try { Message m=Message.obtain(null,what);m.replyTo=inbox;if(data!=null) m.setData(data);bridge.send(m); }
        catch(RemoteException e) { bridge=null;remotePointer.cancel(); }
    }
    private File under(float x,float y) {
        int[] location=new int[2];list.getLocationOnScreen(location);int index=list.pointToPosition((int)x-location[0],(int)y-location[1]);
        return index>=0 && index<entries.size()?entries.get(index):null;
    }
    private View row(File file) { int index=entries.indexOf(file)-list.getFirstVisiblePosition();return index>=0 && index<list.getChildCount()?list.getChildAt(index):list; }
    private void remote(Bundle data) {
        float x=data.getFloat("x"),y=data.getFloat("y");int next=data.getInt("buttons"),previous=data.getInt("previous"),wheel=data.getInt("wheel");
        if(wheel!=0 && next==0) list.smoothScrollBy(-wheel*dp(64),120);
        if((next&2)!=0 && (previous&2)==0) { File target=under(x,y);if(target!=null && !busy) context(target,row(target));return; }
        if(next>1) { remotePointer.cancel();return; }
        if((next&1)!=0 && (previous&1)==0) {
            remoteHold=data.getLong("hold");
            File target=under(x,y);remotePointer.down(target,x,y);remoteTouch=target==null;
            if(remoteTouch) touch(MotionEvent.ACTION_DOWN,x,y);
        } else if(next==1) {
            if(remoteTouch) touch(MotionEvent.ACTION_MOVE,x,y);
            else if(!busy && remotePointer.move(x,y,ViewConfiguration.get(this).getScaledTouchSlop())) beginRemoteDrag((File)remotePointer.item());
        } else if((previous&1)!=0) {
            if(remoteTouch) { touch(MotionEvent.ACTION_UP,x,y);remoteTouch=false; }
            else { File target=under(x,y);boolean dragging=remotePointer.dragging();int action=remotePointer.up(target,SystemClock.uptimeMillis());
                if(dragging) message(4,null);else if(!busy && target!=null) { if(action==2) open(target);else if(action==1) toggle(target); }
            }
        }
    }
    private void touch(int action,float x,float y) {
        int[] origin=new int[2];getWindow().getDecorView().getLocationOnScreen(origin);
        if(action==MotionEvent.ACTION_DOWN) remoteDownTime=SystemClock.uptimeMillis();
        MotionEvent event=MotionEvent.obtain(remoteDownTime,SystemClock.uptimeMillis(),action,x-origin[0],y-origin[1],0);
        dispatchTouchEvent(event);event.recycle();
    }
    private void beginRemoteDrag(File file) {
        if(file==null) return;
        try {
            if(!selected.contains(file)) { selected.clear();selected.add(file);adapter.notifyDataSetChanged();updateSelection(); }
            ArrayList<Uri> files=uris(selected);for(Uri uri:files) grantUriPermission(BRIDGE,uri,Intent.FLAG_GRANT_READ_URI_PERMISSION);
            Bundle data=new Bundle();data.putParcelableArrayList("files",files);data.putLong("hold",remoteHold);message(3,data);
            status.setText("Подготовка файлов. Удерживайте кнопку и тяните к краю со стороны ПК; дождитесь перехода курсора в Windows.");
        } catch(IOException | SecurityException e) { status.setText(e.getMessage()); }
    }
    private final class Rows extends BaseAdapter {
        @Override public int getCount() { return entries.size(); }
        @Override public Object getItem(int position) { return entries.get(position); }
        @Override public long getItemId(int position) { return position; }
        @Override public View getView(int position,View recycled,android.view.ViewGroup parent) {
            File file=entries.get(position);LinearLayout row=new LinearLayout(FilesActivity.this);row.setGravity(Gravity.CENTER_VERTICAL);row.setPadding(dp(8),dp(8),dp(12),dp(8));
            row.setBackgroundColor(selected.contains(file)?0xffdeeff9:0xffffffff);
            CheckBox check=new CheckBox(FilesActivity.this);check.setChecked(selected.contains(file));check.setContentDescription("Выделить "+file.getName());check.setEnabled(!busy);check.setOnClickListener(v -> toggle(file));row.addView(check);
            TextView icon=text(file.isDirectory()?"▰":"▤",27);icon.setTextColor(file.isDirectory()?BLUE:0xff7c8fa0);icon.setPadding(0,0,dp(12),0);row.addView(icon);
            LinearLayout detail=new LinearLayout(FilesActivity.this);detail.setOrientation(LinearLayout.VERTICAL);TextView name=text(file.getName(),16);name.setTextColor(INK);detail.addView(name);
            String description=file.isDirectory()?"Папка":Formatter.formatFileSize(FilesActivity.this,file.length());
            TextView meta=text(description+" · "+android.text.format.DateFormat.format("dd.MM.yyyy HH:mm",file.lastModified()),12);meta.setTextColor(0xff738294);detail.addView(meta);
            row.addView(detail,new LinearLayout.LayoutParams(0,-2,1));
            // Let ListView own finger taps/scrolling; actual mouse holds start global Android drags.
            row.setOnTouchListener((view,event) -> {
                if(!event.isFromSource(InputDevice.SOURCE_MOUSE) || busy) return false;
                int action=event.getActionMasked();
                if((event.getButtonState()&MotionEvent.BUTTON_SECONDARY)!=0) { context(file,view);return true; }
                if(action==MotionEvent.ACTION_DOWN) { nativePointer.down(file,event.getRawX(),event.getRawY());list.requestDisallowInterceptTouchEvent(true);return true; }
                if(action==MotionEvent.ACTION_MOVE) { if(nativePointer.move(event.getRawX(),event.getRawY(),ViewConfiguration.get(FilesActivity.this).getScaledTouchSlop())) nativeDrag(view,file);return true; }
                if(action==MotionEvent.ACTION_UP) { int click=nativePointer.up(file,SystemClock.uptimeMillis());if(click==2) open(file);else if(click==1) toggle(file);return true; }
                if(action==MotionEvent.ACTION_CANCEL) { nativePointer.cancel();return true; }return false;
            });
            row.setOnGenericMotionListener((v,event) -> { if(event.getActionMasked()==MotionEvent.ACTION_BUTTON_PRESS && event.getActionButton()==MotionEvent.BUTTON_SECONDARY) { context(file,v);return true; }return false; });
            return row;
        }
    }
    private void bindBridge() {
        if(!bound) try { bound=bindService(new Intent().setComponent(new ComponentName(BRIDGE,BRIDGE+".FilesBridgeService")),connection,Context.BIND_AUTO_CREATE); }
        catch(SecurityException e) { status.setText("Для управления мышью обновите GalaxyBridge Wi-Fi из этого же комплекта."); }
    }
    @Override protected void onResume() {
        super.onResume();resumed=true;reload();bindBridge();
        register();
    }
    @Override protected void onPause() { resumed=false;message(2,null);remotePointer.cancel();remoteTouch=false;if(folder!=null) getPreferences(MODE_PRIVATE).edit().putString("folder",folder.getPath()).putBoolean("hidden",hidden).apply();super.onPause(); }
    @Override protected void onSaveInstanceState(Bundle out) { out.putString("folder",folder.getPath());out.putBoolean("hidden",hidden);super.onSaveInstanceState(out); }
    @Override public void onBackPressed() { if(busy) return;if(!selected.isEmpty()) { selected.clear();adapter.notifyDataSetChanged();updateSelection(); }else if(!folder.equals(ops.root)) up();else super.onBackPressed(); }
    @Override protected void onDestroy() { main.removeCallbacks(reloadSearch);++loadGeneration;if(bound) unbindService(connection);worker.shutdown();super.onDestroy(); }
}
