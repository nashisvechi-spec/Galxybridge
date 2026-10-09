# Architecture

Galaxy Bridge is a native .NET 8 WinForms application. `GalaxyBridge.Core`
contains protocol encoding, HID reports, scan-code mapping, endpoint parsing
and edge geometry. `GalaxyBridge.Windows` owns the UI, Windows hooks, ADB
processes and the scrcpy control connection.

## Connection

1. Verify the pinned server hash and the four required backend files.
2. Push a uniquely named JAR to `/data/local/tmp/galaxybridge-{scid}.jar`.
3. Request an OS-assigned ADB TCP forward on the host to
   `localabstract:scrcpy_{scid}`.
4. Launch the matching scrcpy server using `adb shell app_process`, with
   video and audio disabled and control enabled.
5. Connect only to the loopback forwarded TCP port and read the dummy byte
   and fixed 64-byte device-name handshake.
6. Create a boot keyboard and relative mouse through UHID messages.

No mirroring window or scrcpy desktop executable is used. The server runs
under the authorized Android debugging session. It does not install an APK.
USB and wireless debugging share this same session path.

## Input

Low-level keyboard and mouse hooks are retained as rooted delegates for the
application's lifetime. Capture uses a topmost, almost transparent overlay
over the Windows virtual desktop. The local pointer is hidden and recentered
on the active monitor; the deltas feed Android's relative UHID mouse.

Keyboard usages come from physical Windows set-1 scan codes. Android assigns
the character layout. Reports include left/right modifiers and six ordinary
keys; more held keys produce the standard boot-keyboard rollover report.
Keyboard LED output packets are consumed to preserve framing, but are not
applied to laptop LEDs. Lock-key synchronization is not implemented.

The hotkey is handled locally. Capture ignores activation keys until release,
releases host modifiers, and refuses entry while other known keys or mouse
buttons are held. Desktop changes, Windows lock/logoff and suspend release
capture. A stalled command queue disconnects rather than discarding key-up
messages. The Windows secure attention sequence is not redirected.

Automatic entry checks the actual monitor bounds and excludes borders leading
to another Windows monitor. Entry uses 350 ms dwell. Native Android cursor
acceleration means host deltas do not tell this client the exact cursor
position on the phone. **Phone-edge return and cursor alignment are omitted**;
return is by Ctrl + Alt + F12.

## Clipboard and files

The Windows UI listens for clipboard changes and caches text, so the keyboard
hook does not query a locked clipboard or perform synchronous I/O.
Ctrl + V / Shift + Insert queue a scrcpy SET_CLIPBOARD packet with paste set.
The server handles Android pasting. Incoming clipboard messages update the
Windows text clipboard only when sharing is enabled.

File transfer is a separately cancellable `adb push` into Download. It has
ADB's replacement semantics for existing names. It is not shell-based
drag-and-drop. Pairing codes go to ADB's stdin, not process arguments. Settings
contain only preferences, not the pairing code, device serial or Wi-Fi address.
ADB itself manages its host authentication keys.

## Shutdown and protocol maintenance

Stop capture first, queue releases and UHID destruction, then close the socket.
Terminate only the shell process started by this session, remove only its own
forward, and attempt to remove its temporary JAR. Do not call `adb kill-server`.
An in-progress connection is cancelled and awaited before application exit.

Wire format is tied to scrcpy **5.0.1**. UHID_CREATE includes vendor and product
fields. Do not substitute a different server version without updating the
codec, tests and both pinned digests.

## Limits of verification

Source inspection and portable protocol tests cannot verify Windows hook
timing, overlay behavior, touchpad integration, One UI UHID access, Android
layout configuration, or real clipboard behavior. Use `TESTING.md` after the
first requested build. The current archive has not been compiled.
