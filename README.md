# Galaxy Bridge — no-debugging edition

## New in 0.8.0

Resumable file transfers in both directions, persistent 100-item transfer
history with retry, connection diagnostics, opt-in Windows tray autostart,
and a phone-screen window with mouse gestures are implemented. Screen capture
requires Android consent for every session. See the [feature guide](docs/FEATURES_0.8.ru.md).
This is a release candidate: source checks pass; new packaging and physical
Windows/S25 capture, gesture and startup checks remain to be completed.

## Release preparation · 0.7.37

Connection screens now explain QR setup, saved-pair reconnection, and permissions.
Windows exposes receive settings directly on Files, explains unavailable actions,
and confirms forgetting a phone. Android preserves tab scroll positions and
rejects a selection over 100 files as a whole instead of silently truncating it.
Both packages use 0.7.37; manual Android builds require an explicit VersionCode.
See the [review and roadmap](docs/RELEASE_REVIEW.ru.md) and device checklist.
This is a release candidate until the new installer/APK and physical-device
checks have been completed.

Windows **0.8.0** / Android **0.8.0**. The application starts directly in its LAN
window. Developer options, USB debugging and wireless debugging are unnecessary.
Only this edition is maintained; the previous debugging edition remains in Git history.

Windows **0.7.35** adds a widget transparency slider in Settings (0–80%).
Changes apply live and are saved for reopening/restart. Missing settings keep
the original appearance. The preview button shows the widget. The alpha blend
retains the artwork silhouette and enough visibility for mouse actions.

Windows **0.7.33** replaces the file widget with the selected blue/teal artwork.
A per-pixel alpha window preserves its transparent silhouette without system
chrome. Click the center to choose files, drop Explorer files there, drag the
blue frame to move, or click the circular cross to close only the widget.
Progress/cancel appear during sending; disconnected widgets reject files.
Connection and the last result are in a tooltip. Position, tray behavior,
keyboard actions and accessible named buttons are retained. Android was 0.7.32 at that release.

Android **0.7.34** explicitly uses the system notification sound for new receipt
channels. Settings reports silent categories and offers **Enable notification
sound**: on a deliberate tap it replaces a silent legacy category once and
posts a test. Later user changes remain authoritative; the button opens that
category’s settings if it is muted again. Global notification blocks open app
settings. Silent mode, notification volume and Do Not Disturb still apply.

Version **0.7.32** fixes file receipt notification handling. Windows uses a native
non-activating corner card even with the main window hidden, with the exact
receipt folder action and a timer reset for subsequent files. Android requests
notification permission once from the visible setup screen, reports disabled
file channels and links to system settings after denial. New file channels use
high importance; existing user settings are preserved and may need heads-up
enabled manually after an upgrade. Each receipt can alert again. Both apps offer
a notification test in Settings; the test creates no file. Background sharing
does not launch a permission prompt, and notification denial never fails receipt.

Version **0.7.31** applies the shared Galaxy Bridge design to native WinForms
and Android screens: device cards, blue actions, teal connection cues and
persisted system/light/dark appearance. Windows has Devices, Control, Files and
Settings, a separate QR window, numeric/slider speed and the last ten verified
session transfers. Android has bottom Connection, Files and Settings navigation,
restyled send/clipboard/scan/drop screens, scrollable content and system/keyboard
insets. Existing background shares, pairing, notification and wake paths remain.
See [design notes and rendered Android screens](docs/UI_DESIGN.ru.md).

Version **0.7.30** wakes a sleeping phone when the user enters control from the
PC edge, button or Ctrl+Alt+F12. Both parts must be updated and the paired LAN
connection must be alive. The bound accessibility service or granted overlay
permission enables the brief private transparent screen-on window. No wake lock,
developer mode or keyguard dismissal is used. The PC cursor stays local until
awake, unlocked input is available. Edge departure, explicit cancellation,
disconnect, pause, DeX selection or the 15-second deadline cancel pending capture.
DeX never wakes the phone screen. Real Samsung wake behavior requires device testing.

Android file sharing now hands content URI grants to the foreground connection
service through a transparent, short-lived share target. It returns directly to
the source app without opening the transfer window. The manual sending window
remains available from Galaxy Bridge. Windows shows a tray notification only
after checksum verification and file publication, using the final saved filename.
Click the notification to open the actual destination folder. Windows notification
settings control presentation; receipt is independent of notification availability.

