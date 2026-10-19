# Testing the no-debugging edition

## Minimal interface (0.8.2)

Run the portable suite, Android display/UI suite and Windows build. Widget hit
tests cover both arrows, the circular close button, drag borders, cancel priority
and the same area while idle at 75%, 100%, 150% and 200% scale. Android tests
verify that a saved pair can reconnect and scan a different QR from Settings,
plus existing navigation, text scaling, rotation, theme and inset checks.

On Windows, check light/dark/system themes, a narrow window and 100/150/200% DPI.
New users must still see QR setup; connected users can start a new pairing from
Settings. Check control, files, diagnostics and the placement map in Management.
Verify the independent 320x184 logical-pixel widget: title/frame dragging, both
arrow click areas, file drop, close, known/unknown progress, cancel, keyboard,
transparency persistence and a lost connection. Closing must leave transfer and
the main app alive. Repeat on the actual S25 with large text and DeX.

## Five new features (0.8.0)

Run source checks, the portable suite (2559 assertions), full Android suite
(96 cases), Windows TLS tests and the manual installer workflow.

On the actual devices:
- Upgrade both apps. Send a large file each way, interrupt Wi-Fi/VPN, reconnect
  and verify continuation from received bytes plus exact file content. Interrupt
  during final verification/publication and final ACK; verify one saved file and
  receipt. Leave a file running for over two minutes before interruption: retry
  grace must begin at the outage. Test empty files, duplicate names, changed
  sources, unavailable URI grants, full disk, explicit cancel and receive disabled.
- Pair another device during retry: the old queue must stop. Retry must not
  silently redirect files. Check 24-hour pruning and a busy old receiver.
- Restart both apps and inspect success/failure/cancel history. Open received
  files/folders, retry existing and removed sources, and clear history. Files must
  survive history clearing. Android may ask for reselection after provider access
  expires; document grants should persist when the provider supports them.
- Run diagnostics with/without a working link, Wi-Fi, VPN, input/overlay and
  notification permissions. Deny folder writes. The report must explain findings
  without pairing tokens/QR pins and must not change firewall/VPN settings.
- Toggle Windows autostart, sign out/in, verify tray launch, open from tray and
  exit. Disable and repeat. Update with the option set, then uninstall and verify
  its Run value is removed while other Run values and application data remain.
- Request the phone screen from Windows; accept, decline and dismiss Android's
  dialog. Repeat with notifications blocked using the manual Android settings
  action. Close Windows before accepting: capture must not start. Rotate while
  the consent dialog is open and after capture starts; never reuse an old grant.
- Test actual picture/rotation, letterboxing, clicks, swipes, wheel, Unicode
  typing, selection, Ctrl+V/A, Backspace/Delete/Enter and unsupported fields.
  Test a password field and a locked/off phone. Gestures/text must address the
  phone display rather than DeX. Confirm the regular DeX control route remains.
- Stop via Windows close, Android notification/system chip, screen lock, network
  loss and app exit. No later consent or reconnect may revive an old capture.
  Check a new session requests fresh consent. Minimize the main PC window and
  keep the viewer usable. The viewer must suppress automatic edge capture while
  focused. Check 100/150/200% DPI and actual keyboard/focus behavior.
- Transfer a large file with the viewer open: frames/input pause and recover,
  file throughput remains usable, and there is no unbounded frame queue. Observe
  phone temperature/battery and CPU. Current 4fps JPEG mode is for UI/documents,
  not video/audio; protected surfaces follow Android capture rules.

Robolectric validates lifecycle/protocol/layout behavior, not Samsung hardware
capture or real accessibility action execution. Native Windows installer/startup,
frame drawing and these physical checks are required before calling it stable.

## Release candidate 0.7.37

