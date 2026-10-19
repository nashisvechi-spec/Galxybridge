package com.galaxybridge.lan;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.content.Context;
import android.content.SharedPreferences;
import android.os.Bundle;
import android.os.Handler;
import android.net.Uri;
import android.provider.Settings;
import android.widget.*;

public final class SetupActivity extends Activity {
    private TextView status,connectionBadge,inputStatus,clipboardStatus,fileStatus,notificationStatus,connectionHelp;
    private Button reconnect,stop,scan;
    private LinearLayout bodyHost;
    private ScrollView activeScroll;
    private final int[] scrollPositions=new int[3];
    private final Button[] tabs=new Button[3];
    private int selectedTab;
    private final Handler handler=new Handler(android.os.Looper.getMainLooper());
    private final Runnable update=new Runnable() { public void run() { refresh();handler.postDelayed(this,1000); } };
    @Override public void onCreate(Bundle saved) {
        BridgeUi.prepare(this);super.onCreate(saved);
        selectedTab=saved==null?0:Math.max(0,Math.min(2,saved.getInt("tab",0)));
        if(saved!=null) for(int i=0;i<3;i++) scrollPositions[i]=Math.max(0,saved.getInt("scroll"+i,0));
        LinearLayout root=BridgeUi.root(this);bodyHost=BridgeUi.column(this);root.addView(bodyHost,new LinearLayout.LayoutParams(-1,0,1));
        LinearLayout nav=new LinearLayout(this);nav.setPadding(BridgeUi.dp(this,12),BridgeUi.dp(this,8),BridgeUi.dp(this,12),BridgeUi.dp(this,8));nav.setBackgroundColor(BridgeUi.palette(this).surface);
        String[] labels={"Связь","Файлы","Настройки"};
        for(int i=0;i<labels.length;i++) {
            final int index=i;Button b=BridgeUi.button(nav,labels[i],false,() -> showTab(index));b.setTextSize(14);b.setPadding(BridgeUi.dp(this,6),BridgeUi.dp(this,10),BridgeUi.dp(this,6),BridgeUi.dp(this,10));
            LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(0,-2,1);lp.setMargins(BridgeUi.dp(this,3),0,BridgeUi.dp(this,3),0);b.setLayoutParams(lp);tabs[i]=b;
        }
        root.addView(nav,new LinearLayout.LayoutParams(-1,-2));BridgeUi.show(this,root);showTab(selectedTab);
        getOnBackInvokedDispatcher().registerOnBackInvokedCallback(android.window.OnBackInvokedDispatcher.PRIORITY_DEFAULT,() -> {
            if(selectedTab!=0) showTab(0);else finish();
        });
    }
    private SharedPreferences prefs() { return getSharedPreferences("lan",MODE_PRIVATE); }
    private void showTab(int index) {
        if(activeScroll!=null) scrollPositions[selectedTab]=activeScroll.getScrollY();
        selectedTab=index;bodyHost.removeAllViews();status=null;connectionBadge=null;inputStatus=null;clipboardStatus=null;fileStatus=null;notificationStatus=null;connectionHelp=null;reconnect=null;stop=null;scan=null;
        for(int i=0;i<tabs.length;i++) BridgeUi.navigation(tabs[i],i==index);
        LinearLayout body=BridgeUi.scrollBody(bodyHost);
        activeScroll=(ScrollView)body.getParent();final ScrollView scroll=activeScroll;final int position=scrollPositions[index];scroll.post(() -> scroll.scrollTo(0,position));
        if(index==0) connection(body);else if(index==1) files(body);else settings(body);refresh();
    }
    private void connection(LinearLayout body) {
        BridgeUi.header(body,"Связь",null,null);
        LinearLayout card=BridgeUi.card(body,prefs().getString("name","Компьютер"));
        connectionBadge=BridgeUi.text(this,"",14,true);card.addView(connectionBadge);
        status=BridgeUi.text(this,"",14,false);status.setPadding(0,BridgeUi.dp(this,12),0,0);card.addView(status);
        connectionHelp=BridgeUi.text(this,"",14,false);card.addView(connectionHelp);
        reconnect=BridgeUi.button(card,"Подключиться к сохранённому ПК",true,this::reconnect);
        scan=BridgeUi.button(card,"Сканировать QR",!paired(),() -> startActivityForResult(new Intent(this,ScanActivity.class),7));
        LinearLayout quick=BridgeUi.card(body,null);
        BridgeUi.button(quick,"Отправить файлы на ПК",false,() -> startActivity(new Intent(this,SendActivity.class)));
        BridgeUi.button(quick,"Текст и ссылки",false,() -> startActivity(new Intent(this,ClipboardActivity.class)));
        BridgeUi.button(body,"Помощь с подключением",false,this::connectionHelp);
    }
    private void files(LinearLayout body) {
        BridgeUi.header(body,"Файлы",null,null);
        LinearLayout send=BridgeUi.card(body,"Телефон → ПК");
        BridgeUi.button(send,"Выбрать файлы",true,() -> startActivity(new Intent(this,SendActivity.class)));
        fileStatus=BridgeUi.text(this,"",14,false);fileStatus.setPadding(0,BridgeUi.dp(this,12),0,0);send.addView(fileStatus);
        send.addView(BridgeUi.text(this,"Поделиться → Galaxy Bridge Wi-Fi — отправка в фоне.",14,false));
        BridgeUi.button(send,"История передач",false,() -> startActivity(new Intent(this,HistoryActivity.class)));
        LinearLayout receive=BridgeUi.card(body,"Полученные с ПК");receive.addView(BridgeUi.text(this,"Файлы сохраняются в Download/GalaxyBridge.",14,false));
        BridgeUi.button(receive,"Открыть папку",false,() -> startActivity(new Intent(this,ReceivedFolderActivity.class)));
        LinearLayout more=BridgeUi.card(body,"Текст и другие способы");
        BridgeUi.button(more,"Передать текст или ссылку",false,() -> startActivity(new Intent(this,ClipboardActivity.class)));
        BridgeUi.button(more,"Приём из «Моих файлов»",false,() -> startActivity(new Intent(this,DropActivity.class)));
        clipboardStatus=BridgeUi.text(this,"",14,false);more.addView(clipboardStatus);
    }
    private void settings(LinearLayout body) {
        BridgeUi.header(body,"Настройки",null,null);
        LinearLayout access=BridgeUi.card(body,"Управление и буфер");
        access.addView(BridgeUi.text(this,"Для передачи файлов эти разрешения не нужны. Специальные возможности включают мышь, разрешение поверх приложений — переходы и общий буфер.",14,false));
        inputStatus=BridgeUi.text(this,"",14,false);access.addView(inputStatus);
        BridgeUi.button(access,"Специальные возможности",false,() -> startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS)));
        clipboardStatus=BridgeUi.text(this,"",14,false);clipboardStatus.setPadding(0,BridgeUi.dp(this,12),0,0);access.addView(clipboardStatus);
        BridgeUi.button(access,"Переходы мыши и общий буфер",false,() -> startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,Uri.parse("package:"+getPackageName()))));
        LinearLayout notifications=BridgeUi.card(body,"Уведомления о файлах");
        notificationStatus=BridgeUi.text(this,"",14,false);notifications.addView(notificationStatus);
        BridgeUi.button(notifications,"Включить звук уведомлений",false,() -> { ReceivedFiles.enableSound(this);refresh(); });
        BridgeUi.button(notifications,"Разрешить уведомления",false,() -> ReceivedFiles.enable(this,false));
        BridgeUi.button(notifications,"Проверить уведомление",false,() -> ReceivedFiles.test(this));
        LinearLayout screen=BridgeUi.card(body,"Экран телефона на ПК");
        screen.addView(BridgeUi.text(this,"На ПК нажмите «Экран телефона на ПК», затем подтвердите показ системным окном Android. Показывается экран телефона; внешний экран DeX управляется прежним способом.",14,false));
        BridgeUi.button(screen,"Разрешить показ экрана",false,() -> startActivity(new Intent(this,ScreenShareActivity.class)));
        LinearLayout appearance=BridgeUi.card(body,"Оформление");Spinner theme=new Spinner(this);
        ArrayAdapter<String> adapter=new ArrayAdapter<>(this,android.R.layout.simple_spinner_item,new String[]{"Как в системе","Светлая","Тёмная"});adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);theme.setAdapter(adapter);
        String current=getSharedPreferences("appearance",MODE_PRIVATE).getString("theme","system");theme.setSelection("dark".equals(current)?2:"light".equals(current)?1:0);
        theme.setMinimumHeight(BridgeUi.dp(this,48));appearance.addView(theme);
        theme.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            public void onNothingSelected(AdapterView<?> parent) { }
            public void onItemSelected(AdapterView<?> parent,android.view.View view,int position,long id) {
                String mode=position==2?"dark":position==1?"light":"system";
                SharedPreferences appearance=getSharedPreferences("appearance",MODE_PRIVATE);
                if(!mode.equals(appearance.getString("theme","system"))) { appearance.edit().putString("theme",mode).apply();recreate(); }
            }
        });
        LinearLayout link=BridgeUi.card(body,"Подключение");status=BridgeUi.text(this,"",14,false);link.addView(status);
        connectionHelp=BridgeUi.text(this,"",14,false);link.addView(connectionHelp);
        reconnect=BridgeUi.button(link,"Подключиться к сохранённому ПК",true,this::reconnect);
        BridgeUi.button(link,"Сканировать QR",false,() -> startActivityForResult(new Intent(this,ScanActivity.class),7));
        stop=BridgeUi.button(link,"Остановить подключение",false,() -> { prefs().edit().putBoolean("enabled",false).commit();stopService(new Intent(this,ConnectionService.class));refresh(); });
        BridgeUi.button(link,"Настройки фоновой работы",false,() -> startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,Uri.parse("package:"+getPackageName()))));
        link.addView(BridgeUi.text(this,"При обрыве проверьте общую Wi-Fi сеть и влияние VPN. Для DeX экран управления выбирается автоматически.",14,false));
        BridgeUi.button(link,"Помощь с подключением",false,this::connectionHelp);
        BridgeUi.button(link,"Проверить подключение",false,() -> Diagnostics.show(this));
        BridgeUi.button(body,"Забыть компьютер",false,() -> new AlertDialog.Builder(this).setTitle("Забыть компьютер?").setMessage("Для следующего подключения потребуется новый QR. Полученные файлы сохранятся.").setNegativeButton("Отмена",null)
            .setPositiveButton("Забыть",(d,w) -> { stopService(new Intent(this,ConnectionService.class));prefs().edit().clear().commit();ConnectionService.status="Компьютер забыт.";showTab(selectedTab); }).show());
        body.addView(BridgeUi.text(this,"Galaxy Bridge · 0.8.4",13,false));
    }
    private boolean paired() { return Wire.hex(prefs().getString("token",""),64); }
    private void reconnect() {
        if(!paired()) { connectionHelp();return; }
        try { startForegroundService(new Intent(this,ConnectionService.class)); }
        catch(SecurityException | IllegalStateException e) {
            ConnectionService.status="Не удалось включить связь. Проверьте разрешения и настройки фоновой работы приложения.";
        }
        refresh();
    }
    private void connectionHelp() {
        new AlertDialog.Builder(this).setTitle("Помощь с подключением")
            .setMessage("1. Подключите ПК и телефон к одной локальной сети Wi-Fi.\n\n2. На ПК откройте Galaxy Bridge → Устройства → Подключить по QR (или Показать новый QR), на телефоне — Сканировать QR. Подтвердите компьютер.\n\n3. Для файлов достаточно подключения. Для курсора включите службу Galaxy Bridge в специальных возможностях.\n\nЕсли связь не появляется: проверьте доступ VPN к локальной сети, брандмауэр Windows и ограничения гостевой Wi-Fi сети.")
            .setPositiveButton("Понятно",null).show();
    }
    private void refresh() {
        BridgeUi.updateText(status,ConnectionService.status);
        boolean connected=ConnectionService.connected();
        boolean enabled=prefs().getBoolean("enabled",false);
        if(status!=null) status.setVisibility((connected && ConnectionService.status.startsWith("Подключён: ") || !paired() && "Подключение выключено.".equals(ConnectionService.status))?android.view.View.GONE:android.view.View.VISIBLE);
        if(connectionBadge!=null) { BridgeUi.updateText(connectionBadge,connected?"Подключено · Wi-Fi":!paired()?"Компьютер не сопряжён":enabled?"Сопряжено · ожидаем связь":"Связь остановлена");connectionBadge.setTextColor(connected?BridgeUi.palette(this).success:BridgeUi.palette(this).muted); }
        BridgeUi.updateText(connectionHelp,!paired()?"На ПК откройте QR в разделе «Устройства», затем нажмите «Сканировать QR». Файлы работают без специальных возможностей.":connected?"Файлы и текст можно передавать. Для мыши проверьте разрешение специальных возможностей.":enabled?"Ищем сохранённый ПК. Проверьте общую Wi-Fi сеть, VPN и запущенное приложение на ПК.":"Сопряжение сохранено. Нажмите «Подключиться к сохранённому ПК». Новый QR не нужен.");
        if(connectionHelp!=null) connectionHelp.setVisibility(connected?android.view.View.GONE:android.view.View.VISIBLE);
        if(reconnect!=null) { reconnect.setEnabled(paired() && !connected);reconnect.setVisibility(paired() && !connected?android.view.View.VISIBLE:android.view.View.GONE); }
        if(scan!=null) scan.setVisibility(paired()?android.view.View.GONE:android.view.View.VISIBLE);
        if(stop!=null) stop.setEnabled(enabled || connected);
        BridgeUi.updateText(inputStatus,"Управление: "+(ControlService.instance==null?"включите специальные возможности":ControlService.ready(this)?"готово":"разблокируйте экран или проверьте экран DeX")+(selectedTab==2?"\n"+ControlDisplays.description(this):"")+(ControlService.controlStatus.isEmpty()?"":"\n"+ControlService.controlStatus));
        BridgeUi.updateText(clipboardStatus,"Поверх других приложений: "+(Settings.canDrawOverlays(this)?"разрешено":"нужно разрешение")+"\n"+ConnectionService.clipboardStatus);
        BridgeUi.updateText(fileStatus,ConnectionService.fileStatus);
        if(fileStatus!=null) fileStatus.setVisibility(ConnectionService.READY_FILES.equals(ConnectionService.fileStatus)?android.view.View.GONE:android.view.View.VISIBLE);
        BridgeUi.updateText(notificationStatus,ReceivedFiles.status(this));
    }
    @Override protected void onSaveInstanceState(Bundle out) {
        out.putInt("tab",selectedTab);if(activeScroll!=null) scrollPositions[selectedTab]=activeScroll.getScrollY();
        for(int i=0;i<3;i++) out.putInt("scroll"+i,scrollPositions[i]);super.onSaveInstanceState(out);
    }
    @Override protected void onResume() { super.onResume();ReceivedFiles.enable(this,true);handler.removeCallbacks(update);handler.post(update); }
    @Override protected void onPause() { handler.removeCallbacks(update); super.onPause(); }
    @Override protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request,result,data);
        if (request != 7 || result != RESULT_OK || data == null) return;
        String raw = data.getStringExtra("qr");
        try {
            Pairing pair = new Pairing(raw);
            if(data.getBooleanExtra("confirmed",false)) {
                if(!data.getBooleanExtra("started",false)) connect(raw);
                else refresh();
                return;
            }
            new AlertDialog.Builder(this).setTitle("Подключить ноутбук?").setMessage(pair.name + "\n" + pair.host + "\n\nОн сможет отправлять файлы и управлять телефоном, пока подключение включено.")
                .setNegativeButton("Отмена",null).setPositiveButton("Подключить",(d,w) -> connect(raw)).show();
        } catch (IllegalArgumentException e) { ConnectionService.status="Это не QR Galaxy Bridge. Покажите новый код на ПК.";refresh(); }
    }
    private void connect(String raw) {
        startPairing(this,raw);refresh();
    }
    static boolean startPairing(Context context,String raw) {
        try {
            Intent intent=new Intent(context,ConnectionService.class);intent.putExtra("qr",raw);
            ConnectionService.status="QR подтверждён. Подключаемся к ноутбуку…";context.startForegroundService(intent);
            return true;
        } catch(RuntimeException e) {
            ConnectionService.status="Не удалось включить подключение: "+e.getClass().getSimpleName()+". Откройте приложение на телефоне и повторите.";
            return false;
        }
    }
}
