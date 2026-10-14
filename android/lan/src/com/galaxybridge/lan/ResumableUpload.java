package com.galaxybridge.lan;

import java.io.*;
import java.security.MessageDigest;
import java.util.Base64;
import java.util.concurrent.atomic.AtomicBoolean;
import org.json.JSONObject;

final class ResumableUpload {
    static final class BusyException extends IOException { BusyException() { super("Завершается предыдущая попытка приёма."); } }
    interface Source { InputStream open() throws Exception; }
    interface Transport { JSONObject request(JSONObject message,int timeout,AtomicBoolean cancel) throws Exception; }
    static final class Identity {
        final long size; final String hash;
        Identity(long size,String hash) { this.size=size;this.hash=hash; }
    }
    static Identity identify(Source source,AtomicBoolean cancel) throws Exception {
        MessageDigest hash=MessageDigest.getInstance("SHA-256");long size=0;byte[] buffer=new byte[Wire.CHUNK];
        try(InputStream input=source.open()) {
            if(input==null) throw new IOException("Не удалось открыть файл.");
            int length;while((length=input.read(buffer))!=-1) {
                Upload.check(cancel);if(length==0) continue;size+=length;
                if(size>Wire.MAX_FILE) throw new IOException("Размер файла ограничен 2 ГБ.");hash.update(buffer,0,length);
            }
        }
        return new Identity(size,Wire.hex(hash.digest()));
    }
    static String send(Source source,String name,Identity file,String transfer,Transport link,AtomicBoolean cancel,Upload.Progress progress) throws Exception {
        if(!Wire.name(name) || !Wire.hex(transfer,32)) throw new IOException("Недопустимый файл.");
        try {
            JSONObject reply=link.request(Wire.message("uploadBegin","transfer",transfer,"name",name,"size",file.size,"sha256",file.hash,"resume",true),20000,cancel);
            long offset=reply.getLong("offset");if(offset<0 || offset>file.size) throw new IOException("Неверная позиция докачки.");
            if(reply.optBoolean("done",false)) return reply.getString("name");
            try(InputStream input=source.open()) {
                if(input==null) throw new IOException("Не удалось открыть файл.");
                long skipped=0;byte[] buffer=new byte[Wire.CHUNK];
                while(skipped<offset) { Upload.check(cancel);int n=input.read(buffer,0,(int)Math.min(buffer.length,offset-skipped));if(n<0) throw new IOException("Исходный файл изменился.");skipped+=n; }
                long sent=offset;int sequence=0,length;progress.update(sent,file.size);
                while((length=input.read(buffer))!=-1) {
                    Upload.check(cancel);if(length==0) continue;sent+=length;
                    if(sent>file.size) throw new IOException("Исходный файл изменился.");
                    link.request(Wire.message("uploadChunk","transfer",transfer,"seq",sequence++,"data",Base64.getEncoder().encodeToString(java.util.Arrays.copyOf(buffer,length)),"resume",true),20000,cancel);
                    progress.update(sent,file.size);
                }
                if(sent!=file.size) throw new IOException("Исходный файл изменился.");
            }
            JSONObject saved=link.request(Wire.message("uploadEnd","transfer",transfer,"size",file.size,"sha256",file.hash,"resume",true),120000,cancel);
            return saved.getString("name");
        } finally {
            if(cancel.get() || Thread.currentThread().isInterrupted()) {
                boolean interrupted=Thread.interrupted();
                try { link.request(Wire.message("uploadAbort","transfer",transfer,"resume",true),2000,new AtomicBoolean()); } catch(Exception ignored) { }
                if(interrupted) Thread.currentThread().interrupt();
            }
        }
    }
}
