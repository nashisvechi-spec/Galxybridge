package com.galaxybridge.lan;

import android.app.Service;
import android.content.*;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.*;
import java.util.*;

/** Signature-protected IPC. Galaxy Files supplies known URIs, not launcher drag payloads. */
public final class FilesBridgeService extends Service {
    private static FilesBridgeService instance;
    private final Handler main=new Handler(Looper.getMainLooper());
    private final Messenger endpoint=new Messenger(new Handler(Looper.getMainLooper(),this::receive));
    private Messenger client;
    private int owner=-1;
    private String batch="",lastStatus="";
    private boolean holding,handed;
    private int buttons,epoch,side,width,height;
    private float x,y;
    private long holdSerial,holdStarted;
    private final FilesDragMotion motion=new FilesDragMotion();
    private final Runnable tick=new Runnable() { public void run() {
        if(client==null) return;
        if(holding && (buttons!=1 || !ConnectionService.edgeFilesReady() || !foreground() || SystemClock.elapsedRealtime()-holdStarted>300000)) cancelDrag();
        if(holding && batch.equals(ConnectionService.dragReadyBatch) && atEdge() && motion.outward(side)) handoff();
        if(holding && !ConnectionService.uploading() && !batch.equals(ConnectionService.dragReadyBatch)) holding=false;
        String status=ConnectionService.fileStatus;
        if(holding && batch.equals(ConnectionService.dragReadyBatch)) status="Файлы подготовлены. Держите кнопку и продолжайте тянуть к краю со стороны ПК, затем отпустите в папке Windows.";
        if(!status.equals(lastStatus)) { lastStatus=status;Bundle data=new Bundle();data.putString("status",status);reply(102,data); }
        main.postDelayed(this,200);
    }};
    @Override public void onCreate() { super.onCreate();instance=this; }
    @Override public IBinder onBind(Intent intent) { return endpoint.getBinder(); }
    private boolean receive(Message message) {
        // Permission protects bind; this also validates each message's actual UID.
        String[] packages=getPackageManager().getPackagesForUid(message.sendingUid);
        if(packages==null || !Arrays.asList(packages).contains("com.galaxybridge.files") ||
            getPackageManager().checkSignatures(getApplicationInfo().uid,message.sendingUid)!=PackageManager.SIGNATURE_MATCH) return true;
        if(message.what==1 && message.replyTo!=null) {
            cancelDrag();client=message.replyTo;owner=message.sendingUid;lastStatus="";
            main.removeCallbacks(tick);main.post(tick);return true;
        }
        if(message.sendingUid!=owner) return true;
        switch(message.what) {
            case 2:cancelDrag();client=null;owner=-1;main.removeCallbacks(tick);break;
            case 3:begin(message.getData());break;
            case 4:if(holding) ConnectionService.cancelFiles();cancelDrag();break;
        }
        return true;
    }
    private boolean foreground() {
        ControlService control=ControlService.instance;return control!=null && control.filesForeground();
    }
    private void begin(Bundle data) {
        if(client==null || buttons!=1 || data.getLong("hold")!=holdSerial || !foreground()) return;
        if(!ConnectionService.edgeFilesReady() || ConnectionService.uploading()) {
            Bundle result=new Bundle();result.putString("status","Для перетаскивания включите «Перенос файлов с телефона через край» в Galaxy Bridge на ПК, подключите Wi-Fi и дождитесь окончания отправки.");reply(102,result);return;
        }
        ArrayList<Uri> files=data.getParcelableArrayList("files",Uri.class);
        if(files==null || files.isEmpty() || files.size()>100) return;
        for(Uri uri:files) if(uri==null || !"content".equals(uri.getScheme()) ||
            !"com.galaxybridge.files.shared".equals(uri.getAuthority()) ||
            checkUriPermission(uri,android.os.Process.myPid(),android.os.Process.myUid(),Intent.FLAG_GRANT_READ_URI_PERMISSION)!=PackageManager.PERMISSION_GRANTED) return;
        cancelDrag();batch=UUID.randomUUID().toString().replace("-","");handed=false;
        holding=ConnectionService.prepareEdgeFiles(files,batch);
        if(!holding) batch="";
    }
    private boolean atEdge() { return side==0?x<16:side==1?x>=width-16:side==2?y>=height-16:y<16; }
    private void handoff() {
        String line=String.format(Locale.ROOT,"GB_EDGE %d 1 %d %d %.2f %.2f 1",epoch,width,height,x,y);
        if(ConnectionService.startFileDrag(batch,line)) { handed=true;holding=false;Bundle data=new Bundle();data.putString("status","Отпустите файлы в папке Windows. Оригиналы остаются на телефоне.");reply(102,data); }
        else cancelDrag();
    }
    private void cancelDrag() {
        if(!batch.isEmpty() && !handed) {
            if(holding && ConnectionService.uploading()) ConnectionService.cancelFiles();
            ConnectionService.discardEdgeFiles(batch);
        }
        batch="";holding=false;handed=false;
    }
    private boolean pointer(float px,float py,int next,int previous,int wheel,int requestedSide,int id,int w,int h,int dx,int dy) {
        if(client==null || !foreground()) return false;
        if(holding && (epoch!=id || side!=requestedSide || width!=w || height!=h)) cancelDrag();
        if(epoch!=id || side!=requestedSide || width!=w || height!=h) motion.reset();
        motion.update(dx,dy);
        if(next==1 && previous!=1) { ++holdSerial;holdStarted=SystemClock.elapsedRealtime(); }
        x=px;y=py;buttons=next;side=requestedSide;epoch=id;width=w;height=h;
        Bundle data=new Bundle();data.putFloat("x",x);data.putFloat("y",y);data.putInt("buttons",next);
        data.putInt("previous",previous);data.putInt("wheel",wheel);
        data.putLong("hold",holdSerial);
        if(!reply(101,data)) return false;
        if(next!=1 && holding) cancelDrag();
        if(holding && atEdge() && motion.outward(side) && batch.equals(ConnectionService.dragReadyBatch)) handoff();
        return true;
    }
    private boolean reply(int what,Bundle data) {
        if(client==null) return false;
        try { Message message=Message.obtain(null,what);message.setData(data);client.send(message);return true; }
        catch(RemoteException e) { cancelDrag();client=null;owner=-1;main.removeCallbacks(tick);return false; }
    }
    static boolean route(float x,float y,int next,int previous,int wheel,int side,int epoch,int width,int height,int dx,int dy) {
        return instance!=null && instance.pointer(x,y,next,previous,wheel,side,epoch,width,height,dx,dy);
    }
    static boolean suppressEdge() {
        // Never let the ordinary edge-return consume a held file before staging is ready.
        return instance!=null && instance.client!=null && instance.foreground() && instance.buttons==1;
    }
    static void resetPointer() {
        if(instance!=null) { instance.cancelDrag();instance.motion.reset();instance.buttons=0;instance.reply(103,new Bundle()); }
    }
    static void disableDrag() { if(instance!=null) instance.cancelDrag(); }
    @Override public boolean onUnbind(Intent intent) { cancelDrag();client=null;owner=-1;main.removeCallbacks(tick);return false; }
    @Override public void onDestroy() { cancelDrag();main.removeCallbacks(tick);if(instance==this) instance=null;super.onDestroy(); }
}
