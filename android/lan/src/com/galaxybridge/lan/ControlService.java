package com.galaxybridge.lan;

import android.accessibilityservice.*;
import android.app.KeyguardManager;
import android.content.Context;
import android.graphics.*;
import android.hardware.display.DisplayManager;
import android.os.*;
import android.util.DisplayMetrics;
import android.view.*;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;
import android.view.accessibility.AccessibilityWindowInfo;
import java.util.List;
import org.json.JSONObject;
import java.util.ArrayDeque;
import java.util.Locale;

public final class ControlService extends AccessibilityService implements DisplayManager.DisplayListener {
    static volatile ControlService instance;
    private final Handler handler=new Handler(Looper.getMainLooper());
    private WindowManager windows;
    private Context inputContext;
    private DisplayManager displays;
    private int displayId;
    private DisplayTarget.Screen capturedScreen,observedScreen;
    private volatile long displayRevision;
    static volatile String controlStatus="";
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
    private final Runnable touchIntent=new Runnable() { public void run() { advanceTouch(); } };
    private final Runnable feedback=new Runnable() { public void run() { if(active) { poll(); if(active) handler.postDelayed(this,100); } } };
    static boolean ready(Context context) {
        return instance!=null && ControlDisplays.unlocked(context);
    }
    @Override protected void onServiceConnected() {
        super.onServiceConnected(); instance=this;displays=getSystemService(DisplayManager.class);
        displays.registerDisplayListener(this,handler);refreshDisplay();
    }
    @Override public InputMethod onCreateInputMethod() { return new InputMethod(this); }
    @Override public void onAccessibilityEvent(AccessibilityEvent event) {
        int focusChanges=AccessibilityEvent.WINDOWS_CHANGE_ACTIVE|AccessibilityEvent.WINDOWS_CHANGE_FOCUSED|
            AccessibilityEvent.WINDOWS_CHANGE_ADDED|AccessibilityEvent.WINDOWS_CHANGE_REMOVED;
        if(event.getEventType()==AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED ||
            event.getEventType()==AccessibilityEvent.TYPE_WINDOWS_CHANGED && (event.getWindowChanges()&focusChanges)!=0) filesForeground.invalidate();
        if(!ready(this)) { ConnectionService.cancelClipboardRead();if(active) reset(); }
    }
    @Override public void onInterrupt() { ConnectionService.cancelClipboardRead();reset(); }
    @Override public void onDestroy() { ConnectionService.cancelClipboardRead();reset();if(displays!=null) displays.unregisterDisplayListener(this); if(instance==this) instance=null; super.onDestroy(); }
    private boolean dimensions() {
        DisplayTarget.Screen screen=ControlDisplays.target(this);
        if(screen==null || active && !DisplayTarget.same(screen,capturedScreen)) { refreshDisplay();return false; }
        width=screen.width;height=screen.height;
        x=Math.max(0,Math.min(width-1,x)); y=Math.max(0,Math.min(height-1,y));
        return true;
    }
    private void refreshDisplay() {
        DisplayTarget.Screen screen=ControlDisplays.target(this);
        if(!DisplayTarget.same(screen,observedScreen)) {
            reset();observedScreen=screen;displayRevision++;
            controlStatus="";ConnectionService.inputScreenChanged();
        }
    }
    static long displayRevision() { ControlService current=instance;return current==null?0:current.displayRevision; }
    static int pointerDisplay() { ControlService current=instance;return current!=null && current.active?current.displayId:-1; }
    static void preferenceChanged() {
        ControlService current=instance;
        if(current!=null) {
            current.reset();current.observedScreen=ControlDisplays.target(current);current.displayRevision++;
            controlStatus="";ConnectionService.inputScreenChanged();
        }
        else ConnectionService.inputScreenChanged();
    }
    @Override public void onDisplayAdded(int id) { refreshDisplay(); }
    @Override public void onDisplayRemoved(int id) { refreshDisplay(); }
    @Override public void onDisplayChanged(int id) { refreshDisplay(); }
    void accept(JSONObject command) {
        try {
            String type=command.getString("type");
            if("release".equals(type)) { reset(); return; }
            if("capture".equals(type)) {
                reset(); if(!command.getBoolean("active") || !ready(this)) return;
                epoch=command.getInt("epoch"); side=command.getInt("side");
                if(epoch<1 || side<0 || side>3) return;
                DisplayTarget.Screen screen=ControlDisplays.target(this);if(screen==null) return;
                capturedScreen=screen;displayId=screen.id;
                Context cursorContext=ControlDisplays.overlay(this,displayId,WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY);
                inputContext=cursorContext;
                windows=cursorContext.getSystemService(WindowManager.class);
                if(!dimensions()) return;x=width/2f;y=height/2f;
                // Enter from the edge facing the laptop, but far enough inside to avoid an immediate return.
                if(side==0) x=32; else if(side==1) x=width-33; else if(side==2) y=height-33; else y=32;
                pointer=new Pointer(cursorContext);
                float unit=cursorContext.getResources().getDisplayMetrics().density;
                pointerLayout=new WindowManager.LayoutParams((int)Math.ceil(20*unit),(int)Math.ceil(27*unit),WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
                    WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE|WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE|WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN|WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS,PixelFormat.TRANSLUCENT);
                pointerLayout.gravity=Gravity.TOP|Gravity.LEFT; pointerLayout.layoutInDisplayCutoutMode=WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_ALWAYS;
                controlStatus="";active=true;showPointer();sequence=0;handler.post(feedback);return;
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
        } catch(Exception e) { inputFailed(e.getClass().getSimpleName()); }
    }
    private void inputFailed(String reason) {
        reset();displayRevision++;controlStatus="Управление экраном остановлено: "+reason+". Проверьте выбранный экран и включите управление снова.";
        ConnectionService.reportInputScreen();
    }
    private InputMethod.AccessibilityInputConnection input() { InputMethod method=getInputMethod(); return method==null?null:method.getCurrentInputConnection(); }
    boolean filesForeground() {
        long now=SystemClock.elapsedRealtime();
        if(!active || !ready(this)) return filesForeground.check(null,false,now);
        // A synchronous tree read on every mouse packet can stall this main
        // thread while the Files app redraws, filling the input command queue.
        if(!filesForeground.refresh(now)) return filesForeground.check(null,true,now);
        AccessibilityNodeInfo root=rootAt(x,y);
        if(root==null) return filesForeground.check(null,true,SystemClock.elapsedRealtime());
        try { return filesForeground.check(root.getPackageName()==null?null:root.getPackageName().toString(),true,SystemClock.elapsedRealtime()); }
        finally { root.recycle(); }
    }
    private final FilesForeground filesForeground=new FilesForeground();
    private void mouse(JSONObject command) throws Exception {
        int dx=command.getInt("dx"),dy=command.getInt("dy"),next=command.getInt("buttons"),wheel=command.getInt("wheel");
        if(Math.abs((long)dx)>8192 || Math.abs((long)dy)>8192 || Math.abs((long)wheel)>32 || next<0 || next>7) throw new IllegalArgumentException();
        if(!dimensions()) return;x=Math.max(0,Math.min(width-1,x+dx));y=Math.max(0,Math.min(height-1,y+dy));
        if(next!=buttons) filesForeground.invalidate(); // Verify the window at a new physical press.
        if(FilesBridgeService.route(x,y,next,buttons,wheel,side,epoch,width,height,dx,dy)) { buttons=next;poll();return; }
        EdgeFileDrag.Pointer heldEdge=DropActivity.edgePointer(new EdgeFileDrag.Pointer(epoch,side,width,height,x,y,next,mouseStroke!=null,displayId));
        if(heldEdge!=null) { x=heldEdge.x;y=heldEdge.y;buttons=next;poll();return; }
        if(SendActivity.remotePointer(x,y,next,buttons,side,epoch,width,height)) { buttons=next;poll();return; }
        if((next&1)!=0 && (buttons&1)==0) {
            mouseStroke=new PointerStroke(ViewConfiguration.get(inputContext).getScaledTouchSlop(),x,y,SystemClock.uptimeMillis());
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
            WheelGesture wheelGesture=new WheelGesture(width,height,x,y,wheel,command.optBoolean("zoom",false));
            PendingGesture tail=gestures.peekLast();
            if(tail!=null && tail.wheel!=null && tail.wheel.merge(wheelGesture)) {
                if(tail.wheel.steps==0) gestures.removeLast();
            } else enqueue(new PendingGesture(wheelGesture));
        }
        buttons=next; poll();
    }

    private void enqueue(PendingGesture pending) {
        if(gestures.size()>=8) { reset(); ConnectionService.disconnect(); return; }
        gestures.add(pending);nextGesture();
    }
    static EdgeFileDrag.Pointer filePointer() {
        ControlService current=instance;
        if(current==null || !current.active || !ready(current)) return null;
        return new EdgeFileDrag.Pointer(current.epoch,current.side,current.width,current.height,current.x,current.y,current.buttons,current.mouseStroke!=null,current.displayId);
    }
    static boolean dropFileTouch(EdgeFileDrag.Pointer pointer,EdgeStrip zone) {
        ControlService current=instance;
        if(pointer==null || current==null || !current.active || current.epoch!=pointer.epoch || current.buttons!=1 ||
            current.side!=pointer.side || current.displayId!=pointer.displayId || current.width!=pointer.width || current.height!=pointer.height ||
            pointer.x<0 || pointer.y<0 || pointer.x>=current.width || pointer.y>=current.height ||
            current.mouseStroke==null || current.currentGesture==null || current.currentGesture.press!=current.mouseStroke ||
            current.touchDispatching || current.touchStroke==null || current.touchEnd==null || zone==null ||
            !zone.contains(pointer.x,pointer.y) || !zone.contains(current.touchEnd.x,current.touchEnd.y)) return false;
        // End only the injected Android finger. The real PC mouse is still held.
        // In-flight motion must complete first; queued overshoot must not move up
        // into Samsung's native app-opening region after contact was confirmed.
        if(!current.mouseStroke.releaseHere(SystemClock.uptimeMillis())) return false;
        current.x=current.touchEnd.x;current.y=current.touchEnd.y;
        current.mouseStroke=null;current.advanceTouch();return true;
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
                    touchReady=true;advanceTouch();
                    return;
                }
                if(!dispatchGesture(wheelDescription(pending.wheel),callback,handler)) inputFailed("Wheel gesture rejected");
            } catch(RuntimeException e) { inputFailed(e.getClass().getSimpleName()); }
        },32);
    }
    private GestureDescription wheelDescription(WheelGesture wheel) {
        GestureDescription.Builder builder=new GestureDescription.Builder().setDisplayId(displayId);
        if(wheel.zoom) {
            float[] p=wheel.pinch();
            for(int sign:new int[] {-1,1}) {
                Path path=new Path();path.moveTo(p[0]+sign*p[2],p[1]);path.lineTo(p[0]+sign*p[3],p[1]);
                builder.addStroke(new GestureDescription.StrokeDescription(path,0,180));
            }
        } else {
            float[] p=wheel.swipe();Path path=new Path();path.moveTo(p[0],p[1]);path.lineTo(p[0],p[2]);
            builder.addStroke(new GestureDescription.StrokeDescription(path,0,180));
        }
        return builder.build();
    }
    private void advanceTouch() {
        if(!active || !touchReady || touchDispatching || currentGesture==null || currentGesture.press==null) return;
        if(!ready(this)) { reset();return; }
        PendingGesture pending=currentGesture;
        handler.removeCallbacks(touchIntent);
        int timeout=ViewConfiguration.getLongPressTimeout();
        long now=SystemClock.uptimeMillis(),wait=pending.press.waitForIntent(now,timeout);
        if(wait>0) { handler.postDelayed(touchIntent,wait);return; }
        // DocumentsUI's gesture listener accepts finger/stylus tool types, whereas
        // accessibility injection may use UNKNOWN. Activate an actionable node
        // for ordinary clicks, regardless of how long the physical click took.
        // Once a drag/hold was injected, only its own touch-up can end it.
        if(pending.press.click(timeout) && clickElement(pending.press.x,pending.press.y)) { finishGesture(pending);return; }
        PointerStroke.Segment segment=pending.press.take(now,timeout);
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
            if(dispatchGesture(new GestureDescription.Builder().setDisplayId(displayId).addStroke(nextStroke).build(),callback,handler)) {
                touchStroke=nextStroke;touchEnd=segment.points[segment.points.length-1];
            } else {
                inputFailed("Gesture rejected");
            }
        } catch(RuntimeException e) { inputFailed(e.getClass().getSimpleName()); }
    }
    private void finishGesture(PendingGesture pending) {
        if(currentGesture!=pending) return;
        handler.removeCallbacks(touchIntent);
        if(mouseStroke==pending.press) mouseStroke=null;
        currentGesture=null;touchStroke=null;touchEnd=null;touchReady=false;touchDispatching=false;
        gestureBusy=false;nextGesture();
    }
    private boolean clickElement(float tapX,float tapY) {
        AccessibilityNodeInfo root=null;
        try {
            root=rootAt(tapX,tapY);
            return root!=null && ClickTarget.click(new ClickNode(root),tapX,tapY);
        } catch(RuntimeException ignored) { return false; }
        finally { if(root!=null) root.recycle(); }
    }
    private AccessibilityNodeInfo rootAt(float px,float py) {
        android.util.SparseArray<List<AccessibilityWindowInfo>> all=getWindowsOnAllDisplays();
        try {
            List<AccessibilityWindowInfo> list=all.get(displayId);
            if(list!=null) for(AccessibilityWindowInfo window:list) {
                if(window.getType()==AccessibilityWindowInfo.TYPE_ACCESSIBILITY_OVERLAY) continue;
                Rect bounds=new Rect();window.getBoundsInScreen(bounds);
                if(bounds.contains((int)px,(int)py)) return window.getRoot();
            }
            // Never use the phone's globally active root for a secondary display.
            if(displayId!=Display.DEFAULT_DISPLAY) return null;
            AccessibilityNodeInfo root=getRootInActiveWindow();
            if(root==null) return null;
            AccessibilityWindowInfo owner=root.getWindow();
            try {
                if(owner!=null && owner.getDisplayId()==displayId) return root;
                root.recycle();return null;
            } finally { if(owner!=null) owner.recycle(); }
        } finally {
            for(int i=0;i<all.size();i++) for(AccessibilityWindowInfo window:all.valueAt(i)) window.recycle();
        }
    }
    private static final class PendingGesture {
        final PointerStroke press;
        final WheelGesture wheel;
        PendingGesture(WheelGesture wheel) { this.wheel=wheel;press=null; }
        PendingGesture(PointerStroke press) { this.press=press;wheel=null; }
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
        if(!dimensions()) return;showPointer();
        if(FilesBridgeService.suppressEdge()) return;
        String line=String.format(Locale.ROOT,"GB_EDGE %d %d %d %d %.2f %.2f %d",epoch,++sequence,width,height,x,y,buttons);
        ConnectionService.send(Wire.message("edge","line",line));
    }
    void reset() {
        filesForeground.reset();
        FilesBridgeService.resetPointer();
        SendActivity.cancelRemoteDrag();
        // Finish even an in-flight continuation on disconnect/lock/re-entry. Android
        // schedules it after the current segment; stale callbacks cannot revive it.
        if(touchStroke!=null && touchStroke.willContinue() && touchEnd!=null) {
            try {
                Path end=new Path();end.moveTo(touchEnd.x,touchEnd.y);
                dispatchGesture(new GestureDescription.Builder().setDisplayId(displayId).addStroke(touchStroke.continueStroke(end,0,1,false)).build(),null,handler);
            } catch(RuntimeException ignored) {}
        }
        active=false;generation++;gestures.clear();gestureBusy=false;mouseStroke=null;buttons=0;handler.removeCallbacks(feedback);handler.removeCallbacks(touchIntent);
        currentGesture=null;touchStroke=null;touchEnd=null;touchReady=false;touchDispatching=false;
        if(pointer!=null) { try { hidePointer(); } catch(RuntimeException ignored){} pointerAttached=false;pointer=null;pointerLayout=null; }
        capturedScreen=null;
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
