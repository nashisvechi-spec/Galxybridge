# Validation — Windows 0.7.7 / Android 0.7.8

This revision adds Bluetooth system-mouse edge return using actual Android hover,
a one-physical-pixel overlay and the paired LAN TLS connection. It preserves
native HID input and automatic Wi-Fi touch fallback. Native edge capability is
negotiated; old APKs retain HID with hotkey return. No debugging edition is added.

## Current verification

- scripts/check_sources.py passed structural validation, including manual-only
  workflow, package pins, signing, permissions and removal checks.
- **2234 Core assertions passed**, including real TLS session tests for native edge
  negotiation, overlay acknowledgement, five buttons, ordered hover/exit, stale
  capture/sequence rejection, and old-APK compatibility. Existing clipboard,
  native report/queue and automatic-route checks remain passing.
- All Core and Windows C# sources compiled against .NET 8.0.19, Windows Forms,
  Windows SDK 19041 and pinned packages with warnings as errors. Standard
  reference-unification warnings 1701/1702 are suppressed for QRCoder.
- All Android LAN Java sources compiled for Java 8 against Android API 35 and
  ZXing 3.5.3 using ECJ 3.38.0. Only pre-existing deprecation/unused warnings remain.
- **257 JVM assertions passed** across wire/pairing, upload, pointer gestures,
  click targets, clipboard and 39 new physical-edge geometry assertions.
- Re-examined the supplied GlideX APK DEX: jn2 installs four boundary hover
  listeners and clears them in its teardown method. Our implementation is independent; no vendor source is copied.

The .NET CLI cannot initialize here: Process.GetStat/StartTime fails while reading
process information. Compilation used the SDK Roslyn compiler directly; tests
ran with the .NET runtime. Java checks compile sources and run portable helpers.
These are not complete MSBuild/Android packaging runs. Windows certificate/TLS
checks, resource linking, dexing, APK signing and final packaging remain in the
manual Windows workflow.

## Practical limits

No Windows UI or physical phone/Bluetooth session is available here. Real GATT
pairing/subscriptions, boundary hit-testing, rotation/IME/system-bar behavior,
protected-app overlays, text selection and clipboard return must be checked on
devices using TESTING.md. The entry cursor keeps its last relative HID position;
absolute alignment with the desktop entry point is not implemented. BLE Peripheral
support is still required; the vendor GlideX Bluetooth backend was not recovered.
No EXE/APK is delivered by a source commit: use Build Windows portable manually.
