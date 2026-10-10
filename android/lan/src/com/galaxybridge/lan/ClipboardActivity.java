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
        super.onCreate(state);
        LinearLayout page=new LinearLayout(this);page.setOrientation(LinearLayout.VERTICAL);
        int p=(int)(20*getResources().getDisplayMetrics().density);page.setPadding(p,p,p,p);
        page.setOnApplyWindowInsetsListener((v,i) -> { Insets b=i.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());v.setPadding(p+b.left,p+b.top,p+b.right,p+b.bottom);return i; });
        TextView title=new TextView(this);title.setText("Буфер → ПК · 0.7.6");title.setTextSize(24);page.addView(title);
        TextView help=new TextView(this);help.setText("Ноутбук: "+getSharedPreferences("lan",MODE_PRIVATE).getString("name","не сопряжён")+"\n\nПроверьте текст или ссылку и нажмите «Отправить». После подтверждения доставки вставьте на ПК через Ctrl+V. Galaxy Bridge на ПК должен быть подключён, а общий текстовый буфер включён.");page.addView(help);
        text=new EditText(this);text.setHint("Текст или ссылка");text.setMinLines(3);text.setMaxLines(8);text.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE);page.addView(text);
        Button read=new Button(this);read.setText("Вставить из буфера телефона");read.setOnClickListener(v -> readClipboard());page.addView(read);
        send=new Button(this);send.setText("Отправить в буфер ПК");send.setOnClickListener(v -> {
            try { ConnectionService.sendClipboard(ClipText.validate(text.getText())); }
            catch(IllegalArgumentException e) { ConnectionService.clipboardStatus=e.getMessage(); }
            progress.setText(ConnectionService.status+"\n\n"+ConnectionService.clipboardStatus);
        });page.addView(send);
        progress=new TextView(this);progress.setTextSize(16);page.addView(progress);
        ScrollView scroll=new ScrollView(this);scroll.addView(page);setContentView(scroll);
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
