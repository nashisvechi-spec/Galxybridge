package com.galaxybridge.lan;

import android.app.ActivityOptions;
import android.content.Context;
import android.content.Intent;
import android.os.PowerManager;
import android.provider.Settings;
import android.view.Display;

/** Wake only the phone selected for input, never the phone behind a DeX monitor. */
final class ScreenWake {
    static boolean awake(Context context) {
        DisplayTarget.Screen screen=ControlDisplays.target(context);
        return screen!=null && screen.awake && (screen.id!=Display.DEFAULT_DISPLAY ||
            context.getSystemService(PowerManager.class).isInteractive());
    }
    static boolean sleeping(Context context) {
        DisplayTarget.Screen screen=ControlDisplays.target(context);
        return screen!=null && screen.id==Display.DEFAULT_DISPLAY &&
            (!screen.awake || !context.getSystemService(PowerManager.class).isInteractive());
    }
    static boolean available(Context context) {
        return sleeping(context) && (ControlService.instance!=null || Settings.canDrawOverlays(context));
    }
    static boolean request(Context context) {
        DisplayTarget.Screen screen=ControlDisplays.target(context);
        if(screen==null || screen.id!=Display.DEFAULT_DISPLAY) return false;
        if(!sleeping(context)) return true; // It woke between the heartbeat and request.
        if(!available(context)) return false;
        try {
            // The bound accessibility service or granted overlay permission allows
            // this user-initiated background launch. No wake lock or keyguard dismissal.
            Context launcher=ControlService.instance!=null?ControlService.instance:context;
            Intent intent=new Intent(launcher,WakeActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|
                Intent.FLAG_ACTIVITY_EXCLUDE_FROM_RECENTS|Intent.FLAG_ACTIVITY_NO_ANIMATION);
            ActivityOptions options=ActivityOptions.makeBasic();options.setLaunchDisplayId(Display.DEFAULT_DISPLAY);
            launcher.startActivity(intent,options.toBundle());return true;
        } catch(RuntimeException e) { return false; }
    }
}
