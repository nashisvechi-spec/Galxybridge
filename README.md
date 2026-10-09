# Independent Wi-Fi preview — 0.6.0

Galaxy Bridge now includes an experimental **Wi-Fi without debugging** mode.
Install **GalaxyBridgeLan.apk** (Android 13+), open the matching Windows mode,
scan its QR inside the phone app, and confirm the laptop. The companion uses
certificate-pinned TLS, persistent pairing and discovery/reconnect across shared
Wi-Fi networks. It provides file upload to Download/GalaxyBridge and basic
accessibility-based cursor, taps, swipes and text input. No ADB is used in this mode.

See [setup and limitations (Russian)](docs/NATIVE_WIFI.ru.md). APK/EXE builds and
real HP/S25 behavior have not been verified. Sources are published only; Actions
still requires manual dispatch. This preview is a different input engine from
Android's native UHID cursor; dragging is replayed on release and clipboard
synchronization / full hardware keyboard behavior are not implemented.

The manual workflow also packages **GalaxyBridgeLan-android**. Both companion
APKs are in the portable ZIP. They have different package IDs. Development signing
keys change each build, so replacing the LAN test APK requires reinstall/pairing.

The following documents the existing ADB mode, which is preserved:

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
- Text clipboard and multiple file uploads by dropping into the window or using the file picker.
  All uploads go into Download/GalaxyBridge; existing names and batch duplicates get suffixes.
- Windows tray, settings, and release of input on disconnect/lock/suspend.

Phone-edge return requires installing **GalaxyBridgeEdge.apk** on the phone,
opening it once, and granting display-over-other-apps permission. A foreground
service creates a transparent four-physical-pixel window only during phone
control, inside the area available around system bars and cutouts. The regular
ADB mouse/keyboard connection and hotkey remain usable without the APK.

Direct cross-screen file dragging from/to Android apps, image clipboard, and cursor positioning on entry are not implemented. Keyboard layouts
are selected on Android. Media/Fn keys are outside the initial scope.

## Status

Version 0.5.1 uses one shared upload folder and adds resilient QR discovery
with direct local IPv4 mDNS fallback, retries and a new-code button. Window
file drops, multiple selection and cancellable temporary uploads remain available. Automatic connection, recovery
and local QR pairing from 0.4.0 remain available.
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

## Sending files to the phone

Connect the phone, open the Files tab and drop ordinary files from Explorer
into the window, or use the multiple-file picker. Files are copied, never moved.
Find them in internal storage → Download → GalaxyBridge. Existing batch folders from 0.5.0 are left in place.
The activity indicator and file counter do not report a per-file percentage.
Cancel stops the batch; confirmed files remain. An offline phone may retain a
`.gb-….part` file if cleanup fails. After reconnection, select remaining files
again. A lost rename acknowledgement may leave the last file already complete:
check the phone folder before retrying. Same-name files receive numbered suffixes. Folders and virtual mail attachments
are not supported. Filename size is limited to 255 UTF-8 bytes.
Use the picker or run without elevation if Explorer drops are blocked by Windows.
No Android companion update is required for file sending.

QR discovery first checks the running ADB server, then sends local IPv4 mDNS
queries from up to eight active interfaces when the pairing service is absent.
It keeps the QR visible through transient lookup/pairing failures and offers
New QR code to restart with fresh credentials. The same fallback resolves the
connection service after successful pairing. It does not restart ADB, change
firewall rules or bypass router isolation. The original QR failure reported by
the user has not been reproduced on their hardware; these fixes still need testing.
