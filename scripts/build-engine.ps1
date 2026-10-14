param([string]$Output = 'artifacts/engine')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$build = Join-Path $root 'artifacts/engine-build'
$source = Join-Path $build 'source'
New-Item -ItemType Directory -Force $build | Out-Null
if (-not (Test-Path "$source/.git")) {
    git clone --depth 1 --branch v3.4.17 https://github.com/Semojum/braille.git $source
    if ($LASTEXITCODE -ne 0) { throw 'Engine source download failed.' }
}
$revision = git -C $source rev-parse HEAD
if ($revision -ne '74d6758bf34cdb028c6121c7ebe7fb568ad35fec') { throw 'Unexpected engine revision.' }
python -m venv "$build/venv"
if ($LASTEXITCODE -ne 0) { throw 'Engine build environment failed.' }
$python = Join-Path $build 'venv/Scripts/python.exe'
& $python -m pip install 'pyinstaller==6.22.3' 'pydantic==2.14.1' $source
if ($LASTEXITCODE -ne 0) { throw 'Engine dependencies failed.' }
& $python -m PyInstaller --noconfirm --clean --onedir --name semojum-engine --collect-all semojum_braille --collect-all braillify --collect-all pydantic --copy-metadata semojum-braille --copy-metadata braillify --exclude-module kiwipiepy --distpath "$build/dist" --workpath "$build/work" --specpath $build "$PSScriptRoot/engine-entry.py"
if ($LASTEXITCODE -ne 0) { throw 'Engine bundle failed.' }
New-Item -ItemType Directory -Force $Output | Out-Null
Copy-Item "$build/dist/semojum-engine/*" $Output -Recurse -Force
Copy-Item "$source/NOTICE.md", "$source/LICENSE-MIT", "$source/LICENSE-APACHE" $Output
Copy-Item "$source/semojum_braille/third_party" $Output -Recurse -Force
& $python "$PSScriptRoot/engine-notices.py" $Output
if ($LASTEXITCODE -ne 0) { throw 'Engine license collection failed.' }
& $python -m pip freeze | Out-File (Join-Path $Output 'build-dependencies.txt') -Encoding utf8
# Smoke the actual frozen executable; a successful Python import is insufficient.
$ping = '{"op":"ping","id":1}' | & (Join-Path $Output 'semojum-engine.exe')
if ($LASTEXITCODE -ne 0) { throw 'Frozen engine failed to start.' }
$response = $ping | ConvertFrom-Json
if ($response.version -ne '3.4.17' -or $response.protocol -ne 1) { throw 'Frozen engine handshake mismatch.' }
