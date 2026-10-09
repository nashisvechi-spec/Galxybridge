package com.galaxybridge.edge;

import android.content.Context;
import android.graphics.PixelFormat;
import android.graphics.Point;
import android.hardware.display.DisplayManager;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.Process;
import android.view.Gravity;
import android.view.Display;
import android.view.InputDevice;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;

/** Transient ADB-shell edge window. No APK, keyboard hook, or screen recording. */
public final class Main {
    private final PrintWriter out = new PrintWriter(System.out, true);
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Context context;
    private final WindowManager windows;
    private View edge;
    private int epoch, side;
    private long sequence;
    private boolean quitting;
    private float lastX, lastY;
    private int lastButtons;
    private Runnable pending;

    private Main(Context context) {
        this.context = context;
        windows = (WindowManager) context.getSystemService(Context.WINDOW_SERVICE);
        if (windows == null) throw new IllegalStateException("WINDOW_SERVICE");
    }

    public static void main(String[] args) {
        try {
            if (Build.VERSION.SDK_INT < 30) throw new IllegalStateException("ANDROID_11_REQUIRED");
            Looper.prepareMainLooper();
            // ADB's shell UID already has INTERNAL_SYSTEM_WINDOW on supported stock builds.
            Class<?> manager = Class.forName("android.app.ActivityManager");
            int granted = (Integer) manager.getMethod("checkComponentPermission", String.class,
                int.class, int.class, boolean.class).invoke(null,
                "android.permission.INTERNAL_SYSTEM_WINDOW", Process.myUid(), -1, true);
            if (granted != 0) throw new SecurityException("SHELL_WINDOW_PERMISSION");
            Class<?> threadClass = Class.forName("android.app.ActivityThread");
            Object thread = threadClass.getMethod("systemMain").invoke(null);
            Context system = (Context) threadClass.getMethod("getSystemContext").invoke(thread);
            Context shell = system.createPackageContext("com.android.shell", Context.CONTEXT_IGNORE_SECURITY);
            DisplayManager displays = (DisplayManager) shell.getSystemService(Context.DISPLAY_SERVICE);
            if (displays == null || displays.getDisplay(Display.DEFAULT_DISPLAY) == null)
                throw new IllegalStateException("DEFAULT_DISPLAY");
            shell = shell.createDisplayContext(displays.getDisplay(Display.DEFAULT_DISPLAY));
            Main main = new Main(shell);
            main.out.println("GB_EDGE_READY 1");
            Thread input = new Thread(() -> main.readCommands(), "GalaxyBridgeEdgeCommands");
            input.setDaemon(true); input.start();
            Looper.loop();
            main.remove();
            System.exit(0);
        } catch (Throwable failure) {
            System.out.println("GB_EDGE_ERROR STARTUP_" + failure.getClass().getSimpleName());
            System.out.flush();
            System.exit(1);
        }
    }

    private void readCommands() {
        try (BufferedReader input = new BufferedReader(new InputStreamReader(System.in, StandardCharsets.UTF_8))) {
            String line;
            while ((line = input.readLine()) != null) {
                if (line.length() > 80) continue;
                final String command = line;
                handler.post(() -> command(command));
            }
        } catch (Exception ignored) { }
        finally { handler.post(() -> quit()); }
    }
    private void command(String text) {
        if (quitting) return;
        try {
            String[] p = text.trim().split(" +");
            if (p.length == 1 && p[0].equals("QUIT")) { quit(); return; }
            if (p.length == 3 && p[0].equals("START")) {
                int id = Integer.parseInt(p[1]), location = Integer.parseInt(p[2]);
                if (id < 1 || location < 0 || location > 3) return;
                remove(); epoch = id; side = location; show();
            } else if (p.length == 2 && p[0].equals("STOP") && Integer.parseInt(p[1]) == epoch) remove();
        } catch (Exception failure) {
            out.println("GB_EDGE_ERROR WINDOW_" + failure.getClass().getSimpleName());
            quit();
        }
    }

    @SuppressWarnings("deprecation")
    private void show() {
        final int capture = epoch;
        View view = new View(context) {
            @Override public boolean onHoverEvent(MotionEvent event) {
                if (edge != this || epoch != capture || !event.isFromSource(InputDevice.SOURCE_MOUSE)) return false;
                InputDevice device = InputDevice.getDevice(event.getDeviceId());
                if (device == null || !"Galaxy Bridge Mouse".equals(device.getName())) return false;
                if (event.getActionMasked() == MotionEvent.ACTION_HOVER_EXIT) {
                    cancelPending(); out.println("GB_EDGE_LEFT " + capture); return true;
                }
                if (event.getActionMasked() != MotionEvent.ACTION_HOVER_ENTER &&
                    event.getActionMasked() != MotionEvent.ACTION_HOVER_MOVE) return true;
                lastX = event.getRawX(); lastY = event.getRawY(); lastButtons = event.getButtonState() & 7;
                if (pending == null) {
                    pending = () -> { pending = null; emit(capture); };
                    handler.postDelayed(pending, 25); // Coalesce, but always deliver the final edge event.
                }
                return true;
            }
        };
        view.setBackgroundColor(0x00000000);
        int flags = WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
            | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL
            | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN
            | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS;
        WindowManager.LayoutParams layout = new WindowManager.LayoutParams(
            side < 2 ? 4 : WindowManager.LayoutParams.MATCH_PARENT,
            side < 2 ? WindowManager.LayoutParams.MATCH_PARENT : 4,
            WindowManager.LayoutParams.TYPE_SYSTEM_ERROR, flags, PixelFormat.TRANSLUCENT);
        // Phone right/left/top/bottom -> phone left/right/bottom/top return edge.
        layout.gravity = side == 0 ? Gravity.TOP | Gravity.LEFT : side == 1 ? Gravity.TOP | Gravity.RIGHT
            : side == 2 ? Gravity.BOTTOM | Gravity.LEFT : Gravity.TOP | Gravity.LEFT;
        layout.setFitInsetsTypes(0);
        layout.layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_ALWAYS;
        layout.setTitle("Galaxy Bridge return edge");
        edge = view;
        try { windows.addView(view, layout); }
        catch (RuntimeException failure) { edge = null; throw failure; }
    }

    @SuppressWarnings("deprecation")
    private void emit(int capture) {
        if (edge == null || capture != epoch || quitting) return;
        Point size = new Point(); windows.getDefaultDisplay().getRealSize(size);
        out.println("GB_EDGE " + capture + " " + (++sequence) + " " + size.x + " " + size.y
            + " " + Float.toString(lastX) + " " + Float.toString(lastY) + " " + lastButtons);
    }
    private void cancelPending() {
        if (pending != null) { handler.removeCallbacks(pending); pending = null; }
    }
    private void remove() {
        cancelPending(); View previous = edge; edge = null; epoch = 0;
        if (previous != null) windows.removeViewImmediate(previous);
    }
    private void quit() {
        if (quitting) return;
        quitting = true;
        try { remove(); } finally { Looper.getMainLooper().quitSafely(); }
    }
}
