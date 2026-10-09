package com.galaxybridge.edge;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.net.LocalServerSocket;
import android.net.LocalSocket;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.provider.Settings;
import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.concurrent.ArrayBlockingQueue;

public final class EdgeService extends Service {
    private final Handler handler = new Handler(Looper.getMainLooper());
    private Run current;

    @Override public void onCreate() {
        super.onCreate();
        NotificationManager notifications = getSystemService(NotificationManager.class);
        notifications.createNotificationChannel(new NotificationChannel("edge", "Подключение Galaxy Bridge", NotificationManager.IMPORTANCE_LOW));
        PendingIntent setup = PendingIntent.getActivity(this, 0, new Intent(this, SetupActivity.class), PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        Notification notification = new Notification.Builder(this, "edge")
            .setSmallIcon(android.R.drawable.ic_menu_share).setContentTitle("Galaxy Bridge Edge")
            .setContentText("Подключение к ноутбуку. Зона включается только во время управления.")
            .setContentIntent(setup).setOngoing(true).build();
        if (Build.VERSION.SDK_INT >= 34) startForeground(1, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE);
        else startForeground(1, notification);
    }
    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        String socket = intent == null ? null : intent.getStringExtra("socket");
        String token = intent == null ? null : intent.getStringExtra("token");
        if (socket == null || !socket.matches("galaxybridge_edge_[0-9a-f]{8}") || token == null || !token.matches("[0-9a-f]{64}")) {
            if (current == null) stopSelf(startId);
            return START_NOT_STICKY;
        }
        if (current != null) current.close();
        current = new Run(socket, token, startId);
        current.begin();
        return START_NOT_STICKY;
    }
    @Override public IBinder onBind(Intent intent) { return null; }
    @Override public void onDestroy() {
        if (current != null) { current.close(); current = null; }
        stopForeground(STOP_FOREGROUND_REMOVE); super.onDestroy();
    }

    private void finish(Run run) {
        run.close();
        if (current == run) { current = null; stopSelf(run.startId); }
    }
    private static String readLine(BufferedReader input) throws IOException {
        StringBuilder line = new StringBuilder();
        int value;
        while ((value = input.read()) != -1) {
            if (value == '\n') return line.toString().trim();
            if (line.length() >= 256) throw new IOException("COMMAND_TOO_LONG");
            line.append((char) value);
        }
        return null;
    }

    private final class Run {
        final String name, token;
        final int startId;
        final ArrayBlockingQueue<String> messages = new ArrayBlockingQueue<>(128);
        volatile boolean running = true, authenticated;
        LocalServerSocket listener;
        LocalSocket client;
        Thread writer;
        EdgeWindow window;
        final Runnable idle = () -> { if (!authenticated && current == this) finish(this); };
        Run(String name, String token, int startId) { this.name = name; this.token = token; this.startId = startId; }
        void begin() {
            handler.postDelayed(idle, 15000);
            Thread worker = new Thread(this::serve, "GalaxyBridgeEdgeRead"); worker.setDaemon(true); worker.start();
        }
        void send(String line) {
            if (running && !messages.offer(line)) { closeSockets(); handler.post(() -> finish(this)); }
        }
        void fail(String stage, Exception failure) {
            String detail = failure.getMessage();
            detail = detail == null ? "" : detail.replaceAll("[\\p{Cntrl}]+", " ");
            if (detail.length() > 300) detail = detail.substring(0, 300);
            send("GB_EDGE_ERROR " + stage + "_" + failure.getClass().getSimpleName() + ": " + detail);
            handler.postDelayed(() -> finish(this), 250);
        }
        private void serve() {
            try {
                LocalServerSocket opened = new LocalServerSocket(name);
                synchronized (this) { if (!running) { opened.close(); return; } listener = opened; }
                while (running && !authenticated) {
                    LocalSocket accepted = opened.accept();
                    synchronized (this) { if (!running) { accepted.close(); return; } client = accepted; }
                    int uid = accepted.getPeerCredentials().getUid();
                    if (uid != 0 && uid != 2000) { accepted.close(); continue; }
                    accepted.setSoTimeout(3000);
                    BufferedReader input = new BufferedReader(new InputStreamReader(accepted.getInputStream(), StandardCharsets.UTF_8));
                    String hello;
                    try { hello = readLine(input); } catch (IOException rejected) { accepted.close(); continue; }
                    if (hello == null || !MessageDigest.isEqual(("HELLO 3 " + token).getBytes(StandardCharsets.UTF_8), hello.getBytes(StandardCharsets.UTF_8))) {
                        accepted.close(); continue;
                    }
                    authenticated = true; handler.removeCallbacks(idle); opened.close();
                    accepted.setSoTimeout(6000);
                    PrintWriter output = new PrintWriter(new OutputStreamWriter(accepted.getOutputStream(), StandardCharsets.UTF_8), true);
                    writer = new Thread(() -> write(output), "GalaxyBridgeEdgeWrite"); writer.setDaemon(true); writer.start();
                    handler.post(() -> {
                        if (!running || current != this) return;
                        if (!Settings.canDrawOverlays(EdgeService.this)) {
                            fail("OVERLAY_PERMISSION", new SecurityException("Откройте Galaxy Bridge Edge на S25 и разрешите показ поверх других приложений."));
                            return;
                        }
                        try {
                            window = new EdgeWindow(EdgeService.this, handler, this::send);
                            send("GB_EDGE_READY 3"); send("GB_EDGE_INFO APK 0.3.0; Android " + Build.VERSION.SDK_INT);
                        } catch (Exception failure) { fail("WINDOW_CONTEXT", failure); }
                    });
                    String command;
                    while (running && (command = readLine(input)) != null) {
                        if (command.equals("PING")) send("GB_EDGE_PONG");
                        else { final String text = command; handler.post(() -> command(text)); }
                    }
                }
            } catch (IOException failure) { /* EOF, lost ADB link, idle peer or socket close: remove the window. */ }
            finally { closeSockets(); handler.post(() -> finish(this)); }
        }
        private void write(PrintWriter output) {
            try {
                while (running) {
                    output.println(messages.take());
                    if (output.checkError()) throw new IOException("SOCKET_WRITE");
                }
            } catch (InterruptedException | IOException failure) { closeSockets(); handler.post(() -> finish(this)); }
        }
        private void command(String text) {
            if (!running || current != this) return;
            try {
                String[] p = text.split(" +");
                if (p.length == 1 && p[0].equals("QUIT")) { finish(this); return; }
                if (window == null) return;
                if (p.length == 3 && p[0].equals("START")) {
                    int id = Integer.parseInt(p[1]), side = Integer.parseInt(p[2]);
                    if (id > 0 && side >= 0 && side <= 3) window.start(id, side);
                } else if (p.length == 2 && p[0].equals("STOP")) window.stop(Integer.parseInt(p[1]));
            } catch (Exception failure) { fail("WINDOW_COMMAND", failure); }
        }
        private synchronized void closeSockets() {
            running = false;
            if (writer != null) writer.interrupt();
            try { if (client != null) client.close(); } catch (IOException ignored) { }
            try { if (listener != null) listener.close(); } catch (IOException ignored) { }
        }
        void close() {
            closeSockets(); handler.removeCallbacks(idle);
            if (window != null) {
                try { window.remove(); } catch (RuntimeException ignored) { }
                window = null;
            }
        }
    }
}
