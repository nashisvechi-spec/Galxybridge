# Testing the no-debugging edition

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
