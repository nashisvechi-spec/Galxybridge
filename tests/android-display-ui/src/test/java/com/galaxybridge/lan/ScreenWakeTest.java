package com.galaxybridge.lan;

import android.app.*;
import android.content.*;
import android.hardware.display.DisplayManager;
import android.os.*;
import android.provider.Settings;
import android.view.Display;
import java.time.Duration;
import org.junit.*;
import org.junit.runner.RunWith;
import org.robolectric.*;
import org.robolectric.annotation.*;
import org.robolectric.android.controller.ActivityController;
import org.robolectric.shadows.ShadowDisplayManager;
import org.robolectric.shadows.ShadowSettings;
import static org.junit.Assert.*;
import static org.robolectric.Shadows.shadowOf;

@RunWith(RobolectricTestRunner.class)
@Config(manifest=Config.NONE,sdk=35)
@LooperMode(LooperMode.Mode.PAUSED)
public class ScreenWakeTest {
    private Context context;
    private DisplayManager displays;
    private PowerManager power;
    private KeyguardManager keyguard;
    @Before public void setup() {
        context=RuntimeEnvironment.getApplication();displays=context.getSystemService(DisplayManager.class);
        power=context.getSystemService(PowerManager.class);keyguard=context.getSystemService(KeyguardManager.class);
        ControlService.instance=null;ShadowSettings.setCanDrawOverlays(true);
        sleep();shadowOf(keyguard).setIsDeviceLocked(false);
    }
    private void sleep() { shadowOf(power).setIsInteractive(false);shadowOf(displays.getDisplay(0)).setState(Display.STATE_OFF); }
    private void awake() { shadowOf(power).setIsInteractive(true);shadowOf(displays.getDisplay(0)).setState(Display.STATE_ON); }
    @After public void cleanup() { ControlService.instance=null; }
    @Test public void sleepingPhoneWithOverlayGrantLaunchesPrivateWakeWindow() {
        assertTrue(Settings.canDrawOverlays(context));assertTrue(ScreenWake.available(context));assertTrue(ScreenWake.request(context));
        Intent launch=shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity();
        assertEquals(WakeActivity.class.getName(),launch.getComponent().getClassName());
        assertTrue((launch.getFlags()&Intent.FLAG_ACTIVITY_NEW_TASK)!=0);assertTrue((launch.getFlags()&Intent.FLAG_ACTIVITY_EXCLUDE_FROM_RECENTS)!=0);
        assertFalse(power.isInteractive());assertFalse(ControlDisplays.unlocked(context)); // launch success is not hardware readiness
    }
    @Test public void awakePhoneDoesNotOpenAnyWindow() {
        awake();assertFalse(ScreenWake.available(context));assertTrue(ScreenWake.request(context));
        assertNull(shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity());
    }
    @Test public void alwaysOnDisplayIsStillSleeping() {
        shadowOf(displays.getDisplay(0)).setState(Display.STATE_DOZE);
        assertTrue(ScreenWake.sleeping(context));assertTrue(ScreenWake.available(context));assertFalse(ScreenWake.awake(context));
    }
    @Test public void missingBackgroundLaunchGrantRejectsWakeWithoutWindow() {
        ShadowSettings.setCanDrawOverlays(false);
        assertFalse(ScreenWake.available(context));assertFalse(ScreenWake.request(context));
        assertNull(shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity());
    }
    @Test public void boundAccessibilityAllowsWakeWithoutOverlayGrant() {
        ShadowSettings.setCanDrawOverlays(false);
        org.robolectric.android.controller.ServiceController<ControlService> controller=Robolectric.buildService(ControlService.class).create();
        try {
            ControlService.instance=controller.get();
            assertTrue(ScreenWake.available(context));assertTrue(ScreenWake.request(context));
            assertEquals(WakeActivity.class.getName(),shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity().getComponent().getClassName());
        } finally { controller.destroy(); }
    }
    @Test public void failedLaunchIsReportedWithoutBreakingConnection() {
        Context denied=new ContextWrapper(context) { @Override public void startActivity(Intent intent,Bundle options) { throw new SecurityException("blocked"); } };
        assertFalse(ScreenWake.request(denied));
    }
    @Test public void connectedDeXNeverWakesPhoneEvenWhenMonitorSleeps() {
        int id=ShadowDisplayManager.addDisplay("w1920dp-h1080dp");shadowOf(displays.getDisplay(id)).setFlags(Display.FLAG_PRESENTATION);
        for(int state:new int[]{Display.STATE_ON,Display.STATE_OFF}) {
            shadowOf(displays.getDisplay(id)).setState(state);
            assertFalse(ScreenWake.sleeping(context));assertFalse(ScreenWake.available(context));assertFalse(ScreenWake.request(context));
        }
        assertNull(shadowOf(RuntimeEnvironment.getApplication()).getNextStartedActivity());
    }
    @Test public void wakeWindowTurnsScreenOnWithoutDismissingKeyguardAndFinishes() {
        shadowOf(keyguard).setIsDeviceLocked(true);
        try(ActivityController<WakeActivity> controller=Robolectric.buildActivity(WakeActivity.class).create().start().resume()) {
            WakeActivity activity=controller.get();assertTrue(shadowOf(activity).getTurnScreenOn());assertTrue(shadowOf(activity).getShowWhenLocked());assertFalse(activity.isFinishing());
            awake();shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(200));
            assertTrue(activity.isFinishing());assertFalse(shadowOf(activity).getTurnScreenOn());assertFalse(shadowOf(activity).getShowWhenLocked());
            assertTrue(keyguard.isDeviceLocked());assertFalse(ControlDisplays.unlocked(context));
        }
    }
    @Test public void wakeWindowHasBoundedLifetimeIfHardwareNeverWakes() {
        try(ActivityController<WakeActivity> controller=Robolectric.buildActivity(WakeActivity.class).create().start().resume()) {
            shadowOf(Looper.getMainLooper()).idleFor(Duration.ofSeconds(3));
            assertTrue(controller.get().isFinishing());assertFalse(shadowOf(controller.get()).getTurnScreenOn());
        }
    }
    @Test public void awakeOrDeXWindowImmediatelyCloses() {
        awake();try(ActivityController<WakeActivity> controller=Robolectric.buildActivity(WakeActivity.class).create()) {
            assertTrue(controller.get().isFinishing());assertFalse(shadowOf(controller.get()).getTurnScreenOn());
        }
    }
}
