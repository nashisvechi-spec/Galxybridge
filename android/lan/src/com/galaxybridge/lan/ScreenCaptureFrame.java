package com.galaxybridge.lan;

import android.graphics.Bitmap;
import java.io.ByteArrayOutputStream;
import java.nio.ByteBuffer;
import java.util.*;
import org.json.JSONObject;

/** A CPU-readable RGBA plane can have row padding, pixel padding and no last-row padding. */
final class ScreenCaptureFrame {
    static Bitmap bitmap(ByteBuffer source,int width,int height,int rowStride,int pixelStride) {
        if(width<1 || height<1 || width>1280 || height>1280 || pixelStride<4 || rowStride<(long)(width-1)*pixelStride+4)
            throw new IllegalArgumentException("Invalid RGBA dimensions or stride");
        ByteBuffer plane=source.duplicate();int start=plane.position();
        long needed=(long)(height-1)*rowStride+(long)(width-1)*pixelStride+4;
        if(needed>plane.remaining()) throw new IllegalArgumentException("Incomplete RGBA plane");
        int[] pixels=new int[width*height];
        for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
            int offset=start+y*rowStride+x*pixelStride;
            // A display is opaque. Some producers leave the unused alpha byte at zero.
            pixels[y*width+x]=0xff000000|((plane.get(offset)&255)<<16)|((plane.get(offset+1)&255)<<8)|(plane.get(offset+2)&255);
        }
        return Bitmap.createBitmap(pixels,width,height,Bitmap.Config.ARGB_8888);
    }
    static byte[] jpeg(Bitmap image) {
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();
        for(int quality:new int[]{55,40,25}) {
            bytes.reset();if(!image.compress(Bitmap.CompressFormat.JPEG,quality,bytes)) throw new IllegalStateException("JPEG encoding failed");
            if(bytes.size()<=512000) return bytes.toByteArray();
        }
        throw new IllegalStateException("Encoded screen frame exceeds limit");
    }
    static List<JSONObject> packets(byte[] jpeg,int width,int height,int revision,String request,String frame) {
        List<JSONObject> packets=new ArrayList<>();
        packets.add(Wire.message("screenBegin","request",request,"frame",frame,"width",width,"height",height,"revision",revision,"size",jpeg.length));
        int sequence=0;for(int offset=0;offset<jpeg.length;offset+=Wire.CHUNK)
            packets.add(Wire.message("screenChunk","request",request,"frame",frame,"seq",sequence++,"data",java.util.Base64.getEncoder().encodeToString(Arrays.copyOfRange(jpeg,offset,Math.min(jpeg.length,offset+Wire.CHUNK)))));
        packets.add(Wire.message("screenEnd","request",request,"frame",frame));return packets;
    }
}