Run the full Android UI/display suite (82 tests), portable core suite, source
checks, and the existing manual Windows installer workflow. Android regression
cases cover no-pairing reconnect, saved-pair reconnect, QR results received while
Files is visible, scroll restoration across tabs/recreation, empty clipboard,
320dp/200% text layout, exactly 100 files, an over-limit share, and late unique
ClipData items after repeated duplicates. Over-limit sharing must send nothing.

On the actual laptop and S25:
- Install the new Setup and update the signed APK in place. Pairing, receive
  folder, widget position/transparency and theme must survive.
- Test first QR, saved-pair reconnect without a new QR, connection stop/resume,
  permission denial/return, VPN and Wi-Fi interruption, and screen lock/resume.
- On Windows Files change the receive folder and allow/deny receiving; verify
  consistency with the separate file window. During reception, changing folder
  is unavailable and Cancel reception cancels the actual transfer. Cancel send
  is available only while sending. Check control/navigation disabled states.
- Cancel Forget phone: pairing must remain. Confirm it: a new QR is required;
  received files remain. Check the QR action remains visible on first launch.
- Use keyboard navigation and 100/150/200% DPI with narrow Windows work areas.
  Check button wrapping, hover, focus, and the borderless widget independently.
- On Android switch tabs after scrolling, rotate, change theme, use TalkBack
  headings and 200% font at 320dp/split-screen. System Back from Files/Settings
  returns to Connection before leaving the app.
- Send files both ways, including 100/101 files and a large video. Share from
  My Files/Gallery must remain in the source app and send in the background.
- Recheck phone wake, DeX monitor switching/return, emergency hotkey, smooth
  cursor, clipboard, Android receipt sound/folder tap and Windows receipt notice.

An Android framework test cannot verify Samsung DeX, hardware sound or Windows
native rendering. Complete these checks before calling this version stable.

## Windows installer (0.7.36)

The manual Windows workflow builds the actual Setup EXE with Inno Setup and
runs `Test-WindowsInstaller.ps1` on its empty CI account before upload. It checks
silent installation, EXE/version/license, separate APK exclusion, Start menu
shortcut, uninstall registration, repeated install without duplicate uninstallers,
and uninstall while preserving a sentinel in the application's data directory.
Never run this CI smoke test against a personal Windows account/installation.

On Windows 10/11, also install through the Russian wizard with the default path
and a path containing spaces/non-ASCII characters. Check desktop task selection
and launch at finish. Set pairing, widget transparency and a custom receive folder;
close via the tray and install a newer build. Verify all settings/files survive,
and that the running-app check prompts if either the installed or portable copy
is active. Uninstall through Windows Settings, verify shortcuts/program files
are removed, then reinstall and verify pairing and saved settings are restored.

## Widget transparency (Windows 0.7.35)

In Settings → File transfer widget, use Show widget, then drag the transparency
slider between 0%, 40% and 80%. Check live appearance and the percent label,
including keyboard arrow/Page Up/Page Down operation. Close/reopen the widget,
then restart the app; the selected transparency must remain. Repeat changing
the value while the widget is closed. Old settings without the property use
0%; out-of-range saved values clamp to 0–80%. At 80%, test choosing/dropping
files, moving and closing; the widget must remain visible and interactive.
During a transfer check progress/cancel and hover/focus cues at each setting,
including after DPI/theme changes. Outer transparent pixels remain transparent.


## Notification sound (Android 0.7.34)

Run the full `tests/android-display-ui/pom.xml` suite. On a physical S25:
- Install as an update. In Settings, check the notification status. Existing
  silent/blocked categories must remain untouched until you tap Enable sound.
- Tap **Включить звук уведомлений** with normal ringer mode and audible
  notification volume. The test and the next verified received file should
  use the system notification tone. Tapping the receipt still opens its folder.
- Repeat with an old low-importance category and with notification permission
  denied. The button must request permission or open the matching settings.
- Mute/block the replacement category, restart, and tap the button again. It
  must open that category’s settings without creating another category.
- Check vibrate/silent, zero notification volume, and Do Not Disturb. Status
  must explain the restriction; the app must not alter system sound settings.
