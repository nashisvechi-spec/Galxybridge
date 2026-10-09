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
                     'licenses/scrcpy-APACHE-2.0.txt', 'docs/ARCHITECTURE.md',
                     'docs/TESTING.md', 'docs/VALIDATION.md', '.gitignore',
                     '.gitattributes', 'backend/README.md', 'scripts/Fetch-Backend.ps1'):
        read(required)
    sdk = json.loads(read('global.json'))['sdk']
    require(str(sdk['version']).startswith('8.0.'), 'SDK must stay on .NET 8')
    require(sdk['rollForward'] == 'latestFeature', 'SDK roll-forward configuration')
    props = ET.fromstring(read('Directory.Build.props'))
    require(props.findtext('.//LangVersion') == '12.0', 'C# 12 configuration')
    require(props.findtext('.//Nullable') == 'enable', 'Nullable analysis configuration')
    projects = list(ROOT.glob('**/*.csproj'))
    require(len(projects) == 3, 'Expected Windows, Core and portable test projects')
    for path in projects:
        tree = ET.parse(path)
        for reference in tree.findall('.//ProjectReference'):
            require((path.parent / reference.attrib['Include']).is_file(),
                    f"Broken project reference in {path.relative_to(ROOT)}")
    windows = ET.parse(ROOT / 'src/GalaxyBridge.Windows/GalaxyBridge.Windows.csproj')
    require(windows.findtext('.//TargetFramework') == 'net8.0-windows', 'Windows framework')
    require(windows.findtext('.//UseWindowsForms') == 'true', 'WinForms enabled')
    require(windows.findtext('.//OutputType') == 'WinExe', 'Windows executable configuration')
    manifest = ET.fromstring(read('src/GalaxyBridge.Windows/app.manifest'))
    level = next(element for element in manifest.iter() if element.tag.endswith('requestedExecutionLevel'))
    require(level.attrib['level'] == 'asInvoker', 'Application should run as current user')

    lock = json.loads(read('scripts/backend.lock.json'))
    version = lock['version']
    require(re.fullmatch(r'\d+\.\d+\.\d+', version) is not None, 'Backend version must be pinned')
    require(lock['zip_url'] == f'https://github.com/Genymobile/scrcpy/releases/download/v{version}/scrcpy-win64-v{version}.zip',
            'Download must point to the exact official scrcpy release')
    for field in ('zip_sha256', 'server_sha256'):
        require(re.fullmatch(r'[0-9a-f]{64}', lock[field]) is not None, f"Invalid {field}")
    protocol = read('src/GalaxyBridge.Core/ControlProtocol.cs')
    client = read('src/GalaxyBridge.Windows/AdbClient.cs')
    fetch = read('scripts/Fetch-Backend.ps1')
    require(f'ServerVersion = "{version}"' in protocol, 'Client protocol/backend version mismatch')
    require(f'ServerSha256 = "{lock["server_sha256"]}"' in client, 'Runtime hash/backend lock mismatch')
    require('manifest.zip_sha256' in fetch and 'manifest.server_sha256' in fetch,
            'Download script must verify both digests')
    require('UseShellExecute = false' in client and 'ArgumentList.Add' in client,
            'ADB arguments must be passed without shell interpolation')
    session = read('src/GalaxyBridge.Windows/PhoneSession.cs')
    require('localabstract:scrcpy_' in session and 'IPAddress.Loopback' in session,
            'Control socket must use session-specific ADB forwarding to loopback')
    require('kill-server' not in session, 'Do not terminate other applications\' ADB server')

    workflow = read('.github/workflows/build-windows.yml')
    trigger = re.search(r'^on:\s*\n((?:[ \t]+[^\n]*\n|\n)+)', workflow, re.MULTILINE)
    require(trigger is not None and trigger.group(1).strip() == 'workflow_dispatch:',
            'Workflow must be manual only; no push/PR/release trigger')
    actions = re.findall(r'uses:\s*([^\s]+)', workflow)
    require(len(actions) == 3 and all(re.fullmatch(r'actions/[a-z-]+@[0-9a-f]{40}', a) for a in actions),
            'Actions must use pinned commit IDs')
    require('contents: read' in workflow and 'contents: write' not in workflow,
            'Build workflow requires read-only repository permissions')
    require('dotnet run --project tests/GalaxyBridge.Core.Tests/' in workflow,
            'Workflow must run portable tests')
    require('--self-contained true' in workflow and '-r win-x64' in workflow,
            'Portable Windows x64 publish configuration')
    sources = [p for p in ROOT.rglob('*.cs') if 'obj' not in p.parts and 'bin' not in p.parts]
    require(len(sources) >= 10, 'Application sources are incomplete')
    for path in sources:
        balanced_csharp(path)
    print(f'PASS: {checks} source-package checks; {len(sources)} C# files inspected')
    print('No C# compilation, executable build, network download, or device test performed.')


if __name__ == '__main__':
    try:
        main()
    except (ValueError, KeyError, OSError, ET.ParseError, json.JSONDecodeError) as error:
        print(f'FAIL: {error}', file=sys.stderr)
        sys.exit(1)
