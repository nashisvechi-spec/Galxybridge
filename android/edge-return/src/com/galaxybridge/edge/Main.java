package com.galaxybridge.edge;

import android.app.Application;
import android.app.Instrumentation;
import android.content.Context;
import android.content.pm.ApplicationInfo;
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
import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.InvocationTargetException;
import java.nio.charset.StandardCharsets;

/** Transient ADB-shell edge window. No APK, keyboard hook, or screen recording. */
public final class Main {
    private static final int WINDOW_TYPE = WindowManager.LayoutParams.TYPE_SYSTEM_ERROR;
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
    private String stage = "COMMAND";

    private Main(Context context) {
        this.context = context;
        windows = (WindowManager) context.getSystemService(Context.WINDOW_SERVICE);
        if (windows == null) throw new IllegalStateException("WINDOW_SERVICE");
    }

    public static void main(String[] args) {
        String startupStage = "STARTUP";
        try {
            if (Build.VERSION.SDK_INT < 30) throw new IllegalStateException("ANDROID_11_REQUIRED");
            Looper.prepareMainLooper();
            // ADB's shell UID already has INTERNAL_SYSTEM_WINDOW on supported stock builds.
            Class<?> manager = Class.forName("android.app.ActivityManager");
            int granted = (Integer) manager.getMethod("checkComponentPermission", String.class,
                int.class, int.class, boolean.class).invoke(null,
                "android.permission.INTERNAL_SYSTEM_WINDOW", Process.myUid(), -1, true);
            if (granted != 0) throw new SecurityException("SHELL_WINDOW_PERMISSION");
            startupStage = "APP_CONTEXT";
            Class<?> threadClass = Class.forName("android.app.ActivityThread");
            Object thread = threadClass.getMethod("systemMain").invoke(null);
            Context system = (Context) threadClass.getMethod("getSystemContext").invoke(thread);
            Context shell = system.createPackageContext("com.android.shell", Context.CONTEXT_IGNORE_SECURITY);
            prepareShellApplication(threadClass, thread, shell);
            startupStage = "DISPLAY_CONTEXT";
            DisplayManager displays = (DisplayManager) shell.getSystemService(Context.DISPLAY_SERVICE);
            if (displays == null || displays.getDisplay(Display.DEFAULT_DISPLAY) == null)
                throw new IllegalStateException("DEFAULT_DISPLAY");
            shell = shell.createDisplayContext(displays.getDisplay(Display.DEFAULT_DISPLAY));
            // A display context is not a UI context. Use the same type as LayoutParams.
            startupStage = "WINDOW_CONTEXT";
            shell = shell.createWindowContext(WINDOW_TYPE, null);
            Main main = new Main(shell);
            main.out.println("GB_EDGE_INFO Android " + Build.VERSION.SDK_INT + "; helper 0.2.1");
            main.out.println("GB_EDGE_READY 1");
            Thread input = new Thread(() -> main.readCommands(), "GalaxyBridgeEdgeCommands");
            input.setDaemon(true); input.start();
            startupStage = "WINDOW_LOOP";
            try { Looper.loop(); } finally { main.remove(); }
            System.exit(0);
        } catch (Throwable failure) {
            reportFailure(startupStage, failure);
            System.exit(1);
        }
    }

