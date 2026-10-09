# Galaxy Bridge

[Инструкция на русском](README.ru.md)

Source project for a Windows 10 x64 application that shares a PC's mouse and
keyboard with an Android phone. The initial target is Samsung Galaxy S25 with an
HP 240 G8. It uses Android debugging and the control channel of scrcpy, with no
video mirroring. It is an independent prototype, not Samsung Multi Control.

## Current scope

- USB or modern Android wireless debugging, with pairing by code or QR.
- Startup connection to the last successfully connected phone.
- Identity-checked reconnection after loss, with bounded retry delays.
- Native Android mouse cursor and physical keyboard via UHID.
- Ctrl + Alt + F12 to enter or leave phone control.
- Optional entry by dwelling at a chosen PC screen edge for 350 ms.
- Automatic return at the phone edge facing the PC, enabled by default.
- Text clipboard and Windows-to-phone file transfer into Download.
- Windows tray, settings, and release of input on disconnect/lock/suspend.

Phone-edge return requires installing **GalaxyBridgeEdge.apk** on the phone,
opening it once, and granting display-over-other-apps permission. A foreground
service creates a transparent four-physical-pixel window only during phone
control, inside the area available around system bars and cutouts. The regular
ADB mouse/keyboard connection and hotkey remain usable without the APK.

Drag-and-drop, image clipboard, and cursor positioning on entry are not implemented. Keyboard layouts
are selected on Android. Media/Fn keys are outside the initial scope.

## Status

Version 0.4.0 adds automatic connection, recovery and local QR pairing.
The Android companion stays at 0.3.0. Version 0.3.0 replaced the shell window helper with an installed Android
companion. The reported **Unknown pid ... uid=2000** arises when WindowManager
cannot find the shell process in its application process map. Initializing a
local context does not register that process; an ordinary installed service
uses the normal Android application lifecycle.

This revision is **unbuilt and untested on hardware**. Source-package checks
do not establish that it compiles or works on Windows or One UI. See
[validation](docs/VALIDATION.md) and the [hardware checklist](docs/TESTING.md).

No executable or backend binaries are included in the source archive.
The [GitHub Actions workflow](.github/workflows/build-windows.yml) only runs
when manually dispatched. A push or PR does not build the application.

## Build later, when requested

Upload the extracted files to a repository **at its root**, including `.github`,
then open **Actions → Build Windows portable → Run workflow**. The workflow runs
the portable C# tests, publishes a self-contained .NET 8 WinForms executable,
downloads the pinned backend with SHA-256 checks, builds the Android companion
with JDK 17 and Android SDK 35, restores the pinned QRCoder 1.8.0 package, and uploads GalaxyBridge-win10-x64 plus
GalaxyBridgeEdge-android. The Windows ZIP also contains the APK.
The development signing key changes per build: uninstall the previous APK
before installing one from another build, then grant overlay permission again.
It does not publish a release.

For local build commands and phone setup, use [README.ru.md](README.ru.md).
For implementation details, use [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Connection behavior

Connect once to remember a phone. Startup connection and loss recovery have
separate switches. The app verifies hardware identity (or Wi-Fi GUID if hardware
identity is unavailable), discovers the current TLS port via ADB mDNS, and uses
the saved endpoint only as a fallback. It never chooses an arbitrary ready phone.
A short shell round trip detects silent transport loss. Retries back off to
30 seconds; manual disconnect/cancel pauses them until explicitly resumed.

On recovery the PC retains mouse/keyboard control. Automatic attempts pause
while Windows is locked or asleep. Profiles store non-secret device identifiers
and a known endpoint locally; pairing secrets are sent through ADB stdin and
are neither saved nor logged.

QR pairing is generated offline and expires after two minutes. Scan it from
Android's Wireless debugging → Pair device with QR code screen. mDNS must work
on the local network. Code pairing and manual IP:port remain available.

## License

Original Galaxy Bridge code is MIT licensed. scrcpy and Android Debug Bridge
have separate licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
