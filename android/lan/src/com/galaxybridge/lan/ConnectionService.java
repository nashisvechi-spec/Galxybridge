package com.galaxybridge.lan;

import android.app.*;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ServiceInfo;
import android.database.Cursor;
import android.provider.OpenableColumns;
import android.net.*;
import android.os.*;
import org.json.JSONObject;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.security.cert.X509Certificate;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;
import javax.net.ssl.*;

public final class ConnectionService extends Service {
    public static volatile String status="Подключение выключено.";
    public static volatile String fileStatus="Выберите файлы или используйте «Поделиться» → Galaxy Bridge Wi-Fi.";
    private static volatile ConnectionService instance;
    private final Handler main=new Handler(Looper.getMainLooper());
    private final AtomicReference<Pairing> pending=new AtomicReference<>();
    private volatile long qrDeadline;
    private volatile boolean running;
    private volatile Link link;
    private volatile Socket connecting;
    private Thread worker;
    private volatile Thread uploadWorker;
    private final AtomicBoolean uploadBusy=new AtomicBoolean();
    private volatile AtomicBoolean uploadCancel=new AtomicBoolean();
    private final AtomicReference<InputStream> uploadInput=new AtomicReference<>();
    private ConnectivityManager manager;
    private final ConnectivityManager.NetworkCallback changes=new ConnectivityManager.NetworkCallback() {
        @Override public void onLost(Network network) { drop(); synchronized(ConnectionService.this) { ConnectionService.this.notifyAll(); } }
        @Override public void onAvailable(Network network) { synchronized(ConnectionService.this) { ConnectionService.this.notifyAll(); } }
    };
    private SharedPreferences prefs() { return getSharedPreferences("lan",MODE_PRIVATE); }
    @Override public void onCreate() { super.onCreate(); instance=this; manager=getSystemService(ConnectivityManager.class); }
    @Override public int onStartCommand(Intent intent,int flags,int startId) {
        if(intent!=null && "stop".equals(intent.getAction())) { prefs().edit().putBoolean("enabled",false).commit(); stopSelf(); return START_NOT_STICKY; }
        if(intent==null && !prefs().getBoolean("enabled",false)) { stopSelf(); return START_NOT_STICKY; }
        NotificationManager notifications=getSystemService(NotificationManager.class);
        notifications.createNotificationChannel(new NotificationChannel("lan","Galaxy Bridge Wi-Fi",NotificationManager.IMPORTANCE_LOW));
        PendingIntent open=PendingIntent.getActivity(this,0,new Intent(this,SetupActivity.class),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
        PendingIntent stop=PendingIntent.getService(this,1,new Intent(this,ConnectionService.class).setAction("stop"),PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
        Notification notification=new Notification.Builder(this,"lan").setContentTitle("Galaxy Bridge Wi-Fi")
            .setContentText("Подключение к сохранённому ноутбуку включено").setSmallIcon(android.R.drawable.ic_menu_share).setContentIntent(open)
            .addAction(new Notification.Action.Builder(null,"Остановить",stop).build()).setOngoing(true).build();
        startForeground(61,notification,ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE);
        if(intent!=null && intent.hasExtra("qr")) {
            try { pending.set(new Pairing(intent.getStringExtra("qr"))); qrDeadline=SystemClock.elapsedRealtime()+120000; drop(); }
            catch(IllegalArgumentException e) { status="Недопустимый QR. Покажите новый код на ноутбуке."; stopSelf(); return START_NOT_STICKY; }
        }
        prefs().edit().putBoolean("enabled",true).commit();
        if(!running) {
            running=true;
            manager.registerNetworkCallback(new NetworkRequest.Builder().addTransportType(NetworkCapabilities.TRANSPORT_WIFI).build(),changes);
            worker=new Thread(this::connectLoop,"GalaxyBridge-LAN"); worker.start();
        }
        synchronized(this) { notifyAll(); }
        if(intent!=null && intent.hasExtra("files")) {
            ArrayList<Uri> files=intent.getParcelableArrayListExtra("files",Uri.class);
            if(files!=null) startUpload(files);
        }
        return START_STICKY;
    }
    private Network wifi() {
        for(Network network:manager.getAllNetworks()) {
            NetworkCapabilities caps=manager.getNetworkCapabilities(network);
            if(caps!=null && caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) return network;
        }
        return null;
    }
    private void connectLoop() {
        int retry=0;
        while(running) {
            Pairing pair=pending.get();
            if(pair!=null && SystemClock.elapsedRealtime()>qrDeadline) { pending.compareAndSet(pair,null); pair=null; status="QR истёк. Покажите новый код на ноутбуке."; }
            try {
                Network network=wifi();
                if(network==null) { status="Ожидаем Wi-Fi…"; waitRetry(2000); continue; }
                SharedPreferences prefs=prefs();
                String hostId=pair!=null?pair.id:prefs.getString("hostId","");
                String pin=pair!=null?pair.pin:prefs.getString("pin","");
                String host=pair!=null?pair.host:prefs.getString("host","");
                if(!Wire.hex(hostId,32) || !Wire.hex(pin,64)) { status="Сначала отсканируйте QR ноутбука."; waitRetry(2000); continue; }
                LinkedHashSet<String> candidates=new LinkedHashSet<>(); if(Wire.ipv4(host)) candidates.add(host);
                // Pair only at the address encoded in the current QR. Reconnect discovers changed addresses.
                if(pair==null) candidates.addAll(discover(network,hostId));
                Exception last=null;
                for(String candidate:candidates) {
                    if(!running) break;
                    try { connect(network,candidate,hostId,pin,pair); last=null; retry=0; break; }
                    catch(Exception e) { last=e; }
                }
                if(last!=null || candidates.isEmpty()) status=pair!=null?"Сопряжение не завершено. Проверьте сеть и брандмауэр; при истечении кода покажите новый QR.":"Ожидаем сохранённый ноутбук в этой сети…";
            } catch(Exception e) { if(running) status="Связь прервалась. Ищем сохранённый ноутбук…"; }
            finally { drop(); main.post(() -> { if(ConnectionService.instance==this && link==null && ControlService.instance!=null) ControlService.instance.reset(); }); }
            if(running) waitRetry(Math.min(30000,1000L<<Math.min(retry++,5)));
        }
    }
    private void waitRetry(long delay) { synchronized(this) { try { wait(delay); } catch(InterruptedException ignored){} } }
    private List<String> discover(Network network,String id) throws Exception {
        List<String> hosts=new ArrayList<>(); LinkProperties properties=manager.getLinkProperties(network); if(properties==null) return hosts;
        Set<InetAddress> broadcasts=new HashSet<>(); broadcasts.add(InetAddress.getByName("255.255.255.255"));
        for(LinkAddress local:properties.getLinkAddresses()) {
            if(!(local.getAddress() instanceof Inet4Address) || local.getPrefixLength()<8 || local.getPrefixLength()>30) continue;
            byte[] b=local.getAddress().getAddress(); int address=((b[0]&255)<<24)|((b[1]&255)<<16)|((b[2]&255)<<8)|(b[3]&255);
            int broadcast=address|(-1>>>local.getPrefixLength());
            broadcasts.add(InetAddress.getByAddress(new byte[]{(byte)(broadcast>>>24),(byte)(broadcast>>>16),(byte)(broadcast>>>8),(byte)broadcast}));
        }
        String nonce=UUID.randomUUID().toString().replace("-",""); byte[] request=("GB_DISCOVER1 "+id+" "+nonce).getBytes(StandardCharsets.US_ASCII);
        try(DatagramSocket udp=new DatagramSocket()) {
            network.bindSocket(udp); udp.setBroadcast(true); udp.setSoTimeout(300);
            for(InetAddress destination:broadcasts) udp.send(new DatagramPacket(request,request.length,destination,Wire.DISCOVERY));
            long end=SystemClock.elapsedRealtime()+1200; int packets=0;
            while(running && SystemClock.elapsedRealtime()<end && packets++<24) {
                byte[] buffer=new byte[160]; DatagramPacket reply=new DatagramPacket(buffer,buffer.length);
                try { udp.receive(reply); } catch(SocketTimeoutException e) { continue; }
                if(reply.getPort()!=Wire.DISCOVERY || !(reply.getAddress() instanceof Inet4Address)) continue;
                String[] fields=new String(reply.getData(),0,reply.getLength(),StandardCharsets.US_ASCII).split(" ");
                String address=reply.getAddress().getHostAddress();
                if(fields.length==4 && fields[0].equals("GB_HERE1") && fields[1].equals(id) && fields[2].equals(nonce) && fields[3].equals(Integer.toString(Wire.PORT)) && Wire.ipv4(address) && !hosts.contains(address)) hosts.add(address);
                if(hosts.size()>=4) break;
            }
        }
        return hosts;
    }
    private void connect(Network network,String host,String hostId,String pin,Pairing pair) throws Exception {
        Socket raw=network.getSocketFactory().createSocket(); connecting=raw;
        try {
            raw.connect(new InetSocketAddress(InetAddress.getByName(host),Wire.PORT),2000); raw.setSoTimeout(6000); raw.setTcpNoDelay(true);
            X509TrustManager trust=new X509TrustManager() {
                public X509Certificate[] getAcceptedIssuers() { return new X509Certificate[0]; }
                public void checkClientTrusted(X509Certificate[] c,String a) throws java.security.cert.CertificateException { throw new java.security.cert.CertificateException("Client certificate not used"); }
                public void checkServerTrusted(X509Certificate[] c,String a) throws java.security.cert.CertificateException {
                    try {
                        if(c.length==0 || !Wire.hex(MessageDigest.getInstance("SHA-256").digest(c[0].getEncoded())).equals(pin)) throw new java.security.cert.CertificateException("Pin mismatch");
                        c[0].checkValidity();
                    } catch(java.security.cert.CertificateException e) { throw e; } catch(Exception e) { throw new java.security.cert.CertificateException(e); }
                }
            };
            SSLContext tls=SSLContext.getInstance("TLS"); tls.init(null,new TrustManager[]{trust},new SecureRandom());
            try(SSLSocket socket=(SSLSocket)tls.getSocketFactory().createSocket(raw,host,Wire.PORT,true)) {
                connecting=socket; socket.setEnabledProtocols(new String[]{"TLSv1.2"}); socket.startHandshake();
                SharedPreferences prefs=prefs(); String device=prefs.getString("device","");
                if(!Wire.hex(device,32)) { device=UUID.randomUUID().toString().replace("-",""); if(!prefs.edit().putString("device",device).commit()) throw new IOException("Cannot save identity"); }
                DataInputStream in=new DataInputStream(socket.getInputStream()); DataOutputStream out=new DataOutputStream(socket.getOutputStream());
                JSONObject hello=Wire.message(pair!=null?"pair":"hello","v",1,"device",device,"name",Build.MODEL);
                if(pair!=null) hello.put("ticket",pair.ticket); else hello.put("token",prefs.getString("token",""));
                Wire.write(out,hello); JSONObject welcome=Wire.read(in);
                if(!"welcome".equals(welcome.getString("type")) || welcome.getInt("v")!=1 || !hostId.equals(welcome.getString("host"))) throw new IOException("Invalid welcome");
                if(pair!=null) {
                    String token=welcome.getString("token"); if(!Wire.hex(token,64)) throw new IOException("Invalid token");
                    if(!prefs.edit().putString("hostId",hostId).putString("pin",pin).putString("token",token).putString("host",host).putString("name",pair.name).commit()) throw new IOException("Cannot save pairing");
                    pending.compareAndSet(pair,null);
                } else prefs.edit().putString("host",host).apply();
                Wire.write(out,Wire.message("saved"));
                socket.setSoTimeout(12000); Link active=new Link(socket,out,welcome.optBoolean("upload",false)); link=active; connecting=null;
                status="Подключён: "+prefs.getString("name","ноутбук"); active.writer.start();
                try(Downloads downloads=new Downloads(this)) {
                    while(running && !active.closed.get()) {
                        JSONObject command=Wire.read(in); String type=command.getString("type");
                        if("ping".equals(type)) {
                            active.send(Wire.message("pong","ready",ControlService.ready(this)));
                            main.post(() -> { if(link==active && ControlService.instance!=null) ControlService.instance.poll(); });
                        } else if("uploadAck".equals(type)) {
                            active.ack(command);
                        } else if(type.startsWith("file")) {
                            String request=command.getString("id"); if(!Wire.hex(request,32)) throw new IOException("Invalid request");
                            try { String name=downloads.accept(command); active.send(Wire.message("ack","id",request,"ok",true,"name",name)); }
                            catch(Exception e) { downloads.close(); active.send(Wire.message("ack","id",request,"ok",false)); }
                        } else {
                            if(!Arrays.asList("capture","release","mouse","text","key","edit","nav").contains(type)) throw new IOException("Unknown command");
                            if(!active.commands.tryAcquire()) throw new IOException("Input queue full");
                            main.post(() -> {
                                try { if(link==active && ControlService.instance!=null) ControlService.instance.accept(command); }
                                finally { active.commands.release(); }
                            });
                        }
                    }
                } finally {
                    if(link==active) link=null; active.close(); active.writer.join(1000);
                    main.post(() -> { if(ConnectionService.instance==this && link==null && ControlService.instance!=null) ControlService.instance.reset(); });
                }
            }
        } finally { try { raw.close(); } catch(IOException ignored){} if(connecting==raw) connecting=null; }
    }
    static void disconnect() { ConnectionService current=instance; if(current!=null) current.drop(); }
    static void send(JSONObject message) { ConnectionService current=instance; if(current!=null) { Link active=current.link; if(active!=null) active.send(message); } }
    static boolean uploading() { ConnectionService current=instance; return current!=null && current.uploadBusy.get(); }
    static void cancelFiles() { ConnectionService current=instance; if(current!=null) current.cancelUpload(); }
    private void cancelUpload() {
        uploadCancel.set(true);
        InputStream input=uploadInput.getAndSet(null); if(input!=null) try { input.close(); } catch(IOException ignored) { }
        Thread thread=uploadWorker; if(thread!=null) thread.interrupt();
    }
    private void startUpload(ArrayList<Uri> files) {
        if(files.isEmpty() || files.size()>100) { fileStatus="Выберите от 1 до 100 файлов."; return; }
        for(Uri uri:files) if(uri==null || !"content".equals(uri.getScheme())) { fileStatus="Недопустимый файл. Используйте системный выбор файлов."; return; }
        if(!uploadBusy.compareAndSet(false,true)) { fileStatus="Дождитесь завершения текущей отправки."; return; }
        AtomicBoolean cancel=new AtomicBoolean(); uploadCancel=cancel;
        fileStatus="Ожидаем подключение к ноутбуку…";
        uploadWorker=new Thread(() -> {
            int completed=0;
            try {
                long deadline=SystemClock.elapsedRealtime()+30000; Link target;
                while((target=link)==null) {
                    Upload.check(cancel);
                    if(!running || SystemClock.elapsedRealtime()>deadline) throw new IOException("Ноутбук не подключён. Проверьте общую сеть и повторите отправку.");
                    synchronized(ConnectionService.this) { ConnectionService.this.wait(200); }
                }
                if(!target.upload) throw new IOException("Обновите Windows Galaxy Bridge до 0.7.0 для приёма файлов.");
                final Link destination=target;
                for(Uri uri:files) {
                    Upload.check(cancel);
                    if(link!=destination || destination.closed.get()) throw new IOException("Связь прервалась. Очередь не повторяется автоматически.");
                    String name="file-"+System.currentTimeMillis()+".bin"; long size=-1;
                    try(Cursor cursor=getContentResolver().query(uri,new String[]{OpenableColumns.DISPLAY_NAME,OpenableColumns.SIZE},null,null,null)) {
                        if(cursor!=null && cursor.moveToFirst()) {
                            int n=cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME), s=cursor.getColumnIndex(OpenableColumns.SIZE);
                            if(n>=0 && !cursor.isNull(n)) name=cursor.getString(n);
                            if(s>=0 && !cursor.isNull(s)) size=cursor.getLong(s);
                        }
                    }
                    if(name==null || !Wire.name(name)) name="file-"+System.currentTimeMillis()+".bin";
                    if(size<0) size=-1;
                    final String display=name; final int number=completed+1;
                    fileStatus="Отправка "+number+" из "+files.size()+": "+display;
                    try(InputStream input=getContentResolver().openInputStream(uri)) {
                        if(input==null) throw new IOException("Не удалось открыть файл.");
                        uploadInput.set(input); Upload.check(cancel);
                        Upload.send(input,name,size,destination::request,cancel,(sent,total) -> {
                            fileStatus="Отправка "+number+" из "+files.size()+": "+display+"\n"+
                                (total>0?Math.min(100,sent*100/total)+"% · ":"")+sent/1024+" КБ";
                        });
                    } finally { uploadInput.set(null); }
                    completed++;
                }
                fileStatus="Отправлено файлов: "+completed+". Папка приёма указана в окне файлов Windows.";
            } catch(Exception e) {
                fileStatus=(cancel.get()?"Отправка отменена.":"Отправка остановлена: "+
                    (e instanceof IOException?e.getMessage():"Проверьте доступ к файлу и соединение."))+"\nОтправлено: "+completed+".";
            } finally { uploadInput.set(null); uploadWorker=null; uploadBusy.set(false); }
        },"GalaxyBridge-upload");
        uploadWorker.start();
    }
    private static final class Link {
        final Socket socket; final DataOutputStream out;
        final AtomicBoolean closed=new AtomicBoolean(); final ArrayBlockingQueue<JSONObject> queue=new ArrayBlockingQueue<>(256);
        final Semaphore commands=new Semaphore(128); final Thread writer;
        final boolean upload;
        final ConcurrentHashMap<String,CompletableFuture<JSONObject>> replies=new ConcurrentHashMap<>();
        Link(Socket socket,DataOutputStream out,boolean upload) { this.socket=socket; this.out=out; this.upload=upload; writer=new Thread(() -> {
            try { while(!closed.get()) Wire.write(out,queue.take()); } catch(Exception e) { close(); }
        },"GalaxyBridge-send"); }
        void send(JSONObject message) { if(!closed.get() && !queue.offer(message)) close(); }
        void ack(JSONObject message) throws Exception {
            String id=message.getString("id"); if(!Wire.hex(id,32)) throw new IOException("Invalid upload reply");
            CompletableFuture<JSONObject> result=replies.remove(id); if(result!=null) result.complete(message);
        }
        String request(JSONObject message,int timeout,AtomicBoolean cancel) throws Exception {
            Upload.check(cancel); if(closed.get()) throw new IOException("Ноутбук отключён.");
            String id=UUID.randomUUID().toString().replace("-",""); message.put("id",id);
            CompletableFuture<JSONObject> result=new CompletableFuture<>(); replies.put(id,result);
            try {
                send(message); long deadline=System.nanoTime()+TimeUnit.MILLISECONDS.toNanos(timeout);
                while(true) {
                    Upload.check(cancel);
                    if(closed.get()) throw new IOException("Связь прервалась.");
                    long remaining=deadline-System.nanoTime(); if(remaining<=0) throw new IOException("ПК не подтвердил приём файла.");
                    try {
                        JSONObject reply=result.get(Math.min(remaining,TimeUnit.MILLISECONDS.toNanos(200)),TimeUnit.NANOSECONDS);
                        if(!reply.getBoolean("ok")) throw new IOException(reply.optString("error","ПК не принял файл."));
                        return reply.optString("name","");
                    } catch(TimeoutException again) { }
                }
            } finally { replies.remove(id); }
        }
        void close() { if(closed.compareAndSet(false,true)) { try { socket.close(); } catch(IOException ignored){} writer.interrupt(); } }
    }
    private void drop() {
        Link active=link; if(active!=null) active.close(); Socket socket=connecting;
        if(socket!=null) { try { socket.close(); } catch(IOException ignored){} }
    }
    @Override public void onDestroy() {
        running=false; cancelUpload(); pending.set(null); drop(); synchronized(this) { notifyAll(); }
        try { manager.unregisterNetworkCallback(changes); } catch(RuntimeException ignored){}
        if(ControlService.instance!=null) ControlService.instance.reset();
        status="Подключение выключено."; if(instance==this) instance=null; stopForeground(STOP_FOREGROUND_REMOVE); super.onDestroy();
    }
    @Override public IBinder onBind(Intent intent) { return null; }
}
