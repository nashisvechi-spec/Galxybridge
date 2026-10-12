package com.galaxybridge.lan;

import android.app.KeyguardManager;
import android.content.Context;
import android.hardware.display.DisplayManager;
import android.os.PowerManager;
import android.view.Display;
import org.junit.*;
import org.junit.runner.RunWith;
import org.robolectric.*;
import org.robolectric.annotation.Config;
import org.robolectric.shadows.ShadowDisplayManager;
import static org.junit.Assert.*;
import static org.robolectric.Shadows.shadowOf;

@RunWith(RobolectricTestRunner.class)
@Config(manifest=Config.NONE,sdk=35)
public class ControlDisplaysTest {
    private Context context;
    private DisplayManager manager;
    @Before public void setup() {
        context=RuntimeEnvironment.getApplication();manager=context.getSystemService(DisplayManager.class);
        shadowOf(manager.getDisplay(Display.DEFAULT_DISPLAY)).setState(Display.STATE_ON);
        shadowOf(context.getSystemService(PowerManager.class)).setIsInteractive(true);
        shadowOf(context.getSystemService(KeyguardManager.class)).setIsDeviceLocked(false);
    }
    private int monitor() {
        int id=ShadowDisplayManager.addDisplay("w1920dp-h1080dp");
        shadowOf(manager.getDisplay(id)).setState(Display.STATE_ON);
        shadowOf(manager.getDisplay(id)).setFlags(Display.FLAG_PRESENTATION);return id;
    }
    @Test public void phoneWithoutDeXKeepsNormalTarget() {
        assertEquals(Display.DEFAULT_DISPLAY,ControlDisplays.id(context));assertFalse(ControlDisplays.desktop(context));assertTrue(ControlDisplays.unlocked(context));
    }
    @Test public void connectedDeXOverridesPreviouslySavedPhonePreference() {
        context.getSharedPreferences("screen",Context.MODE_PRIVATE).edit().putInt("target",0).commit();
        int id=monitor();assertEquals(id,ControlDisplays.id(context));assertTrue(ControlDisplays.desktop(context));
    }
    @Test public void connectedDeXOverridesMissingManualPreference() {
        context.getSharedPreferences("screen",Context.MODE_PRIVATE).edit().putInt("target",999).commit();
        assertEquals(monitor(),ControlDisplays.id(context));
    }
    @Test public void actualRemovalAutomaticallyRestoresPhone() {
        int id=monitor();assertEquals(id,ControlDisplays.id(context));ShadowDisplayManager.removeDisplay(id);
        assertEquals(Display.DEFAULT_DISPLAY,ControlDisplays.id(context));assertFalse(ControlDisplays.desktop(context));
    }
    @Test public void sleepingConnectedMonitorNeverRedirectsInputToPhone() {
        int id=monitor();shadowOf(manager.getDisplay(id)).setState(Display.STATE_OFF);
        assertEquals(id,ControlDisplays.id(context));assertTrue(ControlDisplays.desktop(context));assertFalse(ControlDisplays.unlocked(context));
        shadowOf(manager.getDisplay(id)).setState(Display.STATE_ON);assertTrue(ControlDisplays.unlocked(context));
    }
    @Test public void activeMonitorWorksWithUnlockedPhoneScreenOff() {
        int id=monitor();shadowOf(context.getSystemService(PowerManager.class)).setIsInteractive(false);
        shadowOf(manager.getDisplay(Display.DEFAULT_DISPLAY)).setState(Display.STATE_OFF);
        assertEquals(id,ControlDisplays.id(context));assertTrue(ControlDisplays.unlocked(context));
    }
    @Test public void lockBlocksBothScreens() {
        monitor();shadowOf(context.getSystemService(KeyguardManager.class)).setIsDeviceLocked(true);
        assertFalse(ControlDisplays.unlocked(context));
    }
    @Test public void privateDisplaysAreNotControlTargets() {
        int id=monitor();shadowOf(manager.getDisplay(id)).setFlags(Display.FLAG_PRIVATE|Display.FLAG_PRESENTATION);
        assertEquals(Display.DEFAULT_DISPLAY,ControlDisplays.id(context));
    }
    @Test public void monitorGeometryComesFromSelectedDisplay() {
        int id=monitor();shadowOf(manager.getDisplay(id)).setRealWidth(1920);shadowOf(manager.getDisplay(id)).setRealHeight(1080);
        DisplayTarget.Screen selected=ControlDisplays.target(context);
        assertEquals(id,selected.id);assertEquals(1920,selected.width);assertEquals(1080,selected.height);
    }
}
