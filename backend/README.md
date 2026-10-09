This directory is populated by `scripts/Fetch-Backend.ps1` during the manual GitHub build.

Required runtime files: `adb.exe`, `AdbWinApi.dll`, `AdbWinUsbApi.dll`, `scrcpy-server`.
The server must be version **5.0.1**. The script verifies the release ZIP and server SHA-256 hashes.
No runtime binaries are included in the source archive.

Automatic phone-edge return additionally uses `edge-return.jar` and
`edge-return.sha256`, generated from this repository's Java sources by
`scripts/Build-EdgeHelper.ps1` during the same manual workflow. Keep the helper
and its matching digest together. If absent, basic control still connects,
but automatic return is unavailable and the journal explains why.
