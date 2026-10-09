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

## Discovery, pairing and recovery (Windows 0.4.0)

The UI owns a monotonic ConnectionRecovery schedule and one cancellable
operation at a time. Startup and recovery are separate states/options.
Discovery has a 12-second budget. Retries wait 2, 4, 8, 16, then 30 seconds
after a failed attempt. Manual disconnect/cancel pauses automation, including
late loss callbacks; successful connection resets the schedule. Desktop lock
and suspend stop capture and pause automatic work without entering phone
capture again on resume.

A successful connection saves hardware serial, Wi-Fi GUID, model, current
transport and any known endpoint. DeviceDiscovery first inspects ready
transports, then only matching _adb-tls-connect services, and finally a saved
endpoint. Hardware identity is checked by a getprop round trip before starting
scrcpy. The hardware serial takes precedence over GUID if both are present;
a reused IP cannot select a different phone. Missing identity disables remembering
that connection. The persistent GUID already includes the adb- prefix in AOSP,
and can be the full service instance name. No subnet scanning is performed.

PhoneSession probes a harmless shell echo every three seconds with a four-second
timeout. Unlike get-state, this reaches the phone rather than checking the
server's cached transport state. Socket EOF, write failures, unhealthy response
or probe timeout invoke the existing loss path: release Windows input, clean up
only the current session's server/forwards, and schedule recovery. Closing awaits
both the active operation and the health task.

QrPairingForm creates a fresh studio- name and 128-bit hexadecimal secret,
encodes WIFI:T:ADB;S:<name>;P:<secret>;; locally with pinned QRCoder 1.8.0,
and paints square modules with a quiet zone. The two-minute dialog polls ADB
mDNS for the exact requested pairing instance; only that endpoint receives
the secret through stdin. Successful pairing must be confirmed by ADB's
response. Code pairing now checks the same confirmation.

After pairing, the app waits up to 12 seconds for a TLS-connect service at the
paired phone's IP, connects that transport and starts control. Failure to resolve
that separate port prompts manual entry without claiming pairing failed.
Cancellation and close stop/await the QR worker before disposal. The outer UI
operation yields before opening any modal dialog, so shutdown can track it.

QR credentials are not persisted, logged or included in process arguments.
Device profiles are non-secret local preferences. ADB still stores/manages its
host private key. Setting ADB_MDNS_OPENSCREEN=1 chooses native discovery for a
new server; an already-running ADB server is not restarted. Multicast discovery
and Samsung firmware behavior require hardware validation.

