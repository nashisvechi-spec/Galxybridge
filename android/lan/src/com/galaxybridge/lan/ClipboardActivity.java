package com.galaxybridge.lan;

import android.app.Activity;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.graphics.Insets;
import android.os.*;
import android.view.WindowInsets;
import android.widget.*;

/** Clipboard is read only while this user-opened window has focus. */
public final class ClipboardActivity extends Activity {
    private final Handler handler=new Handler(Looper.getMainLooper());
    private EditText text;
    private TextView progress;
    private Button send;
    private boolean readOnFocus;
    private final Runnable update=new Runnable() { public void run() {
        progress.setText(ConnectionService.status+"\n\n"+ConnectionService.clipboardStatus);
        send.setEnabled(!ConnectionService.sendingClipboard());handler.postDelayed(this,300);
    }};
    @Override public void onCreate(Bundle state) {
        BridgeUi.prepare(this);super.onCreate(state);
        LinearLayout root=BridgeUi.root(this);LinearLayout page=BridgeUi.scrollBody(root);
        BridgeUi.header(page,"Текст и ссылки","Ноутбук: "+getSharedPreferences("lan",MODE_PRIVATE).getString("name","не сопряжён"),this::finish);
        LinearLayout draft=BridgeUi.card(page,"Подготовьте текст");
        draft.addView(BridgeUi.text(this,"Проверьте текст или ссылку. После подтверждения доставки вставьте на ПК через Ctrl+V.",14,false));
        text=new EditText(this);text.setHint("Текст или ссылка");text.setTextColor(BridgeUi.palette(this).text);text.setHintTextColor(BridgeUi.palette(this).muted);text.setTextSize(16);text.setMinLines(3);text.setMaxLines(8);
        text.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE);draft.addView(text);
        BridgeUi.button(draft,"Вставить из буфера телефона",false,this::readClipboard);
        send=BridgeUi.button(draft,"Отправить в буфер ПК",true,() -> {
            try { ConnectionService.sendClipboard(ClipText.validate(text.getText())); }
            catch(IllegalArgumentException e) { ConnectionService.clipboardStatus=e.getMessage(); }
            progress.setText(ConnectionService.status+"\n\n"+ConnectionService.clipboardStatus);
        });
        LinearLayout activity=BridgeUi.card(page,"Результат");progress=BridgeUi.text(this,"",16,false);activity.addView(progress);
        page.addView(BridgeUi.text(this,"На ПК включите общий текстовый буфер. Приложения должны быть подключены по Wi-Fi.",13,false));BridgeUi.show(this,root);
        String shared=getIntent().getStringExtra("sharedText");
        if(state!=null) { text.setText(state.getString("text",""));readOnFocus=false; }
        else if(shared!=null) { text.setText(shared);readOnFocus=false; }
        else readOnFocus=true;
    }
    @Override public void onWindowFocusChanged(boolean focus) {
        super.onWindowFocusChanged(focus);
        if(focus && readOnFocus) { readOnFocus=false;readClipboard(); }
    }
    private void readClipboard() {
        if(!hasWindowFocus()) { readOnFocus=true;return; }
        try {
            ClipboardManager clipboard=getSystemService(ClipboardManager.class);
            ClipData clip=clipboard.getPrimaryClip();
            if(clip==null || clip.getItemCount()==0) throw new IllegalArgumentException("Буфер пуст или недоступен. Вставьте текст в поле вручную.");
            ClipData.Item item=clip.getItemAt(0);CharSequence value=item.getText();
            if(value==null && item.getUri()!=null && ("https".equals(item.getUri().getScheme()) || "http".equals(item.getUri().getScheme()))) value=item.getUri().toString();
            text.setText(ClipText.validate(value));
            ConnectionService.clipboardStatus="Текст подготовлен. Нажмите «Отправить в буфер ПК».";
        } catch(IllegalArgumentException | SecurityException e) {
            ConnectionService.clipboardStatus=e instanceof IllegalArgumentException?e.getMessage():"Буфер недоступен. Вставьте текст в поле вручную.";
        }
        progress.setText(ConnectionService.status+"\n\n"+ConnectionService.clipboardStatus);
    }
    @Override protected void onSaveInstanceState(Bundle state) { state.putString("text",text.getText().toString());super.onSaveInstanceState(state); }
    @Override protected void onResume() { super.onResume();handler.post(update); }
    @Override protected void onPause() { handler.removeCallbacks(update);super.onPause(); }
}
