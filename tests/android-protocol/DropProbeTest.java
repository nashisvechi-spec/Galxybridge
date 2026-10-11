package com.galaxybridge.lan;

import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.atomic.AtomicBoolean;

public final class DropProbeTest {
    private static int checks;
    private static void check(boolean value,String reason) { if(!value) throw new AssertionError(reason);checks++; }
    private static class Sample extends InputStream {
        int reads,closes;boolean empty,fail;
        public int read() throws IOException { reads++;if(fail) throw new IOException("read failed");return empty?-1:42; }
        public void close() { closes++; }
    }
    private static void rejects(List<Integer> files,DropProbe.Access<Integer> access,AtomicBoolean cancel,String reason) {
        try { DropProbe.inspect(files,access,cancel);throw new AssertionError(reason); }
        catch(IOException expected) { checks++; }
    }
    public static void main(String[] args) throws Exception {
        AtomicBoolean cancel=new AtomicBoolean();Sample first=new Sample(),second=new Sample();second.empty=true;
        List<DropProbe.Entry> result=DropProbe.inspect(Arrays.asList(1,2),i -> new DropProbe.Source(i==1?"photo.jpg":"empty.txt",i==1?120:0,i==1?first:second),cancel);
        check(result.size()==2 && result.get(0).name.equals("photo.jpg") && result.get(0).size==120,"metadata retained after actual reading");
        check(!result.get(0).empty && result.get(1).empty,"EOF is valid for an empty file");
        check(first.reads==1 && second.reads==1,"large files are probed with only one byte");
        check(first.closes==1 && second.closes==1,"all probed streams close");
        Sample unknown=new Sample();result=DropProbe.inspect(Collections.singletonList(1),i -> new DropProbe.Source(null,-100,unknown),cancel);
        check(result.get(0).name.equals("Файл 1") && result.get(0).size==-1,"missing metadata remains explicitly unknown");
        Sample controls=new Sample();result=DropProbe.inspect(Collections.singletonList(1),i -> new DropProbe.Source("file\n\u0000.txt",1,controls),cancel);
        check(result.get(0).name.equals("file  .txt"),"provider labels cannot insert control characters into report");
        StringBuilder longName=new StringBuilder();for(int i=0;i<1000;i++) longName.append('a');
        result=DropProbe.inspect(Collections.singletonList(1),i -> new DropProbe.Source(longName.toString(),-1,new ByteArrayInputStream(new byte[]{1})),cancel);
        check(result.get(0).name.length()==160,"display names are bounded");
        int[] opens={0};DropProbe.Access<Integer> counted=i -> { opens[0]++;return new DropProbe.Source("a",1,new Sample()); };
        rejects(Collections.emptyList(),counted,cancel,"empty drop rejected");
        ArrayList<Integer> many=new ArrayList<>();for(int i=0;i<101;i++) many.add(i);
        rejects(many,counted,cancel,"oversized count rejected");check(opens[0]==0,"count limits checked before opening a provider");
        many.remove(100);check(DropProbe.inspect(many,counted,cancel).size()==100,"100-file boundary allowed");
        Sample big=new Sample();rejects(Collections.singletonList(1),i -> new DropProbe.Source("big",DropProbe.MAX_BYTES+1,big),cancel,"individual size limit enforced");
        check(big.reads==0 && big.closes==1,"oversized file stream closes without reading");
        Sample a=new Sample(),b=new Sample();rejects(Arrays.asList(1,2),i -> new DropProbe.Source("big",i==1?DropProbe.MAX_BYTES:1,i==1?a:b),cancel,"total limit enforced");
        check(a.reads==1 && b.reads==0 && a.closes==1 && b.closes==1,"total limit does not leak later stream");
        Sample boundary=new Sample();
        check(DropProbe.inspect(Collections.singletonList(1),i -> new DropProbe.Source("limit",DropProbe.MAX_BYTES,boundary),cancel).size()==1,"exact 2GB boundary allowed");
        Sample failed=new Sample();failed.fail=true;rejects(Collections.singletonList(1),i -> new DropProbe.Source("bad",1,failed),cancel,"read failure cannot look like success");
        check(failed.closes==1,"failed read releases its stream");
        rejects(Collections.singletonList(1),i -> null,cancel,"provider crash/null source rejected");
        rejects(Collections.singletonList(1),i -> new DropProbe.Source("a",1,null),cancel,"missing stream rejected");
        cancel.set(true);int previous=opens[0];rejects(Collections.singletonList(1),counted,cancel,"cancel before provider access");
        check(opens[0]==previous,"cancelled probe never opens provider");cancel.set(false);
        Sample opened=new Sample();rejects(Collections.singletonList(1),i -> { cancel.set(true);return new DropProbe.Source("a",1,opened); },cancel,"cancel while opening");
        check(opened.reads==0 && opened.closes==1,"cancel after opening closes before reading");cancel.set(false);
        Sample during=new Sample() { @Override public int read() { reads++;cancel.set(true);return 42; } };
        rejects(Collections.singletonList(1),i -> new DropProbe.Source("a",1,during),cancel,"cancel while reading");
        check(during.closes==1,"cancel after read closes stream");cancel.set(false);
        try { DropProbe.inspect(Collections.singletonList(1),i -> { throw new SecurityException(); },cancel);throw new AssertionError("denied access"); }
        catch(SecurityException expected) { check(true,"permission denial propagates distinctly"); }
        System.out.println("PASS: "+checks+" dropped-file read-access assertions");
    }
}
