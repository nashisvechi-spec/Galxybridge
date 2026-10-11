package com.galaxybridge.files;

import java.io.*;
import java.nio.file.*;
import java.util.*;

/** Local operations are confined to shared storage; copying never overwrites. */
final class FileOps {
    final File root;
    FileOps(File root) throws IOException { this.root=root.getCanonicalFile(); }
    File checked(File file) throws IOException {
        File canonical=file.getCanonicalFile();
        if(!canonical.equals(root) && !canonical.toPath().startsWith(root.toPath())) throw new IOException("Файл за пределами общей памяти.");
        String relative=root.toPath().relativize(canonical.toPath()).toString().replace(File.separatorChar,'/');
        if(relative.equals("Android/data") || relative.startsWith("Android/data/") ||
            relative.equals("Android/obb") || relative.startsWith("Android/obb/")) throw new IOException("Эта папка защищена Android.");
        if(Files.isSymbolicLink(file.toPath())) throw new IOException("Символические ссылки не поддерживаются.");
        return canonical;
    }
    File child(File folder,String name) throws IOException {
        if(name==null || name.trim().isEmpty() || name.equals(".") || name.equals("..") ||
            name.indexOf('/')>=0 || name.indexOf('\\')>=0 || name.indexOf('\0')>=0 || name.length()>240)
            throw new IOException("Укажите допустимое имя без разделителей пути.");
        return checked(new File(checked(folder),name));
    }
    List<File> list(File folder,String query,boolean hidden) throws IOException {
        File[] children=checked(folder).listFiles();
        if(children==null) throw new IOException("Не удалось прочитать папку. Проверьте разрешение на доступ к файлам.");
        List<File> result=new ArrayList<>();String term=query.toLowerCase(Locale.ROOT);
        for(File f:children) {
            if(!hidden && f.getName().startsWith(".")) continue;
            if(!f.getName().toLowerCase(Locale.ROOT).contains(term)) continue;
            try { result.add(checked(f)); } catch(IOException ignored) { }
        }
        result.sort(Comparator.comparing((File f)->!f.isDirectory()).thenComparing(File::getName,String.CASE_INSENSITIVE_ORDER));
        return result;
    }
    void mkdir(File folder,String name) throws IOException { Files.createDirectory(child(folder,name).toPath()); }
    void rename(File file,String name) throws IOException {
        file=mutable(file);File target=child(file.getParentFile(),name);
        if(target.exists()) throw new IOException("Такое имя уже существует.");
        Files.move(file.toPath(),target.toPath());
    }
    private File mutable(File file) throws IOException {
        file=checked(file);if(file.equals(root)) throw new IOException("Нельзя изменять корень памяти.");return file;
    }
    interface Progress { void update(String name,long bytes) throws IOException; }
    File transfer(File source,File folder,boolean move,Progress progress) throws IOException {
        source=mutable(source);folder=checked(folder);
        if(!folder.isDirectory()) throw new IOException("Папка назначения недоступна.");
        if(folder.toPath().startsWith(source.toPath())) throw new IOException("Нельзя копировать папку внутрь себя.");
        if(move && folder.equals(source.getParentFile())) throw new IOException("Файл уже в этой папке.");
        File target=available(folder,source.getName());
        progress.update(source.getName(),0);
        if(move) { Files.move(source.toPath(),target.toPath());return target; }
        // Build a complete copy in a sibling staging directory, then publish it.
        File staging=Files.createTempDirectory(folder.toPath(),".galaxy-files-").toFile();
        File payload=new File(staging,source.getName());
        try {
            copy(source,payload,progress,new long[]{0});
            Files.move(payload.toPath(),target.toPath());return target;
        } finally { removeStaging(staging); }
    }
    private File available(File folder,String name) throws IOException {
        File result=child(folder,name);int dot=name.lastIndexOf('.');
        String base=dot>0?name.substring(0,dot):name,ext=dot>0?name.substring(dot):"";
        for(int i=1;result.exists();i++) {
            if(i>10000) throw new IOException("Слишком много одинаковых имён.");
            result=child(folder,base+" ("+i+")"+ext);
        }
        return result;
    }
    private void copy(File source,File target,Progress progress,long[] bytes) throws IOException {
        source=checked(source);progress.update(source.getName(),bytes[0]);
        if(source.isDirectory()) {
            Files.createDirectory(target.toPath());File[] children=source.listFiles();
            if(children==null) throw new IOException("Не удалось прочитать "+source.getName());
            for(File file:children) copy(file,new File(target,file.getName()),progress,bytes);
        } else {
            try(InputStream in=new FileInputStream(source);OutputStream out=Files.newOutputStream(target.toPath(),StandardOpenOption.CREATE_NEW)) {
                byte[] buffer=new byte[256*1024];int count;
                while((count=in.read(buffer))!=-1) { out.write(buffer,0,count);bytes[0]+=count;progress.update(source.getName(),bytes[0]); }
            }
        }
    }
    void delete(File file) throws IOException {
        file=mutable(file);
        if(file.isDirectory()) {
            File[] children=file.listFiles();if(children==null) throw new IOException("Не удалось прочитать "+file.getName());
            for(File child:children) delete(child);
        }
        Files.delete(file.toPath());
    }
    private static void removeStaging(File file) throws IOException {
        if(file.isDirectory() && !Files.isSymbolicLink(file.toPath())) {
            File[] children=file.listFiles();if(children==null) throw new IOException("Не удалось очистить временную папку.");
            for(File child:children) removeStaging(child);
        }
        Files.deleteIfExists(file.toPath());
    }
}
