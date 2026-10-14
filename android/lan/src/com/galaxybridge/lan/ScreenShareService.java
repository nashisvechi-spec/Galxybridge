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
import java.io.ByteArrayOutputStream;
import java.nio.ByteBuffer;
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
    private long lastFrame,awaitingSince;
    private String awaiting;
    private boolean cleaned;
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
            active.worker.post(() -> { if(frame.equals(active.awaiting)) active.awaiting=null; });
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
        } catch(RuntimeException e) { status(owner,request,"error","Не удалось начать показ экрана. Повторите запрос.");stopSelf(); }
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
            Point dimensions=new Point();getSystemService(DisplayManager.class).getDisplay(Display.DEFAULT_DISPLAY).getRealSize(dimensions);
            setSize(dimensions.x,dimensions.y);reader=createReader();
            display=projection.createVirtualDisplay("GalaxyBridge-phone",width,height,getResources().getConfiguration().densityDpi,DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,reader.getSurface(),null,worker);
            status(owner,request,"active","Экран телефона подключён. Нажатия и перетаскивание работают при включённых специальных возможностях.");
            worker.post(heartbeat);
        } catch(RuntimeException e) { status(owner,request,"error","Android остановил захват экрана. Запросите показ заново.");stopSelf(); }
    }
    private void setSize(int w,int h) {
        if(w<1 || h<1) throw new IllegalArgumentException("Invalid screen size");
        double scale=Math.min(1,960d/Math.max(w,h));width=Math.max(1,(int)Math.round(w*scale));height=Math.max(1,(int)Math.round(h*scale));revision++;
    }
    private ImageReader createReader() {
        ImageReader result=ImageReader.newInstance(width,height,PixelFormat.RGBA_8888,2);result.setOnImageAvailableListener(this::frame,worker);return result;
    }
    private void resize(int w,int h) {
        try { setSize(w,h);ImageReader old=reader;reader=createReader();display.resize(width,height,getResources().getConfiguration().densityDpi);display.setSurface(reader.getSurface());if(old!=null) old.close();awaiting=null; }
        catch(RuntimeException e) { stopSelf(); }
    }
    private final Runnable heartbeat=new Runnable() { public void run() {
        if(!running) return;if(ConnectionService.screenTransferBusy(owner)) awaitingSince=SystemClock.elapsedRealtime();if(!ConnectionService.screenOwner(owner) || awaiting!=null && SystemClock.elapsedRealtime()-awaitingSince>8000) { stopSelf();return; }
        worker.postDelayed(this,1000);
    }};
    private void frame(ImageReader source) {
        if(source!=reader) return;
        try(Image image=source.acquireLatestImage()) {
            if(image==null || !running || ConnectionService.screenTransferBusy(owner) || awaiting!=null || SystemClock.elapsedRealtime()-lastFrame<250 || !ConnectionService.screenOwner(owner)) return;
            lastFrame=SystemClock.elapsedRealtime();Image.Plane plane=image.getPlanes()[0];int padded=plane.getRowStride()/plane.getPixelStride();
            ByteBuffer buffer=plane.getBuffer();int needed=plane.getRowStride()*height;
            if(buffer.remaining()<needed) { ByteBuffer complete=ByteBuffer.allocate(needed);complete.put(buffer);complete.rewind();buffer=complete; }
            Bitmap raw=Bitmap.createBitmap(padded,height,Bitmap.Config.ARGB_8888),cropped=null;
            try {
                raw.copyPixelsFromBuffer(buffer);cropped=Bitmap.createBitmap(raw,0,0,width,height);
                ByteArrayOutputStream bytes=new ByteArrayOutputStream();cropped.compress(Bitmap.CompressFormat.JPEG,55,bytes);byte[] jpeg=bytes.toByteArray();if(jpeg.length>512000) return;
                String frame=UUID.randomUUID().toString().replace("-","");List<JSONObject> packets=new ArrayList<>();
                packets.add(Wire.message("screenBegin","request",request,"frame",frame,"width",width,"height",height,"revision",revision,"size",jpeg.length));
                int sequence=0;for(int offset=0;offset<jpeg.length;offset+=Wire.CHUNK) packets.add(Wire.message("screenChunk","request",request,"frame",frame,"seq",sequence++,"data",java.util.Base64.getEncoder().encodeToString(Arrays.copyOfRange(jpeg,offset,Math.min(jpeg.length,offset+Wire.CHUNK)))));
                packets.add(Wire.message("screenEnd","request",request,"frame",frame));
                if(ConnectionService.screenPackets(owner,packets)) { awaiting=frame;awaitingSince=SystemClock.elapsedRealtime(); }
            } finally { if(cropped!=null && cropped!=raw) cropped.recycle();raw.recycle(); }
        } catch(RuntimeException e) { if(running) stopSelf(); }
    }
    private void release() {
        if(cleaned) return;cleaned=true;
        if(worker!=null) worker.removeCallbacksAndMessages(null);
        if(display!=null) { display.release();display=null; }if(reader!=null) { reader.close();reader=null; }
        if(projection!=null) { projection.stop();projection=null; }
        if(thread!=null) thread.quitSafely();
    }
    @Override public void onDestroy() {
        running=false;if(ControlService.instance!=null) ControlService.instance.stopScreenGestures();status(owner,request,"stopped","Показ экрана завершён. Для нового показа подтвердите запрос на телефоне.");
        if(instance==this) instance=null;if(request!=null && request.equals(pending)) { pending=null;pendingOwner=null; }
        if(worker!=null) worker.post(this::release);else release();stopForeground(STOP_FOREGROUND_REMOVE);super.onDestroy();
    }
    @Override public IBinder onBind(Intent intent) { return null; }
}
