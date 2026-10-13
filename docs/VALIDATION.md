# Validation — Windows 0.7.36 / Android 0.7.34

## Windows installer (0.7.36)

Based on main 5313976. The manual workflow now publishes a versioned Inno Setup
EXE instead of the portable Windows ZIP, with the Wi-Fi APK as the second artifact.
The installer uses a stable AppId, a per-user Programs directory, Russian/English
UI, Start menu/optional desktop shortcuts, and Windows uninstall registration.
It checks the existing per-user application mutex before install/uninstall.
The packaged self-contained application needs no separate .NET installation.
Settings/identity and received files are outside the installation payload and
are not deleted. The build script reads the version from the Windows project,
rejects missing publish output and failed/empty compilation, and deletes stale
Setup output before compiling.

Validation in this environment: PowerShell parsing and executable build-wrapper
checks pass for success, failed compiler with stale output, missing compiler
output, and missing application publish. Workflow YAML parsing, package checks
and whitespace checks pass. These checks use a compiler fixture, not Inno Setup.
Actual ISCC compilation, install, repeated install, shortcut/registration checks,
uninstall and data preservation are enforced by Test-WindowsInstaller.ps1 in
the Windows workflow before upload; they have not been run here. No Setup EXE
was built locally and no Actions build was started. Android remains 0.7.34.

## Widget transparency (Windows 0.7.35)

Based on main a89ab0e. Settings has a live 0–80% transparency slider, a percent
label and Show widget preview action. FileWidgetTransparency defaults to 0
and clamps loaded/set values; it persists through the existing atomic JSON
save and is applied before showing a newly opened widget. Rendering supplies
the selected global alpha to UpdateLayeredWindow alongside per-pixel alpha,
including hover/focus and transfer overlays. It never sets Form.Opacity.

All Windows/Core production C# sources compile with C# 12 and .NET 8/Windows
reference assemblies, with only the existing QRCoder 1701 reference warnings.
All 2527 existing portable core assertions pass. Source-package and whitespace
validation pass. Native Windows visuals, slider interaction and restart/DPI
behavior need the physical checklist in TESTING.md. No EXE was packaged here.
Android remains at 0.7.34.

## Notification sound (Android 0.7.34)

Based on main a9ef3ff. Fresh receipt channels explicitly use the system
notification URI with notification/sonification audio attributes. Existing
channels and custom sounds remain unchanged on update. Settings reports silent
categories, phone silent/vibrate mode, zero notification volume and DND.
The explicit Enable sound action switches a silent legacy channel to one stable
replacement channel and posts a test; later muting/blocking is respected and
the button opens its settings. Global blocks open application settings.
Foreground connection notifications remain low-importance.

Validation: all 73 Android UI/display/sharing/receipt tests pass, including
eight added checks for sound metadata, explicit repair, preservation, exact
category routing, permission denial, global blocking and phone sound state.
ReceivedFiles is explicitly included in the Maven compiler inputs so incremental
builds recompile it. Source-package and whitespace validation pass.
No APK was packaged, and physical S25 audio output was not tested here.
See the notification sound checklist in TESTING.md.

## Borderless artwork file widget (Windows 0.7.33)

Based on main 7b3e3e5. The selected 1635×962 RGBA reference is embedded unchanged
as GalaxyBridge.FileWidget.png. The unowned topmost WinForms widget uses
FormBorderStyle.None and WS_EX_LAYERED with UpdateLayeredWindow/AC_SRC_ALPHA.
Premultiplied rendering preserves transparent margins, silhouette and glow.
It does not use a chroma key or set WinForms Opacity/TransparencyKey; shared
theme application excludes this independently painted window.

The central area chooses or accepts up to 100 existing regular files; frame
dragging, the circular close action, transient progress/cancel, connection/result
tooltips, keyboard actions and accessible named actions use scaled artwork
coordinates. Drop state and a submission gate prevent overlapping sends. Closing
only the widget leaves the app and active transfer alive; existing position/tray
settings still manage visibility and location. Android remains at 0.7.32.

- All Windows/Core production C# sources compile with C# 12, .NET 8/Windows
  reference assemblies and both embedded resources. Warnings are errors except
  the existing QRCoder reference-unification warnings 1701/1702.
- All 2527 portable assertions pass, including 45 new checks for button/frame
  hit targets at 100/150/200% and compact size, close/cancel precedence, disconnected
  and busy drops, folders, partial missing files and 100/101-file boundaries.
  Existing real TLS file/clipboard/receipt checks also pass.
- Source-package and whitespace checks pass; the PNG is byte-identical to the
  selected reference and retains its alpha channel.

This environment cannot run the Windows UI or full MSBuild packaging. Native
alpha rendering, OLE Explorer drops, screen-reader navigation and actual DPI/
monitor transitions require the Windows checklist in TESTING.md. No EXE or APK
was packaged here; the existing manual Windows Actions workflow remains available.

## File receipt notifications (0.7.32)

Based on afa7c3d. Receipt events and verified publication remain unchanged.
The Windows receiver previously depended solely on a tray balloon with no
reliable visibility acknowledgment. It now shows an unowned native corner
card with ShowWithoutActivation/WS_EX_NOACTIVATE, exact receipt folder action,
ten-second hover-aware dismissal, theme support, updates for subsequent files,
and application-exit cleanup. A Settings test uses the same presentation path.
This card does not create a Notification Center entry. Actual minimized/focus,
multi-monitor/DPI and physical Windows rendering checks remain necessary.

Android previously requested notification permission only from a settings
button, did not report blocked channels, used default channel importance and
suppressed alerts on replacement. Setup now requests POST_NOTIFICATIONS once
from the visible activity; later manual requests open app/channel settings when
appropriate. New receipt channels use HIGH importance, existing DEFAULT/NONE
channels are preserved and explained in the status. A repeated receipt can alert
again. A separate test ID (63) preserves actual receipt ID 62 and connection ID
61. Permission denial or notification exceptions still cannot undo saved files.
ShareActivity/background uploads do not trigger notification permission UI.

Validation on this change:
- Windows Release project build succeeds with zero warnings/errors.
- All 2482 portable assertions pass, including real TLS receipt delivery and
  the verified-file event path with notification subscriber failure.
- All 65 Android API 35 Robolectric tests pass: seven new notification regressions
  cover replacement alerts, blocked/default channels, first/reopened setup,
  denial-to-app-settings, channel settings, and independent test notices. Design
  tests explicitly grant the onboarding permission to isolate navigation.
- All 31 production Java sources compile against Android 15 Robolectric reference
  classes with generated R and ZXing. Source-package and whitespace checks pass.

No signed APK, portable EXE packaging, actual Android permission dialog,
Samsung heads-up or live desktop popup was tested here. Use the existing manual
Windows Actions workflow and the device checks in TESTING.md. Existing channel
importance requires the user's system settings change after an upgrade when
heads-up is desired; the code does not delete/recreate or bypass blocked channels.

