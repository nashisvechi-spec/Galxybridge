package com.galaxybridge.lan;

import android.app.*;
import android.content.*;
import android.content.pm.ServiceInfo;
import android.graphics.*;
import android.hardware.display.*;
import android.media.Image;
import android.media.ImageReader;
import android.media.projection.*;
import android.os.*;
import android.view.Display;
import java.util.*;
import org.json.JSONObject;

public final class ScreenShareService extends Service {
    private static volatile String pending;
    private static volatile Object pendingOwner;
    private static volatile long expires;
    private static volatile ScreenShareService instance;
    private Object owner;
    private String request;
    private volatile boolean running;
    private HandlerThread thread;
    private Handler worker;
    private MediaProjection projection;
    private VirtualDisplay display;
    private ImageReader reader;
    private int width,height;
    private volatile int revision;
    private long lastFrame,awaitingSince,startedAt;
    private String awaiting;
    private boolean cleaned,firstAcknowledged,surfaceRetried;
    private volatile boolean failed;
    private volatile String terminalMessage;
    static String pendingRequest() { return pending; }
    static boolean allowed(String id) { return id!=null && id.equals(pending) && SystemClock.elapsedRealtime()<expires && ConnectionService.screenOwner(pendingOwner); }
    static void request(Context c,Object owner,String id) {
        if(!Wire.hex(id,32) || !ConnectionService.screenOwner(owner)) return;
        if(instance!=null && instance.running) { status(owner,id,"error","Трансляция уже идёт. Остановите её перед новым запросом.");return; }
        pending=id;pendingOwner=owner;expires=SystemClock.elapsedRealtime()+120000;
        NotificationManager notices=c.getSystemService(NotificationManager.class);
        if(!notices.areNotificationsEnabled()) { status(owner,id,"permission","Откройте Galaxy Bridge на телефоне → Настройки → Экран телефона на ПК → Разрешить показ экрана.");return; }
        notices.createNotificationChannel(new NotificationChannel("screen_consent","Показ экрана",NotificationManager.IMPORTANCE_HIGH));
        PendingIntent open=PendingIntent.getActivity(c,112,new Intent(c,ScreenShareActivity.class),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
        try {
            notices.notify(112,new Notification.Builder(c,"screen_consent").setSmallIcon(android.R.drawable.ic_menu_view).setContentTitle("Показать экран на ПК?")
                .setContentText("Нажмите, чтобы подтвердить показ системным окном Android.").setContentIntent(open).setAutoCancel(true).setTimeoutAfter(120000).build());
            status(owner,id,"permission","Подтвердите показ экрана на телефоне: нажмите уведомление Galaxy Bridge или откройте его настройки.");
        } catch(RuntimeException e) { status(owner,id,"permission","Откройте Galaxy Bridge на телефоне → Настройки → Экран телефона на ПК."); }
    }
    static void reject(Context c,String id,String message) {
        if(id!=null && id.equals(pending)) { status(pendingOwner,id,"stopped",message);pending=null;pendingOwner=null;c.getSystemService(NotificationManager.class).cancel(112); }
    }
    static void stop(Context c,Object owner,String id) {
        if(id!=null && id.equals(pending) && owner==pendingOwner) reject(c,id,"Трансляция остановлена.");
        ScreenShareService active=instance;if(active!=null && active.owner==owner && id!=null && id.equals(active.request)) active.stopSelf();
    }
    static void lost(Context c,Object owner) {
        if(owner==pendingOwner) reject(c,pending,"Соединение потеряно. Для показа потребуется новое подтверждение.");
        ScreenShareService active=instance;if(active!=null && active.owner==owner) active.stopSelf();
    }
    static void ack(Object owner,String id,String frame) {
        ScreenShareService active=instance;if(active!=null && active.owner==owner && id.equals(active.request) && active.worker!=null)
            active.worker.post(() -> { if(frame.equals(active.awaiting)) {
                active.awaiting=null;
                if(!active.firstAcknowledged) { active.firstAcknowledged=true;status(owner,id,"active","Первый кадр получен на ПК. Экран телефона подключён."); }
            } });
    }
    static void input(Object owner,JSONObject command) {
        ScreenShareService active=instance;
        if(active==null || !active.running || active.owner!=owner || !active.request.equals(command.optString("request"))) return;
        if(command.optInt("revision",-1)!=active.revision || ConnectionService.screenTransferBusy(owner)) return;
        if(ControlService.instance!=null) {
            if("text".equals(command.optString("kind")) || "key".equals(command.optString("kind"))) {
                if(!ControlService.instance.screenEdit(command)) status(owner,active.request,"input","Выберите обычное текстовое поле на телефоне. Защищённые поля заполняйте на самом телефоне.");
            } else ControlService.instance.screenGesture(command);
        }
        else status(owner,active.request,"input","Для нажатий включите специальные возможности Galaxy Bridge.");
    }
    private static void status(Object owner,String id,String state,String text) { ConnectionService.screenPackets(owner,Collections.singletonList(Wire.message("screenStatus","request",id,"state",state,"text",text))); }
    @Override public int onStartCommand(Intent intent,int flags,int startId) {
        if(intent==null || "stop".equals(intent.getAction())) { stopSelf();return START_NOT_STICKY; }
        String id=intent.getStringExtra("request");Intent grant=intent.getParcelableExtra("grant",Intent.class);
        if(running) return START_NOT_STICKY;
        if(grant==null || intent.getIntExtra("result",0)!=Activity.RESULT_OK || !allowed(id)) { stopSelf();return START_NOT_STICKY; }
        owner=pendingOwner;request=id;instance=this;running=true;pending=null;pendingOwner=null;
        try {
            NotificationManager notices=getSystemService(NotificationManager.class);notices.cancel(112);
            notices.createNotificationChannel(new NotificationChannel("screen_active","Трансляция экрана",NotificationManager.IMPORTANCE_LOW));
            PendingIntent stop=PendingIntent.getService(this,113,new Intent(this,ScreenShareService.class).setAction("stop"),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
            Notification notice=new Notification.Builder(this,"screen_active").setSmallIcon(android.R.drawable.ic_menu_view).setContentTitle("Экран телефона виден на ПК")
                .setContentText("Нажмите «Остановить», чтобы завершить показ.").setOngoing(true).addAction(new Notification.Action.Builder(null,"Остановить",stop).build()).build();
            startForeground(113,notice,ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION);
            thread=new HandlerThread("GalaxyBridge-screen");thread.start();worker=new Handler(thread.getLooper());worker.post(() -> capture(grant));
        } catch(RuntimeException e) { fail("START",e,"Не удалось начать показ экрана."); }
        return START_NOT_STICKY;
    }
    private void capture(Intent grant) {
        try {
            if(!running || !ConnectionService.screenOwner(owner)) { stopSelf();return; }
            projection=getSystemService(MediaProjectionManager.class).getMediaProjection(Activity.RESULT_OK,grant);
            projection.registerCallback(new MediaProjection.Callback() {
                @Override public void onStop() { stopSelf(); }
                @Override public void onCapturedContentResize(int w,int h) { if(running && display!=null) resize(w,h); }
            },worker);
            Display phone=getSystemService(DisplayManager.class).getDisplay(Display.DEFAULT_DISPLAY);
            Rect bounds=createDisplayContext(phone).getSystemService(android.view.WindowManager.class).getMaximumWindowMetrics().getBounds();
            setSize(bounds.width(),bounds.height());reader=createReader();
            display=projection.createVirtualDisplay("GalaxyBridge-phone",width,height,getResources().getConfiguration().densityDpi,DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,reader.getSurface(),null,worker);
            if(display==null) throw new IllegalStateException("No virtual display");
            startedAt=SystemClock.elapsedRealtime();
            status(owner,request,"starting","Android разрешил захват. Ожидаем первый кадр телефона…");
            worker.post(framePump);worker.post(heartbeat);
        } catch(RuntimeException e) { fail("CAPTURE",e,"Android не запустил захват экрана."); }
    }
    private void setSize(int w,int h) {
        if(w<1 || h<1) throw new IllegalArgumentException("Invalid screen size");
        double scale=Math.min(1,960d/Math.max(w,h));width=Math.max(1,(int)Math.round(w*scale));height=Math.max(1,(int)Math.round(h*scale));revision++;
    }
    private ImageReader createReader() {
        ImageReader result=ImageReader.newInstance(width,height,PixelFormat.RGBA_8888,2);result.setOnImageAvailableListener(this::frame,worker);return result;
    }
    private void resize(int w,int h) {
        try {
            double scale=Math.min(1,960d/Math.max(w,h));
            if(width==Math.max(1,(int)Math.round(w*scale)) && height==Math.max(1,(int)Math.round(h*scale))) return;
            setSize(w,h);ImageReader old=reader;reader=createReader();display.resize(width,height,getResources().getConfiguration().densityDpi);display.setSurface(reader.getSurface());if(old!=null) old.close();awaiting=null;lastFrame=0;startedAt=SystemClock.elapsedRealtime();surfaceRetried=false;
        } catch(RuntimeException e) { fail("RESIZE",e,"Не удалось изменить размер трансляции."); }
    }
    private final Runnable heartbeat=new Runnable() { public void run() {
        if(!running || failed) return;
        long now=SystemClock.elapsedRealtime();
        if(!ConnectionService.screenOwner(owner)) { stopSelf();return; }
        if(ConnectionService.screenTransferBusy(owner)) { awaitingSince=now;startedAt=now; }
        else if(awaiting!=null && now-awaitingSince>8000) { fail("FRAME_ACK",null,"ПК не подтвердил получение кадра. Закройте окно и повторите показ.");return; }
        else if(lastFrame==0 && now-startedAt>15000) { fail("NO_FRAMES",null,"Android разрешил захват, но не передал изображение. Разблокируйте телефон и повторите показ.");return; }
        else if(lastFrame==0 && !surfaceRetried && now-startedAt>4000 && display!=null) {
            surfaceRetried=true;
            try { ImageReader old=reader;reader=createReader();display.setSurface(null);display.setSurface(reader.getSurface());if(old!=null) old.close(); }
            catch(RuntimeException e) { fail("SURFACE",e,"Не удалось подключить изображение экрана.");return; }
        }
        worker.postDelayed(this,1000);
    }};
    // Poll as well as listening: retain a frame skipped by the rate limiter and
    // recover when a device delivers an ImageReader callback before capture is ready.
    private final Runnable framePump=new Runnable() { public void run() {
        if(!running || failed) return;
        if(reader!=null) frame(reader);
        worker.postDelayed(this,250);
    }};
    private void frame(ImageReader source) {
        if(source!=reader || !running || failed || ConnectionService.screenTransferBusy(owner) || awaiting!=null ||
            lastFrame!=0 && SystemClock.elapsedRealtime()-lastFrame<250 || !ConnectionService.screenOwner(owner)) return;
        try(Image image=source.acquireLatestImage()) {
            if(image==null) return;
            if(image.getWidth()!=width || image.getHeight()!=height) throw new IllegalArgumentException("Capture dimensions changed");
            Image.Plane plane=image.getPlanes()[0];
            Bitmap bitmap=ScreenCaptureFrame.bitmap(plane.getBuffer(),width,height,plane.getRowStride(),plane.getPixelStride());
            try {
                byte[] jpeg=ScreenCaptureFrame.jpeg(bitmap);
                String frame=UUID.randomUUID().toString().replace("-","");List<JSONObject> packets=ScreenCaptureFrame.packets(jpeg,width,height,revision,request,frame);
                if(ConnectionService.screenPackets(owner,packets)) { awaiting=frame;awaitingSince=SystemClock.elapsedRealtime();lastFrame=awaitingSince; }
            } finally { bitmap.recycle(); }
        } catch(RuntimeException e) { if(running) fail("IMAGE",e,"Не удалось прочитать кадр телефона."); }
    }
    private void fail(String stage,RuntimeException error,String message) {
        if(failed) return;failed=true;
        terminalMessage=message+" ["+stage+(error==null?"":": "+error.getClass().getSimpleName())+"]";
        status(owner,request,"error",terminalMessage);stopSelf();
    }
    private void release() {
        if(cleaned) return;cleaned=true;
        if(worker!=null) worker.removeCallbacksAndMessages(null);
        if(display!=null) { display.release();display=null; }if(reader!=null) { reader.close();reader=null; }
        if(projection!=null) { projection.stop();projection=null; }
        if(thread!=null) thread.quitSafely();
    }
    @Override public void onDestroy() {
        running=false;if(ControlService.instance!=null) ControlService.instance.stopScreenGestures();status(owner,request,failed?"error":"stopped",terminalMessage!=null?terminalMessage:"Показ экрана завершён. Для нового показа подтвердите запрос на телефоне.");
        if(instance==this) instance=null;if(request!=null && request.equals(pending)) { pending=null;pendingOwner=null; }
        if(worker!=null) worker.post(this::release);else release();stopForeground(STOP_FOREGROUND_REMOVE);super.onDestroy();
    }
    @Override public IBinder onBind(Intent intent) { return null; }
}
