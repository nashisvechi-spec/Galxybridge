# Third-party components

The source package contains Galaxy Bridge source only. Original Galaxy Bridge
code is MIT licensed. LAN communication and BLE HID require no external
connection backend or patched vendor software.

## .NET

Galaxy Bridge targets .NET 8 and Windows Forms. A self-contained publish also
includes Microsoft .NET runtime components, which keep their upstream
licenses (principally MIT) and third-party notices.

Copies from the upstream `release/8.0` branches at source-package preparation
are included under `licenses/dotnet-*` and copied to the portable artifact.
When changing the SDK/runtime line, refresh these notices for that line.

- https://github.com/dotnet/runtime/blob/release/8.0/LICENSE.TXT
- https://github.com/dotnet/runtime/blob/release/8.0/THIRD-PARTY-NOTICES.TXT
- https://github.com/dotnet/winforms/blob/release/8.0/LICENSE.TXT

## QRCoder 1.8.0

The Windows project restores the pinned QRCoder NuGet package to encode the
LAN pairing payload. The QR module matrix is drawn
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
use org.json 20240303, not bundled in production applications.

JVM test dependency: https://github.com/stleary/JSON-java/tree/20240303 and
https://repo.maven.apache.org/maven2/org/json/json/20240303/json-20240303.jar.
MIT license (https://github.com/stleary/JSON-java/blob/20240303/LICENSE).
SHA-256 3cf6cd6892e32e2b4c1c39e0f52f5248a2f5b37646fdfbb79a66b46b618414ed.
