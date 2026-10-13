param(
    [string]$SourceDirectory = (Join-Path $PSScriptRoot '../artifacts/GalaxyBridge'),
    [string]$Destination = (Join-Path $PSScriptRoot '../artifacts/installer'),
    [string]$Compiler
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $source 'GalaxyBridge.exe') -PathType Leaf)) {
    throw 'Publish GalaxyBridge.exe before building the installer.'
}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$output = (Resolve-Path -LiteralPath $Destination).Path
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'src/GalaxyBridge.Windows/GalaxyBridge.Windows.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid Windows application version.' }
if (-not $Compiler) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $Compiler = $command.Source }
    else { $Compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
}
if (-not (Test-Path -LiteralPath $Compiler -PathType Leaf)) {
    throw 'Inno Setup 6 compiler ISCC.exe was not found. Install Inno Setup or pass -Compiler.'
}
$setup = Join-Path $output "GalaxyBridge-Setup-$version-x64.exe"
# A failed compiler must never be mistaken for a previous successful build.
if (Test-Path -LiteralPath $setup) { Remove-Item -LiteralPath $setup -Force }
& $Compiler "/DAppVersion=$version" "/DAppSource=$source" "/DSetupOutput=$output" (Join-Path $root 'installer/GalaxyBridge.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath $setup -PathType Leaf) -or (Get-Item -LiteralPath $setup).Length -eq 0) {
    throw 'Inno Setup did not produce the expected installer.'
}
Write-Output $setup
