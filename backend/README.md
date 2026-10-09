This directory is populated by `scripts/Fetch-Backend.ps1` during the manual GitHub build.

Required runtime files: `adb.exe`, `AdbWinApi.dll`, `AdbWinUsbApi.dll`, `scrcpy-server`.
The server must be version **5.0.1**. The script verifies the release ZIP and server SHA-256 hashes.
No runtime binaries are included in the source archive.
