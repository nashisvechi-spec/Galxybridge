# Validation — Windows 0.7.6 / Android 0.7.7

This revision selects mouse input automatically: ready Bluetooth HID has priority;
otherwise ready Wi-Fi touch uses the app cursor. Bluetooth starts with the window.
Pending HID no longer blocks Wi-Fi. Transport changes release the previous capture;
a late BLE clipboard return cannot end a newer LAN capture. Bluetooth HID remains experimental.
Android LAN source, package identity and signing pin are unchanged.

## Current verification

- scripts/check_sources.py passed structural validation, including removal checks.
- **2219 Core assertions passed**, using the real LAN TLS session. Added coverage
  checks route transitions, release ordering and stale BLE clipboard epochs, in addition
  to direct Unicode paste and its UTF-8 bounds.
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