## Shared native interface (0.7.31)

Based on 6088188. The October 10 design concept is implemented in native Java
Android views and C# WinForms. Shared palettes, rounded cards/buttons, device
maps and system/light/dark appearance are used across the maintained apps.
Windows navigation separates Devices, Control, Files and Settings. QR is a
separate owned window; transfer history records only successful saved/ACKed files
for the current app session. Existing input and transfer handlers remain.
Android bottom navigation restores the selected tab across recreation. Appearance
preferences are separate from pairing data. System/keyboard insets, scrollable
content and 48 dp or larger navigation targets support large fonts.

58 Android API 35 Robolectric tests pass, including ten new UI cases for tab
restoration, file/text/folder intents, no connection/upload side effects from
navigation, theme persistence with pairing intact, system night override,
200% font layout, clipboard draft restoration, refreshed notification permission,
keyboard insets and native graphics rendering. All prior wake, share, display and
receipt tests pass. Four Android PNGs in docs/ui are native-rendered fixtures
from the production views, with the real icon asset; they are not S25 screenshots
and do not represent a live connection.

All 2482 portable assertions pass. Full Windows production sources compile
against .NET 8/Windows reference assemblies. Public Android API 35 compilation
covers all 31 production Java sources, and resources/manifest link. Source checks
pass. Direct Windows compilation retains the pre-existing QRCoder reference
unification warnings. Signed EXE/APK packaging uses the manual workflow.

WinForms rendering, keyboard focus and DPI 100/150/200%, live monitor changes,
QR close/expiry, saved manual themes, widget placement and actual S25/DeX/insets
still require physical Windows/Android verification. Local compiler checks do not
establish native Windows rendering or signed packaging success.


## Phone wake on cursor entry (0.7.30)

Based on main commit 23d4c65. Windows negotiates wake support through paired
LAN heartbeat metadata and sends an identified wake request before entering a
sleeping phone. The request is processed only on the current authenticated TLS
link, on the input looper and within the bounded command queue. ACKs correlate
with the latest request and mean launch acceptance, not hardware readiness.
Legacy phones never receive an unknown wake command.

Android selects only the default phone display for wake; connected DeX is excluded.
A bound accessibility service or granted overlay permission enables a private,
transparent activity with no recents entry or main-task affinity. It calls public
setTurnScreenOn/setShowWhenLocked, cannot dismiss the keyguard, and closes after
screen-on or a two-second deadline. There is no wake lock or new permission.
Awake phones are a no-op. Android reports screen power, device lock and input
readiness; accepted launch alone cannot transfer the PC cursor.

Windows keeps a pending user transition for at most 15 seconds and waits for
awake, unlocked input readiness and released physical input. Edge departure,
explicit cancellation, pause, disconnect, file sending and DeX selection cancel
pending capture. A button/hotkey wake does not require an edge position. No
capture is resumed later after cancellation or expiry.

Validation: 48 Android API 35 Robolectric tests pass (10 new wake cases and all
38 prior display/sharing/receipt regressions). All 2482 portable assertions pass,
including real pinned TLS wake packets, correlated/stale ACKs, legacy behavior,
locked-screen gating and DeX rejection. Full Windows production sources compile
against .NET 8/Windows reference assemblies; resource/manifest linking and all
30 Android production Java sources compile against the public API 35 SDK.
Source checks pass. The local direct Windows compiler reports the existing
QRCoder .NET 6-to-8 reference-unification warnings; no new compile errors occur.

Signed EXE/APK packaging, Windows UI and actual Samsung screen wake/background
launch behavior require the manual workflow and physical devices. The tests
simulate display power and do not establish that S25 hardware actually wakes.
See TESTING.md for sleep/AOD/PIN/fingerprint, Bluetooth, DeX and cancellation checks.

## Background file sharing and Windows receipt notices (0.7.29)

Based on d5e394e. Both Windows and Android are version 0.7.29. The exported
ShareActivity uses a transparent theme without a launch preview, no content view,
no app task affinity and no recents entry. At its first resume it forwards every
content URI through ClipData/read grants to ConnectionService and immediately
finishes. The foreground service owns the upload and existing FIFO queue. Saved
submission state prevents recreation/resume from sending twice. Text-only shares
keep the clipboard window; manual file picking/sending stays in private SendActivity.

Windows LanSession emits receipt only after the production receiver validates
size/SHA-256 and publishes the final file. LanHost forwards it before the main
form can attach, so fast receipts do not depend on status polling. The form posts
notification work to its UI thread and uses the tray icon's received-file balloon.
Clicking opens the destination folder from the latest receipt. Notification errors
cannot undo receipt, reverse a successful ACK, or disconnect the transport.

38 Android API 35 Robolectric tests pass, including background handoff with no
opened activity/content view, grants, recreation, multiple/ClipData shares, mixed
text/file sharing, invalid input, pairing, FIFO/busy/cancellation, and the existing
display and Android receipt regressions. All 2459 portable assertions pass over
real TLS, including new receipt-event checks for partial/completed files, rapid
duplicate names, notification callback failure, checksum rejection, cancellation
and disabled receipt. Full Windows production sources compile; Android resources
and manifest link with build-tools 35 and all 28 production Java sources compile
against the public Android API 35 SDK. Source checks pass.

These are source/runtime tests, not signed EXE/APK packaging or device/UI tests.
Run the manual workflow for installable updates and verify S25/My Files/Gallery
handoff with Galaxy Bridge already open/closed, DeX, source-app return, real URI
grants after handoff finishes, large/queued files and Windows receipt notifications.
Windows notification/Focus Assist settings may suppress the balloon without
interrupting receipt. Android's existing foreground connection notice remains.

## Android received-file notifications (0.7.28)

The production Downloads receiver posts a separate, auto-cancelling receipt
notification only after size/SHA-256 checks and successful MediaStore publication.
The final provider filename is shown, with the requested name as fallback when
the query is empty. Notification permission/manager failures cannot undo a saved
file or cause a negative transfer acknowledgement. One notice is updated with
the latest receipt; foreground connection notice 61 remains separate from 62.

An immutable activity PendingIntent targets a private ReceivedFolderActivity.
It opens the external-storage document for primary:Download/GalaxyBridge with
directory VIEW. If unsupported, it launches OPEN_DOCUMENT with that initial
directory, then general Downloads, then a saved-path toast. It requests no tree
or broad storage permission. Files selected from the fallback chooser open with
a temporary read grant.

36 Android API 35 Robolectric tests pass: 14 new receive/notification/navigation
tests plus the existing 22 sharing/display regressions. A fixture ContentProvider
backs the real Downloads byte stream and publication lifecycle. Tests cover
pending/chunk timing, successful publication/name/tap/channel, checksum and size
failures, cancellation, publication failure, denied/disabled notifications,
notification service exceptions, notice replacement, and folder/chooser/downloads
fallbacks. All 27 shipped Java sources compile against public Android API 35;
source checks pass. These checks do not validate Samsung file-manager routing,
lock-screen notification presentation, or APK signing/packaging. Run the manual
workflow and validate PC-to-S25 receipt/tap with the actual installed managers.
Windows stays 0.7.26.

