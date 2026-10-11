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
