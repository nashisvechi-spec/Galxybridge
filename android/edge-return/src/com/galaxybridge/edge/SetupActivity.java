package com.galaxybridge.edge;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.graphics.Insets;
import android.view.WindowInsets;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

public final class SetupActivity extends Activity {
    private TextView state;
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        LinearLayout page = new LinearLayout(this);
        page.setOrientation(LinearLayout.VERTICAL);
        int padding = (int) (24 * getResources().getDisplayMetrics().density);
        page.setPadding(padding, padding, padding, padding);
        page.setOnApplyWindowInsetsListener((view, insets) -> {
            Insets bars = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
            view.setPadding(padding + bars.left, padding + bars.top, padding + bars.right, padding + bars.bottom);
            return insets;
        });
        TextView title = new TextView(this); title.setText("Galaxy Bridge Edge · 0.3.0"); title.setTextSize(24);
        page.addView(title);
        TextView help = new TextView(this);
        help.setText("Этот компонент возвращает курсор с телефона на ноутбук.\n\n"
            + "Разрешите показ поверх других приложений. Затем подключите телефон в Galaxy Bridge на Windows.\n\n"
            + "Узкая зона появляется только при управлении телефоном. Компонент не читает экран, буфер, файлы или уведомления других приложений.");
        help.setTextSize(16); page.addView(help);
        state = new TextView(this); state.setTextSize(17); page.addView(state);
        Button overlay = new Button(this); overlay.setText("Разрешить поверх других приложений");
        overlay.setOnClickListener(v -> startActivity(new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
            Uri.parse("package:" + getPackageName())))); page.addView(overlay);
        if (Build.VERSION.SDK_INT >= 33) {
            Button notifications = new Button(this); notifications.setText("Разрешить уведомление о подключении");
            notifications.setOnClickListener(v -> requestPermissions(new String[] { Manifest.permission.POST_NOTIFICATIONS }, 1));
            page.addView(notifications);
        }
        Button stop = new Button(this); stop.setText("Выключить автовозврат");
        stop.setOnClickListener(v -> { stopService(new Intent(this, EdgeService.class)); update(); }); page.addView(stop);
        setContentView(page);
    }
    @Override public void onResume() { super.onResume(); update(); }
    private void update() {
        if (state != null) state.setText(Settings.canDrawOverlays(this)
            ? "\nРазрешение получено. Можно подключаться с Windows.\n"
            : "\nНужно разрешение на показ поверх других приложений.\n");
    }
}
