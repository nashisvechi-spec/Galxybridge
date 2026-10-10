package com.galaxybridge.lan;

import android.app.KeyguardManager;
import android.content.*;
import android.graphics.PixelFormat;
import android.os.*;
import android.provider.Settings;
import android.view.*;

/** Reads once on control return, using a briefly focused, transparent window. */
final class ClipboardBridge {
    interface Result { void complete(String text,long timestamp,String status); }
    private final Context context;
    private final Handler handler;
    private final WindowManager windows;
    private final ClipboardManager clipboard;
    private final ClipHistory history=new ClipHistory();
    private View overlay;
    private Runnable timeout;
    private long generation;
    private boolean enabled;
    ClipboardBridge(Context context,Handler handler) {
        this.context=context;this.handler=handler;
        windows=context.getSystemService(WindowManager.class);
        clipboard=context.getSystemService(ClipboardManager.class);
    }
    void configure(boolean value) { enabled=value;if(!value) cancel(); }
    private boolean unlocked() {
        return !context.getSystemService(KeyguardManager.class).isDeviceLocked() && context.getSystemService(PowerManager.class).isInteractive();
    }
    boolean receive(String text) {
        if(!enabled || !unlocked()) return false;
        cancel();
        try {
            clipboard.setPrimaryClip(ClipData.newPlainText("Galaxy Bridge",ClipText.validate(text)));
            history.received(text,System.currentTimeMillis());
            return true;
        } catch(RuntimeException e) { return false; }
    }
    void delivered(long timestamp) { history.delivered(timestamp); }
    void read(Result result) {
        cancel();
        if(!enabled || !unlocked()) { result.complete(null,0,"unavailable");return; }
        if(!Settings.canDrawOverlays(context)) { result.complete(null,0,"permission");return; }
        final long current=generation;
        overlay=new View(context) {
            @Override public void onWindowFocusChanged(boolean focused) {
                super.onWindowFocusChanged(focused);
                if(focused) handler.post(() -> {
                    if(current!=generation || overlay!=this) return;
                    String text=null,status="unavailable";long timestamp=0;
                    try {
                        if(enabled && unlocked() && hasWindowFocus()) {
                            ClipData data=clipboard.getPrimaryClip();
                            if(data==null || data.getItemCount()==0) status="unsupported";
                            else {
                                timestamp=data.getDescription().getTimestamp();
                                text=ClipText.validate(data.getItemAt(0).getText());
                                if(!history.shouldSend(text,timestamp)) { text=null;status="unchanged"; }
                            }
                        }
                    } catch(IllegalArgumentException e) { status="unsupported"; }
                    catch(RuntimeException e) { status="unavailable"; }
                    finally { cancel(); }
                    result.complete(text,timestamp,status);
                });
            }
        };
        overlay.setFocusable(true);overlay.setFocusableInTouchMode(true);
        WindowManager.LayoutParams layout=new WindowManager.LayoutParams(1,1,WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL,PixelFormat.TRANSLUCENT);
        layout.gravity=Gravity.CENTER;
        timeout=() -> { if(current==generation) { cancel();result.complete(null,0,"unavailable"); } };
        try { windows.addView(overlay,layout);overlay.requestFocus();handler.postDelayed(timeout,750); }
        catch(RuntimeException e) { cancel();result.complete(null,0,"unavailable"); }
    }
    void cancel() {
        generation++;
        if(timeout!=null) handler.removeCallbacks(timeout);timeout=null;
        View previous=overlay;overlay=null;
        if(previous!=null) try { windows.removeViewImmediate(previous); } catch(RuntimeException ignored) { }
    }
}
