#!/usr/bin/env python3
"""Check this source package without compiling or downloading anything.

Uses only Python's standard library. This is structural validation, not a C#
compiler or a Windows/device test.
"""
from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
checks = 0


def require(condition: bool, message: str) -> None:
    global checks
    if not condition:
        raise ValueError(message)
    checks += 1


def read(relative: str) -> str:
    path = ROOT / relative
    require(path.is_file(), f"Missing required file: {relative}")
    return path.read_text(encoding="utf-8-sig")


def balanced_csharp(path: Path) -> None:
    """Catch unmatched delimiters, strings and comments; no semantic checking."""
    text = path.read_text(encoding="utf-8-sig")
    stack: list[str] = []
    i = 0
    pairs = {')': '(', ']': '[', '}': '{'}
    while i < len(text):
        if text.startswith('//', i):
            end = text.find('\n', i + 2)
            i = len(text) if end < 0 else end + 1
            continue
        if text.startswith('/*', i):
            end = text.find('*/', i + 2)
            require(end >= 0, f"Unclosed comment in {path.relative_to(ROOT)}")
            i = end + 2
            continue
        char = text[i]
        if char in ('"', "'"):
            verbatim = char == '"' and i > 0 and text[i - 1] == '@'
            quote = char
            i += 1
            terminated = False
            while i < len(text):
                require(verbatim or text[i] not in '\r\n', f"Newline in non-verbatim literal in {path.relative_to(ROOT)}")
                if not verbatim and text[i] == '\\':
                    i += 2
                    continue
                if text[i] == quote:
                    if verbatim and text[i:i + 2] == '""':
                        i += 2
                        continue
                    i += 1
                    terminated = True
                    break
                i += 1
            require(terminated, f"Unclosed literal in {path.relative_to(ROOT)}")
            continue
        if char in '([{':
            stack.append(char)
        elif char in ')]}':
            require(bool(stack) and stack[-1] == pairs[char],
                    f"Unmatched delimiter in {path.relative_to(ROOT)} at offset {i}")
            stack.pop()
        i += 1
    require(not stack, f"Unclosed delimiter in {path.relative_to(ROOT)}")


