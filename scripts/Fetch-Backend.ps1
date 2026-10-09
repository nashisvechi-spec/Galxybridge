param([string]$Destination = (Join-Path $PSScriptRoot '..\backend'))
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$manifest = Get-Content (Join-Path $PSScriptRoot 'backend.lock.json') -Raw | ConvertFrom-Json
$tempDirectory = Join-Path ([IO.Path]::GetTempPath()) ('GalaxyBridge-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempDirectory | Out-Null
try {
    $archive = Join-Path $tempDirectory 'scrcpy.zip'
    Invoke-WebRequest -Uri $manifest.zip_url -OutFile $archive -UseBasicParsing
    if ((Get-FileHash -Path $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.zip_sha256) {
        throw 'scrcpy ZIP checksum mismatch. Nothing was installed.'
    }
    $extracted = Join-Path $tempDirectory 'unpacked'
    Expand-Archive -LiteralPath $archive -DestinationPath $extracted
    $runtime = Join-Path $extracted ('scrcpy-win64-v' + $manifest.version)
    $required = @('adb.exe', 'AdbWinApi.dll', 'AdbWinUsbApi.dll', 'scrcpy-server')
    foreach ($file in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $runtime $file) -PathType Leaf)) { throw "Missing backend file: $file" }
    }
    if ((Get-FileHash -LiteralPath (Join-Path $runtime 'scrcpy-server') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.server_sha256) {
        throw 'scrcpy server checksum mismatch. Nothing was installed.'
    }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    foreach ($file in $required) { Copy-Item -LiteralPath (Join-Path $runtime $file) -Destination (Join-Path $Destination $file) -Force }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\THIRD_PARTY_NOTICES.md') -Destination $Destination -Force
    $licenseDirectory = Join-Path $Destination 'licenses'
    New-Item -ItemType Directory -Force -Path $licenseDirectory | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\licenses\scrcpy-APACHE-2.0.txt') -Destination $licenseDirectory -Force
    # Keep every license/notice supplied with the pinned upstream distribution.
    $upstreamLicenses = Get-ChildItem -LiteralPath $runtime -Recurse -File | Where-Object { $_.Name -match '^(LICENSE|NOTICE|COPYING)(\..*)?$' }
    foreach ($notice in $upstreamLicenses) {
        $relative = $notice.FullName.Substring($runtime.Length).TrimStart([char[]]'\/')
        $target = Join-Path (Join-Path $licenseDirectory 'upstream') $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        Copy-Item -LiteralPath $notice.FullName -Destination $target -Force
    }
    Write-Host ('Backend ' + $manifest.version + ' downloaded and verified.')
} finally { Remove-Item -LiteralPath $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue }
