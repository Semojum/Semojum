param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64', [string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $output = Join-Path $root "artifacts/Semojum-$Runtime"
    dotnet publish src/Semojum.Desktop -p:Version=$Version -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if ($Runtime -ne 'win-x64') { throw 'The bundled engine has only been configured for win-x64.' }
    & "$PSScriptRoot/build-engine.ps1" -Output "$output/engine"
    Copy-Item README.md, THIRD-PARTY-NOTICES.md -Destination $output
    Copy-Item licenses -Destination $output -Recurse -Force
    Compress-Archive -Path "$output/*" -DestinationPath "artifacts/Semojum-$Runtime.zip" -Force
    Write-Host "Ready: artifacts/Semojum-$Runtime.zip"
} finally { Pop-Location }