Android 0.7.28 shows a received-file notification only after checksum verification
and successful publication in `Download/GalaxyBridge`. Tap it to open that folder
in a system file manager. A system chooser at the same folder is the fallback;
if unavailable, the general Downloads window is used. Notification permission is
required for the notice, but denying it does not interrupt receipt. A new receipt
updates the notice with the latest filename. Samsung device behavior still needs
verification. The current package excludes Galaxy Files and file-edge transfer;
ordinary sharing/receipt and the Windows send widget remain available.

Windows **0.7.19** shows the native Shell operations progress dialog during
phone-to-PC receipt and drag preparation. Progress follows actual received bytes;
unknown-size files use an indeterminate bar. Cancel targets only the active
host-owned operation, so an old dialog cannot cancel the next file. The dialog
closes on verified completion, cancellation or disconnect. Shell UI failure
leaves receipt running with the existing in-app status. Android 0.7.18 remains
compatible; the wire format is unchanged. Native dialog rendering still needs
a Windows device check. After an Explorer drop, Explorer owns the final copy.

Version **0.7.18** adds an explicitly enabled experimental My Files-to-Explorer
edge transfer. The phone exposes a thin overlay drop target; when an actual
Android file drag enters it, the injected Android touch ends while the physical
Wi-Fi mouse button stays held. Keep holding at the edge during preparation;
the existing verified TLS staging and Windows OLE handoff then continue the drag.
Both applications must be updated. The global overlay drop and temporary URI
permission still require a Samsung/Windows device test. See
[instructions and fallback receiver](docs/FILE_DRAG.ru.md).

Windows **0.7.17** preserves a working connection until a replacement phone
session acknowledges its saved pairing. Aborted parallel attempts cannot
overwrite the connected status. Interrupted TLS attempts explain automatic
reconnection; successful reconnects clear the old diagnostic. Android 0.7.16
remains compatible. The user has confirmed the My Files receiver transfer works;
the intermittent TLS I/O failure's device-specific cause remains unconfirmed.

Android **0.7.16** fixes the observed Samsung drop of a video content URI plus
a launcher PinItemRequest companion. The launcher-only item is skipped while
file reading still must pass the existing probe. Unknown file entries remain
errors; the 100-file limit excludes the companion. Windows 0.7.13 is compatible.

Version **0.7.13** adds a staged phone-to-Windows file drag from the companion’s blue card into Explorer or the desktop. Wi-Fi mouse mode only; originals are preserved. See [setup and device checks](docs/FILE_DRAG.ru.md). The restored 0.7.8 ordinary movement path remains unchanged.