Primary protocol references:
[ADB Wi-Fi architecture](https://android.googlesource.com/platform/packages/modules/adb/+/HEAD/docs/dev/adb_wifi.md)
and [daemon mDNS GUID](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/daemon/mdns.cpp).

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
position on the phone. Return therefore uses native Android hover events rather
than integrating host deltas. Positioning the phone cursor on entry is omitted.

## Phone-edge return

Version 0.3.0 builds android/edge-return as an installed Android 11+ APK.
The user installs and opens it, then grants SYSTEM_ALERT_WINDOW. EdgeService
is a foreground specialUse service with a connection notification. It does not
start at boot and requests no Internet, storage, capture or accessibility access.
Its exported entry point requires android.permission.DUMP, held by ADB shell;
ordinary third-party applications cannot start it.

The old app_process helper failed with Unknown pid ... uid=2000 because
WindowManager's Session constructor looks up the caller in ActivityTaskManager's
application process map. Local Application or context initialization cannot
register a shell process there. The installed service gets normal process
registration through Android's application lifecycle.
Reference: [Session.java](https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/services/core/java/com/android/server/wm/Session.java).

The host uses adb forward tcp:0 to a uniquely named local abstract socket.
It starts the foreground service with the socket name and a fresh 256-bit token.
The service admits only peers with UID 0 or 2000 and verifies HELLO 3 plus the
token before reporting GB_EDGE_READY 3. No phone TCP listener is opened.

START creates a TYPE_APPLICATION_OVERLAY transparent strip four physical pixels
wide at the edge facing the laptop. The window context matches that type.
It fits system-bar and cutout insets and reports the actual on-screen rectangle
in GB_EDGE2 frames, avoiding assumptions that an ordinary overlay reaches the
physical display border. A small touchable area can intercept touches; some
applications hide third-party overlays.

Only hover events from Galaxy Bridge Mouse produce feedback. Frames include
capture epoch, sequence, physical display size, raw pointer position, button
state and zone rectangle. Windows requires the current epoch, fresh feedback
(250 ms), recent outward motion (200 ms), and a 350 ms capture warmup.
Any held mouse button or keyboard key blocks return. Hover exit invalidates
the cached frame. Return places the laptop pointer two pixels inside the
corresponding monitor border with proportional cross-axis coordinates.

Windows command writes use an asynchronous channel. Android socket writes
use a worker thread and a bounded queue; neither performs blocking writes
inside an input callback. PING is sent every second. The phone closes an
inactive authenticated connection after a 6-second read timeout; Windows uses
a 7-second feedback timeout. STOP, QUIT, EOF, service stop or process death
remove the strip. Old-session cleanup cannot stop a newer service session.
Companion failure disables automatic return only; the local hotkey remains
independent. The APK stays installed after disconnecting but its service stops.

The manual workflow packages the APK beside the Windows executable and as
a separate artifact. It uses a new development signing key per build, so an
APK from another build requires uninstalling the previous companion and
granting overlay permission again. Production upgrades need a persistent
private signing key.

## Clipboard and files

The Windows UI listens for clipboard changes and caches text, so the keyboard
hook does not query a locked clipboard or perform synchronous I/O.
Ctrl + V / Shift + Insert queue a scrcpy SET_CLIPBOARD packet with paste set.
The server handles Android pasting. Incoming clipboard messages update the
Windows text clipboard only when sharing is enabled.

File drops use WinForms FileDrop/Copy on the window and child controls. The
file picker uses the same sequential batch path. Only existing ordinary files
are accepted, duplicate source paths are collapsed, and same-name destinations
are disambiguated case-insensitively. UTF-8 leaf names are limited to 255 bytes.
All batches use /sdcard/Download/GalaxyBridge. An idempotent mkdir -p creates
the shared folder. A depth-one, NUL-delimited find listing reserves existing
file/directory names; duplicate destinations get suffixes across batches.
UTF-8 stems are shortened on rune boundaries when needed to fit a suffix. Each push targets a random private
.part name; only successful pushes are renamed to their final names. Push uses
ArgumentList, and every remote shell path is single-quoted with apostrophe
escaping. mv -nT plus target/symlink checks and a temporary-path absence check prevents
silent replacement, including treating an unexpected destination directory as
a file conflict. Concurrent external writers can still cause a conflict that
stops the batch, rather than replacing their file.
Each push has a 30-minute limit, explicit cancellation and best-effort temporary
cleanup with a separate three-second deadline. Completed files are preserved.
After an error the batch stops; reconnect never replays file writes. If a rename
acknowledgement is lost, completion count is only a confirmed lower bound.
The UI shows current-file/count and an activity indicator, not byte percentages.
Input capture is stopped during batches and edge entry is disarmed afterward.
Content and paths are not written to the application journal. No companion
storage/network permissions are added. This is Windows-window dropping, not
cross-screen Android application drag-and-drop. Pairing codes go to ADB's stdin, not process arguments. Settings
contain preferences and a remembered device profile, but no pairing code or QR secret.
ADB itself manages its host authentication keys.

## Shutdown and protocol maintenance

Stop capture first, queue releases and UHID destruction, then close the socket.
Terminate only the shell process started by this session, remove only its own
forward, and attempt to remove the temporary scrcpy JAR. Close the companion channel before
the control socket. Do not call `adb kill-server`.
An in-progress connection is cancelled and awaited before application exit.

Wire format is tied to scrcpy **5.0.1**. UHID_CREATE includes vendor and product
fields. Do not substitute a different server version without updating the
codec, tests and both pinned digests.

## Limits of verification

Source inspection and portable protocol tests cannot verify Windows hook
timing, overlay behavior, touchpad integration, One UI UHID access, Android
layout configuration, or real clipboard behavior. Use `TESTING.md` after the
first requested build. Windows 0.5.1 has not been compiled or tested on hardware.

## QR discovery fallback (Windows 0.5.1)

The QR worker keeps polling through a failed ADB lookup and a failed pairing
attempt, with a three-second pairing retry delay and the existing two-minute
expiry. New QR code cancels/awaits the previous worker before generating fresh
credentials. Diagnostic messages contain only stages, never credentials.
ADB CLI instance names with service/local suffixes are normalized before
matching the exact dialog name.

When the service is missing from ADB, the Windows process sends bounded
one-shot IPv4 mDNS queries for PTR/SRV/A records to 224.0.0.251:5353 on up to
eight active interfaces. Ephemeral source ports request legacy unicast replies
(RFC 6762 sections 5.1 and 6.7). Sessions last at most 1.4 seconds per interface,
with at most 24 queries, 64 replies, 256 cached records and no persistent cache.
Only the requested pairing instance is eligible. Connection discovery uses
the already paired phone's IP. SRV/A answers must agree with the responder's
own IPv4 source and UDP port 5353. QR secrets still go only to ADB stdin.
The wire parser bounds packet size, counts, label lengths, record data and
compression-pointer hops; ignores zero-TTL/unknown records; rejects truncated
and unrelated responses. No scanning of TCP port ranges, ADB restart, firewall
changes, Bonjour dependency or new companion permissions are introduced.
IPv6-only or isolated networks may still need the code/IP fallback.

References: [RFC 6762](https://www.rfc-editor.org/rfc/rfc6762),
[DNS wire/compression](https://www.rfc-editor.org/rfc/rfc1035).

## Independent LAN mode (0.6.0)

`LanForm` is a separate modal UI. MainForm pauses ADB recovery, disconnects its
transport, disposes its hooks, and restores them when the LAN window closes.
Only one input engine owns Windows hooks. NativeAtStartup opens LAN directly
without querying ADB. LanSession implements the same IPhoneControl interface;
the ADB implementation retains its wire protocol and behavior.

Windows hosts TCP 38271 and UDP discovery 38272 while the window is open.
LanIdentity persists a self-signed RSA certificate and one phone token in a
DPAPI CurrentUser protected file. Pair QR includes IPv4, fixed port, host UUID,
SHA-256 certificate DER pin, random 256-bit one-use ticket, version and laptop name.
Tickets expire after 120 seconds on a monotonic clock and are never logged.
Four simultaneous connection handlers, eight-second handshakes, bounded framing
and output queues limit unauthenticated resources. Discovery replies expose only
host UUID/nonce/port; a discovered address cannot authorize an unpinned TLS peer.

Android ConnectionService binds outgoing sockets to a Wi-Fi Network, pins the
certificate before sending credentials, saves the permanent token before sending
`saved`, and reads/writes bounded big-endian length-prefixed UTF-8 JSON frames.
Heartbeats run every three seconds with twelve-second reader deadlines. Input
commands queued to main are bounded and checked against the current Link; old
connection commands are discarded. Reconnect delays reach thirty seconds.
No automatic capture or transfer replay occurs. Foreground connectedDevice service
uses CHANGE_NETWORK_STATE prerequisite, a persistent notification and user stop.

ControlService uses an accessibility overlay with NOT_TOUCHABLE and input-method
editor capability (API 33); no node-content retrieval. Its own pointer supplies
GB_EDGE samples compatible with the existing host edge policy. Taps/swipes are
bounded gestures, queued at most eight and reset on disconnect/lock. Drag paths
are recorded then dispatched at button release, not streamed UHID movement.
Typed text uses the current Windows layout, not full physical keyboard injection.

Downloads accepts only flat file names <=255 UTF-8 bytes, sizes <=2 GiB and
ordered 32 KiB chunks. It owns only its newly inserted pending MediaStore row.
SHA-256 and exact size must match before IS_PENDING is cleared. Cancellation or
socket teardown removes only that pending row. A lost final acknowledgement can
leave a completed file; the queue never retries it automatically.

QR scanning uses the embedded ZXing core decoder with a checksum-pinned 3.5.3
JAR and a legacy Camera preview. No external scanner app/cloud QR service is
required. The new APK has its own package com.galaxybridge.lan and Android 13+
minimum; the legacy Edge APK and ADB protocol remain separate.

## Phone-to-PC files (0.7.0)

The welcome frame advertises `upload: true` after authenticating the phone.
Older phones ignore it; a new phone refuses upload on an older host without
disconnecting input. Protocol version 1 and the pinned TLS connection are retained.
Reverse messages use separate `uploadBegin/Chunk/End/Abort` and `uploadAck` types,
so forward-file ACKs and input commands can share the existing bounded writer queues.
Each request has a random ID, a 20-second deadline and an ordered transfer ID/sequence.

SendActivity handles ACTION_SEND/ACTION_SEND_MULTIPLE content URIs and a system
ACTION_OPEN_DOCUMENT picker. The user sees the saved laptop and confirms sending.
ClipData plus FLAG_GRANT_READ_URI_PERMISSION transfers temporary read grants to
the private foreground service. No broad storage permission is added. Up to 100
files wait at most 30 seconds for a link, then remain bound to that exact Link.
Upload streams 32 KiB chunks with SHA-256; provider size -1 is supported and total
bytes are checked at uploadEnd. Cancellation closes the active provider stream,
interrupts waiting ACKs and sends a best-effort abort without the cancellation flag.

LanFileReceiver is platform-independent and owns only a randomly named `.part`
file in the host-selected directory. It bounds all data, normalizes Windows names,
checks hash and byte count, flushes and atomically moves without overwrite.
Name collisions use numbered suffixes. Receive configuration is locked with file
acceptance; disabling or changing the directory cancels the active file. A 30-second
idle deadline, socket teardown and either endpoint's cancellation remove its pending
file while keeping completed files. A process crash may leave a part file; no broad
directory cleanup or replay is performed. The host never opens received files.
Settings persist the chosen folder and receive toggle; FileDropForm shows progress,
cancel, destination selection and an explicit Explorer action alongside forward sending.
