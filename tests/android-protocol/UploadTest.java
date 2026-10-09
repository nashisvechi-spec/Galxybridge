package com.galaxybridge.lan;
import java.io.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;
import org.json.JSONObject;

public final class UploadTest {
    private static int checks;
    private static void check(boolean value) { if(!value) throw new AssertionError("Upload check "+checks); checks++; }
    private interface Throwing { void run() throws Exception; }
    private static void reject(Throwing action) throws Exception { boolean rejected=false; try { action.run(); } catch(Exception e) { rejected=true; } check(rejected); }
    private static final class Peer implements Upload.Transport {
        final ByteArrayOutputStream received=new ByteArrayOutputStream();
        String transfer, name; long expected; int sequence, aborted; boolean finished;
        final List<String> frames=new ArrayList<>();
        @Override public String request(JSONObject message,int timeout,AtomicBoolean cancel) throws Exception {
            Upload.check(cancel); String type=message.getString("type"); frames.add(type);
            String id=message.getString("transfer"); check(Wire.hex(id,32));
            if(type.equals("uploadAbort")) { aborted++; return ""; }
            if(type.equals("uploadBegin")) { transfer=id; name=message.getString("name"); expected=message.getLong("size"); return ""; }
            check(id.equals(transfer));
            if(type.equals("uploadChunk")) {
                check(message.getInt("seq")==sequence++); byte[] data=Base64.getDecoder().decode(message.getString("data"));
                check(data.length>0 && data.length<=Wire.CHUNK); received.write(data); return "";
            }
            check(type.equals("uploadEnd")); check(message.getLong("size")==received.size());
            check(message.getString("sha256").equals(Wire.hex(MessageDigest.getInstance("SHA-256").digest(received.toByteArray()))));
            check(expected==-1 || expected==received.size()); finished=true; return name;
        }
    }
    public static void main(String[] args) throws Exception {
        byte[] data=new byte[70000]; new Random(42).nextBytes(data);
        for(long size:new long[]{data.length,-1}) {
            Peer peer=new Peer(); final long[] progress={0};
            String saved=Upload.send(new ByteArrayInputStream(data),"Фото 🌍.jpg",size,peer,new AtomicBoolean(),(sent,total) -> progress[0]=sent);
            check(saved.equals("Фото 🌍.jpg")); check(Arrays.equals(peer.received.toByteArray(),data));
            check(peer.sequence==3 && peer.finished && peer.aborted==0 && progress[0]==data.length);
        }
        Peer empty=new Peer(); Upload.send(new ByteArrayInputStream(new byte[0]),"empty",0,empty,new AtomicBoolean(),(a,b) -> {});
        check(empty.finished && empty.sequence==0);
        Peer shortFile=new Peer(); reject(() -> Upload.send(new ByteArrayInputStream(new byte[]{1}),"short",2,shortFile,new AtomicBoolean(),(a,b) -> {})); check(shortFile.aborted==1 && !shortFile.finished);
        Peer grows=new Peer(); reject(() -> Upload.send(new ByteArrayInputStream(new byte[]{1}),"grows",0,grows,new AtomicBoolean(),(a,b) -> {})); check(grows.aborted==1 && grows.sequence==0);
        Peer canceled=new Peer(); AtomicBoolean stop=new AtomicBoolean();
        reject(() -> Upload.send(new ByteArrayInputStream(data),"cancel",data.length,canceled,stop,(a,b) -> stop.set(true))); check(canceled.aborted==1 && !canceled.finished);
        Peer interrupted=new Peer(); AtomicBoolean interruptedFlag=new AtomicBoolean();
        reject(() -> Upload.send(new ByteArrayInputStream(data),"interrupt",data.length,interrupted,interruptedFlag,(a,b) -> Thread.currentThread().interrupt()));
        check(Thread.interrupted()); check(interrupted.aborted==1);
        Peer failed=new Peer(); Upload.Transport deny=(message,timeout,cancel) -> {
            if(message.getString("type").equals("uploadChunk")) throw new IOException("Denied");
            return failed.request(message,timeout,cancel);
        };
        reject(() -> Upload.send(new ByteArrayInputStream(data),"denied",data.length,deny,new AtomicBoolean(),(a,b) -> {})); check(failed.aborted==1 && !failed.finished);
        Peer limit=new Peer(); reject(() -> Upload.send(new ByteArrayInputStream(new byte[0]),"huge",Wire.MAX_FILE+1,limit,new AtomicBoolean(),(a,b) -> {})); check(limit.frames.isEmpty());
        reject(() -> Upload.send(new ByteArrayInputStream(new byte[0]),"../bad",0,limit,new AtomicBoolean(),(a,b) -> {})); check(limit.frames.isEmpty());
        System.out.println("PASS: "+checks+" actual Java upload assertions (JVM only)");
    }
}
