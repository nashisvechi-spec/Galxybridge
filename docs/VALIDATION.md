# Validation — Windows 0.7.9 / Android 0.7.9

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
