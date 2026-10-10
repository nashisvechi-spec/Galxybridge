package com.galaxybridge.lan;
import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.content.SharedPreferences;
import android.os.Bundle;
import android.os.Handler;
import android.provider.Settings;
import android.graphics.Insets;
import android.view.WindowInsets;
import android.widget.*;

public final class SetupActivity extends Activity {
    private TextView status;
    private final Handler handler = new Handler(android.os.Looper.getMainLooper());
    private final Runnable update = new Runnable() { public void run() {
        if (status != null) status.setText(ConnectionService.status + "\n\nСпециальные возможности: " + (ControlService.instance != null ? "включены" : "выключены") + "\n\n" + ConnectionService.fileStatus);
        handler.postDelayed(this, 1000);
    }};
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        LinearLayout page = new LinearLayout(this); page.setOrientation(LinearLayout.VERTICAL);
        int p = (int)(20 * getResources().getDisplayMetrics().density); page.setPadding(p,p,p,p);
        page.setOnApplyWindowInsetsListener((v,i) -> { Insets b = i.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout()); v.setPadding(p+b.left,p+b.top,p+b.right,p+b.bottom); return i; });
        TextView title = new TextView(this); title.setText("Galaxy Bridge Wi-Fi · 0.7.2"); title.setTextSize(24); page.addView(title);
        TextView help = new TextView(this); help.setText("Подключение к ноутбуку без отладки.\n\nНа Windows откройте «Wi-Fi без отладки» и покажите QR. Оба устройства подключите к одной локальной сети.\n\nФайлы сохраняются в Download/GalaxyBridge. Для мыши и ввода текста включите службу в специальных возможностях. Приложение получает команды только от сопряжённого ноутбука."); help.setTextSize(16); page.addView(help);
        status = new TextView(this); status.setTextSize(16); page.addView(status);
        button(page,"Сканировать QR", () -> startActivityForResult(new Intent(this, ScanActivity.class), 7));
        button(page,"Отправить на ПК", () -> startActivity(new Intent(this,SendActivity.class)));
        button(page,"Включить специальные возможности", () -> startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS)));
        button(page,"Разрешить уведомления", () -> requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 8));
        button(page,"Подключиться к сохранённому ноутбуку", () -> startForegroundService(new Intent(this, ConnectionService.class)));
        button(page,"Остановить подключение", () -> { prefs().edit().putBoolean("enabled",false).commit(); stopService(new Intent(this, ConnectionService.class)); });
        button(page,"Забыть ноутбук", () -> new AlertDialog.Builder(this).setMessage("Удалить сопряжение с ноутбуком?")
            .setNegativeButton("Отмена",null).setPositiveButton("Забыть",(d,w) -> { stopService(new Intent(this,ConnectionService.class)); prefs().edit().clear().commit(); ConnectionService.status="Ноутбук забыт."; }).show());
        ScrollView scroll = new ScrollView(this); scroll.addView(page); setContentView(scroll);
    }
    private SharedPreferences prefs() { return getSharedPreferences("lan", MODE_PRIVATE); }
    private void button(LinearLayout page, String text, Runnable run) { Button b = new Button(this); b.setText(text); b.setOnClickListener(v -> run.run()); page.addView(b); }
    @Override protected void onResume() { super.onResume(); handler.post(update); }
    @Override protected void onPause() { handler.removeCallbacks(update); super.onPause(); }
    @Override protected void onActivityResult(int request, int result, Intent data) {
        super.onActivityResult(request,result,data);
        if (request != 7 || result != RESULT_OK || data == null) return;
        String raw = data.getStringExtra("qr");
        try {
            Pairing pair = new Pairing(raw);
            new AlertDialog.Builder(this).setTitle("Подключить ноутбук?").setMessage(pair.name + "\n" + pair.host + "\n\nОн сможет отправлять файлы и управлять телефоном, пока подключение включено.")
                .setNegativeButton("Отмена",null).setPositiveButton("Подключить",(d,w) -> {
                    Intent intent = new Intent(this,ConnectionService.class); intent.putExtra("qr",raw); startForegroundService(intent);
                }).show();
        } catch (IllegalArgumentException e) { status.setText("Это не QR Galaxy Bridge. Покажите новый код в режиме без отладки."); }
    }
}