- Repeated transfers may be quieted by Android notification cooldown. Test
  with individual files spaced apart. The connection service stays quiet.

JVM tests verify category routing, sound metadata, settings and receipt behavior,
not physical speaker output or One UI notification cooldown.

## Borderless file widget (Windows 0.7.33)

On Windows at 100%, 150% and 200% DPI, show the widget from Files or the tray:
- Check the supplied blue/teal artwork, transparent outer pixels and absence of
  title bar, system border, opaque rectangular background and extra taskbar icon.
- Click both arrows; choose several files and confirm one transfer to the phone.
  Cancel the picker and verify no file is sent. Drag normal Explorer files into
  the center; the frame and cross must not accept drops. Folders and batches over
  100 files are rejected. Repeat while disconnected and while already sending.
- Verify the circular cross closes only the widget, including during a transfer.
  Reopen it from the tray. Minimize/hide the main window; the widget stays visible.
- Drag each blue edge between monitors with different DPI and negative desktop
  coordinates. Check the artwork and button hit targets remain aligned. Close
  and reopen, then restart; verify the saved location is on an available screen.
- Check unknown-size animation, actual per-file percentage, cancellation and
  long filename ellipsis. Hover the center after success/failure for the result.
- Switch system/light/dark themes while the widget is open; its alpha and blue
  artwork must remain intact. Use Tab/Shift+Tab, Enter/Space, Esc and a screen
  reader to exercise choose, cancel and close without a mouse.

These are physical Windows checks; portable tests cover hit target geometry
and drop eligibility, not native layered-window rendering or Explorer interaction.

## Receipt notifications (0.7.32)

Run the Android suite including `NotificationSettingsTest` and `ReceivedFilesTest`.
Check first-open permission, reopening after denial, app/channel settings intents,
preserved disabled/default channels, repeated alert flags, and a separate test
notice that does not overwrite receipt or connection notices.

On a real Android device test permission acceptance/denial, disabled application
and receipt channel, heads-up presentation after upgrading from 0.7.31, two
successive files and folder taps. Existing channel importance is user-controlled:
use Settings → Allow notifications to enable heads-up if required. On Windows
test the Settings notification button with the main window normal/minimized,
two successive receipts into different folders, keyboard focus unchanged,
hover/dismiss/X, DPI/monitor work areas, and cleanup on application exit. The
Windows receipt card is an application window, not a Notification Center toast.

## Shared interface (0.7.31)

Run the portable tests and the Android UI suite. The latter now has 58 cases,
including theme, navigation/recreation, large-font, keyboard-inset and rendering
checks. Set `-Dgalaxybridge.preview.dir=/absolute/path` when running Maven to
write four Android native-rendered previews. The main shared icon is loaded from
the real production asset for the resource-free test fixture.

On Windows check every navigation section, QR open/reissue/address change/X/expiry,
the main control button and hotkey, numeric and slider speed (including typed
values between slider steps), delay, Bluetooth, file drop, verified-only history,
receive settings and the independent widget. Change system/manual themes with
file/QR windows open. Test 100/150/200% DPI, smaller work areas, moving between
monitors, keyboard focus and screen-edge control while other sections are visible.

On S25 check all bottom tabs, rotation, system/manual appearance, large fonts,
back buttons, real QR/camera permission and pairing confirmation. Test manual
file picking, clipboard input with the keyboard open, received-folder navigation,
permissions/settings return and foreground/background file sharing. A theme change
must preserve pairing. ShareActivity and WakeActivity must remain transparent
and must not open the new main interface. Repeat on DeX and in split-screen.


## Phone wake on cursor entry (0.7.30)

Run the portable suite and `mvn -B -f tests/android-display-ui/pom.xml test`.
The Android suite now includes ten wake cases alongside the existing 38 cases:
sleep/AOD, already-awake no-op, overlay/accessibility launch grants, rejected
launch, DeX exclusion, screen-on lifecycle, keyguard preservation and timeout.
Production LanSession wake metadata/requests/acknowledgements are exercised over
pinned TLS; pending transitions are checked for held input, edge departure,
explicit cancellation, target/connection changes and the exact 15-second expiry.

