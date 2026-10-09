package com.galaxybridge.lan;

import org.json.JSONObject;
import java.io.*;
import java.security.MessageDigest;
import java.util.Base64;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicBoolean;

// Stream files from a content provider without buffering the whole file or changing the TLS link.
final class Upload {
    interface Transport { String request(JSONObject message, int timeout, AtomicBoolean cancel) throws Exception; }
    interface Progress { void update(long sent, long size); }
    static String send(InputStream input, String name, long size, Transport link, AtomicBoolean cancel, Progress progress) throws Exception {
        if (!Wire.name(name) || size < -1 || size > Wire.MAX_FILE) throw new IOException("Размер файла ограничен 2 ГБ.");
        String transfer=UUID.randomUUID().toString().replace("-",""); boolean finished=false;
        try {
            check(cancel);
            link.request(Wire.message("uploadBegin","transfer",transfer,"name",name,"size",size),20000,cancel);
            MessageDigest digest=MessageDigest.getInstance("SHA-256"); byte[] buffer=new byte[Wire.CHUNK];
            long sent=0; int sequence=0, length;
            while ((length=input.read(buffer))!=-1) {
                check(cancel); if(length==0) continue;
                sent+=length;
                if(sent>Wire.MAX_FILE || (size>=0 && sent>size)) throw new IOException("Размер файла изменился или превышает 2 ГБ.");
                digest.update(buffer,0,length);
                String data=Base64.getEncoder().encodeToString(java.util.Arrays.copyOf(buffer,length));
                link.request(Wire.message("uploadChunk","transfer",transfer,"seq",sequence++,"data",data),20000,cancel);
                progress.update(sent,size);
            }
            check(cancel);
            if(size>=0 && sent!=size) throw new IOException("Размер файла изменился.");
            String saved=link.request(Wire.message("uploadEnd","transfer",transfer,"size",sent,"sha256",Wire.hex(digest.digest())),20000,cancel);
            finished=true; progress.update(sent,sent); return saved;
        } finally {
            if(!finished) {
                boolean interrupted=Thread.interrupted();
                try { link.request(Wire.message("uploadAbort","transfer",transfer),2000,new AtomicBoolean()); }
                catch(Exception ignored) { }
                finally { if(interrupted) Thread.currentThread().interrupt(); }
            }
        }
    }
    static void check(AtomicBoolean cancel) throws InterruptedIOException {
        if(cancel.get() || Thread.currentThread().isInterrupted()) throw new InterruptedIOException("Отправка отменена.");
    }
}
