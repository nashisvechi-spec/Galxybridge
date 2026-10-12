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
    private TextView status,connectionBadge,inputStatus,clipboardStatus,fileStatus,notificationStatus;
    private BridgeUi.Devices diagram;
    private LinearLayout bodyHost;
    private final Button[] tabs=new Button[3];
    private int selectedTab;
    private final Handler handler=new Handler(android.os.Looper.getMainLooper());
    private final Runnable update=new Runnable() { public void run() { refresh();handler.postDelayed(this,1000); } };
    @Override public void onCreate(Bundle saved) {
        BridgeUi.prepare(this);super.onCreate(saved);
        selectedTab=saved==null?0:Math.max(0,Math.min(2,saved.getInt("tab",0)));
        LinearLayout root=BridgeUi.root(this);bodyHost=BridgeUi.column(this);root.addView(bodyHost,new LinearLayout.LayoutParams(-1,0,1));
        LinearLayout nav=new LinearLayout(this);nav.setPadding(BridgeUi.dp(this,12),BridgeUi.dp(this,8),BridgeUi.dp(this,12),BridgeUi.dp(this,8));nav.setBackgroundColor(BridgeUi.palette(this).surface);
        String[] labels={"Связь","Файлы","Настройки"};
        for(int i=0;i<labels.length;i++) {
            final int index=i;Button b=BridgeUi.button(nav,labels[i],false,() -> showTab(index));b.setTextSize(14);b.setPadding(BridgeUi.dp(this,6),BridgeUi.dp(this,10),BridgeUi.dp(this,6),BridgeUi.dp(this,10));
            LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(0,-2,1);lp.setMargins(BridgeUi.dp(this,3),0,BridgeUi.dp(this,3),0);b.setLayoutParams(lp);tabs[i]=b;
        }
        root.addView(nav,new LinearLayout.LayoutParams(-1,-2));BridgeUi.show(this,root);showTab(selectedTab);
    }
    private SharedPreferences prefs() { return getSharedPreferences("lan",MODE_PRIVATE); }
    private void showTab(int index) {
        selectedTab=index;bodyHost.removeAllViews();status=null;connectionBadge=null;inputStatus=null;clipboardStatus=null;fileStatus=null;notificationStatus=null;diagram=null;
        for(int i=0;i<tabs.length;i++) { tabs[i].setSelected(i==index);tabs[i].setStateDescription(i==index?"Выбрано":"");tabs[i].setBackground(BridgeUi.shape(this,i==index?BridgeUi.palette(this).selected:BridgeUi.palette(this).surface,0,12)); }
        LinearLayout body=BridgeUi.scrollBody(bodyHost);
        if(index==0) connection(body);else if(index==1) files(body);else settings(body);refresh();
    }
    private void connection(LinearLayout body) {
        BridgeUi.header(body,"Всё рядом.","Ваш телефон и компьютер — в одной связи.",null);
        LinearLayout card=BridgeUi.card(body,prefs().getString("name","Ваш компьютер"));
        connectionBadge=BridgeUi.text(this,"",14,true);card.addView(connectionBadge);
        diagram=new BridgeUi.Devices(this);card.addView(diagram,new LinearLayout.LayoutParams(-1,BridgeUi.dp(this,146)));
        status=BridgeUi.text(this,"",14,false);status.setPadding(0,BridgeUi.dp(this,12),0,0);card.addView(status);
        BridgeUi.button(card,"Сканировать QR",false,() -> startActivityForResult(new Intent(this,ScanActivity.class),7));
        LinearLayout quick=BridgeUi.card(body,"Всё под рукой");
        BridgeUi.button(quick,"Отправить файлы на ПК",true,() -> startActivity(new Intent(this,SendActivity.class)));
        BridgeUi.button(quick,"Текст и ссылки",false,() -> startActivity(new Intent(this,ClipboardActivity.class)));
        inputStatus=BridgeUi.text(this,"",14,false);inputStatus.setPadding(0,BridgeUi.dp(this,14),0,0);quick.addView(inputStatus);
        BridgeUi.button(quick,"Настроить управление",false,() -> showTab(2));
        LinearLayout trust=BridgeUi.card(body,null);trust.addView(BridgeUi.text(this,"Управляет только сопряжённый компьютер. Отладка не нужна.",14,false));
        BridgeUi.button(body,"Подключиться к сохранённому ПК",false,() -> startForegroundService(new Intent(this,ConnectionService.class)));
    }
    private void files(LinearLayout body) {
        BridgeUi.header(body,"Файлы","Получатель: "+prefs().getString("name","компьютер не сопряжён"),null);
        LinearLayout send=BridgeUi.card(body,"Телефон → ПК");
        BridgeUi.button(send,"Выбрать файлы",true,() -> startActivity(new Intent(this,SendActivity.class)));
        fileStatus=BridgeUi.text(this,"",14,false);fileStatus.setPadding(0,BridgeUi.dp(this,12),0,0);send.addView(fileStatus);
        send.addView(BridgeUi.text(this,"Из Галереи или «Моих файлов»: Поделиться → Galaxy Bridge Wi-Fi. Отправка проходит в фоне.",14,false));
        LinearLayout receive=BridgeUi.card(body,"Полученные с ПК");receive.addView(BridgeUi.text(this,"Файлы сохраняются в Download/GalaxyBridge.",14,false));
        BridgeUi.button(receive,"Открыть папку",false,() -> startActivity(new Intent(this,ReceivedFolderActivity.class)));
        LinearLayout more=BridgeUi.card(body,"Текст и другие способы");
        BridgeUi.button(more,"Передать текст или ссылку",false,() -> startActivity(new Intent(this,ClipboardActivity.class)));
        BridgeUi.button(more,"Приём из «Моих файлов»",false,() -> startActivity(new Intent(this,DropActivity.class)));
        clipboardStatus=BridgeUi.text(this,"",14,false);more.addView(clipboardStatus);
        body.addView(BridgeUi.text(this,"До 100 выбранных файлов, до 2 ГБ каждый. Без папок и доступа ко всему хранилищу.",13,false));
    }
    private void settings(LinearLayout body) {
        BridgeUi.header(body,"Настройки","Подключение и доступ",null);
        LinearLayout access=BridgeUi.card(body,"Разрешения");
        inputStatus=BridgeUi.text(this,"",14,false);access.addView(inputStatus);
        BridgeUi.button(access,"Специальные возможности",false,() -> startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS)));
        clipboardStatus=BridgeUi.text(this,"",14,false);clipboardStatus.setPadding(0,BridgeUi.dp(this,12),0,0);access.addView(clipboardStatus);
        BridgeUi.button(access,"Переходы мыши и общий буфер",false,() -> startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,Uri.parse("package:"+getPackageName()))));
        notificationStatus=BridgeUi.text(this,"",14,false);access.addView(notificationStatus);
        BridgeUi.button(access,"Разрешить уведомления",false,() -> ReceivedFiles.enable(this,false));
        BridgeUi.button(access,"Проверить уведомление",false,() -> ReceivedFiles.test(this));
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
        appearance.addView(BridgeUi.text(this,"Синий — действие. Бирюзовый — связь.",14,false));
        LinearLayout link=BridgeUi.card(body,"Подключение");status=BridgeUi.text(this,"",14,false);link.addView(status);
        BridgeUi.button(link,"Подключиться к сохранённому ПК",true,() -> startForegroundService(new Intent(this,ConnectionService.class)));
        BridgeUi.button(link,"Остановить подключение",false,() -> { prefs().edit().putBoolean("enabled",false).commit();stopService(new Intent(this,ConnectionService.class)); });
        BridgeUi.button(link,"Настройки фоновой работы",false,() -> startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,Uri.parse("package:"+getPackageName()))));
        link.addView(BridgeUi.text(this,"При обрыве проверьте общую Wi-Fi сеть и влияние VPN. Для DeX экран управления выбирается автоматически.",14,false));
        BridgeUi.button(body,"Забыть компьютер",false,() -> new AlertDialog.Builder(this).setMessage("Удалить сопряжение с ноутбуком?").setNegativeButton("Отмена",null)
            .setPositiveButton("Забыть",(d,w) -> { stopService(new Intent(this,ConnectionService.class));prefs().edit().clear().commit();ConnectionService.status="Ноутбук забыт.";showTab(selectedTab); }).show());
        body.addView(BridgeUi.text(this,"Galaxy Bridge · 0.7.32",13,false));
    }
    private void refresh() {
        if(status!=null) status.setText(ConnectionService.status);
        boolean connected=ConnectionService.connected();
        if(connectionBadge!=null) { connectionBadge.setText(connected?"Подключено · Wi-Fi":Wire.hex(prefs().getString("token",""),64)?"Сопряжено · ожидаем связь":"Компьютер не сопряжён");connectionBadge.setTextColor(connected?BridgeUi.palette(this).success:BridgeUi.palette(this).muted); }
        if(diagram!=null && diagram.connected!=connected) { diagram.connected=connected;diagram.invalidate(); }
        if(inputStatus!=null) inputStatus.setText("Управление: "+(ControlService.instance==null?"включите специальные возможности":ControlService.ready(this)?"готово":"разблокируйте экран или проверьте экран DeX")+"\n"+ControlDisplays.description(this)+(ControlService.controlStatus.isEmpty()?"":"\n"+ControlService.controlStatus));
        if(clipboardStatus!=null) clipboardStatus.setText("Поверх других приложений: "+(Settings.canDrawOverlays(this)?"разрешено":"нужно разрешение")+"\n"+ConnectionService.clipboardStatus);
        if(fileStatus!=null) fileStatus.setText(ConnectionService.fileStatus);
        if(notificationStatus!=null) notificationStatus.setText(ReceivedFiles.status(this));
    }
    @Override protected void onSaveInstanceState(Bundle out) { out.putInt("tab",selectedTab);super.onSaveInstanceState(out); }
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
                else status.setText(ConnectionService.status);
                return;
            }
            new AlertDialog.Builder(this).setTitle("Подключить ноутбук?").setMessage(pair.name + "\n" + pair.host + "\n\nОн сможет отправлять файлы и управлять телефоном, пока подключение включено.")
                .setNegativeButton("Отмена",null).setPositiveButton("Подключить",(d,w) -> connect(raw)).show();
        } catch (IllegalArgumentException e) { ConnectionService.status="Это не QR Galaxy Bridge. Покажите новый код в режиме без отладки.";status.setText(ConnectionService.status); }
    }
    private void connect(String raw) {
        startPairing(this,raw);status.setText(ConnectionService.status);
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