Build and install both 0.7.30 applications. On S25 with accessibility enabled,
turn the screen off, leave and re-enter the configured PC edge after its delay.
The screen should wake; the cursor must stay on the PC until input is ready.
Repeat with AOD, no secure lock, PIN/fingerprint lock, the button and hotkey.
With a secure lock, unlock normally; edge entry only completes while still at
that edge. Moving away, cancelling, pausing, disconnecting or waiting 15 seconds
must prevent later unlock from stealing the cursor. Check repeated attempts.

Repeat in Bluetooth input mode with overlay permission and accessibility disabled.
Test missing launch permissions and an Android-rejected background launch: no
cursor capture or reconnect/retry loop. Connect DeX with phone screen off: control
must go to the monitor without waking or opening a phone window. Connect/remove
DeX during a pending phone wake. Check screen state races, Android battery limits,
VPN/network loss and background reconnect with the physical devices. A request
ACK verifies launch acceptance, not that Samsung hardware actually woke.

## Background sharing and Windows notifications (0.7.29)

Run the portable tests and `mvn -B -f tests/android-display-ui/pom.xml test`.
The Android suite has 38 cases; the portable suite checks successful/failed
receipt events against actual TLS/file publication, without polling a last state.

On S25, share one file, several files and a large video from My Files/Gallery.
The source app should remain on screen; no Galaxy Bridge transfer window or
launch preview should appear. Test with the main app already open, stopped but
paired, on DeX, after rotation, and while another selection is uploading. Closing
the share handoff must not revoke the service's read access or cancel its upload.
Test missing pairing, a missing PC, and cancellation from the manual send window.
Text-only sharing should still open the existing clipboard screen.

On Windows, minimize to tray, receive one/multiple/duplicate-name files, and check
that receipt notices only appear after valid complete files are saved. Click the
notice to open the destination folder. Check failed checksum, cancellation,
disconnection and disabled receipt: none may show successful receipt. Repeat with
Windows notifications disabled/Focus Assist enabled; saving and ACK must still
work. These device/UI checks are required after the manual signed build.

## Received-file notification checks (Android 0.7.28)

The same `tests/android-display-ui/pom.xml` suite now has 36 tests, including
14 receipt/notification/folder tests. They drive the production Downloads receiver
through a fixture provider, check successful publication and failure/cancel paths,
permission/notification failures, notice replacement and navigation fallbacks.

On S25, allow notifications, send a file from the Windows widget, wait for receipt,
and tap **Файл получен с ПК**. Verify the actual file in **Download/GalaxyBridge**.
Test a duplicate filename, several files, cancellation/disconnection, and receiving
with notifications disabled. Verify tapping with the app closed and in DeX. Check
that the connection notice remains and that the saved file can be opened from the
folder. If the manager does not support directory VIEW, verify the system chooser
starts in that folder and can open a selected file. These are physical-device checks.

## Current DeX/package checks (0.7.25)

Run `mvn -B -f tests/android-display-ui/pom.xml test` with JDK 17 before packaging.
The nine display framework tests use the real companion display adapter and public
Android API 35 models; they do not validate Samsung hardware overlays. Android
0.7.27 adds thirteen share-window and upload-queue regressions to the same suite.
On a phone, share a file and a multi-file selection into Galaxy Bridge, verify
automatic submission and real saved PC bytes, then test a second share while
the first runs, window recreation and cancellation.
The workflow packages GalaxyBridge.exe and only GalaxyBridgeLan.apk. Galaxy Files
and file-edge transfer are excluded. Earlier file-edge checklists below are
historical and do not apply to 0.7.25.

