package com.galaxybridge.files;

import android.app.*;
import android.content.*;
import android.graphics.Insets;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.content.res.ColorStateList;
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
    private static final int INK=0xff202124,BLUE=0xff3979cf;
    private FileOps ops;
    private File folder;
    private final ArrayList<File> entries=new ArrayList<>(),clipboard=new ArrayList<>();
    private final LinkedHashSet<File> selected=new LinkedHashSet<>();
    private boolean cut,hidden,loading,busy,resumed,bound,dialogOpen,remoteTouch;
    private int loadGeneration;
    private boolean home=true,gridMode,selecting;
    private FileKind category;
    private String sectionTitle="Недавние файлы";
    private TextView title,empty;
    private LinearLayout navigation,viewControls,actionBar,recentStrip;
    private ScrollView homeScroll;
    private GridView grid;
    private final ThumbnailLoader thumbnails=new ThumbnailLoader();
    private final Map<View,File> recentViews=new LinkedHashMap<>();
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
        else if(message.what==102) { setStatus(message.getData().getString("status",""));status.setVisibility(View.VISIBLE); }
        else if(message.what==103) { remotePointer.cancel();remoteTouch=false; }
        return true;
    }));
    private final ServiceConnection connection=new ServiceConnection() {
        @Override public void onServiceConnected(ComponentName name,IBinder service) { bridge=new Messenger(service);register(); }
        @Override public void onServiceDisconnected(ComponentName name) { bridge=null;remotePointer.cancel();setStatus("Связь с GalaxyBridge прервалась. Файловый менеджер доступен."); }
        @Override public void onBindingDied(ComponentName name) { onServiceDisconnected(name);if(bound) { unbindService(this);bound=false; }if(resumed) bindBridge(); }
    };
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        try { ops=new FileOps(Environment.getExternalStorageDirectory());folder=ops.root;
            String remembered=saved!=null?saved.getString("folder",ops.root.getPath()):getPreferences(MODE_PRIVATE).getString("folder",ops.root.getPath());
            try { File restored=ops.checked(new File(remembered));if(restored.isDirectory()) folder=restored; }catch(IOException ignored) { }
            hidden=saved!=null?saved.getBoolean("hidden"):getPreferences(MODE_PRIVATE).getBoolean("hidden",false);
        } catch(IOException e) { toast(e.getMessage());finish();return; }
        gridMode=getPreferences(MODE_PRIVATE).getBoolean("grid",false);
        home=saved==null || saved.getBoolean("home",true);
        if(saved!=null && saved.containsKey("category")) {
            String kind=saved.getString("category","");category=kind.isEmpty()?null:FileKind.valueOf(kind);if(!home && saved.getBoolean("virtual",false)) folder=null;sectionTitle=saved.getString("section","Недавние файлы");
        }
        LinearLayout page=new LinearLayout(this);page.setOrientation(LinearLayout.VERTICAL);page.setBackgroundColor(0xfff5f5f5);
        page.setOnApplyWindowInsetsListener((v,insets) -> {
            Insets bars=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout()|WindowInsets.Type.ime());
            v.setPadding(bars.left,bars.top,bars.right,bars.bottom);return insets;
        });
        LinearLayout header=new LinearLayout(this);header.setPadding(dp(20),dp(24),dp(12),dp(14));header.setGravity(Gravity.CENTER_VERTICAL);
        title=text("Galaxy Files",30);title.setTypeface(Typeface.create("sans-serif-medium",Typeface.NORMAL));header.addView(title,new LinearLayout.LayoutParams(0,-2,1));
        ImageButton find=new ImageButton(this);find.setImageResource(android.R.drawable.ic_menu_search);find.setImageTintList(ColorStateList.valueOf(INK));find.setBackgroundColor(0x00000000);find.setContentDescription("Поиск файлов");find.setOnClickListener(v -> {
            if(home) showCategory(null,"Недавние файлы");search.setVisibility(View.VISIBLE);search.requestFocus();
            getSystemService(android.view.inputmethod.InputMethodManager.class).showSoftInput(search,android.view.inputmethod.InputMethodManager.SHOW_IMPLICIT);
        });header.addView(find,new LinearLayout.LayoutParams(dp(48),dp(48)));
        Button menu=button("⋮",this::mainMenu);menu.setTextSize(24);menu.setContentDescription("Меню файлового менеджера");header.addView(menu,new LinearLayout.LayoutParams(dp(48),dp(48)));page.addView(header);
        navigation=new LinearLayout(this);navigation.setGravity(Gravity.CENTER_VERTICAL);navigation.setPadding(dp(12),0,dp(20),dp(4));
        navigation.addView(button("‹",this::up),new LinearLayout.LayoutParams(dp(44),dp(44)));
        path=text("",13);path.setMaxLines(2);path.setTextColor(0xff767676);navigation.addView(path,new LinearLayout.LayoutParams(0,-2,1));page.addView(navigation);
        search=new EditText(this);search.setSingleLine(true);search.setHint("Поиск по имени");search.setTextSize(16);search.setPadding(dp(20),dp(8),dp(20),dp(8));search.setVisibility(View.GONE);page.addView(search);
        search.addTextChangedListener(new TextWatcher() { public void beforeTextChanged(CharSequence s,int st,int c,int a){} public void onTextChanged(CharSequence s,int st,int b,int c){ main.removeCallbacks(reloadSearch);main.postDelayed(reloadSearch,200); } public void afterTextChanged(Editable e){} });
        viewControls=new LinearLayout(this);viewControls.setGravity(Gravity.CENTER_VERTICAL);viewControls.setPadding(dp(20),0,dp(12),dp(6));
        summary=text("",13);summary.setTextColor(0xff777777);viewControls.addView(summary,new LinearLayout.LayoutParams(0,-2,1));
        Button select=button("Выделить",() -> { selecting=true;adapter.notifyDataSetChanged();updateSelection(); });select.setTag("select");viewControls.addView(select);
        Button layout=button("Вид",this::chooseLayout);layout.setTag("view");viewControls.addView(layout);viewControls.addView(button("Порядок",this::chooseSort));page.addView(viewControls);
        FrameLayout stage=new FrameLayout(this);page.addView(stage,new LinearLayout.LayoutParams(-1,0,1));
        list=new ListView(this);list.setDivider(null);list.setPadding(dp(12),0,dp(12),dp(12));list.setClipToPadding(false);adapter=new Rows();list.setAdapter(adapter);
        list.setOnItemClickListener((parent,v,position,id) -> { if(!busy) open(entries.get(position)); });
        list.setOnItemLongClickListener((parent,v,position,id) -> { if(!busy) selectHeld(entries.get(position));return true; });stage.addView(list);
        grid=new GridView(this);grid.setNumColumns(GridView.AUTO_FIT);grid.setColumnWidth(dp(104));grid.setStretchMode(GridView.STRETCH_COLUMN_WIDTH);grid.setHorizontalSpacing(dp(8));grid.setVerticalSpacing(dp(10));grid.setPadding(dp(16),dp(4),dp(16),dp(16));grid.setClipToPadding(false);grid.setAdapter(adapter);
        grid.setOnItemClickListener((parent,v,position,id) -> { if(!busy) open(entries.get(position)); });stage.addView(grid);
        empty=text("",16);empty.setTextColor(0xff777777);empty.setGravity(Gravity.CENTER);empty.setPadding(dp(24),dp(24),dp(24),dp(24));stage.addView(empty,new FrameLayout.LayoutParams(-1,-1));
        homeScroll=new ScrollView(this);homeScroll.setFillViewport(false);homeScroll.setClipToPadding(false);stage.addView(homeScroll);
        LinearLayout landing=new LinearLayout(this);landing.setOrientation(LinearLayout.VERTICAL);landing.setPadding(dp(12),0,dp(12),dp(16));homeScroll.addView(landing);
        LinearLayout recent=card();LinearLayout recentHeading=new LinearLayout(this);recentHeading.setGravity(Gravity.CENTER_VERTICAL);recentHeading.addView(section("Недавние файлы"),new LinearLayout.LayoutParams(0,-2,1));
        recentHeading.addView(button("Все",() -> showCategory(null,"Недавние файлы")));recent.addView(recentHeading);
        recentStrip=new LinearLayout(this);recentStrip.setPadding(0,dp(8),0,dp(4));recent.addView(recentStrip);landing.addView(recent,cardParams());
        LinearLayout categories=card();categories.addView(section("Категории"));
        FileKind[] kinds={FileKind.IMAGE,FileKind.VIDEO,FileKind.AUDIO,FileKind.DOCUMENT,FileKind.DOWNLOAD,FileKind.APK};
        for(int line=0;line<2;line++) { LinearLayout row=new LinearLayout(this);for(int column=0;column<3;column++) {
            FileKind kind=kinds[line*3+column];LinearLayout tile=new LinearLayout(this);tile.setOrientation(LinearLayout.VERTICAL);tile.setGravity(Gravity.CENTER);tile.setPadding(dp(4),dp(16),dp(4),dp(14));
            ImageView icon=new ImageView(this);icon.setImageDrawable(new FileArtwork(kind,true));tile.addView(icon,new LinearLayout.LayoutParams(dp(48),dp(48)));
            TextView label=text(kind.label,12);label.setGravity(Gravity.CENTER);label.setMinHeight(dp(34));label.setMaxLines(2);tile.addView(label,new LinearLayout.LayoutParams(-1,-2));tile.setContentDescription(kind.label);tile.setTag("category:"+kind.name());
            tile.setOnClickListener(v -> { if(kind==FileKind.DOWNLOAD) navigate(new File(ops.root,"Download"));else showCategory(kind,kind.label); });row.addView(tile,new LinearLayout.LayoutParams(0,-2,1));
        }categories.addView(row); }landing.addView(categories,cardParams());
        LinearLayout storage=card();storage.addView(section("Хранилище"));
        LinearLayout location=new LinearLayout(this);location.setGravity(Gravity.CENTER_VERTICAL);location.setPadding(0,dp(16),0,dp(12));location.setTag("storage");location.setContentDescription("Открыть внутреннюю память");
        ImageView folderIcon=new ImageView(this);folderIcon.setImageDrawable(new FileArtwork(FileKind.FOLDER,false));location.addView(folderIcon,new LinearLayout.LayoutParams(dp(40),dp(40)));
        LinearLayout info=new LinearLayout(this);info.setOrientation(LinearLayout.VERTICAL);info.setPadding(dp(12),0,0,0);info.addView(text("Внутренняя память",16));
        try { StatFs space=new StatFs(ops.root.getPath());info.addView(text(Formatter.formatFileSize(this,space.getTotalBytes()-space.getAvailableBytes())+" / "+Formatter.formatFileSize(this,space.getTotalBytes()),12)); }catch(IllegalArgumentException ignored) { info.addView(text("Файлы на телефоне",12)); }
        location.addView(info,new LinearLayout.LayoutParams(0,-2,1));location.addView(text("›",24));location.setOnClickListener(v -> navigate(ops.root));storage.addView(location);
        try { StatFs space=new StatFs(ops.root.getPath());ProgressBar meter=new ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal);meter.setMax(100);meter.setProgress((int)(100d*(space.getTotalBytes()-space.getAvailableBytes())/Math.max(1,space.getTotalBytes())));meter.setProgressTintList(ColorStateList.valueOf(BLUE));meter.setProgressBackgroundTintList(ColorStateList.valueOf(0xffe8e8e8));storage.addView(meter,new LinearLayout.LayoutParams(-1,dp(4))); }catch(IllegalArgumentException ignored) { }
        landing.addView(storage,cardParams());
        LinearLayout bridgeCard=card();bridgeCard.addView(section("GalaxyBridge"));TextView bridgeHint=text("Файлы между телефоном и компьютером",13);bridgeHint.setTextColor(0xff777777);bridgeCard.addView(bridgeHint);
        LinearLayout bridgeActions=new LinearLayout(this);bridgeActions.addView(button("Полученные с ПК",() -> navigate(new File(ops.root,"Download/GalaxyBridge"))),new LinearLayout.LayoutParams(0,dp(48),1));bridgeActions.addView(button("Как передать",this::help),new LinearLayout.LayoutParams(0,dp(48),1));bridgeCard.addView(bridgeActions);landing.addView(bridgeCard,cardParams());
        actionBar=new LinearLayout(this);actionBar.setOrientation(LinearLayout.VERTICAL);actionBar.setBackgroundColor(0xffffffff);
        LinearLayout actions=new LinearLayout(this);actions.addView(button("На ПК",this::sendToPc),new LinearLayout.LayoutParams(0,dp(52),1));actions.addView(button("Перенести",() -> copySelection(true)),new LinearLayout.LayoutParams(0,dp(52),1));actions.addView(button("Копия",() -> copySelection(false)),new LinearLayout.LayoutParams(0,dp(52),1));actions.addView(button("Ещё",this::selectionMenu),new LinearLayout.LayoutParams(0,dp(52),1));actionBar.addView(actions);
        paste=button("Вставить",this::paste);actionBar.addView(paste,new LinearLayout.LayoutParams(-1,dp(48)));page.addView(actionBar);
        status=text("",13);status.setPadding(dp(20),dp(8),dp(20),dp(12));status.setTextColor(BLUE);page.addView(status);status.setVisibility(View.GONE);
        setContentView(page);updateSelection();updateView();
    }
    private LinearLayout card() { LinearLayout card=new LinearLayout(this);card.setOrientation(LinearLayout.VERTICAL);card.setPadding(dp(18),dp(16),dp(18),dp(14));card.setBackground(round(0xffffffff,18));return card; }
    private LinearLayout.LayoutParams cardParams() { LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,-2);p.bottomMargin=dp(12);return p; }
    private GradientDrawable round(int color,int radius) { GradientDrawable d=new GradientDrawable();d.setColor(color);d.setCornerRadius(dp(radius));return d; }
    private TextView section(String label) { TextView v=text(label,18);v.setTypeface(Typeface.create("sans-serif-medium",Typeface.NORMAL));return v; }
    private AbsListView browser() { return gridMode?grid:list; }
    private void updateView() {
        title.setTextSize(home?30:23);title.setSingleLine(true);title.setEllipsize(TextUtils.TruncateAt.END);
        title.setText(home?"Galaxy Files":category!=null?category.label:folder==null?sectionTitle:folder.equals(ops.root)?"Внутренняя память":folder.getName());
        homeScroll.setVisibility(home?View.VISIBLE:View.GONE);navigation.setVisibility(home?View.GONE:View.VISIBLE);viewControls.setVisibility(home?View.GONE:View.VISIBLE);
        list.setVisibility(!home && !gridMode?View.VISIBLE:View.GONE);grid.setVisibility(!home && gridMode?View.VISIBLE:View.GONE);
        empty.setVisibility(!home && entries.isEmpty() && !loading?View.VISIBLE:View.GONE);
    }
    private void showHome() { if(busy) return;home=true;category=null;folder=ops.root;selected.clear();selecting=false;search.setText("");main.removeCallbacks(reloadSearch);search.setVisibility(View.GONE);reload();updateView();updateSelection(); }
    private void showCategory(FileKind kind,String label) { if(busy) return;home=false;category=kind;folder=null;sectionTitle=label;if(kind==null) sorting=1;selected.clear();selecting=false;search.setText("");main.removeCallbacks(reloadSearch);reload();updateView();updateSelection(); }
    private void chooseLayout() { showDialog(new AlertDialog.Builder(this).setTitle("Вид файлов").setSingleChoiceItems(new String[]{"Список","Сетка"},gridMode?1:0,(d,i) -> { gridMode=i==1;getPreferences(MODE_PRIVATE).edit().putBoolean("grid",gridMode).apply();adapter.notifyDataSetChanged();updateView();d.dismiss(); }).create()); }
    private int sorting;
    private void chooseSort() { showDialog(new AlertDialog.Builder(this).setTitle("Сортировка").setSingleChoiceItems(new String[]{"По имени","Сначала новые","По размеру"},sorting,(d,i) -> { sorting=i;sortEntries();adapter.notifyDataSetChanged();d.dismiss(); }).create()); }
    private void sortEntries() { Comparator<File> order=sorting==1?Comparator.comparingLong(File::lastModified).reversed():sorting==2?Comparator.comparingLong(File::length).reversed():Comparator.comparing(File::getName,String.CASE_INSENSITIVE_ORDER);entries.sort(Comparator.comparing((File f)->!f.isDirectory()).thenComparing(order)); }
    private void selectHeld(File file) { if(busy) return;selecting=true;selected.add(file);adapter.notifyDataSetChanged();updateSelection(); }
    private void renderRecent(List<File> files) {
        recentViews.clear();recentStrip.removeAllViews();if(files.isEmpty()) { TextView hint=text(Environment.isExternalStorageManager()?"Пока нет файлов":"Разрешите доступ к файлам в меню ⋮",13);hint.setTextColor(0xff777777);recentStrip.addView(hint);return; }
        for(File file:files) { LinearLayout tile=new LinearLayout(this);tile.setOrientation(LinearLayout.VERTICAL);tile.setGravity(Gravity.CENTER);tile.setPadding(dp(3),0,dp(3),0);
            ImageView preview=new MediaPreview(this,FileKind.of(file));preview.setScaleType(ImageView.ScaleType.CENTER_CROP);preview.setBackground(round(0xfff5f5f5,12));preview.setClipToOutline(true);tile.addView(preview,new LinearLayout.LayoutParams(dp(60),dp(60)));thumbnails.bind(preview,file,dp(96));
            TextView name=text(file.getName(),11);name.setGravity(Gravity.CENTER);name.setMaxLines(2);name.setEllipsize(TextUtils.TruncateAt.END);name.setMinHeight(dp(34));tile.addView(name,new LinearLayout.LayoutParams(-1,-2));tile.setOnClickListener(v -> open(file));tile.setOnLongClickListener(v -> { selectHeld(file);return true; });mouseInput(tile,file,homeScroll);recentViews.put(tile,file);recentStrip.addView(tile,new LinearLayout.LayoutParams(0,-2,1));
        }
    }
    private final Runnable reloadSearch=() -> reload();
    private int dp(int n) { return Math.round(n*getResources().getDisplayMetrics().density); }
    private TextView text(String value,int size) { TextView view=new TextView(this);view.setText(value);view.setTextSize(size);view.setTextColor(INK);return view; }
    private Button button(String label,Runnable action) { Button b=new Button(this);b.setText(label);b.setAllCaps(false);b.setTextSize(12);b.setTextColor(INK);b.setMinWidth(0);b.setMinimumWidth(0);b.setPadding(dp(8),0,dp(8),0);b.setBackgroundColor(0x00000000);b.setOnClickListener(v -> action.run());return b; }
    private void setStatus(String value) { status.setText(value);status.setVisibility(value==null || value.isEmpty()?View.GONE:View.VISIBLE); }
    private void toast(String message) { Toast.makeText(this,message==null?"Операция не выполнена.":message,Toast.LENGTH_LONG).show(); }
    private void navigate(File destination) {
        if(busy) return;
        try { destination=ops.checked(destination);if(!destination.isDirectory()) { toast("Папка пока не создана.");return; }home=false;category=null;folder=destination;selected.clear();selecting=false;search.setText("");main.removeCallbacks(reloadSearch);reload();updateView();updateSelection(); }
        catch(IOException e) { toast(e.getMessage()); }
    }
    private void up() { if(folder==null || folder.equals(ops.root)) showHome();else navigate(folder.getParentFile()); }
    private void reload() {
        if(ops==null || !resumed) return;
        final int generation=++loadGeneration;
        if(!Environment.isExternalStorageManager()) { loading=false;entries.clear();selected.clear();adapter.notifyDataSetChanged();empty.setText("Разрешите доступ к памяти в меню ⋮");renderRecent(Collections.emptyList());updateView();updateSelection();return; }
        File target=folder;FileKind filter=category;boolean landing=home;String query=search.getText().toString();boolean showHidden=hidden;
        path.setText(target==null?"Главная  ›  "+(filter==null?sectionTitle:filter.label):target.equals(ops.root)?"Главная  ›  Внутренняя память":"Внутренняя память  ›  "+ops.root.toPath().relativize(target.toPath()).toString().replace(File.separator,"  ›  "));
        loading=true;summary.setText("Загрузка…");empty.setVisibility(View.GONE);updateView();
        worker.execute(() -> {
            try { List<File> result=landing || target==null?FileCatalog.load(this,ops,filter,query,showHidden,landing?4:2000):ops.list(target,query,showHidden);main.post(() -> {
                if(generation!=loadGeneration || isDestroyed()) return;loading=false;
                if(landing) { entries.clear();entries.addAll(result);selected.retainAll(entries);renderRecent(result);updateView();updateSelection();return; }
                entries.clear();entries.addAll(result);sortEntries();
                selected.retainAll(entries);empty.setText("Папка пуста или нет совпадений");adapter.notifyDataSetChanged();updateSelection();updateView();
            }); } catch(IOException | SecurityException e) { main.post(() -> { if(generation!=loadGeneration || isDestroyed()) return;loading=false;entries.clear();selected.clear();adapter.notifyDataSetChanged();empty.setText(e.getMessage());updateView();updateSelection(); }); }
        });
    }
    private void toggle(File file) { selecting=true;if(!selected.remove(file)) selected.add(file);adapter.notifyDataSetChanged();updateSelection(); }
    private void updateSelection() {
        if(summary!=null && !loading) summary.setText(selected.isEmpty()?(entries.isEmpty()?"Папка пуста или нет совпадений":"Элементов: "+entries.size()):"Выбрано: "+selected.size());
        if(paste!=null) { paste.setEnabled(!clipboard.isEmpty() && !busy && !home && folder!=null);paste.setText((cut?"Переместить сюда":"Копировать сюда")+" ("+clipboard.size()+")");paste.setVisibility(clipboard.isEmpty()?View.GONE:View.VISIBLE); }
        for(Map.Entry<View,File> recent:recentViews.entrySet()) recent.getKey().setBackground(round(selected.contains(recent.getValue())?0xffe6f0ff:0x00000000,12));
        if(actionBar!=null) actionBar.setVisibility(!selected.isEmpty() || !clipboard.isEmpty()?View.VISIBLE:View.GONE);
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
        clipboard.clear();clipboard.addAll(files);cut=moving;updateSelection();setStatus("Выберите папку назначения и нажмите «Вставить». "+(cut?"Файлы будут перемещены.":"Оригиналы сохранятся."));
    }
    private void paste() {
        if(busy || clipboard.isEmpty() || home || folder==null) return;ArrayList<File> files=new ArrayList<>(clipboard);File destination=folder;boolean moving=cut;
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
                busy=false;if(isDestroyed()) return;progress.dismiss();if(result==null) { success.run();setStatus(title+": готово."); }else { setStatus(result);toast(result); }reload();updateSelection();
            });
        });
    }
    private void askName(String title,String initial,java.util.function.Consumer<String> action) {
        EditText input=new EditText(this);input.setSingleLine(true);input.setText(initial);input.selectAll();
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle(title).setView(input).setNegativeButton("Отмена",null).setPositiveButton("Готово",(d,w) -> action.accept(input.getText().toString())).create();showDialog(dialog);
    }
    private void rename(File file) { askName("Переименовать",file.getName(),name -> perform("Переименование",p -> ops.rename(file,name),() -> selected.clear())); }
    private void newFolder() { if(home || folder==null) { toast("Сначала откройте папку во внутренней памяти.");return; }File target=folder;askName("Новая папка","",name -> perform("Создание папки",p -> ops.mkdir(target,name),() -> {})); }
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
        if(!selected.contains(file)) { selecting=true;selected.clear();selected.add(file);adapter.notifyDataSetChanged();updateSelection(); }
        // Use a dialog so Wi-Fi input can fall back to standard accessibility while it is open.
        String[] labels={"Открыть","Отправить на ПК","Копировать","Переместить","Переименовать","Поделиться","Перетащить в Android","Удалить"};
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle(file.getName()).setItems(labels,(d,index) -> {
            // Post after dismiss; prevents a new operation dialog losing its IPC suspension.
            main.post(() -> { switch(index) { case 0:open(file);break;case 1:sendToPc();break;case 2:copySelection(false);break;case 3:copySelection(true);break;case 4:rename(file);break;case 5:share();break;case 6:nativeDrag(anchor,file);break;case 7:deleteSelection();break; } });
        }).create();showDialog(dialog);
    }
    private void mainMenu() {
        String[] labels={"Новая папка","Обновить",hidden?"Скрыть скрытые файлы":"Показать скрытые файлы","Доступ к файлам","Как передавать на ПК"};
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle("Galaxy Files · 0.2.2").setItems(labels,(d,index) -> main.post(() -> {
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
    private void help() { showDialog(new AlertDialog.Builder(this).setTitle("Телефон → ПК").setMessage("Кнопка «На ПК» отправляет выделенные файлы через GalaxyBridge Wi-Fi.\n\nМышь по Wi-Fi: один клик выделяет, двойной открывает, правый показывает действия. В окне Windows включите «Перенос файлов с телефона через край». Зажмите файл и тяните к краю со стороны ПК. Дождитесь подготовки, удерживая кнопку, затем отпустите файл в папке Windows.\n\nДля обычной Bluetooth-мыши доступно перетаскивание Android; включите приём файлов у края в GalaxyBridge.\n\nПередача папок на ПК пока не поддерживается. Оригиналы файлов на телефоне сохраняются.").setPositiveButton("Понятно",null).create()); }
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
        if(home) { for(Map.Entry<View,File> item:recentViews.entrySet()) if(contains(item.getKey(),x,y)) return item.getValue();return null; }int[] location=new int[2];browser().getLocationOnScreen(location);int index=browser().pointToPosition((int)x-location[0],(int)y-location[1]);
        return index>=0 && index<entries.size()?entries.get(index):null;
    }
    private View row(File file) { if(home) { for(Map.Entry<View,File> item:recentViews.entrySet()) if(item.getValue().equals(file)) return item.getKey();return homeScroll; }int index=entries.indexOf(file)-browser().getFirstVisiblePosition();return index>=0 && index<browser().getChildCount()?browser().getChildAt(index):browser(); }
    private void remote(Bundle data) {
        float x=data.getFloat("x"),y=data.getFloat("y");int next=data.getInt("buttons"),previous=data.getInt("previous"),wheel=data.getInt("wheel");
        if(wheel!=0 && next==0) { if(home) homeScroll.smoothScrollBy(0,-wheel*dp(64));else browser().smoothScrollBy(-wheel*dp(64),120); }
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
            if(!selected.contains(file)) { selecting=true;selected.clear();selected.add(file);adapter.notifyDataSetChanged();updateSelection(); }
            ArrayList<Uri> files=uris(selected);for(Uri uri:files) grantUriPermission(BRIDGE,uri,Intent.FLAG_GRANT_READ_URI_PERMISSION);
            Bundle data=new Bundle();data.putParcelableArrayList("files",files);data.putLong("hold",remoteHold);message(3,data);
            setStatus("Подготовка файлов. Удерживайте кнопку и тяните к краю со стороны ПК; дождитесь перехода курсора в Windows.");
        } catch(IOException | SecurityException e) { setStatus(e.getMessage()); }
    }
    private boolean contains(View view,float x,float y) {
        android.graphics.Rect bounds=new android.graphics.Rect();int[] location=new int[2];view.getLocationOnScreen(location);
        if(!view.getLocalVisibleRect(bounds)) return false;bounds.offset(location[0],location[1]);return bounds.contains((int)x,(int)y);
    }
    private void mouseInput(View row,File file,ViewGroup parent) {
        row.setOnTouchListener((view,event) -> {
            if(!event.isFromSource(InputDevice.SOURCE_MOUSE) || busy) return false;
            int action=event.getActionMasked();
            if((event.getButtonState()&MotionEvent.BUTTON_SECONDARY)!=0) { context(file,view);return true; }
            if(action==MotionEvent.ACTION_DOWN) { nativePointer.down(file,event.getRawX(),event.getRawY());parent.requestDisallowInterceptTouchEvent(true);return true; }
            if(action==MotionEvent.ACTION_MOVE) { if(nativePointer.move(event.getRawX(),event.getRawY(),ViewConfiguration.get(this).getScaledTouchSlop())) nativeDrag(view,file);return true; }
            if(action==MotionEvent.ACTION_UP) { int click=nativePointer.up(file,SystemClock.uptimeMillis());if(click==2) open(file);else if(click==1) toggle(file);parent.requestDisallowInterceptTouchEvent(false);return true; }
            if(action==MotionEvent.ACTION_CANCEL) { nativePointer.cancel();parent.requestDisallowInterceptTouchEvent(false);return true; }return false;
        });
        row.setOnGenericMotionListener((v,event) -> { if(event.getActionMasked()==MotionEvent.ACTION_BUTTON_PRESS && event.getActionButton()==MotionEvent.BUTTON_SECONDARY) { context(file,v);return true; }return false; });
    }
    private final class Rows extends BaseAdapter {
        @Override public int getCount() { return entries.size(); }
        @Override public Object getItem(int position) { return entries.get(position); }
        @Override public long getItemId(int position) { return position; }
        @Override public View getView(int position,View recycled,ViewGroup parent) {
            File file=entries.get(position);boolean tiles=parent==grid;
            LinearLayout row=new LinearLayout(FilesActivity.this);row.setOrientation(tiles?LinearLayout.VERTICAL:LinearLayout.HORIZONTAL);row.setGravity(tiles?Gravity.CENTER:Gravity.CENTER_VERTICAL);row.setPadding(dp(tiles?8:10),dp(tiles?8:9),dp(tiles?8:14),dp(tiles?8:9));
            row.setDescendantFocusability(ViewGroup.FOCUS_BLOCK_DESCENDANTS);row.setFocusable(false);row.setTag(file);
            row.setOnClickListener(v -> { if(!busy) open(file); });row.setOnLongClickListener(v -> { if(!busy) selectHeld(file);return true; });
            row.setBackground(round(selected.contains(file)?0xffe6f0ff:0xffffffff,tiles?14:10));
            CheckBox check=new CheckBox(FilesActivity.this);check.setFocusable(false);check.setFocusableInTouchMode(false);check.setButtonTintList(ColorStateList.valueOf(BLUE));check.setChecked(selected.contains(file));check.setContentDescription("Выделить "+file.getName());check.setEnabled(!busy);check.setVisibility(selecting?View.VISIBLE:View.GONE);check.setOnClickListener(v -> { nativePointer.cancel();toggle(file); });row.addView(check,new LinearLayout.LayoutParams(dp(32),dp(32)));
            ImageView preview=new MediaPreview(FilesActivity.this,FileKind.of(file));preview.setScaleType(ImageView.ScaleType.CENTER_CROP);preview.setBackground(round(0xfff5f5f5,10));preview.setClipToOutline(true);
            LinearLayout.LayoutParams art=new LinearLayout.LayoutParams(dp(tiles?84:44),dp(tiles?84:44));if(!tiles) art.rightMargin=dp(14);else art.bottomMargin=dp(8);row.addView(preview,art);thumbnails.bind(preview,file,dp(tiles?144:80));
            LinearLayout detail=new LinearLayout(FilesActivity.this);detail.setOrientation(LinearLayout.VERTICAL);
            TextView name=text(file.getName(),tiles?13:16);name.setMaxLines(tiles?2:1);name.setEllipsize(TextUtils.TruncateAt.END);if(tiles) { name.setGravity(Gravity.CENTER);name.setMinHeight(dp(34)); }detail.addView(name,new LinearLayout.LayoutParams(-1,-2));
            String description=file.isDirectory()?"Папка":Formatter.formatFileSize(FilesActivity.this,file.length());
            TextView meta=text(tiles?description:android.text.format.DateFormat.format("dd.MM.yyyy HH:mm",file.lastModified())+"  ·  "+description,11);meta.setTextColor(0xff898989);if(tiles) meta.setGravity(Gravity.CENTER);detail.addView(meta,new LinearLayout.LayoutParams(-1,-2));
            row.addView(detail,tiles?new LinearLayout.LayoutParams(-1,-2):new LinearLayout.LayoutParams(0,-2,1));
            mouseInput(row,file,parent);return row;
        }
    }
    private void bindBridge() {
        if(!bound) try { bound=bindService(new Intent().setComponent(new ComponentName(BRIDGE,BRIDGE+".FilesBridgeService")),connection,Context.BIND_AUTO_CREATE); }
        catch(SecurityException e) { setStatus("Для управления мышью обновите GalaxyBridge Wi-Fi из этого же комплекта."); }
    }
    @Override protected void onResume() {
        super.onResume();resumed=true;reload();bindBridge();
        register();
    }
    @Override protected void onPause() { resumed=false;message(2,null);remotePointer.cancel();remoteTouch=false;if(folder!=null) getPreferences(MODE_PRIVATE).edit().putString("folder",folder.getPath()).putBoolean("hidden",hidden).apply();super.onPause(); }
    @Override protected void onSaveInstanceState(Bundle out) { out.putString("folder",folder==null?ops.root.getPath():folder.getPath());out.putBoolean("home",home);out.putBoolean("virtual",folder==null);out.putString("category",category==null?"":category.name());out.putString("section",sectionTitle);out.putBoolean("hidden",hidden);super.onSaveInstanceState(out); }
    @Override public void onBackPressed() { if(busy) return;if(!selected.isEmpty()) { selected.clear();selecting=false;adapter.notifyDataSetChanged();updateSelection(); }else if(selecting) { selecting=false;adapter.notifyDataSetChanged(); }else if(!home) up();else super.onBackPressed(); }
    @Override protected void onDestroy() { thumbnails.close();main.removeCallbacks(reloadSearch);++loadGeneration;if(bound) unbindService(connection);worker.shutdown();super.onDestroy(); }
}
