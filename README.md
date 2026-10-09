# Galaxy Bridge

[Инструкция на русском](README.ru.md)

Source project for a Windows 10 x64 application that shares a PC's mouse and
keyboard with an Android phone. The initial target is Samsung Galaxy S25 with an
HP 240 G8. It uses Android debugging and the control channel of scrcpy, with no
video mirroring. It is an independent prototype, not Samsung Multi Control.

## Current scope

- USB or modern Android wireless debugging, including pairing by code.
- Native Android mouse cursor and physical keyboard via UHID.
- Ctrl + Alt + F12 to enter or leave phone control.
- Optional entry by dwelling at a chosen PC screen edge for 350 ms.
- Automatic return at the phone edge facing the PC, enabled by default.
- Text clipboard and Windows-to-phone file transfer into Download.
- Windows tray, settings, and release of input on disconnect/lock/suspend.

Phone-edge return uses a temporary transparent four-pixel Android window, launched
through authorized ADB without installing an APK. If the firmware does not allow
that window, the journal reports the error and the hotkey remains available.
Drag-and-drop, image clipboard, and cursor positioning on entry are not implemented. Keyboard layouts
are selected on Android. Media/Fn keys are outside the initial scope.

## Status

Version 0.2.1 fixes initialization of the Android shell Application and the
typed window context, and retains detailed edge-helper errors in the journal.
This revision is **unbuilt and untested on hardware**. Source-package checks have
been performed; these do not establish that the application compiles or works
on Windows. See [validation](docs/VALIDATION.md) and the
[hardware checklist](docs/TESTING.md).

No executable or backend binaries are included in the source archive.
The [GitHub Actions workflow](.github/workflows/build-windows.yml) only runs
when manually dispatched. A push or PR does not build the application.

## Build later, when requested

Upload the extracted files to a repository **at its root**, including `.github`,
then open **Actions → Build Windows portable → Run workflow**. The workflow runs
the portable C# tests, publishes a self-contained .NET 8 WinForms executable,
downloads the pinned backend with SHA-256 checks, builds the Android edge helper
using JDK 17 and Android SDK 35, and uploads a portable ZIP.
It does not publish a release.

For local build commands and phone setup, use [README.ru.md](README.ru.md).
For implementation details, use [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## License

Original Galaxy Bridge code is MIT licensed. scrcpy and Android Debug Bridge
have separate licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
