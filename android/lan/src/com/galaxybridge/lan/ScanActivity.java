package com.galaxybridge.lan;
import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
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
import java.util.concurrent.RejectedExecutionException;
import java.util.concurrent.atomic.AtomicBoolean;

@SuppressWarnings("deprecation")
public final class ScanActivity extends Activity implements SurfaceHolder.Callback {
    private Camera camera;
    private SurfaceView preview;
    private TextView message;
    private final ExecutorService decoder = Executors.newSingleThreadExecutor();
    private final AtomicBoolean decoding = new AtomicBoolean();
    private volatile boolean done;
    private boolean surface,resumed;
    private String pendingQr;
    private AlertDialog confirmation;
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        if(saved!=null) pendingQr=saved.getString("pendingQr");
        done=pendingQr!=null;
        LinearLayout page = new LinearLayout(this); page.setOrientation(LinearLayout.VERTICAL);
        message = new TextView(this); message.setText("Наведите камеру на QR из окна «Wi-Fi без отладки». Держите код полностью в кадре."); message.setPadding(24,48,24,24); page.addView(message);
        preview = new SurfaceView(this); page.addView(preview,new LinearLayout.LayoutParams(-1,0,1));
        preview.getHolder().addCallback(this); setContentView(page);
        if (!done && checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) requestPermissions(new String[]{Manifest.permission.CAMERA},1);
    }
    @Override public void onRequestPermissionsResult(int request,String[] permissions,int[] grants) {
        super.onRequestPermissionsResult(request,permissions,grants);
        if (grants.length > 0 && grants[0] == PackageManager.PERMISSION_GRANTED) open(); else message.setText("Для QR нужно разрешение на камеру. Можно закрыть окно и разрешить камеру в настройках приложения.");
    }
    @Override public void surfaceCreated(SurfaceHolder holder) { surface=true; open(); }
    @Override public void surfaceChanged(SurfaceHolder h,int f,int w,int height) { }
    @Override public void surfaceDestroyed(SurfaceHolder holder) { surface=false; release(); }
    private void open() {
        if (!resumed || !surface || camera != null || done || checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) return;
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
                try { decoder.execute(() -> decode(owned,size.width,size.height)); }
                catch(RejectedExecutionException ignored) { decoding.set(false); }
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
            runOnUiThread(() -> {
                if(isFinishing() || isDestroyed()) return;
                pendingQr=raw;release();confirm();
            });
        } catch (ReaderException ignored) { }
        catch (IllegalArgumentException e) { runOnUiThread(() -> message.setText("Нужен QR из окна Galaxy Bridge «Wi-Fi без отладки».")); }
        finally { decoding.set(false); }
    }
    private void confirm() {
        if(!resumed || pendingQr==null || confirmation!=null || isFinishing()) return;
        final String raw=pendingQr;
        final Pairing pair;
        try { pair=new Pairing(raw); }
        catch(IllegalArgumentException e) { retryScan();message.setText("Это не QR Galaxy Bridge. Покажите новый код в режиме без отладки.");return; }
        message.setText("QR распознан. Проверьте название и адрес ноутбука и нажмите «Подключить».");
        confirmation=new AlertDialog.Builder(this).setTitle("Подключить ноутбук?")
            .setMessage(pair.name+"\n"+pair.host+"\n\nОн сможет отправлять файлы и управлять телефоном, пока подключение включено.")
            .setNegativeButton("Сканировать заново",(d,w) -> retryScan())
            .setPositiveButton("Подключить",null).setOnCancelListener(d -> retryScan()).create();
        confirmation.show();
        confirmation.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v -> {
            // Start while this activity is visible, after explicit confirmation.
            // Connection no longer depends on delivery of the activity result.
            v.setEnabled(false);
            if(!SetupActivity.startPairing(this,raw)) { v.setEnabled(true);message.setText(ConnectionService.status);return; }
            Intent data=new Intent();data.putExtra("qr",raw);data.putExtra("confirmed",true);data.putExtra("started",true);
            setResult(RESULT_OK,data);finish();
        });
    }
    private void retryScan() {
        confirmation=null;pendingQr=null;done=false;
        message.setText("Наведите камеру на новый QR из окна «Wi-Fi без отладки».");open();
    }
    private void release() {
        Camera closing=camera;camera=null;
        if(closing==null) return;
        // Camera drivers can throw when the preview has already stopped.
        // Cleanup must not prevent confirmation or delivery of the scan result.
        try { closing.setPreviewCallback(null); } catch(RuntimeException ignored) { }
        try { closing.stopPreview(); } catch(RuntimeException ignored) { }
        try { closing.release(); } catch(RuntimeException ignored) { }
    }
    @Override protected void onSaveInstanceState(Bundle state) {
        state.putString("pendingQr",pendingQr);super.onSaveInstanceState(state);
    }
    @Override protected void onPause() { resumed=false;release();super.onPause(); }
    @Override protected void onResume() { super.onResume();resumed=true;if(pendingQr!=null) confirm();else open(); }
    @Override protected void onDestroy() {
        done=true;release();decoder.shutdownNow();
        if(confirmation!=null) { confirmation.dismiss();confirmation=null; }
        super.onDestroy();
    }
}
