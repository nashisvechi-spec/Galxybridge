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

Samsung, Galaxy and HP names describe intended devices. This project is not
affiliated with Samsung or HP.