On devices, verify connection to a monitor while the phone cursor is active,
entry from an idle PC directly into DeX, release of held buttons before migration,
return to the phone after removal, a sleeping monitor and phone lock/unlock.
Also check ordinary file sending, receiving and the independent Windows widget.

## Automated checks

```powershell
python scripts/check_sources.py
dotnet run --project tests/GalaxyBridge.Core.Tests -c Release
dotnet run --project tests/GalaxyBridge.LanTls.Tests -c Release
./scripts/Test-LanProtocol.ps1
```

Source validation checks pinned packages/actions, manual-only builds, persistent
APK signing, Android permissions and the absence of removed debug components.
Core tests exercise HID report sizes and button/wheel splitting, ordered transport,
release/cancellation and failures, automatic route selection and release-before-switch,
desktop/phone edge geometry, LAN framing,
clipboard TLS delivery and stale-result rejection, and file receipt validation.
The portable suite also runs production LAN host/session code over pinned
loopback TLS with in-memory storage fixtures. It checks aborted reconnects,
preservation of a working session until `saved`, successful replacement, and
Pause/Forget while waiting for that acknowledgement.
LAN TLS tests use the production certificate import on Windows. JVM tests cover
pairing/wire parsing, file upload, pointer gestures, click targets, clipboard and
physical edge geometry with navigation/cutout insets. Real TLS tests exercise native
edge capability negotiation, acknowledgements, hover/exit, five buttons and stale epochs.

The GitHub workflow runs these before packaging. Final artifacts must be only
GalaxyBridge-win10-x64 and GalaxyBridgeLan-android; the ZIP must contain no adb.exe,
scrcpy-server, backend directory or GalaxyBridgeEdge.apk.

## Windows/Android device checks

1. Start with existing settings that include the old mode selector; confirm
   the LAN screen always opens. Existing QR pairing and sensitivity survive.
2. Pair a fresh phone from the in-app QR scanner; test rejection, expiry,
   a second phone and explicit forget. Change both devices to another common LAN.
3. Test without Bluetooth: accessibility taps, long press, continuous drag,
   selected text handles, file picker, scroll and navigation. Check all four
   screen sides, multiple monitors and scaling. Ctrl+Alt+F12 must always return.
4. Test BLE with a Peripheral-capable adapter: pair, select the phone, verify
   both subscriptions, real cursor/hover, five buttons, double/triple click,
   Shift/Ctrl drag, both wheel axes and keyboard layouts. Disconnect while
   dragging; no buttons or modifiers may remain held. Test reconnect/restart.
5. Keep Wi-Fi working while Bluetooth starts/waits for a phone. Attach ready
   HID: it must take priority. Disconnect/suspend HID: Wi-Fi must remain usable.
   Do this while dragging or holding modifiers: stop old capture and release;
   do not replay held input. Test stored pairing without HID subscriptions and
   multiple subscribed phones. A late BLE clipboard return must not interrupt
   a new LAN capture. Test an unsupported/disabled Bluetooth adapter: show a clear failure and
   keep LAN touch available. No debugging setup should be suggested.
6. Copy Unicode text/links in both directions. Return from LAN and BLE input;
   check no echo, no stale response after new copying, disabled clipboard,
   occupied clipboard, locked phone and focus failure. Check the on-screen
   keyboard and protected applications during the brief clipboard focus change.
7. Send files both ways, duplicate names and 2 GB boundary. Cancel, disconnect,
   fill disk, deny receipt and change receive folder. No partial file or overwrite.
8. Minimize, reopen via tray, pause/resume, sleep/wake, lock/unlock, exit while
   sending or starting BLE, and launch a second instance. All input must release.
9. Install the next persistently signed APK over the previous APK. Confirm
   app data, pairing and permission state are retained as Android permits.

10. Install APK 0.7.8 for BLE edge return and pair the same phone over LAN. Grant
   overlay permission, disable accessibility and verify that HID still works and
   the boundary returns to Windows. Test all four phone positions, portrait/landscape,
   cutout, navigation buttons/gestures and IME. Return must stay blocked during a
   drag, any of the five buttons, and held keyboard modifiers. Leave the edge,
   reverse direction quickly: old hover must not return from inside the screen.