## Automatic Android file sharing (0.7.27)

ACTION_SEND and ACTION_SEND_MULTIPLE with content URIs automatically submit
one foreground-service upload on the first resumed sending window. Saved pending
state preserves submission across pre-resume recreation and prevents replay after
rotation or revisiting an already-submitted share. Text-only sharing still opens
the clipboard window. Finished redirect windows do not schedule an uninitialized
progress view. Pairing validation and explicit manual retry remain.

The foreground service owns a FIFO queue of copied URI lists, bounded to eight
waiting selections of up to 100 files each. New shares cannot disappear behind
the previous upload's busy flag. Worker completion posts queue advancement back
to the main looper; cancellation clears waiting selections and interrupts the
current read, and service shutdown cannot restart the queue. Each service intent
forwards all shared URIs in ClipData with a read grant. File framing, checksums,
PC receiving, cursor/DeX handling and removed file-edge transfer are unchanged.

22 Android API 35 Robolectric tests pass: 13 file-share/lifecycle/queue cases and
9 existing display-adapter regressions. Tests verify service submission and URI
read grants, one/multiple/ClipData-only shares, recreation/resume, pairing, text
redirect, busy submission, copied FIFO data, queue capacity and cancellation.
They do not simulate the phone provider's actual bytes or a live PC. Existing
843 LAN JVM assertions pass, including production upload bytes/checksums and
payload/read-access tests. Shipped Java compiles against public Android API 35;
source checks pass. Signed APK packaging and phone-to-PC sharing require the
manual workflow and device validation. Windows remains 0.7.26.

## Cursor speed and edge-entry delay settings (Windows 0.7.26)

The Windows form labels the existing 0.25–4x speed setting for phone/DeX and
adds a saved edge-entry delay editor (0–2000 ms in 50 ms steps, default 350 ms).
Each editor has a default-reset button. EdgeEntryDelayMs replaces the fixed
350 ms threshold in the PC-edge entry check. The existing 100 ms polling,
held-input guards and adjacent-PC-monitor checks still apply. Changing delay
clears the pending edge timer and requires leaving the edge before a new entry.
Hotplug migration remains controlled by the DeX capture-switch policy.

Old settings files keep their speed and default the missing delay to 350 ms.
Out-of-range saved delays are clamped before constructing the numeric editor.
Nine local smoke assertions compile the real Settings class and exercise actual
Save/Load in an isolated application-data folder, including old-file migration,
JSON persistence, limits and subsequent saves. All 2445 portable assertions pass
and full Windows production sources compile with .NET 8/Roslyn reference
assemblies. Source checks pass. Windows UI rendering and complete signed/portable
packaging still require native validation and the manual workflow. Android
runtime and its 0.7.25 version are unchanged by this Windows-only setting.

## Automatic external DeX and simplified package (0.7.25)

Based on c89948aa721afcbe8b9b297dfc3d114cb740f2ae. Galaxy Files is no longer
built, tested or uploaded by the active portable workflow. Its independent
sources remain in the repository. The package check now requires one EXE and
one APK (GalaxyBridgeLan.apk). The companion's Files signature IPC and receiver
are removed, as are file-edge overlays, preparation cards, desktop OLE handoff
and the edge-transfer preference. Removed staging requests are rejected without
breaking ordinary file TLS; legacy cancellation is accepted and old peers receive
forced-disabled configuration. Upload/receive, native receive progress, the
Windows send widget and cursor edge transitions remain supported.

The companion always chooses the public external display automatically and
ignores old phone/manual-display preferences. Display validity is separate from
wake state: a connected sleeping monitor keeps the DeX target while input waits,
so it cannot briefly steer the smartphone. Actual monitor removal restores the
phone. Geometry, pointer context and gestures still use the selected display.
Target announcements run on the input looper after display revision and old
stroke cancellation have been synchronized.

Windows distinguishes target changes from geometry/error revisions. An existing
capture can resume on the new target, after release and readiness, with a fresh
capture epoch and no held-input replay. An idle PC cursor is not captured just
because a monitor connected. Pending resume is cancelled by user stop, pause,
lock, disconnect, file sending or a 15-second deadline; ordinary input failures
do not start an automatic retry loop. DeX uses Wi-Fi even when BLE is ready.

Local verification:
- 9 Android API 35 Robolectric display-adapter tests pass: old preferences,
  hotplug/removal, screen-off DeX, lock, private displays and target geometry.
- 843 LAN JVM assertions pass, including 27 display-selection assertions,
  pointer/wheel/clipboard, actual upload bytes/checksums and drop payload parsing.
- 2,445 portable Core assertions pass: real TLS target/revision events, removed
  edge rejection and exact ordinary received bytes, widget sending/cancel,
  route priority and automatic-capture migration policy.
- All shipped Android Java and Windows production sources compile. Source
  package checks pass. Archived Files UI tests are not part of this package.
- Signed EXE/APK packaging and real Samsung DeX cursor/touch/hotplug remain for
  the manual workflow and physical devices.

## CI Files window-display regression (0.7.24 / 0.2.3)

Manual run 38229996709 passed the portable protocol tests, Windows TLS tests,
Windows x64 publishing, LAN JVM tests and file-operation tests. Its Files UI
test `wifiMouseDoubleClickOpensFolder` then threw UnsupportedOperationException
because the framework test activity's Context had no associated display.

FilesActivity now reads the display of its attached decor View for registration,
remote input and focus changes. No attached/valid window means no input and an
unregister message; it never guesses the phone display. This also binds DeX
coordinates to the actual window instead of a potentially unrelated Context.
Legacy packets without display metadata remain accepted only on the phone.

All 19 Robolectric Android API 35 UI tests pass locally (16 activity tests and
3 presentation tests), including the original failure, cross-display rejection,
legacy phone input, detached-window rejection and window-scoped IPC registration.
The two detached-window tests restore their windows for ordinary lifecycle
cleanup. All Galaxy Files production sources compile against public API 35 and
source-package checks pass. Runtime versions remain 0.7.24 / 0.2.3; full APK
packaging still needs a new manual workflow run from corrected main.

## CI display-test heartbeat regression (0.7.24)

Manual run 38227935380 on Windows failed in the portable protocol tests at
`legacy phone readiness preserves existing transport policy`, before packaging.
The simulated peer always answered ping with `ready=false`, overwriting a
preceding display-status packet depending on scheduling. It now retains the
scenario's current pong (including display, revision and desktop policy) and
uses it for heartbeat replies. Each display test deliberately sends an extra
heartbeat before its ordered request barrier, covering both readiness states
and display changes without relying on runner timing. Production code and
application versions are unchanged.

