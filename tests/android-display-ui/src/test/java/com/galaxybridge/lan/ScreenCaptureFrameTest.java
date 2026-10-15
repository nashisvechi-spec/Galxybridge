package com.galaxybridge.lan;

import android.graphics.*;
import java.io.*;
import java.nio.ByteBuffer;
import java.util.*;
import org.json.JSONObject;
import org.junit.*;
import org.junit.runner.RunWith;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.annotation.*;
import static org.junit.Assert.*;

@RunWith(RobolectricTestRunner.class)
@Config(manifest=Config.NONE,sdk=35)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
public class ScreenCaptureFrameTest {
    @Test public void paddedRowsWithoutLastRowPaddingPreserveColorsAndBufferPosition() {
        ByteBuffer source=ByteBuffer.allocate(23);source.position(3);
        source.put(new byte[]{(byte)255,0,0,0, 0,(byte)255,0,0, 7,7,7,7, 0,0,(byte)255,0, (byte)255,(byte)255,(byte)255,0});source.position(3);
        Bitmap image=ScreenCaptureFrame.bitmap(source,2,2,12,4);
        try { assertEquals(Color.RED,image.getPixel(0,0));assertEquals(Color.GREEN,image.getPixel(1,0));assertEquals(Color.BLUE,image.getPixel(0,1));assertEquals(Color.WHITE,image.getPixel(1,1));assertEquals(3,source.position()); }
        finally { image.recycle(); }
    }
    @Test public void pixelPaddingIsSkippedAndUnusedAlphaCannotMakeScreenTransparent() {
        ByteBuffer source=ByteBuffer.wrap(new byte[]{(byte)255,0,0,0, 9,9,9,9, 0,0,(byte)255,0});
        Bitmap image=ScreenCaptureFrame.bitmap(source,2,1,16,8);
        try { assertEquals(Color.RED,image.getPixel(0,0));assertEquals(Color.BLUE,image.getPixel(1,0)); }
        finally { image.recycle(); }
    }
    @Test public void truncatedOrInvalidPlanesAreRejectedBeforeReadingPixels() {
        assertThrows(IllegalArgumentException.class,() -> ScreenCaptureFrame.bitmap(ByteBuffer.allocate(7),2,1,8,4));
        assertThrows(IllegalArgumentException.class,() -> ScreenCaptureFrame.bitmap(ByteBuffer.allocate(20),2,2,4,4));
        assertThrows(IllegalArgumentException.class,() -> ScreenCaptureFrame.bitmap(ByteBuffer.allocate(20),1281,1,5124,4));
    }
    @Test public void realEncodedFrameSurvivesChunkedWireTransportAndDecodes() throws Exception {
        int width=448,height=960;int[] pixels=new int[width*height];Random random=new Random(25);
        for(int i=0;i<pixels.length;i++) pixels[i]=0xff000000|random.nextInt(0x1000000);
        Bitmap image=Bitmap.createBitmap(pixels,width,height,Bitmap.Config.ARGB_8888);byte[] jpeg;
        try { jpeg=ScreenCaptureFrame.jpeg(image); } finally { image.recycle(); }
        assertTrue(jpeg.length>Wire.CHUNK);assertTrue(jpeg.length<=512000);
        String request="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",frame="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        List<JSONObject> packets=ScreenCaptureFrame.packets(jpeg,width,height,3,request,frame);
        ByteArrayOutputStream network=new ByteArrayOutputStream();DataOutputStream writer=new DataOutputStream(network);
        for(JSONObject packet:packets) Wire.write(writer,packet);
        DataInputStream reader=new DataInputStream(new ByteArrayInputStream(network.toByteArray()));
        JSONObject begin=Wire.read(reader);assertEquals(width,begin.getInt("width"));assertEquals(height,begin.getInt("height"));assertEquals(jpeg.length,begin.getInt("size"));
        ByteArrayOutputStream assembled=new ByteArrayOutputStream();int sequence=0;
        while(true) { JSONObject packet=Wire.read(reader);assertEquals(request,packet.getString("request"));assertEquals(frame,packet.getString("frame"));
            if(packet.getString("type").equals("screenEnd")) break;
            assertEquals(sequence++,packet.getInt("seq"));assembled.write(java.util.Base64.getDecoder().decode(packet.getString("data")));
        }
        assertEquals(0,reader.available());assertArrayEquals(jpeg,assembled.toByteArray());
        Bitmap decoded=BitmapFactory.decodeByteArray(assembled.toByteArray(),0,assembled.size());assertNotNull(decoded);
        try { assertEquals(width,decoded.getWidth());assertEquals(height,decoded.getHeight());assertTrue(Color.red(decoded.getPixel(100,100))>0 || Color.green(decoded.getPixel(100,100))>0); }
        finally { decoded.recycle(); }
        String fixture=System.getProperty("galaxybridge.screen.fixture");if(fixture!=null) try(FileOutputStream out=new FileOutputStream(fixture)) { out.write(network.toByteArray()); }
    }
}