11. Revoke overlay permission, lock, hide overlays in a protected app and disconnect
   LAN while BLE remains connected. Ctrl+Alt+F12 must return; no overlay may remain
   after disconnect or release. Test an old APK: HID remains available, no unknown
   nativeCapture command is sent. Check click behavior on the single boundary pixel.
12. Select/copy text by the native mouse, return through the phone edge, then Ctrl+V
   on Windows. Clipboard reading must follow neutral HID reports, without affecting
   a newer capture. Verify entry preserves the phone's previous HID cursor position.

Passing source/transport tests does not establish real Bluetooth compatibility
or the behavior of Android applications. See VALIDATION.md for this revision.

## Historical checklist: Wi-Fi wheel zoom and delayed edge return (0.7.9)

- Update both Windows and the APK; preserve the existing APK signing identity.
- In a PDF viewer supporting pinch, move the pointer over the page. Plain wheel
  should scroll; Ctrl+wheel should zoom in/out. Test both Ctrl keys and release
  Ctrl: the next plain wheel should scroll. Enable the saved wheel zoom option
  and verify plain wheel zoom, restart persistence, and scroll after disabling it.
- Turn the wheel rapidly in both directions: the LAN link should stay alive and
  the pending gestures should remain bounded. Test portrait and landscape, and
  pointer positions near every screen edge.
- Test all four phone sides. Move outward toward the PC and stop at the boundary:
  return should work with delayed Wi-Fi feedback. Immediately reverse direction:
  old feedback must not return the cursor. Drag, scroll or hold a key at the edge:
  capture must remain on the phone. Re-enter: old-capture feedback must not return.
- Check Ctrl+Alt+F12 and BLE HID behavior still work. An old APK retains legacy
  scrolling/edge return and must not receive enhanced touch commands.

## Native receive progress (Windows 0.7.19)

- Update Windows; APK 0.7.18 remains compatible. Enable receipt and send a large
  video normally, then prepare it for dragging. A native modeless Shell dialog
  should show received bytes; unknown-size files must not claim a percentage.
- Cancel midway in the Shell dialog: no incomplete file may be published;
  staged batches must not become ready. Repeat receipt immediately afterward.
- Receive several files, including two identical names. Test unknown and empty
  files, phone abort, lost connection, lock, pause, disabled receipt and exit.
- After an Explorer drop, verify Explorer's own final-copy progress and confirm
  the phone originals remain intact. Quick operations may not show a dialog.
- Portable fake-backend lifecycle checks do not verify native COM/UI rendering.

## Experimental direct file edge (0.7.18)

- Update Windows and APK together. Enable file receipt on Windows and explicitly
  enable the phone overlay mode; minimize using its button rather than Back.
- Long-press a small file in fullscreen My Files with the Wi-Fi mouse, wait for
  a real Android drag, enter the phone edge facing the PC, and keep left held
  throughout preparation. Drop in Explorer only after the cursor returns.
- Test all four sides, system-bar insets, several files and identical names.
- Release during probe/upload, move inward, rotate, lock, disconnect or disable
  receipt; no delayed Windows drag may start. Back must remove the strip.
- On failure copy receiver diagnostics: drag-start, edge-entry, payload/grant
  and read-probe status. Check the existing split-screen/card fallback too.
- EdgeFileDragTest runs 100 pure JVM checks; payload fixtures cannot verify
  overlay delivery or Android grants. TLS and Core handoff tests also do not
  replace physical-button/Explorer verification on Windows.

## Historical checklist: pointer responsiveness (0.7.10)

- Update Windows and APK. In Wi-Fi touch mode, move the pointer slowly, quickly
  and diagonally, then stop. It should follow promptly without replaying queued
  movement after stopping. Test 60 Hz and 120 Hz display modes where available.
