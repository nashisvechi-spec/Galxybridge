$ErrorActionPreference = 'Stop'
$javaRoot = $env:JAVA_HOME_17_X64
if (-not $javaRoot) { $javaRoot = $env:JAVA_HOME }
if (-not $javaRoot) { throw 'JDK 17 is required for LAN parser tests.' }
$java = Join-Path $javaRoot 'bin\java.exe'
$javac = Join-Path $javaRoot 'bin\javac.exe'
$work = Join-Path ([IO.Path]::GetTempPath()) ('GalaxyBridgeLanTests-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $json = Join-Path $work 'json.jar'
    Invoke-WebRequest -Uri 'https://repo.maven.apache.org/maven2/org/json/json/20240303/json-20240303.jar' -OutFile $json
    if ((Get-FileHash -LiteralPath $json -Algorithm SHA256).Hash.ToLowerInvariant() -ne '3cf6cd6892e32e2b4c1c39e0f52f5248a2f5b37646fdfbb79a66b46b618414ed') { throw 'Test JSON checksum mismatch.' }
    $source = Join-Path $PSScriptRoot '..\android\lan\src\com\galaxybridge\lan'
    & $javac --release 8 -encoding UTF-8 -classpath $json -d $work (Join-Path $source 'Wire.java') (Join-Path $source 'Pairing.java') (Join-Path $source 'Upload.java') (Join-Path $source 'PointerPress.java') (Join-Path $source 'ClickTarget.java') (Join-Path $source 'ClipText.java') (Join-Path $PSScriptRoot '..\tests\android-protocol\WireTest.java') (Join-Path $PSScriptRoot '..\tests\android-protocol\UploadTest.java') (Join-Path $PSScriptRoot '..\tests\android-protocol\PointerPressTest.java') (Join-Path $PSScriptRoot '..\tests\android-protocol\ClickTargetTest.java') (Join-Path $PSScriptRoot '..\tests\android-protocol\ClipTextTest.java')
    if ($LASTEXITCODE -ne 0) { throw 'LAN parser test compilation failed.' }
    & $java -classpath "$work;$json" com.galaxybridge.lan.WireTest
    if ($LASTEXITCODE -ne 0) { throw 'LAN parser tests failed.' }
    & $java -classpath "$work;$json" com.galaxybridge.lan.UploadTest
    if ($LASTEXITCODE -ne 0) { throw 'LAN upload tests failed.' }
    & $java -classpath "$work;$json" com.galaxybridge.lan.PointerPressTest
    if ($LASTEXITCODE -ne 0) { throw 'Mouse press tests failed.' }
    & $java -classpath "$work;$json" com.galaxybridge.lan.ClickTargetTest
    if ($LASTEXITCODE -ne 0) { throw 'Click target tests failed.' }
    & $java -classpath "$work;$json" com.galaxybridge.lan.ClipTextTest
    if ($LASTEXITCODE -ne 0) { throw 'Clipboard text tests failed.' }
} finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
