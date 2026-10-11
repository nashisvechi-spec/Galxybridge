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

/** Public overlay window; URI permissions are owned by the containing activity. */
final class FileEdgeReceiver {
    interface Sink { boolean drag(DragEvent event,EdgeStrip zone); }
    private final Activity owner;
    private final Sink sink;
    private WindowManager windows;
    private View strip;
    private EdgeStrip zone;
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
            EdgeStrip wanted=EdgeStrip.create(width,height,i.left,i.top,i.right,i.bottom,requestedSide);
            View view=new View(context);strip=view;side=requestedSide;
            view.setBackgroundColor(0xff1976d2);
            WindowManager.LayoutParams params=new WindowManager.LayoutParams(wanted.width,wanted.height,
                WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE|WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL|
                WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN|WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,PixelFormat.TRANSLUCENT);
            params.gravity=Gravity.TOP|Gravity.LEFT;params.x=wanted.x;params.y=wanted.y;
            params.layoutInDisplayCutoutMode=WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_ALWAYS;
            params.setTitle("Galaxy Bridge file drop edge");
            view.setOnDragListener((v,event) -> zone!=null && sink.drag(event,zone));
            view.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob) -> {
                int[] position=new int[2];v.getLocationOnScreen(position);
                try {
                    EdgeStrip actual=EdgeStrip.create(width,height,position[0],position[1],
                        width-position[0]-v.getWidth(),height-position[1]-v.getHeight(),side);
                    if(actual.x!=position[0] || actual.y!=position[1] || actual.width!=v.getWidth() || actual.height!=v.getHeight())
                        throw new IllegalArgumentException("Unexpected drop edge bounds");
                    zone=actual;
                } catch(RuntimeException e) { zone=null; }
            });
            windows.addView(view,params);return true;
        } catch(RuntimeException e) { close();return false; }
    }
    boolean matches(EdgeFileDrag.Pointer pointer) {
        return pointer!=null && pointer.side==side && pointer.width==width && pointer.height==height && zone!=null;
    }
    void highlight(boolean active) { if(strip!=null) strip.setBackgroundColor(active?0xff2eae60:0xff1976d2); }
    void close() {
        View old=strip;strip=null;zone=null;side=-1;
        if(old!=null && windows!=null) try { windows.removeViewImmediate(old); } catch(RuntimeException ignored) { }
    }
}
