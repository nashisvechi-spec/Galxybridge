$ErrorActionPreference = 'Stop'
$javaRoot = $env:JAVA_HOME_17_X64
if (-not $javaRoot) { $javaRoot = $env:JAVA_HOME }
if (-not $javaRoot) { throw 'JDK 17 is required.' }
$work = Join-Path ([IO.Path]::GetTempPath()) ('GalaxyFilesTests-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $source = Join-Path $PSScriptRoot '../android/files/src/com/galaxybridge/files'
    $tests = Join-Path $PSScriptRoot '../tests/android-files'
    & (Join-Path $javaRoot 'bin/javac.exe') --release 8 -encoding UTF-8 -d $work (Join-Path $source 'FileOps.java') (Join-Path $source 'FilePointer.java') (Join-Path $tests 'FileOpsTest.java') (Join-Path $tests 'FilePointerTest.java')
    if ($LASTEXITCODE -ne 0) { throw 'File manager test compilation failed.' }
    foreach ($test in 'FileOpsTest', 'FilePointerTest') {
        & (Join-Path $javaRoot 'bin/java.exe') -classpath $work "com.galaxybridge.files.$test"
        if ($LASTEXITCODE -ne 0) { throw "File manager tests failed: $test" }
    }
} finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
