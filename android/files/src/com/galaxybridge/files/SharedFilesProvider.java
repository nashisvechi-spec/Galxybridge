package com.galaxybridge.files;

import android.content.*;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.*;
import android.provider.OpenableColumns;
import android.webkit.MimeTypeMap;
import java.io.*;
import java.util.*;

/** Grants expose one checked file, read-only, without making the provider public. */
public final class SharedFilesProvider extends ContentProvider {
    static final String AUTHORITY="com.galaxybridge.files.shared";
    static Uri uri(File file) throws IOException {
        FileOps ops=new FileOps(Environment.getExternalStorageDirectory());file=ops.checked(file);
        if(!file.isFile()) throw new IOException("Выберите файл, а не папку.");
        return new Uri.Builder().scheme("content").authority(AUTHORITY).appendPath("file")
            .appendPath(ops.root.toPath().relativize(file.toPath()).toString()).build();
    }
    private File file(Uri uri) throws IOException {
        if(!"content".equals(uri.getScheme()) || !AUTHORITY.equals(uri.getAuthority()) || uri.getPathSegments().size()!=2 ||
            !"file".equals(uri.getPathSegments().get(0))) throw new FileNotFoundException("Недопустимый адрес файла.");
        FileOps ops=new FileOps(Environment.getExternalStorageDirectory());
        File result=ops.checked(new File(ops.root,uri.getPathSegments().get(1)));
        if(!result.isFile()) throw new FileNotFoundException("Файл недоступен.");return result;
    }
    static String mime(File file) {
        String name=file.getName();int dot=name.lastIndexOf('.');String type=dot<0?null:
            MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substring(dot+1).toLowerCase(Locale.ROOT));
        return type==null?"application/octet-stream":type;
    }
    @Override public boolean onCreate() { return true; }
    @Override public String getType(Uri uri) { try { return mime(file(uri)); } catch(IOException e) { return "application/octet-stream"; } }
    @Override public Cursor query(Uri uri,String[] projection,String selection,String[] args,String sort) {
        try {
            File file=file(uri);String[] columns=projection==null?new String[]{OpenableColumns.DISPLAY_NAME,OpenableColumns.SIZE}:projection;
            MatrixCursor cursor=new MatrixCursor(columns);Object[] row=new Object[columns.length];
            for(int i=0;i<columns.length;i++) row[i]=OpenableColumns.DISPLAY_NAME.equals(columns[i])?file.getName():OpenableColumns.SIZE.equals(columns[i])?file.length():null;
            cursor.addRow(row);return cursor;
        } catch(IOException e) { return null; }
    }
    @Override public ParcelFileDescriptor openFile(Uri uri,String mode) throws FileNotFoundException {
        if(!"r".equals(mode)) throw new FileNotFoundException("Доступ только для чтения.");
        try { return ParcelFileDescriptor.open(file(uri),ParcelFileDescriptor.MODE_READ_ONLY); }
        catch(IOException e) { throw new FileNotFoundException(e.getMessage()); }
    }
    @Override public Uri insert(Uri uri,ContentValues values) { throw new UnsupportedOperationException(); }
    @Override public int update(Uri uri,ContentValues values,String selection,String[] args) { throw new UnsupportedOperationException(); }
    @Override public int delete(Uri uri,String selection,String[] args) { throw new UnsupportedOperationException(); }
}
