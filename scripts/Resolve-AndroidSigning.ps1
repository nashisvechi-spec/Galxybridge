param([Parameter(Mandatory)][string]$WorkDirectory)
$ErrorActionPreference = 'Stop'
# Only the public certificate fingerprint belongs in source control.
$expected = (Get-Content -LiteralPath (Join-Path $PSScriptRoot '../android/signing-cert.sha256') -Raw).Trim().ToLowerInvariant()
if ($expected -notmatch '^[0-9a-f]{64}$') { throw 'Invalid Android signing certificate pin.' }
if ([string]::IsNullOrWhiteSpace($env:GALAXYBRIDGE_ANDROID_SIGNING_BUNDLE)) {
    throw 'Add repository secret ANDROID_SIGNING_BUNDLE before building. See docs/ANDROID_UPDATES.ru.md. A replacement signing key will not be generated.'
}
try { $bundle = $env:GALAXYBRIDGE_ANDROID_SIGNING_BUNDLE | ConvertFrom-Json }
catch { throw 'Invalid ANDROID_SIGNING_BUNDLE JSON.' }
if ($bundle.version -ne 1 -or $bundle.alias -isnot [string] -or $bundle.alias -notmatch '^[a-zA-Z0-9_-]{1,80}$' -or
    $bundle.password -isnot [string] -or $bundle.password.Length -lt 12 -or $bundle.password.Length -gt 200 -or
    $bundle.keystore -isnot [string] -or $bundle.keystore.Length -gt 60000) {
    throw 'Invalid ANDROID_SIGNING_BUNDLE fields.'
}
try { $bytes = [Convert]::FromBase64String($bundle.keystore) }
catch { throw 'Invalid ANDROID_SIGNING_BUNDLE keystore encoding.' }
if ($bytes.Length -lt 512) { throw 'Invalid Android keystore size.' }
if (-not (Test-Path -LiteralPath $WorkDirectory -PathType Container)) { throw 'Signing work directory is missing.' }
$javaRoot = $env:JAVA_HOME_17_X64
if (-not $javaRoot) { $javaRoot = $env:JAVA_HOME }
if ($javaRoot) {
    $keytoolName = if ($IsWindows) { 'bin/keytool.exe' } else { 'bin/keytool' }
    $keytool = Join-Path $javaRoot $keytoolName
} else { $keytool = (Get-Command keytool -ErrorAction Stop).Source }
$keystore = Join-Path $WorkDirectory 'android-signing.p12'
$certificate = Join-Path $WorkDirectory 'android-signing.der'
try {
    [IO.File]::WriteAllBytes($keystore, $bytes)
    $env:GALAXYBRIDGE_ANDROID_SIGNING_PASSWORD = $bundle.password
    # Passwords are passed by environment name, never as command-line values.
    & $keytool -exportcert -storetype PKCS12 -keystore $keystore -alias $bundle.alias -storepass:env GALAXYBRIDGE_ANDROID_SIGNING_PASSWORD -file $certificate 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Android signing key could not be opened.' }
    $actual = (Get-FileHash -LiteralPath $certificate -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) { throw 'Android signing certificate differs from the pinned update key. Build stopped.' }
    [pscustomobject]@{ Keystore = $keystore; Alias = $bundle.alias; CertificateSha256 = $actual }
} catch {
    [Environment]::SetEnvironmentVariable('GALAXYBRIDGE_ANDROID_SIGNING_PASSWORD', $null)
    Remove-Item -LiteralPath $keystore -Force -ErrorAction SilentlyContinue
    # Never include native output or bundle values in an error.
    throw 'Android signing key validation failed. Check ANDROID_SIGNING_BUNDLE against the original saved key.'
} finally {
    Remove-Item -LiteralPath $certificate -Force -ErrorAction SilentlyContinue
}
