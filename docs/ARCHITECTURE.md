# Architecture — no-debugging edition

Program keeps the existing per-user single-instance mutex and opens LanForm
with Settings.Load(). There is one application window, a tray menu and an
asynchronous shutdown path. Minimize hides to the tray; closing exits after
releasing captured input, cancelling transfers and disposing LAN/BLE services.

## LAN

LanHost exposes certificate-pinned TLS on TCP 38271 and discovery on UDP 38272.
QR pairing uses a short-lived secret and explicit phone confirmation. LanIdentity
persists the host certificate and phone token with CurrentUser DPAPI. Its existing
file path, certificate import flags and format are preserved across updates.
Settings keeps the same folder and surviving JSON property names; old unknown
properties are ignored. Pairing is not reset when the previous mode is removed.

LanProtocol frames bounded JSON with a four-byte length. LanSession validates
messages, negotiates clipboard features, serializes writes and cancels pending
operations on disconnect. Android ConnectionService reconnects to the paired host;
ControlService performs touch gestures, navigation and editing through accessibility.
Android input is guarded by device/session state and release handling.

InputCapture translates Windows low-level keyboard/mouse hooks into the selected
IPhoneControl transport. PhoneControlRoute gives ready BLE HID priority over
ready LAN touch, releasing capture before a transport change. Bluetooth starts
with the window; pending or absent HID does not block LAN. The only subscribed
phone is selected automatically; multiple phones require selection. Delayed BLE
clipboard-return completion is guarded by the LAN control generation, so it
cannot interrupt a newer touch capture. LAN Paste passes Unicode text directly, with no legacy
binary protocol adapter. Phone edge feedback uses the LAN JSON channel. Geometry,
button state and keyboard state live in Core.

## BLE HID

BleHidSession publishes encrypted HID GATT services through Windows WinRT. It
requires Bluetooth LE Peripheral support. Hid defines standard keyboard and
five-button mouse reports including vertical and horizontal wheels. HidReportPump
orders keyboard and mouse input and releases the old destination when cancelled.
Only the selected subscribed phone receives reports. BLE has no implemented
phone-edge return; Ctrl+Alt+F12 remains the return mechanism.

LAN carries files and clipboard when BLE supplies mouse/keyboard input. Bluetooth
pairing and LAN authorization are independent; users must connect the same phone
for combined operation. Physical keyboard layout belongs to Android.

## Clipboard and files

Windows listens for clipboard sequence changes; it suppresses feedback loops.
Android reads its system clipboard briefly on control return using an authorized
focusable overlay, rather than continuously monitoring the background clipboard.
Bounded requests, generation/sequence checks and cancellation prevent stale text
from replacing newer local data. Text and links are limited to 16 KB UTF-8.

Files use chunked transfer, bounded lengths, safe leaf names, unique allocation,
explicit receipt acknowledgements and temporary-file cleanup on cancellation.
LanFileReceiver validates Windows destinations; Android Downloads writes through
MediaStore. Files are limited to 2 GB; folders are unsupported.

## Build

The manual workflow tests source structure, Core/LAN behavior, Windows TLS and
JVM helpers, publishes the Windows executable, then builds one persistently signed
GalaxyBridgeLan.apk. The ZIP has no connection backend. Source checks also reject
reintroduction of the removed debugging launcher, transport or edge companion.
