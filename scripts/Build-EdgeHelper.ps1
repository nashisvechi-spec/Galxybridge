param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\artifacts\GalaxyBridge'),
    [ValidateRange(1,2100000000)][int]$VersionCode = 302
)
$ErrorActionPreference = 'Stop'
$sdkRoot = $env:ANDROID_HOME
if (-not $sdkRoot) { $sdkRoot = $env:ANDROID_SDK_ROOT }
if (-not $sdkRoot) { throw 'Android SDK is required. Set ANDROID_HOME.' }
$androidJar = Join-Path $sdkRoot 'platforms\android-35\android.jar'
$buildTools = Join-Path $sdkRoot 'build-tools\35.0.0'
foreach ($tool in 'd8.bat', 'aapt2.exe', 'zipalign.exe', 'apksigner.bat') {
    if (-not (Test-Path -LiteralPath (Join-Path $buildTools $tool))) { throw "Missing Android tool: $tool" }
}
if (-not (Test-Path -LiteralPath $androidJar)) { throw 'Install platforms;android-35 with sdkmanager.' }
$javaRoot = $env:JAVA_HOME_17_X64
if (-not $javaRoot) { $javaRoot = $env:JAVA_HOME }
if (-not $javaRoot) { throw 'JDK 17 is required. Set JAVA_HOME.' }
$javac = Join-Path $javaRoot 'bin\javac.exe'
$jarTool = Join-Path $javaRoot 'bin\jar.exe'
$env:JAVA_HOME = $javaRoot
$work = Join-Path ([IO.Path]::GetTempPath()) ('GalaxyBridgeEdge-' + [Guid]::NewGuid().ToString('N'))
try {
    $classes = Join-Path $work 'classes'
    $dex = Join-Path $work 'dex'
    New-Item -ItemType Directory -Path $classes, $dex -Force | Out-Null
    $signing = & (Join-Path $PSScriptRoot 'Resolve-AndroidSigning.ps1') -WorkDirectory $work
    $sources = @(Get-ChildItem (Join-Path $PSScriptRoot '..\android\edge-return\src') -Recurse -Filter '*.java' | ForEach-Object { $_.FullName })
    & $javac --release 8 -encoding UTF-8 -classpath $androidJar -d $classes @sources
    if ($LASTEXITCODE -ne 0) { throw 'Edge companion Java compilation failed.' }
    $bytecode = Join-Path $work 'classes.jar'
    & $jarTool --create --file $bytecode -C $classes .
    if ($LASTEXITCODE -ne 0) { throw 'Edge companion class packaging failed.' }
    & (Join-Path $buildTools 'd8.bat') --min-api 30 --lib $androidJar --output $dex $bytecode
    if ($LASTEXITCODE -ne 0) { throw 'Edge companion dex compilation failed.' }
    $unsigned = Join-Path $work 'unsigned.apk'
    $sourceRoot = Join-Path $PSScriptRoot '..\android\edge-return'
    $manifest = Join-Path $sourceRoot 'AndroidManifest.xml'
    $resources = Join-Path $work 'resources.zip'
    & (Join-Path $buildTools 'aapt2.exe') compile --dir (Join-Path $sourceRoot 'res') -o $resources
    if ($LASTEXITCODE -ne 0) { throw 'Edge companion resource compilation failed.' }
    & (Join-Path $buildTools 'aapt2.exe') link -o $unsigned --manifest $manifest -I $androidJar --min-sdk-version 30 --target-sdk-version 35 --version-code $VersionCode --version-name 0.3.2 $resources
    if ($LASTEXITCODE -ne 0) { throw 'Edge companion manifest packaging failed.' }
    & $jarTool --update --file $unsigned -C $dex classes.dex
    if ($LASTEXITCODE -ne 0) { throw 'Edge companion dex insertion failed.' }
    $aligned = Join-Path $work 'aligned.apk'
    & (Join-Path $buildTools 'zipalign.exe') -f 4 $unsigned $aligned
    if ($LASTEXITCODE -ne 0) { throw 'Edge companion zipalign failed.' }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $apk = Join-Path $Destination 'GalaxyBridgeEdge.apk'
    & (Join-Path $buildTools 'apksigner.bat') sign --ks $signing.Keystore --ks-key-alias $signing.Alias --ks-pass env:GALAXYBRIDGE_ANDROID_SIGNING_PASSWORD --key-pass env:GALAXYBRIDGE_ANDROID_SIGNING_PASSWORD --out $apk $aligned
    if ($LASTEXITCODE -ne 0) { throw 'Edge companion signing failed.' }
    & (Join-Path $buildTools 'apksigner.bat') verify --verbose $apk
    if ($LASTEXITCODE -ne 0) { throw 'Edge companion signature verification failed.' }
    Write-Host 'GalaxyBridgeEdge.apk created. Install it manually on the phone and grant overlay permission.'
} finally {
    [Environment]::SetEnvironmentVariable('GALAXYBRIDGE_ANDROID_SIGNING_PASSWORD', $null)
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