- Test sensitivity 0.25x, 1x and 4x, a low polling-rate mouse and a high-rate mouse.
  Fast movement followed immediately by a click/Shift/drag must use the correct
  coordinates and previous button/modifier state for pending movement.
- Send multiple packets between display frames: the arrow should show the latest
  position once per frame. Clicks, wheel gestures and releases must remain ordered.
- Rotate, lock, disconnect and re-enter control; no old frame callback may restore
  a removed arrow. Actual taps/drag segments/zoom/navigation must remain blocked
  while locked. Plain arrow motion injects no touch and is stopped by lock events
  or the 100ms readiness heartbeat.
- Recheck all four edge-return directions, Ctrl-wheel zoom and shared clipboard.
  Compare perceived responsiveness on the same Wi-Fi with VPN disabled.

## Restored 0.7.8 mouse behavior (0.7.11)

- Build and update Windows and APK 0.7.11 together using the existing signing key.
  Verify installation over the old APK retains pairing and settings.
- Compare cursor movement with the known Windows/Android 0.7.8 pair: slow/fast
  horizontal, vertical and diagonal motion, stop/reversal, and 0.25x/1x/4x speed.
  The Windows timer and immediate Android window update now use that baseline.
- Test a click immediately after moving, text selection by hold/drag, modifiers,
  and return through each edge. Edge return uses the original 0.7.8 freshness
  policy again; Ctrl+Alt+F12 remains available.
- Confirm plain wheel scroll, Ctrl+wheel PDF zoom, persisted wheel zoom setting,
  Bluetooth routing, clipboard and file transfer still work.
- APK 0.7.8 must receive the original mouse packet and scroll normally. APK
  0.7.9/0.7.10 with new Windows must also receive baseline motion without seq.
  Use the matching 0.7.11 APK to restore Android's pointer rendering too.

## File picker clicks (0.7.12)

- Update APK 0.7.12 over the previous APK. In Wi-Fi control open Send files,
  click Choose files and click a file name. Try ordinary clicks lasting roughly
  50ms, 100ms and 300ms. The file must return to Send with a nonzero selection.
  Verify both the system picker and the alternative file application.
- Click a folder, go back and select a file. Repeat on image/video/PDF rows,
  in list/grid layouts and after scrolling. Send the selected file to Windows.
- Compare free pointer movement with 0.7.11 / the 0.7.8 baseline.
- Check that an ordinary button activates once, including rapid repeated clicks.
  A gesture-only surface falls back to one completed tap. Drag before releasing:
  it must continue live and must not activate the original row on release.
- Hold stationary until text selection appears, then drag a selection handle.
  Classification delays touch-down until the system long-press timeout; a
  stationary long press takes about two timeouts. A release just after that
  timeout must still produce a hold rather than an accidental tap. Some app
  gesture listeners reject accessibility's UNKNOWN tool type; hardware testing
  remains necessary for long press/drag in protected file lists.
- Exit control, lock or disconnect while waiting for hold classification and
  during an injected hold. No deferred click/hold may run after release/reset.
  Re-enter and click again. Check wheel zoom and Bluetooth routing.

## Screen startup regression (0.8.1)

On Windows and a real Samsung phone, start screen viewing and accept Android's
system consent. Keep the phone unlocked on an ordinary visible app. The viewer
must show the first image, not just permission/connected text. Repeat from a
static screen and rotate the phone during startup. Test again after closing
the viewer; fresh Android consent must still be required.

If capture stalls, the viewer must show NO_FRAMES after the surface retry, or
FRAME_ACK if a sent frame is not acknowledged. IMAGE/CAPTURE/SURFACE errors must
retain their stage after the foreground service stops. The LAN connection must
remain usable. Verify that starting a file transfer pauses capture and its
startup timeout, and frames resume after the transfer. These physical checks
remain pending; 103 framework tests and portable wire checks are automated.
