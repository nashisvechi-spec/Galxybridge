# Galaxy Bridge — no-debugging edition

Windows **0.7.12** / Android **0.7.12**. The application starts directly in its LAN
window. Developer options, USB debugging and wireless debugging are unnecessary.
Only this edition is maintained; the previous debugging edition remains in Git history.

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
The ZIP contains GalaxyBridge.exe, GalaxyBridgeLan.apk, instructions and notices.
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
