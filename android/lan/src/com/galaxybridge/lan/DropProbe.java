package com.galaxybridge.lan;

import java.io.Closeable;
import java.io.IOException;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.atomic.AtomicBoolean;

/** Checks real read access without copying or consuming a whole large file. */
final class DropProbe {
    static final int MAX_FILES=100;
    static final long MAX_BYTES=2L*1024*1024*1024;
    interface Access<T> { Source open(T file) throws IOException; }
    static final class Source implements Closeable {
        final String name;
        final long size;
        final InputStream input;
        Source(String name,long size,InputStream input) { this.name=name;this.size=size;this.input=input; }
        public void close() throws IOException { if(input!=null) input.close(); }
    }
    static final class Entry {
        final String name;
        final long size;
        final boolean empty;
        Entry(String name,long size,boolean empty) { this.name=name;this.size=size;this.empty=empty; }
    }
    static <T> List<Entry> inspect(List<T> files,Access<T> access,AtomicBoolean cancelled) throws IOException {
        if(files.isEmpty() || files.size()>MAX_FILES) throw new IOException("Принимается от 1 до 100 файлов.");
        ArrayList<Entry> result=new ArrayList<>();
        long knownBytes=0;
        for(T file:files) {
            check(cancelled);
            try(Source source=access.open(file)) {
                check(cancelled);
                if(source==null || source.input==null) throw new IOException("Файловое приложение не открыло содержимое файла.");
                long size=source.size<0?-1:source.size;
                if(size>MAX_BYTES || (size>=0 && size>MAX_BYTES-knownBytes))
                    throw new IOException("Подборка для переноса на ПК ограничена 2 ГБ.");
                if(size>=0) knownBytes+=size;
                int first=source.input.read();
                check(cancelled);
                result.add(new Entry(label(source.name,result.size()+1),size,first==-1));
            }
        }
        return result;
    }
    private static void check(AtomicBoolean cancelled) throws IOException {
        if(cancelled.get() || Thread.currentThread().isInterrupted()) throw new IOException("Проверка отменена.");
    }
    private static String label(String name,int number) {
        if(name==null || name.trim().isEmpty()) return "Файл "+number;
        StringBuilder text=new StringBuilder();
        for(int i=0;i<name.length() && text.length()<160;i++) {
            char c=name.charAt(i);text.append(Character.isISOControl(c)?' ':c);
        }
        return text.toString();
    }
}
