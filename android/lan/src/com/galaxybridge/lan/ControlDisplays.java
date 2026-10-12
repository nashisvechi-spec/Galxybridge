package com.galaxybridge.lan;

import android.app.KeyguardManager;
import android.content.Context;
import android.hardware.display.DisplayManager;
import android.os.PowerManager;
import android.util.DisplayMetrics;
import android.view.Display;
import java.util.ArrayList;

final class ControlDisplays {
    static DisplayTarget.Screen[] screens(Context context) {
        ArrayList<DisplayTarget.Screen> result=new ArrayList<>();
        for(Display display:context.getSystemService(DisplayManager.class).getDisplays()) {
            DisplayMetrics metrics=new DisplayMetrics();display.getRealMetrics(metrics);
            boolean publicScreen=(display.getFlags()&Display.FLAG_PRIVATE)==0;
            result.add(new DisplayTarget.Screen(display.getDisplayId(),metrics.widthPixels,metrics.heightPixels,display.getRotation(),
                display.isValid() && publicScreen,display.getDisplayId()!=Display.DEFAULT_DISPLAY,
                (display.getFlags()&Display.FLAG_PRESENTATION)!=0,metrics.densityDpi,display.getState()==Display.STATE_ON));
        }
        return result.toArray(new DisplayTarget.Screen[0]);
    }
    static DisplayTarget.Screen target(Context context) { return DisplayTarget.choose(DisplayTarget.AUTO,screens(context)); }
    static int id(Context context) { DisplayTarget.Screen screen=target(context);return screen==null?-1:screen.id; }
    static boolean desktop(Context context) { return id(context)>0; }
    static boolean unlocked(Context context) {
        DisplayTarget.Screen screen=target(context);
        return screen!=null && screen.awake && !context.getSystemService(KeyguardManager.class).isDeviceLocked() &&
            (screen.id!=Display.DEFAULT_DISPLAY || context.getSystemService(PowerManager.class).isInteractive());
    }
    static Context overlay(Context context,int id,int type) {
        Display display=context.getSystemService(DisplayManager.class).getDisplay(id);
        if(display==null || !display.isValid()) throw new IllegalArgumentException("Selected display unavailable");
        return context.createDisplayContext(display).createWindowContext(type,null);
    }
    static String description(Context context) {
        DisplayTarget.Screen screen=target(context);
        if(screen==null) return "Выбранный экран недоступен. Подключите монитор или выберите другой экран.";
        Display display=context.getSystemService(DisplayManager.class).getDisplay(screen.id);
        return (screen.id==0?"Экран телефона":"Внешний экран / DeX: "+(display==null?screen.id:display.getName()))+
            " · "+screen.width+"×"+screen.height;
    }
}
