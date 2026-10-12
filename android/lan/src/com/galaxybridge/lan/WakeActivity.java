package com.galaxybridge.lan;

import android.app.Activity;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.view.WindowManager;

/** Brief transparent window using the supported Android screen-on API. */
public final class WakeActivity extends Activity {
    private final Handler handler=new Handler(Looper.getMainLooper());
    private long deadline;
    private final Runnable check=new Runnable() { public void run() {
        if(!ScreenWake.sleeping(WakeActivity.this) || SystemClock.uptimeMillis()>=deadline) {
            setTurnScreenOn(false);setShowWhenLocked(false);finish();
            ConnectionService.reportInputScreen();
        } else handler.postDelayed(this,50);
    } };
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        if(!ScreenWake.sleeping(this)) { finish();return; }
        setShowWhenLocked(true);setTurnScreenOn(true);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE);
        deadline=SystemClock.uptimeMillis()+2000;
    }
    @Override protected void onResume() {
        super.onResume();handler.removeCallbacks(check);if(!isFinishing()) handler.postDelayed(check,150);
    }
    @Override protected void onDestroy() {
        handler.removeCallbacks(check);super.onDestroy();
    }
}