All 2,493 portable assertions pass with real TLS. Replaying the old fixed-false
heartbeat in a separate test assembly deterministically reproduces the exact CI
failure. Source-package checks pass. Local compilation uses Roslyn; the SDK CLI
cannot start in this runtime due to its process-information lookup. The full
Windows packaging workflow still needs a new manual run from the corrected main.

## Explicit Android display routing for external Samsung DeX (0.7.24)

Based on ad901dea590565ae8f25f7e591d54827f5ebd545. User confirmed that DeX is
on a separate monitor/TV and the GalaxyBridge pointer remains on the phone.
ControlService previously read the default phone display, created its overlay
there and dispatched gestures without displayId. This change selects a display
before capture, creates its typed window context and sends every touch, continued
stroke, ending stroke and wheel/pinch with that displayId. It reads windows on
all displays and resolves target nodes on the captured screen. The phone root
is never used to click a secondary display. Screen metrics, density and slop
come from the target. No Samsung-private APIs, ADB or hidden moved-display
callback are used.

Android setup provides a saved Auto/external/manual-phone choice and selected
screen diagnostics. Auto favors a valid public active external presentation
screen, without assuming DeX is ID 1, then other public external screens, then
phone. Explicit missing targets have no phone fallback. Display removal or
changes in ID, metrics, rotation or density cancel the capture before another
can begin. The old touch-up remains bound to its old display. Input/overlay
failure now stops input and reports a diagnostic rather than dropping file TLS.
Locked devices still reject input; an active external screen may work while the
unlocked phone's own screen is off.

Pong carries optional display/revision/desktopInput metadata. Windows invalidates
old edge samples on changes and chooses Wi-Fi for a secondary target, including
when BLE HID is connected; an unavailable explicit secondary target cannot
fall back to a phone Bluetooth cursor. Normal phone transport priority remains.
File IPC binds registration and mouse packets to the activity's actual display.
A signed Files press retains ownership while dragging outside its DeX window;
release, unregister/reset or migration clears ownership. Edge overlays and file
holds are also tied to the selected display. The earlier opt-in checkbox,
Windows widget and ordinary transfers remain supported.

- All companion and Files Java sources compile against public Android API 35.
- 1,134 LAN JVM assertions pass, including 28 display selection, geometry identity,
  non-default IDs, phone-off/external-on selection, explicit missing targets,
  display-scoped IPC and file hold/dwell checks. These are not native displays.
- 2,493 portable Core assertions pass. Real TLS tests cover legacy readiness,
  external target/revision updates, stable captures, stale-sample rejection,
  removal and return to phone, and malformed metadata. Route tests check DeX
  Wi-Fi priority and no unintended Bluetooth fallback.
- Full Windows production sources compile with .NET 8/Roslyn reference assemblies.
- EXE/APK packaging, OEM accessibility overlay placement and touch delivery,
  window focus/caption behavior, display hotplug/lock, keyboard/global navigation
  and actual DeX file handoff require manual device validation. Robolectric Files
  UI regressions remain required by the packaging workflow; the later window-display
  fix restores the local Maven runtime and all 19 UI tests pass. Existing phone
  smoothing/input JVM tests pass.

Android API references:
https://developer.android.com/reference/android/accessibilityservice/GestureDescription.Builder#setDisplayId(int)
https://developer.android.com/reference/android/accessibilityservice/AccessibilityService#getWindowsOnAllDisplays()
https://developer.android.com/reference/android/accessibilityservice/AccessibilityService#createDisplayContext(android.view.Display)

## Opt-in edge files and standalone Windows send widget (0.7.23)

Based on 4e70028528d183a8d17d77ff65e307c9f512dc44. The existing edge file
transfer remains available under a separate, persisted, off-by-default checkbox
in the main Windows form. The cursor's EdgeEntryEnabled preference is unchanged.
fileEdgeConfig=1 is negotiated in hello/welcome; welcome carries the initial
preference and supported peers receive updates. Legacy peers are not sent an
unknown command. The host independently refuses preparation/start when edge
transfer is disabled, even for an old APK. Disabling disposes only staged files,
not ordinary incoming/outgoing transfers, and Android cancels file-only staging.
Updates on reconnect use the saved Windows setting; interrupted batches are not
replayed. Samsung's overlay receiver still requires its explicit Android enable.

A separate topmost tool window provides drag/drop and file-picker sending to the
paired phone using the existing SendAsync/SendFileAsync transport. It has no owner
so minimizing the main form does not minimize it. Availability follows the same
session/pause/lock/busy state as existing sending. It accepts ordinary files
only (up to 100), presents current-file byte progress and Cancel, and sends to
Download/GalaxyBridge. Progress uses ACKed chunks; success still requires fileEnd
ACK. Closing the widget does not cancel the shared transfer. Main app shutdown
closes it and cancels through the existing lifetime. Persisted visibility and
position are restored and clamped into an available working area.

- 2,467 portable Core assertions pass. New real TLS tests cover negotiated initial
  state, enable/disable commands, disabled staging rejection, cancellation of
  staging without dropping TLS or ordinary receiving, blocked stale dragStart,
  acknowledged multi-chunk progress, actual bytes/checksum, final save ACK and
  cancellation before chunk ACK without disconnecting.
- Full Windows production sources compile with .NET 8/Roslyn references, including
  the actual native widget. A Linux run does not render WinForms or exercise OLE.
- Both Android applications compile with JDK 17/Java 8 bytecode, API 35 and ZXing
  for the companion. Existing JVM protocol/file tests remain in the workflow.
- Manual Windows packaging and physical verification remain required: checkbox
  persistence/reconnect and Android update, actual widget rendering/DPI/position,
  main minimize/restore, tray opening, offline and busy drop rejection, cancellation
  and delivered phone file. These native UI and device checks were not executed.

## Intermittent Galaxy Files edge-transfer disconnect (0.7.22 / Files 0.2.1)

Based on 5cbe844ee042d070ecac8754cbff54751e426e16. The user reported native
receive progress followed by no desktop copy and intermittent connection loss.
A concrete protocol fault was found: discardEdgeFiles emitted dragCancel without
an id, while LanSession required an id for every drag request. Physical release,
foreground cancellation or reset could therefore close TLS rather than simply
cancel one staged batch. Android now gives fire-and-forget cancellation a fresh
request id. Windows also accepts the exact legacy id-less dragCancel shape for
valid opaque batch IDs; other missing/null/invalid correlation remains invalid.
Stale cancel cannot remove a newer batch.

Foreground detection previously treated one null accessibility tree as app
exit and queried it several times per mouse packet. The routing policy now
allows up to one second of unknown tree state after a confirmed Galaxy Files
window, caches reads for 100ms, and invalidates on actual window changes. A
confirmed different app, lock/inactive capture or reset still cancels immediately.
Files handoff mirrors the desktop's last nonzero mouse direction: a ready file
at the edge waits for outward motion rather than sending a dragStart that the
host will reject after inward/parallel jitter. Zero-motion button/heartbeat
packets preserve direction. Capture side/display changes reset the intent and
cancel a pending batch. The file manager now shows a preparation-specific status
rather than instructions for the separate blue-card sender.

