param([string]$Destination = (Join-Path $PSScriptRoot '..\artifacts\GalaxyBridge'))
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
$keytool = Join-Path $javaRoot 'bin\keytool.exe'
$env:JAVA_HOME = $javaRoot
$work = Join-Path ([IO.Path]::GetTempPath()) ('GalaxyBridgeEdge-' + [Guid]::NewGuid().ToString('N'))
try {
    $classes = Join-Path $work 'classes'
    $dex = Join-Path $work 'dex'
    New-Item -ItemType Directory -Path $classes, $dex -Force | Out-Null
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
    & (Join-Path $buildTools 'aapt2.exe') link -o $unsigned --manifest $manifest -I $androidJar --min-sdk-version 30 --target-sdk-version 35 --version-code 301 --version-name 0.3.1 $resources
    if ($LASTEXITCODE -ne 0) { throw 'Edge companion manifest packaging failed.' }
    & $jarTool --update --file $unsigned -C $dex classes.dex
    if ($LASTEXITCODE -ne 0) { throw 'Edge companion dex insertion failed.' }
    $aligned = Join-Path $work 'aligned.apk'
    & (Join-Path $buildTools 'zipalign.exe') -f 4 $unsigned $aligned
    if ($LASTEXITCODE -ne 0) { throw 'Edge companion zipalign failed.' }

    # Development APK only. Supply a persistent private signing key for production upgrades.
    $keystore = Join-Path $work 'debug.keystore'
    & $keytool -genkeypair -noprompt -keystore $keystore -storepass android -keypass android -alias androiddebugkey -keyalg RSA -keysize 2048 -validity 10000 -dname 'CN=Android Debug,O=Android,C=US'
    if ($LASTEXITCODE -ne 0) { throw 'Development signing key generation failed.' }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $apk = Join-Path $Destination 'GalaxyBridgeEdge.apk'
    & (Join-Path $buildTools 'apksigner.bat') sign --ks $keystore --ks-pass pass:android --key-pass pass:android --out $apk $aligned
    if ($LASTEXITCODE -ne 0) { throw 'Edge companion signing failed.' }
    & (Join-Path $buildTools 'apksigner.bat') verify --verbose $apk
    if ($LASTEXITCODE -ne 0) { throw 'Edge companion signature verification failed.' }
    Write-Host 'GalaxyBridgeEdge.apk created. Install it manually on the phone and grant overlay permission.'
} finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
