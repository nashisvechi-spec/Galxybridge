package com.galaxybridge.lan;

import android.content.*;
import android.net.Uri;
import android.os.Environment;
import android.provider.MediaStore;
import android.util.Base64;
import java.io.*;
import java.security.MessageDigest;
import java.util.Arrays;
import org.json.JSONObject;

final class ResumableDownloads implements AutoCloseable {
    private final Context context;
    private final File cache;
    private String transfer,name,checksum;
    private long size;
    private int sequence;
    private RandomAccessFile output;
    private JSONObject metadata;
    long offset;boolean done;
    ResumableDownloads(Context context) {
        this.context=context;
        String host=context.getSharedPreferences("lan",Context.MODE_PRIVATE).getString("hostId","");
        cache=new File(context.getCacheDir(),"resume-"+(Wire.hex(host,32)?host:"paired"));
    }
    private File part() { return new File(cache,transfer+".part"); }
    private File manifest() { return new File(cache,transfer+".json"); }
    String accept(JSONObject command) throws Exception {
        String type=command.getString("type"),id=command.getString("transfer");
        if(!Wire.hex(id,32)) throw new IOException("Invalid transfer");
        if("fileAbort".equals(type)) { if(id.equals(transfer)) cancel();return ""; }
        if("fileBegin".equals(type)) return begin(command,id);
        if(!id.equals(transfer) || output==null) throw new IOException("Unknown resume transfer");
        try {
            if("fileChunk".equals(type)) {
                String encoded=command.getString("data");if(encoded.length()>43692 || command.getInt("seq")!=sequence) throw new IOException("Invalid chunk");
                byte[] data=Base64.decode(encoded,Base64.NO_WRAP);
                if(data.length<1 || data.length>Wire.CHUNK || output.length()+data.length>size) throw new IOException("Invalid size");
                output.write(data);offset=output.getFilePointer();sequence++;return "";
            }
            if(!"fileEnd".equals(type) || output.length()!=size || !checksum.equals(command.getString("sha256"))) throw new IOException("Invalid completed file");
            output.getFD().sync();output.seek(0);MessageDigest digest=MessageDigest.getInstance("SHA-256");byte[] buffer=new byte[Wire.CHUNK];int n;
            while((n=output.read(buffer))!=-1) digest.update(buffer,0,n);
            if(!MessageDigest.isEqual(digest.digest(),hex(checksum))) throw new IOException("File checksum mismatch");
            ContentValues values=new ContentValues();values.put(MediaStore.Downloads.DISPLAY_NAME,name);values.put(MediaStore.Downloads.MIME_TYPE,"application/octet-stream");
            values.put(MediaStore.Downloads.RELATIVE_PATH,Environment.DIRECTORY_DOWNLOADS+"/GalaxyBridge/");values.put(MediaStore.Downloads.IS_PENDING,1);
            Uri row=context.getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI,values);if(row==null) throw new IOException("Cannot create download");
            boolean published=false;
            try {
                output.seek(0);try(OutputStream destination=context.getContentResolver().openOutputStream(row,"w")) {
                    if(destination==null) throw new IOException("Cannot open download");while((n=output.read(buffer))!=-1) destination.write(buffer,0,n);
                }
                values.clear();values.put(MediaStore.Downloads.IS_PENDING,0);
                if(context.getContentResolver().update(row,values,null,null)!=1) throw new IOException("Cannot publish download");
                published=true;
                try(android.database.Cursor cursor=context.getContentResolver().query(row,new String[]{MediaStore.Downloads.DISPLAY_NAME},null,null,null)) { if(cursor!=null && cursor.moveToFirst()) name=cursor.getString(0); }
                metadata.put("saved",name);writeMetadata();output.close();output=null;part().delete();
                TransferHistory.add(context,"ПК → телефон",name,"Получен",row.toString(),"");
                ReceivedFiles.notifySaved(context,name);return name;
            } finally { if(!published) context.getContentResolver().delete(row,null,null); }
        } catch(Exception e) { cancel();throw e; }
    }
    private String begin(JSONObject command,String id) throws Exception {
        if(output!=null) { if(!id.equals(transfer)) throw new IOException("Another file is active");close(); }
        String supplied=command.getString("name"),hash=command.getString("sha256");long length=command.getLong("size");
        if(!Wire.name(supplied) || !Wire.hex(hash,64) || length<0 || length>Wire.MAX_FILE) throw new IOException("Invalid file");
        if(!cache.isDirectory() && !cache.mkdirs()) throw new IOException("Cannot create resume cache");prune();
        transfer=id;name=supplied;checksum=hash;size=length;metadata=null;done=false;
        if(manifest().isFile() && manifest().length()<16384) try(FileInputStream input=new FileInputStream(manifest())) {
            byte[] bytes=new byte[(int)manifest().length()];new DataInputStream(input).readFully(bytes);metadata=new JSONObject(new String(bytes,java.nio.charset.StandardCharsets.UTF_8));
        }
        if(metadata!=null && (!name.equals(metadata.getString("name")) || !checksum.equals(metadata.getString("hash")) || size!=metadata.getLong("size"))) throw new IOException("Transfer identity mismatch");
        if(metadata==null) metadata=Wire.message("metadata","name",name,"hash",checksum,"size",size);
        if(metadata.has("saved")) { done=true;offset=size;return metadata.getString("saved"); }
        output=new RandomAccessFile(part(),"rw");if(output.length()>size) { cancel();throw new IOException("Invalid partial size"); }
        output.seek(output.length());offset=output.length();sequence=0;writeMetadata();return "";
    }
    private void writeMetadata() throws Exception {
        File pending=new File(cache,transfer+".json.tmp");
        try(FileOutputStream stream=new FileOutputStream(pending)) { stream.write(metadata.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));stream.getFD().sync(); }
        if(!pending.renameTo(manifest())) throw new IOException("Cannot save resume metadata");
    }
    private void prune() {
        File[] files=cache.listFiles((dir,n) -> n.endsWith(".json") && Wire.hex(n.substring(0,n.length()-5),32));if(files==null) return;
        Arrays.sort(files,(a,b) -> Long.compare(b.lastModified(),a.lastModified()));
        for(int i=0;i<files.length;i++) if(i>=10 || System.currentTimeMillis()-files[i].lastModified()>86400000L) {
            new File(cache,files[i].getName().replace(".json",".part")).delete();files[i].delete();
        }
    }
    private static byte[] hex(String text) { byte[] bytes=new byte[text.length()/2];for(int i=0;i<bytes.length;i++) bytes[i]=(byte)Integer.parseInt(text.substring(i*2,i*2+2),16);return bytes; }
    void cancel() throws IOException { close();if(transfer!=null) { part().delete();manifest().delete(); }transfer=null; }
    @Override public void close() throws IOException { if(output!=null) { output.close();output=null; } }
}
