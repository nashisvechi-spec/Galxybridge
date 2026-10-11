# Validation — Windows 0.7.5 / Android 0.7.7

This revision removes the debugging edition: its launcher, connection backend,
protocol, edge companion, download/build scripts and packaging steps. Windows
starts the existing LAN screen directly. Bluetooth HID remains experimental.
Android LAN source, package identity and signing pin are unchanged.

## Current verification

- scripts/check_sources.py passed structural validation, including removal checks.
- **2203 Core assertions passed**, using the real LAN TLS session. Added coverage
  checks direct Unicode paste, the 16 KB UTF-8 boundary and rejection without disconnect.
- All remaining Core and Windows C# sources compiled against .NET 8.0.19,
  Windows Forms, Windows SDK 19041 and the pinned packages with warnings as errors.
  Standard reference-unification warnings 1701/1702 are suppressed for QRCoder.

The .NET CLI cannot initialize here: Process.GetStat/StartTime fails while reading
process information. Compilation used the SDK's Roslyn compiler directly; tests
ran with the .NET runtime. This is not a complete MSBuild publish. Windows
certificate/TLS tests and final packaging remain in the manual Windows workflow.
Android LAN Java sources are unchanged; JVM tests remain in that workflow.

## Practical limits

No Windows UI or HP/S25 device session is available in this environment. Real
Bluetooth GATT pairing/subscriptions, Android text selection, clipboard overlay
focus and APK installation must be checked on devices. No EXE/APK is delivered
by a source commit: use the manual Build Windows portable workflow to package.
