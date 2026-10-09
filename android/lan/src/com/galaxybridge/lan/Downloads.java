package com.galaxybridge.lan;
import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.net.Uri;
import android.os.Environment;
import android.provider.MediaStore;
import android.util.Base64;
import org.json.JSONObject;
import java.io.IOException;
import java.io.OutputStream;
import java.security.MessageDigest;

// Only app-created pending rows are touched; no broad storage permission or directory cleanup.
final class Downloads implements AutoCloseable {
    private final Context context;
    private Uri row;
    private OutputStream out;
    private String transfer;
    private long expected, received;
    private int sequence;
    private MessageDigest digest;
    Downloads(Context context) { this.context=context; }
    String accept(JSONObject command) throws Exception {
        String type=command.getString("type"), incoming=command.getString("transfer");
        if (!Wire.hex(incoming,32)) throw new IOException("Invalid transfer");
        if ("fileBegin".equals(type)) {
            if (row != null) throw new IOException("Transfer already active");
            String name=command.getString("name"); long size=command.getLong("size");
            if (!Wire.name(name) || size<0 || size>Wire.MAX_FILE) throw new IOException("Invalid file");
            ContentValues values=new ContentValues(); values.put(MediaStore.Downloads.DISPLAY_NAME,name);
            values.put(MediaStore.Downloads.MIME_TYPE,"application/octet-stream");
            values.put(MediaStore.Downloads.RELATIVE_PATH,Environment.DIRECTORY_DOWNLOADS+"/GalaxyBridge/");
            values.put(MediaStore.Downloads.IS_PENDING,1);
            row=context.getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI,values);
            if (row==null) throw new IOException("Cannot create file");
            out=context.getContentResolver().openOutputStream(row,"w"); if(out==null) throw new IOException("Cannot open file");
            transfer=incoming; expected=size; received=0; sequence=0; digest=MessageDigest.getInstance("SHA-256"); return "";
        }
        if (!incoming.equals(transfer) || row==null) {
            if ("fileAbort".equals(type)) return "";
            throw new IOException("Unknown transfer");
        }
        if ("fileChunk".equals(type)) {
            String encoded=command.getString("data");
            if (encoded.length()>43692 || command.getInt("seq")!=sequence) throw new IOException("Invalid chunk");
            byte[] data=Base64.decode(encoded,Base64.NO_WRAP);
            if(data.length<1 || data.length>Wire.CHUNK || received+data.length>expected) throw new IOException("Invalid chunk");
            out.write(data); digest.update(data); received+=data.length; sequence++; return "";
        }
        if ("fileEnd".equals(type)) {
            if(received!=expected || !Wire.hex(command.getString("sha256"),64) || !Wire.hex(digest.digest()).equals(command.getString("sha256"))) throw new IOException("File checksum mismatch");
            out.close(); out=null;
            String name="";
            try (Cursor cursor=context.getContentResolver().query(row,new String[]{MediaStore.Downloads.DISPLAY_NAME},null,null,null)) {
                if(cursor!=null && cursor.moveToFirst()) name=cursor.getString(0);
            }
            ContentValues values=new ContentValues(); values.put(MediaStore.Downloads.IS_PENDING,0);
            if(context.getContentResolver().update(row,values,null,null)!=1) throw new IOException("Cannot publish file");
            row=null; transfer=null; return name;
        }
        if ("fileAbort".equals(type)) { close(); return ""; }
        throw new IOException("Unknown file command");
    }
    @Override public void close() {
        if(out!=null) { try { out.close(); } catch(IOException ignored){} out=null; }
        if(row!=null) { try { context.getContentResolver().delete(row,null,null); } catch(RuntimeException ignored){} row=null; }
        transfer=null;
    }
}
