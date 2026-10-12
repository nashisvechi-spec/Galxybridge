# Galaxy Bridge — no-debugging edition

Windows **0.7.26** / Android **0.7.28**. The application starts directly in its LAN
window. Developer options, USB debugging and wireless debugging are unnecessary.
Only this edition is maintained; the previous debugging edition remains in Git history.

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

Builds run **manually only**: Actions → Build Windows portable → Run workflow.
Two artifacts are published: **GalaxyBridge-win10-x64** and **GalaxyBridgeLan-android**.
The ZIP contains GalaxyBridge.exe, GalaxyBridgeLan.apk, GalaxyFiles.apk, instructions and notices.
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

Use .NET 8, PowerShell 7, JDK 17, Android SDK platform 35 and build-tools 35.0.0.
See the manual workflow for the complete build and packaging sequence.

```powershell
python scripts/check_sources.py
dotnet run --project tests/GalaxyBridge.Core.Tests -c Release
dotnet run --project tests/GalaxyBridge.LanTls.Tests -c Release
./scripts/Test-LanProtocol.ps1
dotnet publish src/GalaxyBridge.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/GalaxyBridge
./scripts/Build-LanCompanion.ps1 -Destination artifacts/GalaxyBridge -VersionCode 100500
```

Set GALAXYBRIDGE_ANDROID_SIGNING_BUNDLE to the existing persistent signing bundle
and choose a VersionCode greater than the installed APK. GitHub Actions restores
this bundle from ANDROID_SIGNING_BUNDLE and verifies its public certificate pin.
MIT license; packaged dependencies retain [their notices](THIRD_PARTY_NOTICES.md).

## Standalone Galaxy Files

This repository also contains **Galaxy Files 0.2.0**, an independently installed
Android 13+ file manager (`android/files`, package `com.galaxybridge.files`).
The manual Windows workflow builds `GalaxyFiles.apk` as `GalaxyFiles-android`
and includes it beside the Wi-Fi companion in the portable ZIP.
The home screen offers recent files, six categories and storage usage.
List/grid views use original type icons and background photo/video thumbnails.
Local browsing and file operations work without a PC. Sending and Wi-Fi mouse
dragging use the separately installed GalaxyBridge companion and its existing
connection. Both APKs use the persistent project signing key.
See [installation, features and limitations](docs/GALAXY_FILES.ru.md).
