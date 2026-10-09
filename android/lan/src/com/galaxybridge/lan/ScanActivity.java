package com.galaxybridge.lan;
import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.hardware.Camera;
import android.os.Bundle;
import android.view.SurfaceHolder;
import android.view.SurfaceView;
import android.widget.LinearLayout;
import android.widget.TextView;
import com.google.zxing.*;
import com.google.zxing.common.HybridBinarizer;
import java.util.Collections;
import java.util.EnumMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

@SuppressWarnings("deprecation")
public final class ScanActivity extends Activity implements SurfaceHolder.Callback {
    private Camera camera;
    private SurfaceView preview;
    private TextView message;
    private final ExecutorService decoder = Executors.newSingleThreadExecutor();
    private final AtomicBoolean decoding = new AtomicBoolean();
    private volatile boolean done;
    private boolean surface;
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        LinearLayout page = new LinearLayout(this); page.setOrientation(LinearLayout.VERTICAL);
        message = new TextView(this); message.setText("Наведите камеру на QR из окна «Wi-Fi без отладки». Держите код полностью в кадре."); message.setPadding(24,48,24,24); page.addView(message);
        preview = new SurfaceView(this); page.addView(preview,new LinearLayout.LayoutParams(-1,0,1));
        preview.getHolder().addCallback(this); setContentView(page);
        if (checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) requestPermissions(new String[]{Manifest.permission.CAMERA},1);
    }
    @Override public void onRequestPermissionsResult(int request,String[] permissions,int[] grants) {
        super.onRequestPermissionsResult(request,permissions,grants);
        if (grants.length > 0 && grants[0] == PackageManager.PERMISSION_GRANTED) open(); else message.setText("Для QR нужно разрешение на камеру. Можно закрыть окно и разрешить камеру в настройках приложения.");
    }
    @Override public void surfaceCreated(SurfaceHolder holder) { surface=true; open(); }
    @Override public void surfaceChanged(SurfaceHolder h,int f,int w,int height) { }
    @Override public void surfaceDestroyed(SurfaceHolder holder) { surface=false; release(); }
    private void open() {
        if (!surface || camera != null || done || checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) return;
        try {
            camera = Camera.open(); Camera.Parameters params = camera.getParameters();
            Camera.Size best = null;
            for (Camera.Size size : params.getSupportedPreviewSizes())
                if (size.width <= 1280 && size.height <= 960 && (best == null || size.width*size.height > best.width*best.height)) best=size;
            if (best != null) params.setPreviewSize(best.width,best.height);
            params.setPreviewFormat(android.graphics.ImageFormat.NV21);
            if (params.getSupportedFocusModes().contains(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE)) params.setFocusMode(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE);
            camera.setParameters(params);
            Camera.CameraInfo info = new Camera.CameraInfo(); Camera.getCameraInfo(0,info);
            int rotation = getWindowManager().getDefaultDisplay().getRotation()*90;
            camera.setDisplayOrientation((info.orientation-rotation+360)%360);
            camera.setPreviewDisplay(preview.getHolder());
            Camera.Size size = camera.getParameters().getPreviewSize();
            camera.setPreviewCallback((bytes,c) -> {
                if (done || !decoding.compareAndSet(false,true)) return;
                byte[] owned = bytes.clone();
                decoder.execute(() -> decode(owned,size.width,size.height));
            });
            camera.startPreview();
        } catch (Exception e) { release(); message.setText("Не удалось открыть камеру. Закройте другие приложения с камерой и повторите."); }
    }
    private void decode(byte[] bytes,int width,int height) {
        try {
            EnumMap<DecodeHintType,Object> hints = new EnumMap<>(DecodeHintType.class);
            hints.put(DecodeHintType.POSSIBLE_FORMATS,Collections.singletonList(BarcodeFormat.QR_CODE)); hints.put(DecodeHintType.TRY_HARDER,Boolean.TRUE);
            MultiFormatReader reader = new MultiFormatReader(); reader.setHints(hints);
            String raw = reader.decodeWithState(new BinaryBitmap(new HybridBinarizer(new PlanarYUVLuminanceSource(bytes,width,height,0,0,width,height,false)))).getText();
            new Pairing(raw); done=true;
            runOnUiThread(() -> { release(); Intent data = new Intent(); data.putExtra("qr",raw); setResult(RESULT_OK,data); finish(); });
        } catch (ReaderException ignored) { }
        catch (IllegalArgumentException e) { runOnUiThread(() -> message.setText("Нужен QR из окна Galaxy Bridge «Wi-Fi без отладки».")); }
        finally { decoding.set(false); }
    }
    private void release() { if (camera != null) { camera.setPreviewCallback(null); camera.stopPreview(); camera.release(); camera=null; } }
    @Override protected void onPause() { release(); super.onPause(); }
    @Override protected void onResume() { super.onResume(); open(); }
    @Override protected void onDestroy() { done=true; release(); decoder.shutdown(); super.onDestroy(); }
}