- Regression using the previous production LanSession reproduces TLS EOF on
  real loopback TLS when an id-less cancellation is followed by another packet.
- The corrected production Core/session/host pass 2,441 portable assertions.
  New tests check legacy stale and active cancellation during a partial receive,
  correlated ACK, a subsequent complete drag on the same session, and continued
  rejection of malformed preparation, null id and invalid legacy batch.
- Production Android companion and Files sources compile with JDK 17, Java 8
  bytecode, Android API 35 (and ZXing for the companion).
- 1,106 LAN JVM assertions and 41 file-operation/pointer assertions pass. The
  44 new transfer-policy assertions cover correlation IDs, bounded null-tree
  grace, focus-query cadence, lock/app change/reset and all four handoff directions.
- All Windows production sources compile with .NET 8/Roslyn reference assemblies.
- EXE/APK packaging, Android lifecycle timing, real Samsung Wi-Fi pointer routing,
  native progress presentation and final Explorer drop still require a new
  manual workflow build and the user's phone/PC. These are not covered by the
  Linux JVM/TLS tests. No reconnect/replay of an interrupted file was added.

Manual device regression: update GalaxyBridge EXE/APK to 0.7.22 and Galaxy Files
to 0.2.1 from the same new manual run. Test a small file and a large video while
keeping left held until the Windows cursor takes over. Move outward again if
preparation finishes after moving parallel/inward. Release early during another
preparation: it must cancel without disconnect, and the next drag must succeed.

## Visible inset file drop target (0.7.20)

Based on 208978c2fbd5edec5a0280315171d88330db0f50. The user's Samsung My Files
video showed native app-opening UI and Gallery launch during an attempted
edge transfer; the earlier toast appeared before a payload was received.
This experiment replaces the permanently blue one-pixel receiver with a
visible “На ПК” target during an actual global drag. Its nominal thickness is
64dp, with a 16dp gap from the usable edge; both shrink to fit the PC-facing
outer quarter. System bars and cutouts are excluded. The same already-attached
window is resized at STARTED so it retains its drag subscription. Idle mode
keeps only the transparent one-pixel monitoring window.

ENTERED does not end the gesture. Measured LOCATION inside the target and a
200ms dwell with a fresh contact, same capture, display, side, and physical left
hold are required. Ordinary mouse motion continues during this dwell. The
control service additionally waits for an in-flight accessibility continuation
to complete with its actual endpoint inside the target. File-only touch-up
clears queued overshoot and remains at that exact endpoint; ordinary pointer
release behavior is unchanged. The real PC button remains held. After touch-up,
DROP/ENDED or the existing three-second deadline decides delivery; EXIT alone
does not hide a pending target. Preparation still requires an actual in-bounds
DROP, Activity-owned URI permission attempt and a successful read probe.
The preparation toast now appears only after that probe and staging start.

Windows advertises the additional fileDropZone=1 capability; the new APK
requires it for this mode. Legacy fileEdge=1 remains advertised for 0.7.18.
Handoff accepts an entirely outer-quarter target with an in-bounds sample,
while retaining physical-button, epoch, freshness, modifier and outward-motion
checks. Existing card/receiver flows and the 0.7.19 receive progress dialog
remain supported. Status and diagnostic-copy controls are above the large
manual receiver so they are easier to reach in split-screen.

- All production Android sources compile as Java 8 against API 35 and ZXing.
- 1,062 JVM assertions pass, including 146 new target/contact tests and five
  additional file-only endpoint/up assertions. Real Android grants and drag
  delivery are not exercised by these JVM fixtures.
- 2,428 portable Core assertions pass, including real pinned TLS welcome
  capability checks and valid/invalid broad targets on all four sides.
- All Windows production sources compile using direct Roslyn/.NET 8 references.
- APK/EXE packaging and on-device Samsung overlay delivery, window resizing,
  touch-up and final Explorer handoff require a new manual Windows workflow
  build and a real phone/PC test. This does not establish that an application
  overlay can override Samsung's system app-opening receiver. If Samsung still
  consumes the drop, use the already confirmed real Activity receiver in
  split-screen/pop-up; automatic Samsung freeform launch is not implemented.

Android API references:
https://developer.android.com/reference/android/view/DragEvent
https://developer.android.com/reference/android/view/WindowManager.LayoutParams#TYPE_APPLICATION_OVERLAY

## Native receive progress dialog (Windows 0.7.19)

Based on 9d2327388323e31036b9ba9290305e05a6743b9c. Windows receipt and drag
preparation now use the actual Shell IOperationsProgressDialog / CLSID_ProgressDialog.
The modeless dialog lives on the WinForms STA thread, separately from the TLS
reader that writes and hashes bytes. UpdateProgress reports received bytes;
unknown/zero sizes remain indeterminate until verified publish. No extra local
copy is introduced. A collection is presented per file, not as an invented
aggregate size. Very short operations can finish between UI polls.

The receiver generates a new local operation ID even when the paired sender
reuses its wire transfer ID. Progress snapshots retain this ID through publish,
failure and cancellation. Native Cancel is checked against the current session,
operation and ordinary/staging channel under the receive lock; stale dialogs
cannot cancel the next upload. Staging cancellation destroys the unhanded batch.
Dialogs close on termination, session change, pause/lock, or application exit.
COM failure suppresses repeated retries for that operation but does not cancel
the transfer; the existing label remains available. No Pause button is enabled.
Native source-location text is omitted because Android URIs are not Shell paths.
Explorer retains ownership of the final OLE copy and its progress after a drop.

- Windows production sources compile with direct Roslyn/.NET 8 references.
- 2,399 portable Core assertions pass. Tests cover dialog lifecycle through a fake backend, actual byte
  values, unknown sizes, successful versus failed completion, cancellation,
  back-to-back files, reconnect, COM failure and disposal. Real TLS integration
  checks host-owned IDs and scoped cancellation for normal and staged uploads.
- Android sources, manifest, version and wire messages are unchanged (0.7.18).
- Rendering, native Cancel and Shell COM activation still require the manual
  Windows build and a real Windows 10/11 test; a Linux fake is not that test.

Microsoft API references:
https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ioperationsprogressdialog
https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ioperationsprogressdialog-startprogressdialog
GUIDs and vtable verified against Microsoft's win32metadata ShlGuid.h and
ShObjIdl_core.h SDK headers.

## Explicit global file-edge transfer (0.7.18)

