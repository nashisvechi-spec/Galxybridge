package com.galaxybridge.lan;

import android.app.Activity;
import android.content.Intent;
import android.media.projection.*;
import android.os.Bundle;
import android.widget.*;

public final class ScreenShareActivity extends Activity {
    private String request;
    private boolean submitted;
    @Override public void onCreate(Bundle saved) {
        BridgeUi.prepare(this);super.onCreate(saved);request=saved==null?ScreenShareService.pendingRequest():saved.getString("request");submitted=saved!=null && saved.getBoolean("submitted",false);
        if(submitted) { finish();return; }
        if(!ScreenShareService.allowed(request)) {
            LinearLayout root=BridgeUi.root(this),page=BridgeUi.scrollBody(root);
            BridgeUi.header(page,"Экран телефона","",this::finish);
            page.addView(BridgeUi.text(this,"На ПК откройте «Устройства → Экран телефона на ПК», затем подтвердите показ здесь. Запрос действует две минуты. Для нажатий нужна служба специальных возможностей.",16,false));
            BridgeUi.button(page,"Закрыть",false,this::finish);BridgeUi.show(this,root);return;
        }
        if(saved==null) {
            MediaProjectionManager manager=getSystemService(MediaProjectionManager.class);
            Intent consent=android.os.Build.VERSION.SDK_INT>=34?manager.createScreenCaptureIntent(MediaProjectionConfig.createConfigForDefaultDisplay()):manager.createScreenCaptureIntent();
            try { startActivityForResult(consent,41); }
            catch(RuntimeException e) { ScreenShareService.reject(this,request,"Система не разрешила захват экрана.");finish(); }
        }
    }
    @Override protected void onSaveInstanceState(Bundle state) { state.putString("request",request);state.putBoolean("submitted",submitted);super.onSaveInstanceState(state); }
    @Override protected void onActivityResult(int code,int result,Intent data) {
        super.onActivityResult(code,result,data);if(code!=41 || submitted) return;submitted=true;
        if(result==RESULT_OK && data!=null && ScreenShareService.allowed(request)) {
            try { startForegroundService(new Intent(this,ScreenShareService.class).putExtra("request",request).putExtra("result",result).putExtra("grant",data)); }
            catch(RuntimeException e) { ScreenShareService.reject(this,request,"Не удалось начать трансляцию. Повторите запрос на ПК."); }
        } else ScreenShareService.reject(this,request,"Показ экрана отменён на телефоне.");
        finish();
    }
}
