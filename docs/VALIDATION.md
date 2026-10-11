# Validation — Windows 0.7.13 / Android 0.7.13

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