Android **0.7.14** adds a visible multi-window drop receiver for Samsung My Files
and other applications that provide readable content URIs. It checks actual
read access, then forwards the selected files and temporary permission to the
existing preparation/card flow. Windows 0.7.13 remains compatible. This is a
two-step flow that remains available alongside the experimental 0.7.18 edge mode.
See [the phone test procedure](docs/FILE_DRAG.ru.md#приём-из-моих-файлов--android-0714).

Android **0.7.15** also reads file references inside a dropped Intent, nested
ClipData, EXTRA_STREAM, or a whole content URI in text. The receiver now shows
the payload format and provides a copy-diagnostics button when a source uses
an unsupported representation. The user subsequently confirmed this receiver
works with the 0.7.16 companion-item fix.

- **LAN touch control:** Android AccessibilityService performs taps, long presses,
  continued touch drags, swipes and two-finger zoom. No Bluetooth adapter is required.
  With APK 0.7.11, Ctrl+wheel zooms PDFs/images; the saved “Масштабировать колёсиком
  по Wi-Fi” option enables zoom with the wheel alone. The viewer must support pinch
  gestures. Wi-Fi edge return uses the original direction and freshness checks.
- **Bluetooth system mouse (experimental):** Windows advertises a BLE HID keyboard
  and mouse. Requires Windows 10 2004+ and a BLE Peripheral-capable adapter.
  Pair from the phone's Bluetooth settings, confirm on both devices, select the
  phone in Galaxy Bridge when multiple phones are connected. Bluetooth starts
  with the application. Active HID takes priority automatically; while it is
  unavailable, ready Wi-Fi touch remains usable. A stored pairing alone is not
  HID readiness. Route changes release the previous capture; enter control again
  to use the new transport. Actual HP/S25 compatibility still requires device testing.
- Files and text clipboard use the paired LAN TLS connection in either mode.
  Ctrl+Alt+F12 switches control. BLE return through the phone edge uses actual
  Android mouse hover over a one-pixel overlay and the authorized LAN connection.
  It requires APK 0.7.8 and overlay permission; it does not require accessibility.

Version 0.7.11 restores movement behavior from Windows 0.7.8 and its Android
0.7.8 companion: 16ms timer batching, the original motion packet and immediate
Android pointer updates. Update both Windows and APK. Wheel zoom remains an
optional field on wheel events only. Pairing, signing identity and settings remain
compatible; the new version number permits an in-place APK update.

Version 0.7.12 routes ordinary left clicks through the accessibility click action
on button release, including 50–499ms clicks that previously became injected
continued touches. Drag begins at touch slop. Stationary hold is classified at
Android's long-press deadline, then injected; selection can therefore take about
two long-press timeouts. Free pointer movement retains the 0.7.8 pipeline.

The supplied Windows GlideX uses a separate Classic Bluetooth HID driver.
BLE Peripheral support does not measure Classic HID compatibility; Galaxy Bridge
does not yet include a Classic HID backend. See [the analysis](docs/GLIDEX_WINDOWS.ru.md).

Builds run **manually only**: Actions → Build Windows installer → Run workflow.
Two artifacts are published: **GalaxyBridge-Windows-Setup** and **GalaxyBridgeLan-android**.
The Windows artifact contains `GalaxyBridge-Setup-0.8.0-x64.exe`, an Inno Setup
installer with Russian/English UI, Start menu and optional desktop shortcuts,
and a registered uninstaller. It installs for the current user, with the .NET
runtime included. Update by running the next installer after closing the app;
pairing/settings and received files are preserved, including on uninstall.
Android remains a separate APK; Galaxy Files is excluded. The GitHub artifact
download is a ZIP wrapper: extract it and run the Setup EXE.
See [Windows installation](docs/WINDOWS_INSTALLER.ru.md).
No external connection backend or separate edge companion is bundled.

Install the APK, put both devices on the same LAN, show a new QR in Windows,
scan it inside Galaxy Bridge Wi-Fi and confirm the laptop. Permit Windows TCP
38271 / UDP 38272. Enable Android accessibility for touch control and overlay
permission for Bluetooth edge return and the shared text clipboard. Existing LAN pairing and settings survive
Windows updates; APK updates require the existing persistent signing key.

[Русская инструкция](README.ru.md) · [LAN guide](docs/NATIVE_WIFI.ru.md) ·
[System mouse](docs/SYSTEM_MOUSE.ru.md) · [Architecture](docs/ARCHITECTURE.md) ·
[Testing](docs/TESTING.md) · [APK updates](docs/ANDROID_UPDATES.ru.md)

## Build

Use .NET 8, PowerShell 7, Inno Setup 6, JDK 17, Android SDK platform 35 and build-tools 35.0.0.
See the manual workflow for the complete build and packaging sequence.

```powershell
python scripts/check_sources.py
dotnet run --project tests/GalaxyBridge.Core.Tests -c Release
dotnet run --project tests/GalaxyBridge.LanTls.Tests -c Release
./scripts/Test-LanProtocol.ps1
dotnet publish src/GalaxyBridge.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/GalaxyBridge
./scripts/Build-LanCompanion.ps1 -Destination artifacts/GalaxyBridge -VersionCode 100500
# Copy README.ru.md, LICENSE, THIRD_PARTY_NOTICES.md, licenses/ and docs/ into the publish directory.
./scripts/Build-WindowsInstaller.ps1
```

Set GALAXYBRIDGE_ANDROID_SIGNING_BUNDLE to the existing persistent signing bundle
and choose a VersionCode greater than the installed APK. GitHub Actions restores
this bundle from ANDROID_SIGNING_BUNDLE and verifies its public certificate pin.
MIT license; packaged dependencies retain [their notices](THIRD_PARTY_NOTICES.md).

## Standalone Galaxy Files

This repository also contains **Galaxy Files 0.2.0**, an independently installed
Android 13+ file manager (`android/files`, package `com.galaxybridge.files`).
Galaxy Files sources remain in this repository, but the active Windows
workflow and installer do not build or include `GalaxyFiles.apk`.
The home screen offers recent files, six categories and storage usage.
List/grid views use original type icons and background photo/video thumbnails.
Local browsing and file operations work without a PC. Sending and Wi-Fi mouse
dragging use the separately installed GalaxyBridge companion and its existing
connection. Both APKs use the persistent project signing key.
See [installation, features and limitations](docs/GALAXY_FILES.ru.md).
