package com.galaxybridge.files;

import android.content.*;
import android.database.Cursor;
import android.os.*;
import android.provider.MediaStore;
import java.io.*;
import java.util.*;

/** Indexed categories plus a bounded fallback for files not scanned by Android yet. */
final class FileCatalog {
    static List<File> load(Context context,FileOps ops,FileKind category,String query,boolean hidden,int limit) {
        LinkedHashSet<File> results=new LinkedHashSet<>();
        Bundle args=new Bundle();args.putStringArray(ContentResolver.QUERY_ARG_SORT_COLUMNS,new String[]{MediaStore.MediaColumns.DATE_MODIFIED});
        args.putInt(ContentResolver.QUERY_ARG_SORT_DIRECTION,ContentResolver.QUERY_SORT_DIRECTION_DESCENDING);args.putInt(ContentResolver.QUERY_ARG_LIMIT,category==null?80:5000);
        if(category==FileKind.IMAGE || category==FileKind.VIDEO || category==FileKind.AUDIO) {
            int type=category==FileKind.IMAGE?MediaStore.Files.FileColumns.MEDIA_TYPE_IMAGE:category==FileKind.VIDEO?MediaStore.Files.FileColumns.MEDIA_TYPE_VIDEO:MediaStore.Files.FileColumns.MEDIA_TYPE_AUDIO;
            args.putString(ContentResolver.QUERY_ARG_SQL_SELECTION,MediaStore.Files.FileColumns.MEDIA_TYPE+" = ?");args.putStringArray(ContentResolver.QUERY_ARG_SQL_SELECTION_ARGS,new String[]{Integer.toString(type)});
        }
        try(Cursor cursor=context.getContentResolver().query(MediaStore.Files.getContentUri("external"),new String[]{MediaStore.MediaColumns.DATA},args,null)) {
            if(cursor!=null) while(cursor.moveToNext()) { String path=cursor.getString(0);if(path!=null) add(results,ops,new File(path),category,query,hidden); }
        } catch(RuntimeException ignored) { }
        // Include newly created files and work without a MediaStore provider (e.g. tests).
        int[] budget={6000};Set<File> visited=new HashSet<>();
        scan(results,ops,ops.root,category,query,hidden,false,budget,visited);
        for(String name:new String[]{"DCIM","Pictures","Movies","Music","Documents","Download"}) {
            if(budget[0]<=0) break;scan(results,ops,new File(ops.root,name),category,query,hidden,true,budget,visited);
        }
        ArrayList<File> sorted=new ArrayList<>(results);sorted.sort(Comparator.comparingLong(File::lastModified).reversed().thenComparing(File::getName,String.CASE_INSENSITIVE_ORDER));
        return new ArrayList<>(sorted.subList(0,Math.min(limit,sorted.size())));
    }
    private static void add(Set<File> results,FileOps ops,File file,FileKind category,String query,boolean hidden) {
        try {
            file=ops.checked(file);if(!file.isFile() || (!hidden && isHidden(ops.root,file)) ||
                (category!=null && !category.matches(file)) || !file.getName().toLowerCase(Locale.ROOT).contains(query.toLowerCase(Locale.ROOT))) return;
            results.add(file);
        } catch(IOException | SecurityException ignored) { }
    }
    private static boolean isHidden(File root,File file) {
        for(java.nio.file.Path part:root.toPath().relativize(file.toPath())) if(part.toString().startsWith(".")) return true;
        return false;
    }
    private static void scan(Set<File> result,FileOps ops,File folder,FileKind category,String query,boolean hidden,boolean recurse,int[] budget,Set<File> visited) {
        try {
            folder=ops.checked(folder);if(!folder.isDirectory() || !visited.add(folder)) return;
            File[] children=folder.listFiles();if(children==null) return;
            for(File child:children) {
                if(--budget[0]<0 || Thread.currentThread().isInterrupted()) return;
                if(!hidden && child.getName().startsWith(".")) continue;
                if(child.isDirectory()) { if(recurse) scan(result,ops,child,category,query,hidden,true,budget,visited); }
                else add(result,ops,child,category,query,hidden);
            }
        } catch(IOException | SecurityException ignored) { }
    }
}
