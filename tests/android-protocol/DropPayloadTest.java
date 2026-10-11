package com.galaxybridge.lan;

import android.content.ClipData;
import android.content.Intent;
import android.content.pm.LauncherApps;
import android.net.Uri;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Arrays;

/** Parser tests with JVM data fixtures, not a test of Android URI permissions. */
public final class DropPayloadTest {
    private static int checks;
    private static final String A="content://files/photo.jpg",B="content://files/movie.mp4";
    private static void check(boolean value,String reason) { if(!value) throw new AssertionError(reason);checks++; }
    private static ClipData clip(ClipData.Item item) { return new ClipData("files",new String[]{"application/octet-stream"},item); }
    private static ClipData.Item pin() { return new ClipData.Item(new Intent().putExtra(LauncherApps.EXTRA_PIN_ITEM_REQUEST,new LauncherApps.PinItemRequest())); }
    private static void accepts(ClipData data,int count,String reason) throws Exception {
        ArrayList<Uri> files=new DropPayload().extract(data);check(files.size()==count,reason);
        check(files.get(0).toString().equals(A),"original reference preserved");
    }
    private static void rejects(ClipData data,String reason) {
        try { new DropPayload().extract(data);throw new AssertionError(reason); }
        catch(IOException expected) { checks++; }
    }
    public static void main(String[] args) throws Exception {
        accepts(clip(new ClipData.Item(Uri.parse(A))),1,"direct content URI");
        accepts(clip(new ClipData.Item(new Intent().setData(Uri.parse(A)))),1,"URI inside Intent.data");
        accepts(clip(new ClipData.Item(new Intent().putExtra(Intent.EXTRA_STREAM,Uri.parse(A)))),1,"URI inside EXTRA_STREAM");
        accepts(clip(new ClipData.Item(new Intent().putExtra(Intent.EXTRA_STREAM,new ArrayList<>(Arrays.asList(Uri.parse(A),Uri.parse(B)))))),2,"multiple streams");
        accepts(clip(new ClipData.Item(new Intent().putExtra(Intent.EXTRA_STREAM,new Uri[]{Uri.parse(A),Uri.parse(B)}))),2,"stream array");
        accepts(clip(new ClipData.Item(new Intent().setClipData(clip(new ClipData.Item(Uri.parse(A)))))),1,"nested ClipData");
        accepts(clip(new ClipData.Item(new Intent().putExtra(Intent.EXTRA_INTENT,new Intent().setData(Uri.parse(A))))),1,"wrapped Intent");
        accepts(clip(new ClipData.Item("  "+A+"  ")),1,"whole content URI in text");
        accepts(clip(new ClipData.Item(B,null,Uri.parse(A))),1,"direct reference preferred over alternate text");
        accepts(clip(new ClipData.Item(new Intent().setData(Uri.parse("content://files/collection")).putExtra(Intent.EXTRA_STREAM,Uri.parse(A)))),1,"stream preferred over ancillary Intent data");
        ClipData duplicate=clip(new ClipData.Item(Uri.parse(A)));duplicate.addItem(new ClipData.Item(Uri.parse(A)));
        accepts(duplicate,1,"duplicate source items remain valid and deduplicate");
        ClipData video=new ClipData("video",new String[]{"video/mp4"},new ClipData.Item(Uri.parse(B)));video.addItem(pin());
        DropPayload samsung=new DropPayload();ArrayList<Uri> videoFiles=samsung.extract(video);
        check(videoFiles.size()==1 && videoFiles.get(0).toString().equals(B),"observed Samsung video plus PinItemRequest yields the video");
        check(samsung.diagnostic().contains("PIN_ITEM_REQUEST пропущен") && samsung.diagnostic().contains("Файлов: 1"),"diagnostic explains the ignored launcher companion");
        ClipData pinFirst=clip(pin());pinFirst.addItem(new ClipData.Item(Uri.parse(A)));
        accepts(pinFirst,1,"launcher companion before file is also ignored");
        rejects(clip(pin()),"pin request alone is not a file");
        ClipData malformedPin=clip(new ClipData.Item(Uri.parse(A)));malformedPin.addItem(new ClipData.Item(new Intent().putExtra(LauncherApps.EXTRA_PIN_ITEM_REQUEST,"not a request")));
        rejects(malformedPin,"key name without genuine PinItemRequest is not ignored");
        ClipData ambiguousPin=clip(new ClipData.Item(Uri.parse(A)));ambiguousPin.addItem(new ClipData.Item(new Intent().putExtra(LauncherApps.EXTRA_PIN_ITEM_REQUEST,new LauncherApps.PinItemRequest()).putExtra("vendor.files",new String[]{"/storage/a"})));
        rejects(ambiguousPin,"unknown additional fields are not silently discarded");
        ClipData pinFilePath=clip(new ClipData.Item(Uri.parse(A)));pinFilePath.addItem(new ClipData.Item(new Intent().putExtra(LauncherApps.EXTRA_PIN_ITEM_REQUEST,new LauncherApps.PinItemRequest()).setData(Uri.parse("file:///storage/a"))));
        rejects(pinFilePath,"file-bearing intent is not classified as launcher-only");
        ClipData pinPlainText=clip(new ClipData.Item(Uri.parse(A)));pinPlainText.addItem(new ClipData.Item("not a reference",new Intent().putExtra(LauncherApps.EXTRA_PIN_ITEM_REQUEST,new LauncherApps.PinItemRequest()),null));
        rejects(pinPlainText,"unsupported text beside a pin request remains an error");
        ClipData pinAndFile=clip(new ClipData.Item(Uri.parse(A)));pinAndFile.addItem(new ClipData.Item(null,new Intent().putExtra(LauncherApps.EXTRA_PIN_ITEM_REQUEST,new LauncherApps.PinItemRequest()),Uri.parse(B)));
        accepts(pinAndFile,2,"a real file URI beside launcher metadata is retained");
        accepts(clip(new ClipData.Item(new Intent().setClipData(pinFirst))),1,"nested file and launcher companion accepted");
        rejects(clip(new ClipData.Item("photo.jpg")),"filename is not a reference");
        rejects(clip(new ClipData.Item("/storage/emulated/0/private/photo.jpg")),"path is not converted into access");
        rejects(clip(new ClipData.Item(Uri.parse("file:///storage/emulated/0/photo.jpg"))),"file scheme not bypassed");
        rejects(clip(new ClipData.Item("https://server/photo.jpg")),"web URL not treated as local file");
        rejects(clip(new ClipData.Item("intent://something")),"serialized Intent never launched or parsed");
        rejects(clip(new ClipData.Item("content:///no-authority")),"missing content authority");
        rejects(clip(new ClipData.Item("content://files/a\nb")),"embedded control characters rejected");
        rejects(clip(new ClipData.Item("look at "+A)),"reference must be the entire text");
        ClipData mixed=clip(new ClipData.Item(Uri.parse(A)));mixed.addItem(new ClipData.Item("bad"));
        rejects(mixed,"unsupported later item cannot silently truncate a selection");
        rejects(clip(new ClipData.Item(new Intent().putExtra(Intent.EXTRA_STREAM,Arrays.asList(Uri.parse(A),"bad")))),"partial stream list rejected");
        rejects(clip(new ClipData.Item(new Intent().putExtra("unrelated.referrer",Uri.parse(A)))),"unrelated extras are not files");
        Intent cyclic=new Intent();cyclic.putExtra(Intent.EXTRA_INTENT,cyclic);
        rejects(clip(new ClipData.Item(cyclic)),"cyclic input bounded");
        ClipData many=clip(new ClipData.Item(Uri.parse(A)));for(int i=1;i<100;i++) many.addItem(new ClipData.Item(Uri.parse("content://files/"+i)));
        accepts(many,100,"100 item boundary");many.addItem(pin());accepts(many,100,"100 files plus launcher companion allowed");
        many.addItem(new ClipData.Item(Uri.parse(B)));rejects(many,"raw count still bounded");
        ClipData tooMany=clip(new ClipData.Item(Uri.parse(A)));for(int i=1;i<=100;i++) tooMany.addItem(new ClipData.Item(Uri.parse("content://files/"+i)));
        rejects(tooMany,"allowing a companion does not permit 101 actual files");
        DropLinks links=new DropLinks();for(int i=0;i<100;i++) links.add("content://files/"+i);
        try { links.add(A);throw new AssertionError("aggregate count"); } catch(IOException expected) { checks++; }
        check(links.add("content://files/0"),"duplicate at limit allowed");
        StringBuilder large=new StringBuilder("content://files/");while(large.length()<8193) large.append('a');
        check(!new DropLinks().text(large),"oversized text bounded before parsing");
        DropPayload payload=new DropPayload();String secret="/storage/emulated/0/secret-photo.jpg";
        try { payload.extract(clip(new ClipData.Item(new Intent().putExtra("vendor.files",new String[]{secret})))); } catch(IOException expected) { }
        String diagnostic=payload.diagnostic();
        check(diagnostic.contains("vendor.files=String[]") && diagnostic.contains("MIME:"),"vendor format and types reported");
        check(!diagnostic.contains(secret),"diagnostic omits filenames, paths and extra values");
        System.out.println("PASS: "+checks+" dropped-payload parser assertions (JVM fixtures only)");
    }
}
