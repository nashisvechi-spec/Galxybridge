$ErrorActionPreference = 'Stop'
$originalBundle = $env:GALAXYBRIDGE_ANDROID_SIGNING_BUNDLE
$resolver = Join-Path $PSScriptRoot 'Resolve-AndroidSigning.ps1'
$work = Join-Path ([IO.Path]::GetTempPath()) ('GalaxyBridgeSigningTests-' + [Guid]::NewGuid().ToString('N'))
$checks = 0
function Check([bool]$condition,[string]$reason) {
    if (-not $condition) { throw $reason }
    $script:checks++
}
function Reject([string]$value,[string]$scriptPath = $resolver) {
    $env:GALAXYBRIDGE_ANDROID_SIGNING_BUNDLE = $value
    $rejected = $false
    try { & $scriptPath -WorkDirectory (Join-Path $work 'invalid') | Out-Null }
    catch { $rejected = $true }
    Check $rejected 'Invalid signing configuration was accepted.'
}
try {
    foreach ($name in 'first','second','invalid','fixture/scripts','fixture/android') {
        New-Item -ItemType Directory -Path (Join-Path $work $name) -Force | Out-Null
    }
    # Two independent build directories must recover exactly the same update key.
    $first = & $resolver -WorkDirectory (Join-Path $work 'first')
    $second = & $resolver -WorkDirectory (Join-Path $work 'second')
    Check ($first.CertificateSha256 -eq $second.CertificateSha256) 'Update certificates changed between build directories.'
    Check ($first.Keystore -ne $second.Keystore) 'Signing test directories must be independent.'
    Check ((Get-FileHash $first.Keystore).Hash -eq (Get-FileHash $second.Keystore).Hash) 'Restored private keys differ.'
    Check (-not ($first.PSObject.Properties.Name -contains 'Password')) 'Resolver must not return passwords.'
    Reject ''
    Reject '{broken json'
    Reject '{"version":2,"alias":"galaxybridge","password":"invalid configuration","keystore":"AA=="}'
    $wrongPassword = $originalBundle | ConvertFrom-Json
    $wrongPassword.password = 'deliberately-incorrect-test-password'
    Reject ($wrongPassword | ConvertTo-Json -Compress)
    Check ([string]::IsNullOrEmpty($env:GALAXYBRIDGE_ANDROID_SIGNING_PASSWORD)) 'Failed key validation leaked the password environment.'
    Check (-not (Test-Path (Join-Path $work 'invalid/android-signing.p12'))) 'Failed validation retained private key bytes.'
    # A valid key with a mismatched pinned certificate must also stop the build.
    Copy-Item -LiteralPath $resolver -Destination (Join-Path $work 'fixture/scripts/Resolve-AndroidSigning.ps1')
    Set-Content -LiteralPath (Join-Path $work 'fixture/android/signing-cert.sha256') -Value ('0' * 64)
    Reject $originalBundle (Join-Path $work 'fixture/scripts/Resolve-AndroidSigning.ps1')
    Write-Host "PASS: $checks Android signing checks; no APK/EXE built."
} finally {
    $env:GALAXYBRIDGE_ANDROID_SIGNING_BUNDLE = $originalBundle
    [Environment]::SetEnvironmentVariable('GALAXYBRIDGE_ANDROID_SIGNING_PASSWORD', $null)
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
