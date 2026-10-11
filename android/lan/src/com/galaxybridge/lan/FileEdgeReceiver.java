package com.galaxybridge.lan;

import android.app.Activity;
import android.content.Context;
import android.graphics.Insets;
import android.graphics.PixelFormat;
import android.graphics.Rect;
import android.provider.Settings;
import android.view.DragEvent;
import android.view.Gravity;
import android.view.View;
import android.view.WindowInsets;
import android.view.WindowManager;
import android.view.WindowMetrics;
import android.widget.TextView;

/** Public overlay window; URI permissions are owned by the containing activity. */
final class FileEdgeReceiver {
    interface Sink { boolean drag(DragEvent event,EdgeStrip zone); }
    private final Activity owner;
    private final Sink sink;
    private WindowManager windows;
    private TextView strip;
    private EdgeStrip zone,idleZone,targetZone,wanted;
    private WindowManager.LayoutParams params;
    private boolean showing;
    private int side=-1,width,height;
    FileEdgeReceiver(Activity owner,Sink sink) { this.owner=owner;this.sink=sink; }
    boolean install(int requestedSide) {
        if(strip!=null && side==requestedSide) return true;
        close();
        if(!Settings.canDrawOverlays(owner)) return false;
        try {
            Context context=owner.createWindowContext(WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,null);
            windows=context.getSystemService(WindowManager.class);
            WindowMetrics metrics=windows.getMaximumWindowMetrics();Rect bounds=metrics.getBounds();
            width=bounds.width();height=bounds.height();
            Insets i=metrics.getWindowInsets().getInsetsIgnoringVisibility(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());
            idleZone=EdgeStrip.create(width,height,i.left,i.top,i.right,i.bottom,requestedSide);
            targetZone=EdgeStrip.dropZone(width,height,i.left,i.top,i.right,i.bottom,requestedSide,context.getResources().getDisplayMetrics().density);
            wanted=idleZone;
            TextView view=new TextView(context);strip=view;side=requestedSide;
            view.setGravity(Gravity.CENTER);view.setTextSize(18);view.setTextColor(0xffffffff);
            view.setBackgroundColor(0x00000000);
            params=new WindowManager.LayoutParams(wanted.width,wanted.height,
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE|WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL|
                WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN|WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,PixelFormat.TRANSLUCENT);
            params.gravity=Gravity.TOP|Gravity.LEFT;params.x=wanted.x;params.y=wanted.y;
            params.layoutInDisplayCutoutMode=WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_ALWAYS;
            params.setTitle("Galaxy Bridge file drop zone");
            view.setOnDragListener((v,event) -> sink.drag(event,zone));
            view.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob) -> {
                int[] position=new int[2];v.getLocationOnScreen(position);
                try {
                    if(wanted.x!=position[0] || wanted.y!=position[1] || wanted.width!=v.getWidth() || wanted.height!=v.getHeight())
                        throw new IllegalArgumentException("Unexpected drop edge bounds");
                    zone=wanted;
                } catch(RuntimeException e) { zone=null; }
            });
            windows.addView(view,params);return true;
        } catch(RuntimeException e) { close();return false; }
    }
    boolean matches(EdgeFileDrag.Pointer pointer) {
        return strip!=null && pointer!=null && pointer.side==side && pointer.width==width && pointer.height==height;
    }
    EdgeStrip bounds() { return showing?zone:null; }
    boolean showTarget(boolean active) {
        if(strip==null) return false;
        if(showing==active) return true;
        // Resize the already subscribed window: a new window mid-drag may not
        // receive STARTED and therefore may never become an eligible receiver.
        showing=active;zone=null;wanted=active?targetZone:idleZone;
        strip.setText(active?(side<2?"На\nПК":"На ПК"):"");highlight(false);
        params.x=wanted.x;params.y=wanted.y;params.width=wanted.width;params.height=wanted.height;
        try { windows.updateViewLayout(strip,params);return true; }
        catch(RuntimeException e) { close();return false; }
    }
    void highlight(boolean active) { if(strip!=null) strip.setBackgroundColor(showing?(active?0xff2eae60:0xff1976d2):0x00000000); }
    void status(String text) { if(strip!=null && showing) strip.setText(text); }
    void close() {
        View old=strip;strip=null;zone=null;side=-1;showing=false;wanted=idleZone=targetZone=null;
        if(old!=null && windows!=null) try { windows.removeViewImmediate(old); } catch(RuntimeException ignored) { }
    }
}
