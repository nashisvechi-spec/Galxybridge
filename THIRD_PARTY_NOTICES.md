# Third-party components

The source package contains Galaxy Bridge source only. Original Galaxy Bridge
code is MIT licensed. It implements a compatible control-channel client;
it does not contain patched Samsung software or Samsung binaries.

## scrcpy 5.0.1

- Project: https://github.com/Genymobile/scrcpy
- Pinned tag and protocol: https://github.com/Genymobile/scrcpy/tree/v5.0.1
- Release: https://github.com/Genymobile/scrcpy/releases/tag/v5.0.1
- License: Apache License 2.0; a full upstream copy with copyright notices is
  included in `licenses/scrcpy-APACHE-2.0.txt`.
- Copyright (C) 2018 Genymobile; Copyright (C) 2018-2026 Romain Vimont.

The fetch script downloads the official Windows release when run, verifies
the ZIP and server SHA-256, and extracts the unmodified `scrcpy-server` and
ADB connection files. Version and digests are in `scripts/backend.lock.json`.
The server and this client's protocol must be updated together.

## Android Debug Bridge (ADB)

The runtime backend includes `adb.exe`, `AdbWinApi.dll`, and `AdbWinUsbApi.dll`
from the pinned official scrcpy distribution. ADB is an Android Open Source
Project component, primarily Apache 2.0; bundled dependencies retain their
own notices and licenses.

- Upstream: https://android.googlesource.com/platform/packages/modules/adb/
- Full Apache 2.0 terms: `licenses/scrcpy-APACHE-2.0.txt`.

The fetch script also retains any LICENSE, NOTICE and COPYING files supplied
by the upstream release, under `backend/licenses/upstream`. If publishing a
binary distribution, keep all supplied notices with it and inspect the first
generated artifact for additional ADB dependency notices.

## .NET

Galaxy Bridge targets .NET 8 and Windows Forms. A self-contained publish also
includes Microsoft .NET runtime components, which keep their upstream
licenses (principally MIT) and third-party notices. The fetch script does not
modify these components.

Copies from the upstream `release/8.0` branches at source-package preparation
are included under `licenses/dotnet-*` and copied to the portable artifact.
When changing the SDK/runtime line, refresh these notices for that line.

- https://github.com/dotnet/runtime/blob/release/8.0/LICENSE.TXT
- https://github.com/dotnet/runtime/blob/release/8.0/THIRD-PARTY-NOTICES.TXT
- https://github.com/dotnet/winforms/blob/release/8.0/LICENSE.TXT

## QRCoder 1.8.0

The Windows project restores the pinned QRCoder NuGet package to encode the
Android wireless-debugging pairing payload. The QR module matrix is drawn
locally in a Windows control; no online QR generator is used.

- Project/tag: https://github.com/Shane32/QRCoder/tree/v1.8.0
- Package: https://www.nuget.org/packages/QRCoder/1.8.0
- License: MIT; full upstream text in licenses/QRCoder-LICENSE.txt.
- Copyright (c) 2013-2025 Raffael Herrmann; (c) 2024-2025 Shane Krueger.

System.Drawing.Common is explicitly pinned to 8.0.30 instead of QRCoder's
older minimum dependency. It and Microsoft.Win32.SystemEvents are Microsoft
.NET components covered by the .NET license and notices above.

Samsung, Galaxy and HP names describe intended devices. This project is not
affiliated with Samsung or HP.

## ZXing core 3.5.3 (independent Wi-Fi companion)

- Source/tag: https://github.com/zxing/zxing/tree/zxing-3.5.3
- JAR: https://repo.maven.apache.org/maven2/com/google/zxing/core/3.5.3/core-3.5.3.jar
- SHA-256: 8d8064c1636fdaef7189dd9055c7d59950a8940a12f2293956446ec3c109fd82
- Apache 2.0; upstream license and notices in licenses/ZXing-APACHE-2.0.txt.
- Copyright ZXing authors; downloaded only during manual APK build.

QRCoder also generates the independent LAN pairing QR. System.Security.Cryptography.
ProtectedData 8.0.0 is explicitly pinned for Windows DPAPI; it is covered by the
.NET runtime license and notices already included above. JVM-only parser tests
use org.json 20240303, not bundled in either production application.

JVM test dependency: https://github.com/stleary/JSON-java/tree/20240303 and
https://repo.maven.apache.org/maven2/org/json/json/20240303/json-20240303.jar.
MIT license (https://github.com/stleary/JSON-java/blob/20240303/LICENSE).
SHA-256 3cf6cd6892e32e2b4c1c39e0f52f5248a2f5b37646fdfbb79a66b46b618414ed.