    /** Fill framework metadata for the existing shell UID; no package is installed or impersonated. */
    private static void prepareShellApplication(Class<?> threadClass, Object thread, Context shell) throws Exception {
        ApplicationInfo info = shell.getApplicationInfo();
        if (info.uid != Process.myUid()) throw new SecurityException("SHELL_UID_MISMATCH");
        // systemMain() initializes the framework Application, but leaves AppBindData absent.
        // OEM UI services also consult currentPackageName() and the Application context.
        Class<?> bindingClass = Class.forName("android.app.ActivityThread$AppBindData");
        Constructor<?> constructor = bindingClass.getDeclaredConstructor();
        constructor.setAccessible(true);
        Object binding = constructor.newInstance();
        Field appInfo = bindingClass.getDeclaredField("appInfo");
        appInfo.setAccessible(true); appInfo.set(binding, info);
        Field processName = bindingClass.getDeclaredField("processName");
        processName.setAccessible(true); processName.set(binding, info.packageName);
        Field bound = threadClass.getDeclaredField("mBoundApplication");
        bound.setAccessible(true); bound.set(thread, binding);
        // Populate LoadedApk too: assigning only mInitialApplication leaves
        // shell.getApplicationContext() null, which OEM View services cannot use.
        Field loadedPackage = Class.forName("android.app.ContextImpl").getDeclaredField("mPackageInfo");
        loadedPackage.setAccessible(true);
        Object loaded = loadedPackage.get(shell);
        Application application = (Application) loaded.getClass()
            .getMethod("makeApplication", boolean.class, Instrumentation.class).invoke(loaded, true, null);
        Field initial = threadClass.getDeclaredField("mInitialApplication");
        initial.setAccessible(true); initial.set(thread, application);
        if (shell.getApplicationContext() == null) throw new IllegalStateException("SHELL_APPLICATION_CONTEXT");
    }

    private static void reportFailure(String stage, Throwable failure) {
        Throwable cause = failure;
        for (int i = 0; i < 8 && cause instanceof InvocationTargetException && cause.getCause() != null; i++)
            cause = cause.getCause();
        String message = cause.getMessage();
        message = message == null ? "" : message.replaceAll("[\\p{Cntrl}]+", " ").trim();
        if (message.length() > 300) message = message.substring(0, 300);
        System.out.println("GB_EDGE_ERROR " + stage + "_" + cause.getClass().getSimpleName()
            + (message.isEmpty() ? "" : ": " + message));
        System.out.flush();
        // This process only handles the edge window; the stack contains no clipboard/file payloads.
        cause.printStackTrace(System.err); System.err.flush();
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
            stage = "COMMAND";
            String[] p = text.trim().split(" +");
            if (p.length == 1 && p[0].equals("QUIT")) { quit(); return; }
            if (p.length == 3 && p[0].equals("START")) {
                int id = Integer.parseInt(p[1]), location = Integer.parseInt(p[2]);
                if (id < 1 || location < 0 || location > 3) return;
                remove(); epoch = id; side = location; show();
            } else if (p.length == 2 && p[0].equals("STOP") && Integer.parseInt(p[1]) == epoch) remove();
        } catch (Exception failure) {
            reportFailure(stage, failure);
            quit();
        }
    }

    @SuppressWarnings("deprecation")
    private void show() {
        stage = "VIEW_CREATE";
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
            WINDOW_TYPE, flags, PixelFormat.TRANSLUCENT);
        // Phone right/left/top/bottom -> phone left/right/bottom/top return edge.
        layout.gravity = side == 0 ? Gravity.TOP | Gravity.LEFT : side == 1 ? Gravity.TOP | Gravity.RIGHT
            : side == 2 ? Gravity.BOTTOM | Gravity.LEFT : Gravity.TOP | Gravity.LEFT;
        layout.setFitInsetsTypes(0);
        layout.layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_ALWAYS;
        layout.setTitle("Galaxy Bridge return edge");
        edge = view;
        stage = "WINDOW_ADD";
        try { windows.addView(view, layout); }
        catch (RuntimeException failure) { edge = null; throw failure; }
        out.println("GB_EDGE_ACTIVE " + capture);
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
        stage = "WINDOW_REMOVE";
        cancelPending(); View previous = edge; edge = null; epoch = 0;
        if (previous != null) windows.removeViewImmediate(previous);
    }
    private void quit() {
        if (quitting) return;
        quitting = true;
        try { remove(); } finally { Looper.getMainLooper().quitSafely(); }
    }
}