Based on cf71c79345c309b9192a547c83571fd8ffec7a64. The existing My Files
receiver and prepared-card-to-Explorer flow are user-confirmed. A new explicit
experimental mode installs a public TYPE_APPLICATION_OVERLAY drop strip owned
by the private DropActivity; it remains alive when its task is minimized.
Only an actual global drag entering measured edge bounds while the same Wi-Fi
capture holds the left button can end the injected Android touch and obtain
the payload at ACTION_DROP. The existing parser, URI grant, read probe and
TLS/hash staging are reused. No incoming Intent is executed.

A five-minute hold binds preparation to capture epoch, side, display geometry,
measured edge and physical left-button state. Drop delivery has a three-second
deadline. Release, inward movement, rotation, lock, connection loss or owner
destruction cancels; staged cancellation uses the existing protocol. A prepared
batch consumes the hold once and emits fresh GB_EDGE2 with the actual strip.
Windows advertises fileEdge=1 and validates physical buttons, freshness,
capture epoch, thin outer-quarter bounds and outward movement before OLE.
The prepared-card path remains supported. Bluetooth/finger-only transfer is
not implemented. Both applications are versioned 0.7.18.

- All Android sources compile as Java 8 against Android API 35 and ZXing.
- 911 JVM assertions pass: 753 existing protocol/input/read checks, 100 new
  hold/projection/cancellation/one-shot checks across all four sides, and 58
  payload parser checks using data fixtures rather than Android runtime grants.
- 2,365 portable Core assertions pass. Added tests cover real loopback TLS
  delivery of GB_EDGE2 dragStart and valid/invalid measured-zone handoffs.
- Full Windows production sources compile with direct Roslyn/.NET 8 references.
  Local MSBuild remains unavailable due process-metadata access in this runtime.
- Device validation and EXE/APK packaging remain in the manual Windows workflow.
  In particular, Samsung global overlay delivery, the Activity-owned grant from
  that window, final injected touch-up position, and Explorer OLE must be checked
  on the actual phone/PC. This is an experimental implementation, not a claim
  that those device-specific steps have already passed.

## Interrupted TLS attempts and replacement sessions (Windows 0.7.17)

Build follow-up: Windows Actions run 38154658334 stopped in the new host test
at its first TLS client authentication (`LanHostTests.cs:43/106`, unexpected
transport EOF). Its in-memory identity fixture used the directly generated
certificate rather than the production PFX/UserKeySet import. The fixture now
exports/imports the certificate with exactly those key-storage flags and clears
the temporary PFX bytes. All host/lifecycle assertions remain enabled. This is
a test-fixture correction; application code and versions are unchanged. Local
portable assertions pass after the correction; the Windows workflow must be
rerun to confirm Schannel and full packaging.

Based on 3d1d4f4f74b9d70963ad031340c56cd231bd8168. The user confirmed the
My Files receiver transfer works, but reported intermittent Windows status
`TLS; IOException, 0x80131620`. This is the general IOException HRESULT,
not proof of a certificate, VPN, firewall or particular network failure.
Reference: https://learn.microsoft.com/en-us/dotnet/api/system.io.ioexception

Source inspection found two independently reproducible defects: the host closed
its current session before a replacement acknowledged `saved`, and pending
attempts could overwrite the status of a working session. Activation now occurs
only after valid acknowledgement and a fresh authorization/Pause check. Pending
states are suppressed while a current session is alive; the UI also checks the
current connection when executing queued callbacks and restores success status
on reconnect. I/O/timeout diagnostics explain automatic reconnect and preserve
stage/type/code without exposing provider messages. Authentication failures
remain distinct. Certificate validation, pinning, mouse cadence, and Android
code are unchanged. Interrupted file transfers are not automatically replayed.

- Production Windows sources compile with Roslyn/.NET 8 reference assemblies.
- 2,339 portable core assertions pass, including 18 host/diagnostic checks.
  New integration cases use the production LanHost and LanSession over real
  certificate-pinned loopback TLS: aborted handshake, missing acknowledgement,
  preservation of the old socket/heartbeat/status, successful replacement,
  and Pause/Forget during the acknowledgement wait.
- The host integration suite substitutes only in-memory identity storage and
  receive settings; production certificate import/DPAPI tests remain separate.
- Local .NET CLI/MSBuild cannot access process metadata in this environment.
  Compilation used the installed Roslyn compiler directly, followed by direct
  execution of the test assembly on .NET 8. Linux TLS uses OpenSSL, not Schannel.
- Full portable EXE packaging and Windows/phone checks remain in the manual
  workflow. The specific cause of the user's intermittent TLS failure has not
  been reproduced or established; the lifecycle/status fixes do not claim to
  prevent all network failures.

## File URI plus launcher pin-request companion (0.7.16)

Based on 9a92cd053576e8dfd0e80da1b4b95a504ccad442. The user's diagnostic
identified the exact failing shape: video/mp4 with a direct content URI item
followed by a URI-less Intent containing only EXTRA_PIN_ITEM_REQUEST of type
PinItemRequest. 0.7.15 rejected that companion before requesting drag permission
or reading the actual video.

The parser now skips only the recognized launcher-only companion, requiring
the actual LauncherApps.PinItemRequest type and no file/text/HTML/data/nested
clip/other-extra fields. It never accepts the pin request or launches an Intent.
At least one real content reference is required. File-bearing and unknown
entries remain checked, and a partly unsupported selection is still rejected.
The raw clip allowance is 101 items so 100 files plus a companion remain valid;
the actual reference cap stays at 100. Nested clips use the same rule.

- Companion sources compile for Java 8 against Android API 35/ZXing.
- 58 payload parser assertions pass on JVM-only data fixtures, including 16 new
  checks for the observed Samsung shape, companion ordering, companion-only
  rejection, incorrect request types, additional unknown/file fields, nested
  clips, and 100-file / 101-actual-file boundaries.
- Structural source checks pass. The fixture PinItemRequest has no launcher
  operations and is outside the APK source tree.
- No Windows, mouse, URI access, LAN upload, signing, or permission changes.

Actual Android permission acquisition, video reading and end-to-end transfer
still require the repeat device test; the diagnostic alone proves the payload
shape, not byte access. Full signed APK packaging remains in the manual workflow.
API reference: https://developer.android.com/reference/android/content/pm/LauncherApps#EXTRA_PIN_ITEM_REQUEST

## Drop payload parsing and format diagnostics (0.7.15)

Based on 6f8171f6319b0bc07f102e6c1b60b1bef859f91d. The supplied 30.59-second
recording 1000161484.mp4 shows drag-start feedback, target highlight, and the
generic unsupported-reference message after a drop. Source inspection places
that failure before permission request/probing: Item.getUri() was either null
or used a non-content scheme. The video does not reveal the actual payload or
prove that Samsung omits content references altogether.

Adds bounded extraction of direct URIs, Intent.data, nested ClipData,
EXTRA_STREAM and EXTRA_INTENT, plus whole content URI text. Gives direct file
representations precedence over alternate text/ancillary Intent data. Does not
execute received Intents, parse serialized Intents, map filenames to storage
paths, or accept a partly unsupported selection. Existing/public read access
is tested even if requestDragAndDropPermissions returns null. Diagnostics show
MIME, field presence, schemes, extra keys and types without dumping values.

