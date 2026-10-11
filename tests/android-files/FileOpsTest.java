package com.galaxybridge.files;

import java.io.*;
import java.nio.file.*;
import java.util.*;

public final class FileOpsTest {
    private static int checks;
    private static void check(boolean condition,String label) { if(!condition) throw new AssertionError(label);checks++; }
    private interface Work { void run() throws IOException; }
    private static void fails(Work action,String label) throws IOException {
        try { action.run();throw new AssertionError(label); } catch(IOException expected) { checks++; }
    }
    public static void main(String[] args) throws Exception {
        Path work=Files.createTempDirectory("galaxy-files-test-");
        try {
            File root=Files.createDirectory(work.resolve("storage")).toFile();FileOps ops=new FileOps(root);
            File a=new File(root,"a.txt");Files.write(a.toPath(),"original".getBytes("UTF-8"));
            fails(() -> ops.checked(work.resolve("outside").toFile()),"outside shared storage");
            fails(() -> ops.child(root,"../outside"),"path traversal");
            fails(() -> ops.child(root,".."),"parent name");
            fails(() -> ops.checked(new File(root,"Android/data/app/private")),"protected Android data");
            fails(() -> ops.checked(new File(root,"Android/obb")),"protected Android obb");
            fails(() -> ops.delete(root),"root deletion");
            fails(() -> ops.rename(root,"renamed"),"root rename");
            ops.mkdir(root,"target");File target=new File(root,"target");
            File copy=ops.transfer(a,root,false,(n,b)->{});
            check(copy.getName().equals("a (1).txt"),"copy receives unused name");
            check(new String(Files.readAllBytes(a.toPath()),"UTF-8").equals("original"),"original intact");
            Files.write(copy.toPath(),"existing".getBytes("UTF-8"));
            File second=ops.transfer(a,root,false,(n,b)->{});
            check(second.getName().equals("a (2).txt"),"second collision");
            check(new String(Files.readAllBytes(copy.toPath()),"UTF-8").equals("existing"),"no overwrite");
            fails(() -> ops.rename(a,copy.getName()),"rename collision");
            fails(() -> ops.transfer(a,root,true,(n,b)->{}),"move into same folder");
            ops.mkdir(root,"tree");File tree=new File(root,"tree");ops.mkdir(tree,"inner");
            Files.write(new File(tree,"inner/file.bin").toPath(),new byte[]{1,2,3,4});
            fails(() -> ops.transfer(tree,new File(tree,"inner"),false,(n,b)->{}),"recursive self copy");
            File treeCopy=ops.transfer(tree,target,false,(n,b)->{});
            check(Files.size(new File(treeCopy,"inner/file.bin").toPath())==4,"recursive directory copy");
            fails(() -> ops.transfer(a,target,false,(n,b)->{ if(b>0) throw new IOException("cancelled"); }),"failed copy");
            check(!new File(target,"a.txt").exists(),"failed copy never published");
            check(Arrays.stream(target.list()).noneMatch(n -> n.startsWith(".galaxy-files-")),"staging cleanup");
            File moved=ops.transfer(a,target,true,(n,b)->{});check(!a.exists() && moved.isFile(),"move completes");
            ops.rename(moved,"проверка.txt");check(new File(target,"проверка.txt").isFile(),"Unicode rename");
            Files.write(new File(root,".hidden").toPath(),new byte[]{0});
            check(ops.list(root,"HIDDEN",false).isEmpty(),"hidden filter");
            check(ops.list(root,"HIDDEN",true).size()==1,"case insensitive search");
            List<File> list=ops.list(root,"",false);check(list.get(0).isDirectory(),"folders first");
            try {
                Files.createSymbolicLink(new File(tree,"link").toPath(),target.toPath());
                fails(() -> ops.transfer(tree,target,false,(n,b)->{}),"symlink copy rejected");
                check(!new File(target,"tree (1)").exists(),"failed tree not published");
                Files.delete(new File(tree,"link").toPath());
            } catch(UnsupportedOperationException | FileSystemException unsupported) { System.out.println("Symlink test skipped: filesystem does not support it."); }
            ops.delete(treeCopy);check(!treeCopy.exists(),"recursive delete");
            System.out.println("PASS: "+checks+" file-operation checks");
        } finally {
            try(java.util.stream.Stream<Path> paths=Files.walk(work)) { for(Path p:(Iterable<Path>)paths.sorted(Comparator.reverseOrder())::iterator) Files.deleteIfExists(p); }
        }
    }
}
