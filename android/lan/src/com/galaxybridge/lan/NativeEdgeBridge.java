package com.galaxybridge.lan;

import android.app.KeyguardManager;
import android.content.Context;
import android.graphics.Insets;
import android.graphics.PixelFormat;
import android.graphics.Rect;
import android.hardware.display.DisplayManager;
import android.os.Handler;
import android.os.PowerManager;
import android.os.SystemClock;
import android.provider.Settings;
import android.view.Gravity;
import android.view.InputDevice;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowInsets;
import android.view.WindowManager;
import android.view.WindowMetrics;
import java.util.Locale;
import org.json.JSONObject;

/** Observes real Android mouse hover; it never moves or replaces the system cursor. */
final class NativeEdgeBridge implements DisplayManager.DisplayListener {
    interface Sink { void send(JSONObject message); }
    private final Context context;
    private final Handler main;
    private final DisplayManager displays;
    private WindowManager windows;
    private View strip;
    private Sink sink;
    private int epoch,side,width,height,buttons,displayId=-1;
    private long generation,sequence,heartbeat,layoutStarted;
    private EdgeStrip geometry;
    private float x,y;
    private boolean hovering,ready;
    private final Runnable tick=new Runnable() { public void run() {
        if(epoch==0) return;
        if(!Settings.canDrawOverlays(context)) { fail("permission");return; }
        if(!unlocked()) { fail("locked");return; }
        if(SystemClock.elapsedRealtime()-heartbeat>6000) { fail("timeout");return; }
        if(!ready && SystemClock.elapsedRealtime()-layoutStarted>2000) { fail("layout");return; }
        if(strip==null || !strip.isShown() || strip.getWindowVisibility()!=View.VISIBLE) leave();
        if(ready && hovering && geometry!=null && geometry.contains(x,y)) report();
        main.postDelayed(this,100);
    }};
    NativeEdgeBridge(Context context,Handler main) {
        this.context=context;this.main=main;displays=context.getSystemService(DisplayManager.class);
        displays.registerDisplayListener(this,main);
    }
    void heartbeat() { heartbeat=SystemClock.elapsedRealtime(); }
    void accept(JSONObject command,Sink destination) {
        int id=command.optInt("epoch",0), requestedSide=command.optInt("side",-1);
        if(!command.optBoolean("active",false)) { if(id==epoch) stop();return; }
        if(id<1 || requestedSide<0 || requestedSide>3) return;
        stop();epoch=id;side=requestedSide;sink=destination;heartbeat();
        if(!Settings.canDrawOverlays(context)) { fail("permission");return; }
        if(!unlocked()) { fail("locked");return; }
        install();
        if(epoch!=0) main.post(tick);
    }
    private boolean unlocked() {
        return ControlDisplays.unlocked(context) &&
            !context.getSystemService(KeyguardManager.class).isKeyguardLocked();
    }
    private void install() {
        removeWindow();layoutStarted=SystemClock.elapsedRealtime();final long version=++generation;
        try {
            displayId=ControlDisplays.id(context);if(displayId<0) { fail("display");return; }
            Context windowContext=ControlDisplays.overlay(context,displayId,WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY);
            windows=windowContext.getSystemService(WindowManager.class);
            WindowMetrics metrics=windows.getMaximumWindowMetrics();Rect bounds=metrics.getBounds();
            width=bounds.width();height=bounds.height();
            Insets insets=metrics.getWindowInsets().getInsetsIgnoringVisibility(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());
            EdgeStrip wanted=EdgeStrip.create(width,height,insets.left,insets.top,insets.right,insets.bottom,side);
            View view=new View(windowContext);strip=view;
            WindowManager.LayoutParams params=new WindowManager.LayoutParams(wanted.width,wanted.height,
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE|WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL|
                WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN|WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,
                PixelFormat.TRANSLUCENT);
            params.gravity=Gravity.TOP|Gravity.LEFT;params.x=wanted.x;params.y=wanted.y;
            params.layoutInDisplayCutoutMode=WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_ALWAYS;
            params.alpha=0.01f;params.setTitle("Galaxy Bridge mouse edge");
            view.setOnHoverListener((v,event) -> {
                if(version!=generation || !ready || !event.isFromSource(InputDevice.SOURCE_MOUSE)) return false;
                int action=event.getActionMasked();
                if(action==MotionEvent.ACTION_HOVER_EXIT) { leave();return true; }
                if(action!=MotionEvent.ACTION_HOVER_ENTER && action!=MotionEvent.ACTION_HOVER_MOVE) return false;
                x=event.getRawX();y=event.getRawY();buttons=event.getButtonState()&31;
                if(geometry==null || !geometry.contains(x,y)) { leave();return true; }
                hovering=true;report();return true;
            });
            // A click on the single boundary pixel must not leave a stale hover sample.
            view.setOnTouchListener((v,event) -> { leave();return false; });
            view.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob) -> {
                if(version!=generation || epoch==0) return;
                int[] position=new int[2];v.getLocationOnScreen(position);
                try {
                    EdgeStrip actual=EdgeStrip.create(width,height,position[0],position[1],
                        width-position[0]-v.getWidth(),height-position[1]-v.getHeight(),side);
                    if(actual.x!=position[0] || actual.y!=position[1] || actual.width!=v.getWidth() || actual.height!=v.getHeight())
                        throw new IllegalArgumentException("Unexpected overlay bounds");
                    geometry=actual;hovering=false;
                    if(!ready) { ready=true;ack(true,""); }
                } catch(RuntimeException e) { main.post(() -> { if(version==generation) fail("layout"); }); }
            });
            windows.addView(view,params);
        } catch(RuntimeException e) { fail("layout"); }
    }
    private void report() {
        String line=String.format(Locale.ROOT,"GB_EDGE2 %d %d %d %d %.3f %.3f %d %d %d %d %d",
            epoch,++sequence,width,height,x,y,buttons,geometry.x,geometry.y,geometry.width,geometry.height);
        sink.send(Wire.message("nativeEdge","line",line));
    }
    private void leave() {
        if(hovering && sink!=null) sink.send(Wire.message("nativeEdgeLeave","epoch",epoch,"sequence",++sequence));
        hovering=false;
    }
    private void ack(boolean ok,String reason) { if(sink!=null) sink.send(Wire.message("nativeCaptureAck","epoch",epoch,"ready",ok,"reason",reason)); }
    private void fail(String reason) { ack(false,reason);stop(); }
    private void removeWindow() {
        ready=false;hovering=false;geometry=null;++generation;
        View previous=strip;strip=null;
        if(previous!=null && windows!=null) try { windows.removeViewImmediate(previous); } catch(RuntimeException ignored) { }
        windows=null;
    }
    void stop() { main.removeCallbacks(tick);removeWindow();epoch=0;sequence=0;sink=null; }
    void close() { stop();displays.unregisterDisplayListener(this); }
    @Override public void onDisplayChanged(int id) {
        if(epoch!=0 && id==displayId) { ack(false,"layout");install(); }
    }
    @Override public void onDisplayAdded(int id) {
        if(epoch!=0 && ControlDisplays.id(context)!=displayId) fail("display");
        ConnectionService.reportInputScreen();
    }
    @Override public void onDisplayRemoved(int id) {
        if(id==displayId && epoch!=0) fail("display");ConnectionService.reportInputScreen();
    }
}
