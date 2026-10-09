This directory is populated by scripts/Fetch-Backend.ps1 during the manual GitHub build.

Required runtime files: adb.exe, AdbWinApi.dll, AdbWinUsbApi.dll, scrcpy-server.
The server must be version **5.0.1**. The script verifies release ZIP and server SHA-256 hashes.
No runtime binaries are included in the source repository.

Automatic phone-edge return uses GalaxyBridgeEdge.apk, built from this repository
by scripts/Build-EdgeHelper.ps1. The APK is placed beside GalaxyBridge.exe and must
be installed manually on the phone, opened, and granted overlay permission.
Basic control still connects without it. Legacy edge-return.jar files are no
longer used and are not shipped by the workflow.
