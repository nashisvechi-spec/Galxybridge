param([Parameter(Mandatory)][string]$Installer)
$ErrorActionPreference = 'Stop'
$setup = (Resolve-Path -LiteralPath $Installer).Path
$root = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'src/GalaxyBridge.Windows/GalaxyBridge.Windows.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
$work = Join-Path ([IO.Path]::GetTempPath()) ('GalaxyBridgeInstaller-' + [Guid]::NewGuid().ToString('N'))
$install = Join-Path $work 'application'
$data = Join-Path $env:LOCALAPPDATA 'GalaxyBridge'
$startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{E7EA5A53-03BA-490F-ABBD-0F19AC1384DE}_is1'
# This smoke test is for an empty CI account, never a user's existing install.
if ((Test-Path -LiteralPath $data) -or (Test-Path -LiteralPath $key) -or ($null -ne (Get-ItemProperty -LiteralPath $startupKey -Name 'GalaxyBridge' -ErrorAction SilentlyContinue))) { throw 'Installer smoke test requires an empty CI account.' }
New-Item -ItemType Directory -Path $data -Force | Out-Null
$marker = Join-Path $data 'installer-test-sentinel.txt'
[IO.File]::WriteAllText($marker, 'Preserve application data')
New-Item -ItemType Directory -Path $work -Force | Out-Null
function Check([bool]$condition, [string]$reason) { if (-not $condition) { throw $reason } }
function Run-Setup([string]$path, [string[]]$arguments) {
    $process = Start-Process -FilePath $path -ArgumentList $arguments -Wait -PassThru
    Check ($process.ExitCode -eq 0) "Installer exited with code $($process.ExitCode). Logs: $work"
}
try {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/TASKS=', "/DIR=`"$install`"")
    Run-Setup $setup ($arguments + "/LOG=`"$(Join-Path $work 'install.log')`"")
    $exe = Join-Path $install 'GalaxyBridge.exe'
    $uninstall = Join-Path $install 'unins000.exe'
    Check (Test-Path -LiteralPath $exe) 'Installed Windows executable is missing.'
    Check (Test-Path -LiteralPath $uninstall) 'Windows uninstaller is missing.'
    Check (Test-Path -LiteralPath (Join-Path $install 'LICENSE')) 'Packaged license is missing.'
    Check (-not (Test-Path -LiteralPath (Join-Path $install 'GalaxyBridgeLan.apk'))) 'APK must remain a separate artifact.'
    Check ((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion.StartsWith($version)) 'Installed version does not match the project.'
    Check (Test-Path -LiteralPath $key) 'Application is not registered for uninstall.'
    Check ((Get-ItemProperty -LiteralPath $key).DisplayVersion -eq $version) 'Uninstall registration has the wrong version.'
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Galaxy Bridge/Galaxy Bridge.lnk'
    Check (Test-Path -LiteralPath $shortcut) 'Start menu shortcut is missing.'
    New-Item -Path $startupKey -Force | Out-Null
    $startupCommand = '"' + $exe + '" --minimized'
    New-ItemProperty -LiteralPath $startupKey -Name 'GalaxyBridge' -Value $startupCommand -PropertyType String -Force | Out-Null
    Run-Setup $setup ($arguments + "/LOG=`"$(Join-Path $work 'update.log')`"")
    Check ((Get-ItemPropertyValue -LiteralPath $startupKey -Name 'GalaxyBridge') -eq $startupCommand) 'Update changed the user autostart choice.'
    Check (Test-Path -LiteralPath $exe) 'Updating the installed copy removed the application.'
    Check ([IO.File]::ReadAllText($marker) -eq 'Preserve application data') 'Updating replaced application data.'
    Check ((Get-ChildItem -LiteralPath $install -Filter 'unins*.exe').Count -eq 1) 'Update created a duplicate uninstaller.'
    Run-Setup $uninstall @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$(Join-Path $work 'uninstall.log')`"")
    Check (-not (Test-Path -LiteralPath $exe)) 'Uninstall left the application executable behind.'
    Check (-not (Test-Path -LiteralPath $key)) 'Uninstall left its registration behind.'
    Check (-not (Test-Path -LiteralPath $shortcut)) 'Uninstall left the Start menu shortcut behind.'
    Check ($null -eq (Get-ItemProperty -LiteralPath $startupKey -Name 'GalaxyBridge' -ErrorAction SilentlyContinue)) 'Uninstall left the app autostart value behind.'
    Check ([IO.File]::ReadAllText($marker) -eq 'Preserve application data') 'Uninstall deleted application data.'
    Write-Output 'PASS: installer, update, shortcuts, uninstall and data preservation.'
}
finally {
    Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
    # Only the empty directory made by this test can be removed here.
    if ((Get-ChildItem -LiteralPath $data -Force | Measure-Object).Count -eq 0) { Remove-Item -LiteralPath $data }
}
