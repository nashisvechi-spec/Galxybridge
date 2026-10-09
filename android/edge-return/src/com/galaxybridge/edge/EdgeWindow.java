package com.galaxybridge.edge;

import android.content.Context;
import android.graphics.PixelFormat;
import android.graphics.Point;
import android.hardware.display.DisplayManager;
import android.os.Handler;
import android.view.Display;
import android.view.Gravity;
import android.view.InputDevice;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowInsets;
import android.view.WindowManager;
import java.util.function.Consumer;

/** All methods and callbacks run on the service's main thread. */
final class EdgeWindow {
    private final Context context;
    private final WindowManager windows;
    private final Handler handler;
    private final Consumer<String> send;
    private View edge;
    private int epoch;
    private long sequence;
    private float x, y;
    private int buttons;
    private Runnable pending;

    EdgeWindow(Context app, Handler handler, Consumer<String> send) {
        DisplayManager displays = app.getSystemService(DisplayManager.class);
        Display display = displays == null ? null : displays.getDisplay(Display.DEFAULT_DISPLAY);
        if (display == null) throw new IllegalStateException("DEFAULT_DISPLAY");
        context = app.createDisplayContext(display).createWindowContext(WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY, null);
        windows = context.getSystemService(WindowManager.class);
        this.handler = handler; this.send = send;
    }

    void start(int id, int side) {
        remove(); epoch = id;
        final int capture = id;
        View view = new View(context) {
            @Override public boolean onHoverEvent(MotionEvent event) {
                if (edge != this || epoch != capture || !event.isFromSource(InputDevice.SOURCE_MOUSE)) return false;
                InputDevice device = InputDevice.getDevice(event.getDeviceId());
                if (device == null || !"Galaxy Bridge Mouse".equals(device.getName())) return false;
                if (event.getActionMasked() == MotionEvent.ACTION_HOVER_EXIT) {
                    cancelPending(); send.accept("GB_EDGE_LEFT " + capture); return true;
                }
                if (event.getActionMasked() == MotionEvent.ACTION_HOVER_ENTER || event.getActionMasked() == MotionEvent.ACTION_HOVER_MOVE) {
                    x = event.getRawX(); y = event.getRawY(); buttons = event.getButtonState() & 7;
                    if (pending == null) {
                        pending = () -> { pending = null; emit(capture); };
                        handler.postDelayed(pending, 25);
                    }
                }
                return true;
            }
        };
        view.setBackgroundColor(0x00000000);
        WindowManager.LayoutParams layout = new WindowManager.LayoutParams(
            side < 2 ? 4 : WindowManager.LayoutParams.MATCH_PARENT,
            side < 2 ? WindowManager.LayoutParams.MATCH_PARENT : 4,
            WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL
                | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN, PixelFormat.TRANSLUCENT);
        layout.gravity = side == 0 ? Gravity.TOP | Gravity.LEFT : side == 1 ? Gravity.TOP | Gravity.RIGHT
            : side == 2 ? Gravity.BOTTOM | Gravity.LEFT : Gravity.TOP | Gravity.LEFT;
        // Ordinary overlays sit below system bars. Place the strip in the usable area
        // and report its actual rectangle instead of guessing bar sizes on Windows.
        layout.setFitInsetsTypes(WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
        layout.setTitle("Galaxy Bridge return edge");
        edge = view;
        try { windows.addView(view, layout); }
        catch (RuntimeException failure) { edge = null; throw failure; }
        send.accept("GB_EDGE_ACTIVE " + capture);
    }

    @SuppressWarnings("deprecation")
    private void emit(int capture) {
        if (edge == null || capture != epoch) return;
        Point size = new Point(); windows.getDefaultDisplay().getRealSize(size);
        int[] position = new int[2]; edge.getLocationOnScreen(position);
        int width = edge.getWidth(), height = edge.getHeight();
        if (width < 1 || height < 1) return;
        send.accept("GB_EDGE2 " + capture + " " + (++sequence) + " " + size.x + " " + size.y
            + " " + Float.toString(x) + " " + Float.toString(y) + " " + buttons
            + " " + position[0] + " " + position[1] + " " + width + " " + height);
    }
    void stop(int id) { if (id == epoch) remove(); }
    private void cancelPending() { if (pending != null) { handler.removeCallbacks(pending); pending = null; } }
    void remove() {
        cancelPending(); View previous = edge; edge = null; epoch = 0;
        if (previous != null) windows.removeViewImmediate(previous);
    }
}
