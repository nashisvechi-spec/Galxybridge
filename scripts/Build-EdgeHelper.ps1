param([string]$Destination = (Join-Path $PSScriptRoot '..\backend'))
$ErrorActionPreference = 'Stop'
$sdkRoot = $env:ANDROID_HOME
if (-not $sdkRoot) { $sdkRoot = $env:ANDROID_SDK_ROOT }
if (-not $sdkRoot) { throw 'Android SDK is required. Set ANDROID_HOME.' }
$androidJar = Join-Path $sdkRoot 'platforms\android-35\android.jar'
$d8 = Join-Path $sdkRoot 'build-tools\35.0.0\d8.bat'
if (-not (Test-Path -LiteralPath $androidJar) -or -not (Test-Path -LiteralPath $d8)) {
    throw 'Install platforms;android-35 and build-tools;35.0.0 with sdkmanager.'
}
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
    $sources = @(Get-ChildItem (Join-Path $PSScriptRoot '..\android\edge-return\src') -Recurse -Filter '*.java' | ForEach-Object { $_.FullName })
    & $javac --release 8 -encoding UTF-8 -classpath $androidJar -d $classes @sources
    if ($LASTEXITCODE -ne 0) { throw 'Edge helper Java compilation failed.' }
    $bytecode = Join-Path $work 'classes.jar'
    & $jarTool --create --file $bytecode -C $classes .
    if ($LASTEXITCODE -ne 0) { throw 'Edge helper class packaging failed.' }
    & $d8 --min-api 30 --lib $androidJar --output $dex $bytecode
    if ($LASTEXITCODE -ne 0) { throw 'Edge helper dex compilation failed.' }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $output = Join-Path $Destination 'edge-return.jar'
    & $jarTool --create --file $output -C $dex classes.dex
    if ($LASTEXITCODE -ne 0) { throw 'Edge helper dex packaging failed.' }
    (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath (Join-Path $Destination 'edge-return.sha256') -Encoding ascii
    Write-Host 'Edge-return helper built and SHA-256 recorded.'
} finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