- Companion Java sources compile against Android API 35/ZXing for Java 8.
- Payload parser tests run on minimal JVM-only data fixtures outside the APK;
  they exercise the production parser, but do not simulate framework grants.
- 795 portable JVM assertions pass, including 42 payload-parser checks for
  alternate containers, precedence, selection completeness, count/depth bounds,
  unsupported paths/URLs, and diagnostic omission of extra values.
- Portable stream, input, upload, clipboard and wheel tests remain in the workflow.
- Windows, mouse processing, pairing, signing and upload protocol are unchanged.
- Samsung payload details, successful file reads, permission forwarding and
  eventual transfer still need the next physical-device test. No signed APK
  packaging or real Android runtime execution occurred in this environment.

Official references:
- https://developer.android.com/reference/android/content/ClipData.Item
- https://developer.android.com/reference/android/content/Intent
- https://developer.android.com/reference/android/app/Activity#requestDragAndDropPermissions(android.view.DragEvent)

## Android multi-window file-drop receiver (0.7.14)

Based on f310889385bae93693cc79cde8b29495456817cb. Adds a private, resizable
DropActivity, opened from Setup, using the framework OnDragListener. Content URIs
are read only at ACTION_DROP and permission is requested before provider access.
Metadata queries, opening and the first-byte probe run off the UI thread with
cancellation, a 20-second cancellation request, and deterministic stream cleanup.
The button forwards ClipData/read permission into SendActivity before the original
owner closes, then starts the existing staged preparation. No data is uploaded
by the probe itself; all files must pass before the button becomes available.
Recreation requires a fresh drop rather than restoring expired drag permissions.

- All companion Java sources compile for Java 8 against Android API 35/ZXing
  with ECJ; only pre-existing production warnings remain.
- 753 portable JVM assertions pass, including 27 new stream/read-access checks:
  valid and empty files, missing metadata, one-byte reads, count and known-size
  limits, I/O failure, permission rejection, cancellation and stream cleanup.
- Source-package checks pass; manual workflow includes the new probe tests.
- Windows, LAN upload protocol and ordinary mouse gesture processing are unchanged.
  Windows 0.7.13 remains compatible; signing and increasing workflow versionCode
  retain the existing APK update mechanism.

The user confirmed that the existing card-to-Windows-folder operation works.
The Samsung My Files global drag, permission forwarding, split/pop-up window,
and accessibility-injected drag still require device testing. Framework behavior
cannot be verified by JVM tests. Full signed APK/EXE packaging remains in the
existing manual Windows workflow. This receiver is a two-gesture prototype,
not direct My Files-to-Windows screen-edge handoff or virtual-file streaming.

Official API references used for implementation:
- https://developer.android.com/develop/ui/views/touch-and-input/drag-drop/multi-window
- https://developer.android.com/reference/android/view/DragEvent#getClipData()
- https://developer.android.com/reference/android/content/ContentResolver

See the device checklist in [FILE_DRAG.ru.md](FILE_DRAG.ru.md).

## Staged phone-to-Windows file drag (0.7.13)

Based on main 71bae15bc06505fd020c1d583a8655fe58325745 (0.7.12), retaining
the latest Android file-picker click fix. Adds optional
`fileDrag=1` capability, private SHA-256-verified staging and Windows OLE FileDrop.
The companion's own prepared-files card hands off only during Wi-Fi capture.
No desktop click is injected: QueryContinueDrag follows the physical mouse hook's
release after the original DOWN was consumed by phone capture. Ordinary 16ms
mouse batching, movement packets, pointer rendering and edge-return policy remain
unchanged; only an active prepared file card gets the new drag interception.

- Core/staging/real-TLS tests include checksum verification, duplicate names,
  batch size/count caps, partial preparation, stale batches/captures, cancellation,
  released buttons, all four sides, replay rejection, and legacy upload compatibility.
- All Core/Windows sources compile with Roslyn/C# 12 and .NET 8 reference assemblies
  (warnings as errors, standard assembly-version unification warning 1701 excluded).
- All companion sources compile for Java 8 against Android API 35/ZXing using ECJ;
  only existing deprecation/unused-field/import warnings are reported.
- 2321 Core assertions and 726 JVM protocol/input assertions pass.
  Structural source checks pass.

The environment cannot run MSBuild because its process-info APIs are unavailable;
direct Roslyn compilation and execution of the portable Core suite were used.
Full signed APK/EXE packaging remains in the existing manual Windows workflow.
Explorer OLE, physical mouse handoff and Samsung S25 behavior have not been tested
on devices. See [the device test procedure](FILE_DRAG.ru.md). Originals are preserved;
this initial version does not drag directly from Samsung My Files or use native HID.

## File picker click path (0.7.12)

The supplied 15.56s recording shows file rows highlighting without completing
selection. Source inspection found that ACTION_CLICK was attempted only if
mouse-up arrived before the 32ms overlay-removal delay. Typical longer clicks
were sent only as continued accessibility touch strokes.

AOSP MotionEventInjector assigns TOOL_TYPE_UNKNOWN to injected touches;
DocumentsUI configures its gesture listeners for FINGER/STYLUS and provides a
separate accessibility click handler to open/select items. This explains a
plausible failure path; the exact Samsung implementation is not available here.
Sources inspected:
- https://android.googlesource.com/platform/frameworks/base/+/refs/heads/main/services/accessibility/java/com/android/server/accessibility/MotionEventInjector.java
- https://android.googlesource.com/platform/packages/apps/DocumentsUI/+/master/src/com/android/documentsui/dirlist/DirectoryFragment.java

Ordinary stationary clicks now stay uncommitted until mouse-up and activate
one accessible element. Node rejection falls back to one completed tap. Movement
past touch slop begins a continued drag immediately. Stationary hold begins at
the system long-press timeout; Android then needs its own recognition interval,
so selection can take about two timeouts. Early physical release of a classified
hold does not turn it into a tap. Deferred intent work is removed on reset/end;
timer callbacks recheck lock/readiness. No package-specific workaround is used.

Windows InputCapture, Core EdgeReturn and Android free pointer motion/rendering
retain the 0.7.8 pipeline. No permissions, protocol, Bluetooth or signing change.
APK activity titles now show 0.7.12; they previously still displayed 0.7.8 even
when the build version was newer. Full packaging remains in the manual workflow;
actual S25 system-picker selection has not been tested here.

- 726 portable JVM assertions passed, including 62 pointer-stroke checks for
  20/50/100/200/499ms clicks, click fallback, drag, jitter, bounded backpressure,
  rapid next clicks, timeout boundaries and early release of a classified hold.