def main() -> None:
    for required in ('README.md', 'README.ru.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md',
                     'docs/ARCHITECTURE.md',
                     'docs/TESTING.md', 'docs/VALIDATION.md', '.gitignore',
                     '.gitattributes', 'src/GalaxyBridge.Windows/BleHidSession.cs'):
        read(required)
    sdk = json.loads(read('global.json'))['sdk']
    require(str(sdk['version']).startswith('8.0.'), 'SDK must stay on .NET 8')
    require(sdk['rollForward'] == 'latestFeature', 'SDK roll-forward configuration')
    props = ET.fromstring(read('Directory.Build.props'))
    require(props.findtext('.//LangVersion') == '12.0', 'C# 12 configuration')
    require(props.findtext('.//Nullable') == 'enable', 'Nullable analysis configuration')
    projects = list(ROOT.glob('**/*.csproj'))
    require(len(projects) == 4, 'Expected Windows, Core, portable and LAN TLS test projects')
    for path in projects:
        tree = ET.parse(path)
        for reference in tree.findall('.//ProjectReference'):
            require((path.parent / reference.attrib['Include']).is_file(),
                    f"Broken project reference in {path.relative_to(ROOT)}")
    windows = ET.parse(ROOT / 'src/GalaxyBridge.Windows/GalaxyBridge.Windows.csproj')
    require(windows.findtext('.//TargetFramework') == 'net8.0-windows10.0.19041.0', 'Windows framework with BLE GATT server support')
    require(windows.findtext('.//WindowsSdkPackageVersion') == '10.0.19041.56', 'Pinned Windows SDK projections')
    require(windows.findtext('.//UseWindowsForms') == 'true', 'WinForms enabled')
    require(windows.findtext('.//OutputType') == 'WinExe', 'Windows executable configuration')
    qr_package = windows.find('.//PackageReference[@Include="QRCoder"]')
    require(qr_package is not None and qr_package.get('Version') == '1.8.0', 'QR dependency must be pinned')
    drawing_package = windows.find('.//PackageReference[@Include="System.Drawing.Common"]')
    require(drawing_package is not None and drawing_package.get('Version') == '8.0.30', 'Drawing dependency stays on .NET 8')
    read('licenses/QRCoder-LICENSE.txt')
    manifest = ET.fromstring(read('src/GalaxyBridge.Windows/app.manifest'))
    level = next(element for element in manifest.iter() if element.tag.endswith('requestedExecutionLevel'))
    require(level.attrib['level'] == 'asInvoker', 'Application should run as current user')

    workflow = read('.github/workflows/build-windows.yml')
    trigger = re.search(r'^on:\s*\n((?:[ \t]+[^\n]*\n|\n)+)', workflow, re.MULTILINE)
    require(trigger is not None and trigger.group(1).strip() == 'workflow_dispatch:',
            'Workflow must be manual only; no push/PR/release trigger')
    actions = re.findall(r'uses:\s*([^\s]+)', workflow)
    require(len(actions) == 5 and all(re.fullmatch(r'(actions|android-actions)/[a-z-]+@[0-9a-f]{40}', a) for a in actions),
            'Actions must use pinned commit IDs')
    require('contents: read' in workflow and 'contents: write' not in workflow,
            'Build workflow requires read-only repository permissions')
    program = read('src/GalaxyBridge.Windows/Program.cs')
    require('Application.Run(new LanForm(Settings.Load()))' in program, 'Start directly in the application without debugging')
    for removed in ('android/edge-return', 'backend', 'scripts/Fetch-Backend.ps1',
                    'scripts/Build-EdgeHelper.ps1', 'scripts/backend.lock.json',
                    'src/GalaxyBridge.Windows/MainForm.cs', 'src/GalaxyBridge.Windows/AdbClient.cs',
                    'src/GalaxyBridge.Windows/PhoneSession.cs', 'src/GalaxyBridge.Core/ControlProtocol.cs'):
        require(not (ROOT / removed).exists(), f'Legacy debug component must be absent: {removed}')
    require(all(word not in workflow for word in ('Fetch-Backend', 'Build-EdgeHelper', 'GalaxyBridgeEdge', 'backend/')),
            'Portable artifacts must contain only the application without debugging')
    for path in (ROOT / 'src').rglob('*.cs'):
        require(not re.search(r'\b(AdbClient|AdbEndpoint|AdbDevice|ControlProtocol|MainForm)\b', path.read_text(encoding='utf-8-sig')),
                f'No legacy runtime dependency in {path.relative_to(ROOT)}')
    signing_pin = read('android/signing-cert.sha256').strip()
    require(re.fullmatch(r'[0-9a-f]{64}', signing_pin) is not None, 'Android update certificate pin')
    read('scripts/Resolve-AndroidSigning.ps1')
    read('scripts/Test-AndroidSigning.ps1')
    read('docs/ANDROID_UPDATES.ru.md')
    require('secrets.ANDROID_SIGNING_BUNDLE' in workflow and './scripts/Test-AndroidSigning.ps1' in workflow,
            'Manual workflow validates persistent APK signing before builds')
    require('-VersionCode (100000 + [int]$env:GITHUB_RUN_NUMBER)' in workflow,
            'APK version code must increase with manual workflow runs')
    for script_name in ('scripts/Build-LanCompanion.ps1',):
        signing_script = read(script_name)
        require('Resolve-AndroidSigning.ps1' in signing_script and '-genkeypair' not in signing_script,
                'APK builds must restore the persistent key instead of replacing it')
    require('dotnet run --project tests/GalaxyBridge.Core.Tests/' in workflow,
            'Workflow must run portable tests')
    require('dotnet run --project tests/GalaxyBridge.LanTls.Tests/' in workflow,
            'Workflow must test the production certificate import with Windows TLS')
    require('--self-contained true' in workflow and '-r win-x64' in workflow,
            'Portable Windows x64 publish configuration')
    ns = '{http://schemas.android.com/apk/res/android}'
    lan = ET.fromstring(read('android/lan/AndroidManifest.xml'))
    require(lan.find('uses-sdk').get(ns + 'minSdkVersion') == '33', 'Native input needs Android 13+')
    lan_permissions = {e.get(ns + 'name') for e in lan.findall('uses-permission')}
    require(lan_permissions == {'android.permission.INTERNET', 'android.permission.ACCESS_NETWORK_STATE',
        'android.permission.CHANGE_NETWORK_STATE', 'android.permission.FOREGROUND_SERVICE',
        'android.permission.FOREGROUND_SERVICE_CONNECTED_DEVICE', 'android.permission.POST_NOTIFICATIONS',
        'android.permission.CAMERA', 'android.permission.SYSTEM_ALERT_WINDOW'}, 'Native companion permission scope')
    control = lan.find('.//service[@' + ns + 'name=".ControlService"]')
    require(control is not None and control.get(ns + 'permission') == 'android.permission.BIND_ACCESSIBILITY_SERVICE',
        'Control service must require Android accessibility binding')
    connection = lan.find('.//service[@' + ns + 'name=".ConnectionService"]')
    require(connection is not None and connection.get(ns + 'exported') == 'false', 'LAN service must not be exported')
    for xml in (ROOT / 'android/lan/res').rglob('*.xml'):
        ET.parse(xml)
    native_script = read('scripts/Build-LanCompanion.ps1')
    require('8d8064c1636fdaef7189dd9055c7d59950a8940a12f2293956446ec3c109fd82' in native_script,
        'ZXing decoder must be checksum-pinned')
    require('GalaxyBridgeLan-android' in workflow and './scripts/Build-LanCompanion.ps1' in workflow,
        'Manual workflow must include independent Wi-Fi APK')
    read('docs/NATIVE_WIFI.ru.md')
    require('./scripts/Test-LanProtocol.ps1' in workflow, 'Manual workflow runs actual Android wire/pairing parser tests')
    read('scripts/Test-LanProtocol.ps1')
    read('licenses/ZXing-APACHE-2.0.txt')
    java = list((ROOT / 'android/lan/src').rglob('*.java')) + list((ROOT / 'tests/android-protocol').rglob('*.java')) + list((ROOT / 'tests/android-drop-stubs').rglob('*.java'))
    require(len(java) == 43, 'Expected active companion protocol/input/clipboard/wheel/drop test sources')
    files = ET.fromstring(read('android/files/AndroidManifest.xml'))
    require(files.get('package') == 'com.galaxybridge.files', 'File manager is a separate Android package')
    provider = files.find('.//provider')
    require(provider is not None and provider.get(ns + 'exported') == 'false' and provider.get(ns + 'grantUriPermissions') == 'true',
            'File sharing provider is private with explicit URI grants')
    require(lan.find('.//service[@' + ns + 'name=".FilesBridgeService"]') is None,
            'Removed Files IPC must not be exposed by the companion')
    require('./scripts/Build-Files.ps1' not in workflow and 'GalaxyFiles-android' not in workflow and 'android-files-ui/pom.xml' not in workflow,
            'Portable build includes only the Galaxy Bridge companion, not Galaxy Files')
    require("$apks.Count -ne 1" in workflow and "'GalaxyBridgeLan.apk'" in workflow, 'Package validates a single companion APK')
    require('getSharedPreferences("screen"' not in read('android/lan/src/com/galaxybridge/lan/ControlDisplays.java'),
            'Connected DeX must win automatically despite old screen preferences')
    ET.fromstring(read('tests/android-display-ui/pom.xml'))
    require('mvn -B -f tests/android-display-ui/pom.xml test' in workflow, 'Automatic DeX selection has Android framework regressions')
    java += list((ROOT / 'tests/android-display-ui/src/test/java').rglob('*.java'))
    read('docs/GALAXY_FILES.ru.md')
    for xml in (ROOT / 'android/files/res').rglob('*.xml'):
        ET.parse(xml)
    java += list((ROOT / 'android/files/src').rglob('*.java')) + list((ROOT / 'tests/android-files').rglob('*.java'))
    java += list((ROOT / 'tests/android-files-ui/src/test/java').rglob('*.java'))
    for helper in java:
        balanced_csharp(helper)
    sources = [p for p in ROOT.rglob('*.cs') if 'obj' not in p.parts and 'bin' not in p.parts]
    require(len(sources) >= 10, 'Application sources are incomplete')
    for path in sources:
        balanced_csharp(path)
    print(f'PASS: {checks} source-package checks; {len(sources)} C# files and {len(java)} Java files inspected')
    print('No C#/Java compilation, executable build, network download, or device test performed.')


if __name__ == '__main__':
    try:
        main()
    except (ValueError, KeyError, OSError, ET.ParseError, json.JSONDecodeError) as error:
        print(f'FAIL: {error}', file=sys.stderr)
        sys.exit(1)
