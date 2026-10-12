package com.galaxybridge.files;
import android.graphics.*;
import android.graphics.drawable.BitmapDrawable;
import android.os.*;
import android.widget.ImageView;
import org.junit.*;
import org.junit.runner.RunWith;
import org.robolectric.*;
import org.robolectric.annotation.*;
import java.io.*;
import java.nio.file.*;
import java.lang.reflect.Field;
import java.util.*;
import java.util.concurrent.*;
import static org.junit.Assert.*;
import static org.robolectric.Shadows.shadowOf;
@RunWith(RobolectricTestRunner.class)
@Config(manifest=Config.NONE,sdk=35,shadows=FilePresentationTest.StreamThumbnail.class)
@LooperMode(LooperMode.Mode.PAUSED)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
public class FilePresentationTest {
 // Robolectric's SDK 35 ImageDecoder file binding requires Android. Substitute
 // only that file binding with stream decoding; exercise real PNG bytes and loader.
 @Implements(android.media.ThumbnailUtils.class)
 public static class StreamThumbnail {
  @Implementation protected static Bitmap createImageThumbnail(File file,android.util.Size size,CancellationSignal signal) throws IOException {
   try(FileInputStream stream=new FileInputStream(file)) {
    Bitmap decoded=BitmapFactory.decodeStream(stream);if(decoded==null) throw new IOException("Invalid image");
    if(signal!=null) signal.throwIfCanceled();return Bitmap.createScaledBitmap(decoded,size.getWidth(),size.getHeight(),true);
   }
  }
 }

 @Test public void categoryClassificationHandlesCaseAndOfficeFormats() {
  assertEquals(FileKind.IMAGE,FileKind.ofName("Фото.HEIC"));assertEquals(FileKind.VIDEO,FileKind.ofName("clip.MP4"));
  for(String name:new String[]{"report.PDF","work.docx","sheet.xlsx","slides.pptx","notes.txt"}) assertTrue(FileKind.DOCUMENT.matches(new File(name)));
  assertFalse(FileKind.DOCUMENT.matches(new File("photo.jpg")));assertEquals(FileKind.APK,FileKind.ofName("app.apk"));assertEquals(FileKind.ARCHIVE,FileKind.ofName("files.zip"));
 }
 @Test public void catalogFiltersFallbackAndOrdersNewestFirst() throws Exception {
  File root=Files.createTempDirectory("catalog").toFile(),docs=new File(root,"Documents");docs.mkdir();File older=new File(docs,"older.pdf"),newer=new File(docs,"newer.docx"),photo=new File(docs,"photo.png");
  Files.write(older.toPath(),new byte[]{1});Files.write(newer.toPath(),new byte[]{1});Files.write(photo.toPath(),new byte[]{1});assertTrue(older.setLastModified(1000));assertTrue(newer.setLastModified(2000));
  File hidden=new File(docs,".private");hidden.mkdir();Files.write(new File(hidden,"hidden.pdf").toPath(),new byte[]{1});
  FileOps ops=new FileOps(root);List<File> result=FileCatalog.load(RuntimeEnvironment.getApplication(),ops,FileKind.DOCUMENT,"",false,20);
  assertEquals(Arrays.asList(newer.getCanonicalFile(),older.getCanonicalFile()),result);
  assertEquals(Collections.singletonList(newer.getCanonicalFile()),FileCatalog.load(RuntimeEnvironment.getApplication(),ops,FileKind.DOCUMENT,"NEWER",false,1));
  assertEquals(3,FileCatalog.load(RuntimeEnvironment.getApplication(),ops,FileKind.DOCUMENT,"",true,20).size());
  ops.delete(docs);assertTrue(root.delete());
 }
 @Test public void thumbnailDecodesPhotoAndDoesNotOverwriteReboundView() throws Exception {
  File image=File.createTempFile("thumbnail",".png");Bitmap b=Bitmap.createBitmap(100,80,Bitmap.Config.ARGB_8888);b.eraseColor(Color.GREEN);try(FileOutputStream out=new FileOutputStream(image)){assertTrue(b.compress(Bitmap.CompressFormat.PNG,100,out));}
  ThumbnailLoader loader=new ThumbnailLoader();try {
   ImageView view=new ImageView(RuntimeEnvironment.getApplication());loader.bind(view,image,64);await(loader);assertTrue("Actual photo must replace placeholder",view.getDrawable() instanceof BitmapDrawable);
   ImageView rebound=new ImageView(RuntimeEnvironment.getApplication());loader.bind(rebound,image,80);loader.bind(rebound,new File("document.pdf"),80);await(loader);assertTrue("Old decode must not replace another file",rebound.getDrawable() instanceof FileArtwork);
  }finally{loader.close();assertTrue(image.delete());}
 }
 private void await(ThumbnailLoader loader) throws Exception {
  Field f=ThumbnailLoader.class.getDeclaredField("workers");f.setAccessible(true);ThreadPoolExecutor p=(ThreadPoolExecutor)f.get(loader);long until=System.nanoTime()+TimeUnit.SECONDS.toNanos(5);
  Thread.sleep(20);
  while((p.getActiveCount()>0 || !p.getQueue().isEmpty()) && System.nanoTime()<until) Thread.sleep(10);
  assertEquals(0,p.getActiveCount());assertTrue(p.getQueue().isEmpty());shadowOf(Looper.getMainLooper()).idle();
 }
}