- 2249 Core assertions passed, including actual TLS motion/wheel/clipboard tests.
- All Windows/Core sources compiled with warnings as errors. All Android LAN
  sources compiled for Java 8 against API 35/ZXing with existing warnings only.
- 47,954 structural source-package checks passed. Workflow remains manual-only.
- Exact comparison confirmed Windows InputCapture/Core EdgeReturn and Android
  readiness, dimensions, showPointer and poll against the 0.7.8 baseline.

The tests verify code paths, not physical S25 picker behavior. See TESTING.md
for device checks and the long-press timing tradeoff.

## Historical: restore 0.7.8 movement (0.7.11)

The user requested the mouse movement behavior from Windows 0.7.8 and its
Android 0.7.8 companion. Baseline commit: 22177123178b152ef0ed8ece7a392c8f627a6f54.
InputCapture.cs and the edge policy match the baseline exactly. Android readiness,
dimension lookup, movement/clamping, showPointer, poll and reset return to their
original implementation; differences in ControlService concern wheel gestures only.
The 8ms hook path, Choreographer and per-motion input acknowledgement metadata
are removed. Ordinary TLS mouse packets have precisely the baseline's five fields.

Wheel zoom remains optional on wheel events and has a separate wheelZoom
capability. The host also accepts touchInput from 0.7.9/0.7.10 APKs as evidence of
wheel support while sending baseline motion packets. Old APKs retain ordinary
scrolling. Touch edge feedback still rejects stale captures/sequences and feedback
after release without adding locks to the mouse hot path. Native Bluetooth,
clipboard, files, pairing and the persistent APK signing identity remain unchanged.

- 2249 Core assertions passed, including real TLS delivery of 64 alternating
  baseline-format movement packets, Ctrl-wheel/setting behavior, old-APK wheel
  compatibility, and capture/sequence rejection without input metadata.
- All Core/Windows sources compiled with warnings as errors; all Android LAN
  sources compiled against API 35/ZXing for Java 8 with existing warnings only.
- 694 portable JVM assertions and 46,919 structural source-package checks passed.
- Exact source comparison verified Windows InputCapture/edge policy and Android
  readiness, dimensions, mouse movement, rendering, poll, reset and continued drag
  methods against the 0.7.8 baseline (wheel-specific changes excluded).

The restored behavior is source-verified, not hardware-measured. Full MSBuild/APK
packaging remains in the manual workflow. Release numbers increase to 0.7.11 so
the APK can be upgraded in place; there is no downgrade of Android VersionCode.

## Historical validation (superseded by 0.7.11)

## Pointer responsiveness (0.7.10)

The supplied 7.88-second screen recording shows the Wi-Fi arrow over ChatGPT.
It demonstrates the reported uneven movement; physical mouse input and packet
latency are not recorded, so it does not isolate a single measured root cause.

Windows now batches high-rate hook events at 8ms intervals without relying on
the UI timer for every movement. The timer still flushes the final tail. Forced
flushes drain all pending bounded packets before button/modifier transitions;
fractional sensitivity and capture resets retain their previous semantics.

Android uses Choreographer to apply the newest pointer coordinates once per
frame. Pure arrow motion no longer queries lock/display state twice per packet
or replies to every packet. The 100ms poll validates readiness and dimensions;
actions still check readiness immediately, and presses/gestures refresh dimensions.
Button and exit-intent transitions send feedback immediately. Reset removes the
pending frame callback. No predictive motion or interpolation changes click targets.

- 2410 Core assertions passed, including 139 motion pacing/ordering checks and
  the existing actual TLS tests for zoom, edge return, clipboard and compatibility.
- All Core/Windows C# sources compiled with warnings as errors.
- All Android LAN sources compiled against API 35 / ZXing for Java 8. Only the
  existing deprecation/unused warnings remain. Portable Java helpers are unchanged;
  their preceding 725 passing assertions are listed under 0.7.9 below.
- 49,225 structural source-package checks passed; workflow remains manual-only.

Physical frame timing and responsiveness still require the user's Windows/phone
session; no hardware improvement is claimed as measured here. Full MSBuild/APK
packaging remains in the manual workflow. No new permissions or debugging added.

## Wi-Fi edge return and PDF zoom (0.7.9)

Touch input capability negotiates sequenced mouse packets, zoom intent and phone
acknowledgements. Phone-confirmed outward movement survives a short stop at the
boundary or delayed Wi-Fi feedback. Reversals, wheel input and button presses
invalidate old feedback before the phone receives them. Captures and feedback
are monotonic; modern touch feedback without outward intent cannot fall back to
an old host direction. Native Bluetooth hover continues using its existing policy.

Ctrl+wheel sends a two-finger zoom gesture. The persisted wheel zoom setting
makes plain wheel zoom; its default remains scrolling. Pending identical wheel
modes coalesce and opposite pending ticks cancel. Scroll geometry preserves a
nonzero swipe near the top/bottom boundaries. Viewer pinch support is required.

- 725 portable JVM assertions passed, including 31 outward-intent checks and
  437 zoom/scroll geometry and accumulation checks.
- All Android LAN sources compiled against API 35 / ZXing using ECJ Java 8;
  only existing deprecation/unused warnings remain.
- 2271 Core assertions passed, including actual TLS delivery of left/right Ctrl
  zoom, plain-wheel mode, old-APK compatibility, capture/input ordering, immediate
  reversal/scroll/press invalidation and late-feedback rejection.
- All Core and Windows C# sources compiled with warnings as errors.
- 48,336 structural source-package checks passed; workflow remains manual-only.
- Full Windows/Android packaging and physical PDF/edge interaction remain for
  the manual Windows workflow and device checklist in TESTING.md.

## Earlier revisions

The previous revision added Bluetooth system-mouse edge return using actual Android hover,
a one-physical-pixel overlay and the paired LAN TLS connection. It preserves
native HID input and automatic Wi-Fi touch fallback. Native edge capability is
negotiated; old APKs retain HID with hotkey return. No debugging edition is added.

## Windows 0.7.8 diagnostic correction

The BLE Peripheral failure message now describes the limitation of our current
BLE mode instead of declaring the adapter generally unsuitable for system input.
The driver package in supplied Windows GlideX was unpacked and statically examined;
vendor executables and drivers were not run. No Classic HID backend is added.
Android and transport logic are unchanged.

- Current structural validation passed: **45,152 source-package checks**.
- All Core and Windows C# sources, including the changed diagnostics, compiled
  with the existing .NET 8 / Windows SDK reference set and warnings as errors.
  The Core test executable also compiled. Transport tests were not rerun for
  this text-only change; their previous results are listed below.

## Previous transport verification

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
support is still required for the current BLE backend. Static Windows GlideX
analysis identifies a separate Classic Bluetooth HID driver; it does not
implement or test that transport in Galaxy Bridge. See GLIDEX_WINDOWS.ru.md.
No EXE/APK is delivered by a source commit: use Build Windows portable manually.
