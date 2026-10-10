package com.galaxybridge.lan;

import android.accessibilityservice.*;
import android.app.KeyguardManager;
import android.content.Context;
import android.graphics.*;
import android.os.*;
import android.util.DisplayMetrics;
import android.view.*;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;
import org.json.JSONObject;
import java.util.ArrayDeque;
import java.util.Locale;

public final class ControlService extends AccessibilityService {
    static volatile ControlService instance;
    private final Handler handler=new Handler(Looper.getMainLooper());
    private WindowManager windows;
    private Pointer pointer;
    private WindowManager.LayoutParams pointerLayout;
    private boolean active, gestureBusy, pointerAttached;
    private int epoch, width, height, buttons, side;
    private long sequence, generation;
    private float x,y;
    private PointerStroke mouseStroke;
    private PendingGesture currentGesture;
    private boolean touchReady, touchDispatching;
    private GestureDescription.StrokeDescription touchStroke;
    private PointerStroke.Point touchEnd;
    private final ArrayDeque<PendingGesture> gestures=new ArrayDeque<>();
    private final Runnable feedback=new Runnable() { public void run() { if(active) { poll(); if(active) handler.postDelayed(this,100); } } };
    static boolean ready(Context context) {
        KeyguardManager key=context.getSystemService(KeyguardManager.class);
        PowerManager power=context.getSystemService(PowerManager.class);
        return instance!=null && !key.isDeviceLocked() && power.isInteractive();
    }
    @Override protected void onServiceConnected() {
        super.onServiceConnected(); instance=this; windows=getSystemService(WindowManager.class);
    }
    @Override public InputMethod onCreateInputMethod() { return new InputMethod(this); }
    @Override public void onAccessibilityEvent(AccessibilityEvent event) { if(!ready(this)) { ConnectionService.cancelClipboardRead();if(active) reset(); } }
    @Override public void onInterrupt() { ConnectionService.cancelClipboardRead();reset(); }
    @Override public void onDestroy() { ConnectionService.cancelClipboardRead();reset(); if(instance==this) instance=null; super.onDestroy(); }
    private void dimensions() {
        DisplayMetrics metrics=new DisplayMetrics(); windows.getDefaultDisplay().getRealMetrics(metrics);
        width=metrics.widthPixels; height=metrics.heightPixels;
        x=Math.max(0,Math.min(width-1,x)); y=Math.max(0,Math.min(height-1,y));
    }
    void accept(JSONObject command) {
        try {
            String type=command.getString("type");
            if("release".equals(type)) { reset(); return; }
            if("capture".equals(type)) {
                reset(); if(!command.getBoolean("active") || !ready(this)) return;
                epoch=command.getInt("epoch"); side=command.getInt("side");
                if(epoch<1 || side<0 || side>3) return;
                dimensions(); x=width/2f; y=height/2f;
                // Enter from the edge facing the laptop, but far enough inside to avoid an immediate return.
                if(side==0) x=32; else if(side==1) x=width-33; else if(side==2) y=height-33; else y=32;
                pointer=new Pointer(this);
                float unit=getResources().getDisplayMetrics().density;
                pointerLayout=new WindowManager.LayoutParams((int)Math.ceil(20*unit),(int)Math.ceil(27*unit),WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
                    WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE|WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE|WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN|WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,PixelFormat.TRANSLUCENT);
                pointerLayout.gravity=Gravity.TOP|Gravity.LEFT; pointerLayout.layoutInDisplayCutoutMode=WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_ALWAYS;
                active=true; showPointer(); sequence=0; handler.post(feedback); return;
            }
            if("nav".equals(type) && ready(this)) {
                String nav=command.getString("action");
                if(nav.equals("back")) performGlobalAction(GLOBAL_ACTION_BACK); else if(nav.equals("home")) performGlobalAction(GLOBAL_ACTION_HOME); else if(nav.equals("recents")) performGlobalAction(GLOBAL_ACTION_RECENTS);
                return;
            }
            if(!active || !ready(this)) { if(active) reset(); return; }
            switch(type) {
                case "mouse": mouse(command); break;
                case "text":
                    String text=command.getString("text"); if(text.getBytes(java.nio.charset.StandardCharsets.UTF_8).length>16000) throw new IllegalArgumentException();
                    InputMethod.AccessibilityInputConnection textInput=input(); if(textInput!=null) textInput.commitText(text,1,null); break;
                case "key":
                    int code=command.getInt("code");
                    if(code==4) performGlobalAction(GLOBAL_ACTION_BACK);
                    else if(code==66 || code==67 || code==61 || code==112 || (code>=19 && code<=22)) {
                        InputMethod.AccessibilityInputConnection keyInput=input();
                        if(keyInput!=null) { keyInput.sendKeyEvent(new KeyEvent(KeyEvent.ACTION_DOWN,code)); keyInput.sendKeyEvent(new KeyEvent(KeyEvent.ACTION_UP,code)); }
                    }
                    break;
                case "edit":
                    String action=command.getString("action"); int id=action.equals("selectAll")?android.R.id.selectAll:action.equals("copy")?android.R.id.copy:action.equals("cut")?android.R.id.cut:0;
                    if(id>0 && input()!=null) input().performContextMenuAction(id); break;
                default: throw new IllegalArgumentException();
            }
        } catch(Exception e) { reset(); ConnectionService.disconnect(); }
    }
    private InputMethod.AccessibilityInputConnection input() { InputMethod method=getInputMethod(); return method==null?null:method.getCurrentInputConnection(); }
    private void mouse(JSONObject command) throws Exception {
        int dx=command.getInt("dx"),dy=command.getInt("dy"),next=command.getInt("buttons"),wheel=command.getInt("wheel");
        if(Math.abs((long)dx)>8192 || Math.abs((long)dy)>8192 || Math.abs((long)wheel)>32 || next<0 || next>7) throw new IllegalArgumentException();
        dimensions(); x=Math.max(0,Math.min(width-1,x+dx)); y=Math.max(0,Math.min(height-1,y+dy));
        if((next&1)!=0 && (buttons&1)==0) {
            mouseStroke=new PointerStroke(ViewConfiguration.get(this).getScaledTouchSlop(),x,y,SystemClock.uptimeMillis());
            enqueue(new PendingGesture(mouseStroke));
        }
        if(mouseStroke!=null) {
            mouseStroke.move(x,y);
            if((next&1)==0) { mouseStroke.release(x,y,SystemClock.uptimeMillis());mouseStroke=null; }
            advanceTouch();
        }
        if((next&2)!=0 && (buttons&2)==0) performGlobalAction(GLOBAL_ACTION_BACK);
        if((next&4)!=0 && (buttons&4)==0) performGlobalAction(GLOBAL_ACTION_RECENTS);
        if(wheel!=0 && next==0) {
            float top=Math.max(48,height*.2f),bottom=Math.min(height-48,height*.8f),start=Math.max(top,Math.min(bottom,y));
            float end=Math.max(top,Math.min(bottom,start+(wheel>0?1:-1)*Math.min(height*.35f,Math.abs(wheel)*100)));
            Path swipe=new Path();swipe.moveTo(Math.max(24,Math.min(width-24,x)),start);swipe.lineTo(Math.max(24,Math.min(width-24,x)),end);gesture(swipe,180);
        }
        buttons=next; poll();
    }
    private void gesture(Path path,long duration) {
        enqueue(new PendingGesture(new GestureDescription.Builder().addStroke(new GestureDescription.StrokeDescription(path,0,duration)).build()));
    }
    private void enqueue(PendingGesture pending) {
        if(gestures.size()>=8) { reset(); ConnectionService.disconnect(); return; }
        gestures.add(pending);nextGesture();
    }
    private void nextGesture() {
        if(gestureBusy || !active) return;
        if(gestures.isEmpty()) { showPointer(); return; }
        PendingGesture pending=gestures.remove();currentGesture=pending;gestureBusy=true;long current=generation;
        touchReady=false;touchDispatching=false;touchStroke=null;touchEnd=null;
        // Remove the actual window, not just its pixels, before injecting a touch.
        // File pickers and other protected controls can reject obscured touches.
        hidePointer();
        GestureResultCallback callback=new GestureResultCallback() {
            private void done() { if(current==generation) finishGesture(pending); }
            @Override public void onCompleted(GestureDescription gesture) { done(); }
            @Override public void onCancelled(GestureDescription gesture) { done(); }
        };
        // Let WindowManager finish removing the overlay before dispatching.
        handler.postDelayed(() -> {
            if(current!=generation || !active) return;
            if(!ready(this)) { reset(); return; }
            try {
                if(pending.press!=null) {
                    // Retain node activation for clicks released before dispatch.
                    // An injected press is ended through its own stroke, never clicked twice.
                    if(pending.press.click(ViewConfiguration.getLongPressTimeout()) && clickElement(pending.press.x,pending.press.y)) finishGesture(pending);
                    else { touchReady=true;advanceTouch(); }
                    return;
                }
                if(!dispatchGesture(pending.description,callback,handler)) finishGesture(pending);
            } catch(RuntimeException e) { reset(); ConnectionService.disconnect(); }
        },32);
    }
    private void advanceTouch() {
        if(!active || !touchReady || touchDispatching || currentGesture==null || currentGesture.press==null) return;
        PendingGesture pending=currentGesture;
        PointerStroke.Segment segment=pending.press.take(ViewConfiguration.getLongPressTimeout());
        if(segment==null) return; // A stationary continued stroke keeps the finger down.
        Path path=new Path();path.moveTo(segment.points[0].x,segment.points[0].y);
        for(int i=1;i<segment.points.length;i++) path.lineTo(segment.points[i].x,segment.points[i].y);
        try {
            GestureDescription.StrokeDescription nextStroke=touchStroke==null?new GestureDescription.StrokeDescription(path,0,segment.duration,segment.continues):touchStroke.continueStroke(path,0,segment.duration,segment.continues);
            touchDispatching=true;long current=generation;
            GestureResultCallback callback=new GestureResultCallback() {
                @Override public void onCompleted(GestureDescription gesture) {
                    if(current!=generation || currentGesture!=pending) return;
                    touchDispatching=false;
                    if(segment.continues) advanceTouch(); else finishGesture(pending);
                }
                @Override public void onCancelled(GestureDescription gesture) {
                    if(current==generation && currentGesture==pending) finishGesture(pending);
                }
            };
            if(dispatchGesture(new GestureDescription.Builder().addStroke(nextStroke).build(),callback,handler)) {
                touchStroke=nextStroke;touchEnd=segment.points[segment.points.length-1];
            } else {
                reset();ConnectionService.disconnect();
            }
        } catch(RuntimeException e) { reset();ConnectionService.disconnect(); }
    }
    private void finishGesture(PendingGesture pending) {
        if(currentGesture!=pending) return;
        if(mouseStroke==pending.press) mouseStroke=null;
        currentGesture=null;touchStroke=null;touchEnd=null;touchReady=false;touchDispatching=false;
        gestureBusy=false;nextGesture();
    }
    private boolean clickElement(float tapX,float tapY) {
        AccessibilityNodeInfo root=null;
        try {
            root=getRootInActiveWindow();
            return root!=null && ClickTarget.click(new ClickNode(root),tapX,tapY);
        } catch(RuntimeException ignored) { return false; }
        finally { if(root!=null) root.recycle(); }
    }
    private static final class PendingGesture {
        final GestureDescription description;
        final PointerStroke press;
        PendingGesture(GestureDescription description) { this.description=description;press=null; }
        PendingGesture(PointerStroke press) { this.press=press;description=null; }
    }
    private static final class ClickNode implements ClickTarget.Node {
        private final AccessibilityNodeInfo node;
        ClickNode(AccessibilityNodeInfo node) { this.node=node; }
        public boolean contains(float x,float y) {
            Rect bounds=new Rect();node.getBoundsInScreen(bounds);
            return x>=bounds.left && x<bounds.right && y>=bounds.top && y<bounds.bottom;
        }
        public boolean available() { return node.isVisibleToUser() && node.isEnabled(); }
        public boolean clickable() {
            if(node.isClickable()) return true;
            for(AccessibilityNodeInfo.AccessibilityAction action:node.getActionList())
                if(action.getId()==AccessibilityNodeInfo.ACTION_CLICK) return true;
            return false;
        }
        public int childCount() { return node.getChildCount(); }
        public ClickTarget.Node child(int index) {
            AccessibilityNodeInfo child=node.getChild(index);
            return child==null?null:new ClickNode(child);
        }
        public boolean click() { return node.performAction(AccessibilityNodeInfo.ACTION_CLICK); }
        public void release() { node.recycle(); }
    }
    private void showPointer() {
        if(!active || gestureBusy || pointer==null) return;
        int left=Math.round(x),top=Math.round(y);
        boolean moved=pointerLayout.x!=left || pointerLayout.y!=top;
        pointerLayout.x=left; pointerLayout.y=top;
        if(pointerAttached) { if(moved) windows.updateViewLayout(pointer,pointerLayout); }
        else { windows.addView(pointer,pointerLayout); pointerAttached=true; }
    }
    private void hidePointer() {
        if(pointerAttached) { windows.removeViewImmediate(pointer); pointerAttached=false; }
    }
    void poll() {
        if(!active) return; if(!ready(this)) { reset(); ConnectionService.send(Wire.message("pong","ready",false)); return; }
        dimensions(); showPointer();
        String line=String.format(Locale.ROOT,"GB_EDGE %d %d %d %d %.2f %.2f %d",epoch,++sequence,width,height,x,y,buttons);
        ConnectionService.send(Wire.message("edge","line",line));
    }
    void reset() {
        // Finish even an in-flight continuation on disconnect/lock/re-entry. Android
        // schedules it after the current segment; stale callbacks cannot revive it.
        if(touchStroke!=null && touchStroke.willContinue() && touchEnd!=null) {
            try {
                Path end=new Path();end.moveTo(touchEnd.x,touchEnd.y);
                dispatchGesture(new GestureDescription.Builder().addStroke(touchStroke.continueStroke(end,0,1,false)).build(),null,handler);
            } catch(RuntimeException ignored) {}
        }
        active=false;generation++;gestures.clear();gestureBusy=false;mouseStroke=null;buttons=0;handler.removeCallbacks(feedback);
        currentGesture=null;touchStroke=null;touchEnd=null;touchReady=false;touchDispatching=false;
        if(pointer!=null) { try { hidePointer(); } catch(RuntimeException ignored){} pointerAttached=false;pointer=null;pointerLayout=null; }
    }
    private final class Pointer extends View {
        private final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Path arrow=new Path();
        Pointer(Context context) {
            super(context);
            float unit=getResources().getDisplayMetrics().density;
            // The tip is (0,0): drawing never changes the tap/drag/edge coordinates.
            arrow.moveTo(0,0);
            arrow.lineTo(0,18*unit); arrow.lineTo(5*unit,14*unit);
            arrow.lineTo(9*unit,23*unit); arrow.lineTo(13*unit,21*unit);
            arrow.lineTo(9*unit,12*unit); arrow.lineTo(17*unit,12*unit); arrow.close();
            paint.setStrokeWidth(1.5f*unit); paint.setStrokeJoin(Paint.Join.ROUND);
        }
        @Override protected void onDraw(Canvas canvas) {
            paint.setStyle(Paint.Style.FILL); paint.setColor(Color.WHITE); canvas.drawPath(arrow,paint);
            paint.setStyle(Paint.Style.STROKE); paint.setColor(Color.BLACK); canvas.drawPath(arrow,paint);
        }
    }
}
