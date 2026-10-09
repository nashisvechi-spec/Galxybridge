param([string]$Destination = (Join-Path $PSScriptRoot '..\artifacts\GalaxyBridge'))
$ErrorActionPreference = 'Stop'
$sdkRoot = $env:ANDROID_HOME
if (-not $sdkRoot) { $sdkRoot = $env:ANDROID_SDK_ROOT }
if (-not $sdkRoot) { throw 'Android SDK is required.' }
$androidJar = Join-Path $sdkRoot 'platforms\android-35\android.jar'
$buildTools = Join-Path $sdkRoot 'build-tools\35.0.0'
$javaRoot = $env:JAVA_HOME_17_X64
if (-not $javaRoot) { $javaRoot = $env:JAVA_HOME }
if (-not $javaRoot) { throw 'JDK 17 is required.' }
$env:JAVA_HOME = $javaRoot
$javac = Join-Path $javaRoot 'bin\javac.exe'
$jarTool = Join-Path $javaRoot 'bin\jar.exe'
$keytool = Join-Path $javaRoot 'bin\keytool.exe'
foreach ($tool in 'd8.bat', 'aapt2.exe', 'zipalign.exe', 'apksigner.bat') {
    if (-not (Test-Path -LiteralPath (Join-Path $buildTools $tool))) { throw "Missing Android tool: $tool" }
}
if (-not (Test-Path -LiteralPath $androidJar)) { throw 'Install platforms;android-35.' }
$sourceRoot = Join-Path $PSScriptRoot '..\android\lan'
$work = Join-Path ([IO.Path]::GetTempPath()) ('GalaxyBridgeLan-' + [Guid]::NewGuid().ToString('N'))
try {
    $classes = Join-Path $work 'classes'
    $dex = Join-Path $work 'dex'
    $generated = Join-Path $work 'generated'
    New-Item -ItemType Directory -Path $classes, $dex, $generated -Force | Out-Null
    $zxing = Join-Path $work 'zxing-core-3.5.3.jar'
    Invoke-WebRequest -Uri 'https://repo.maven.apache.org/maven2/com/google/zxing/core/3.5.3/core-3.5.3.jar' -OutFile $zxing
    if ((Get-FileHash -LiteralPath $zxing -Algorithm SHA256).Hash.ToLowerInvariant() -ne '8d8064c1636fdaef7189dd9055c7d59950a8940a12f2293956446ec3c109fd82') { throw 'ZXing checksum mismatch.' }
    $resources = Join-Path $work 'resources.zip'
    & (Join-Path $buildTools 'aapt2.exe') compile --dir (Join-Path $sourceRoot 'res') -o $resources
    if ($LASTEXITCODE -ne 0) { throw 'LAN companion resource compilation failed.' }
    $unsigned = Join-Path $work 'unsigned.apk'
    & (Join-Path $buildTools 'aapt2.exe') link -o $unsigned --manifest (Join-Path $sourceRoot 'AndroidManifest.xml') -I $androidJar --min-sdk-version 33 --target-sdk-version 35 --version-code 700 --version-name 0.7.0 --java $generated $resources
    if ($LASTEXITCODE -ne 0) { throw 'LAN companion resource linking failed.' }
    $sources = @(Get-ChildItem (Join-Path $sourceRoot 'src') -Recurse -Filter '*.java' | ForEach-Object { $_.FullName })
    $sources += @(Get-ChildItem $generated -Recurse -Filter '*.java' | ForEach-Object { $_.FullName })
    & $javac --release 8 -encoding UTF-8 -classpath "$androidJar;$zxing" -d $classes @sources
    if ($LASTEXITCODE -ne 0) { throw 'LAN companion Java compilation failed.' }
    $bytecode = Join-Path $work 'classes.jar'
    & $jarTool --create --file $bytecode -C $classes .
    if ($LASTEXITCODE -ne 0) { throw 'LAN companion class packaging failed.' }
    & (Join-Path $buildTools 'd8.bat') --min-api 33 --lib $androidJar --output $dex $bytecode $zxing
    if ($LASTEXITCODE -ne 0) { throw 'LAN companion dex compilation failed.' }
    & $jarTool --update --file $unsigned -C $dex classes.dex
    if ($LASTEXITCODE -ne 0) { throw 'LAN companion dex insertion failed.' }
    $aligned = Join-Path $work 'aligned.apk'
    & (Join-Path $buildTools 'zipalign.exe') -f 4 $unsigned $aligned
    if ($LASTEXITCODE -ne 0) { throw 'LAN companion zipalign failed.' }
    # Development signing only. A new key each manual run requires uninstalling an older test APK.
    $keystore = Join-Path $work 'debug.keystore'
    & $keytool -genkeypair -noprompt -keystore $keystore -storepass android -keypass android -alias androiddebugkey -keyalg RSA -keysize 2048 -validity 10000 -dname 'CN=Android Debug,O=Android,C=US'
    if ($LASTEXITCODE -ne 0) { throw 'LAN companion signing key generation failed.' }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $apk = Join-Path $Destination 'GalaxyBridgeLan.apk'
    & (Join-Path $buildTools 'apksigner.bat') sign --ks $keystore --ks-pass pass:android --key-pass pass:android --out $apk $aligned
    if ($LASTEXITCODE -ne 0) { throw 'LAN companion signing failed.' }
    & (Join-Path $buildTools 'apksigner.bat') verify --verbose $apk
    if ($LASTEXITCODE -ne 0) { throw 'LAN companion signature verification failed.' }
} finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
