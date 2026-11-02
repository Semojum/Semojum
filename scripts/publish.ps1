param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64', [string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $output = Join-Path $root "artifacts/Semojum-$Runtime"
    # Without -Version the app keeps the version set in Directory.Build.props.
    $versionArgs = if ($Version) { @("-p:Version=$Version") } else { @() }
    dotnet publish src/Semojum.Desktop @versionArgs -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if ($Runtime -ne 'win-x64') { throw 'The bundled engine has only been configured for win-x64.' }
    & "$PSScriptRoot/build-engine.ps1" -Output "$output/engine"
    Copy-Item README.md, THIRD-PARTY-NOTICES.md -Destination $output
    Copy-Item licenses -Destination $output -Recurse -Force
    # The zip stays the in-app update package; the MSI installs the same folder per user and starts the app.
    Compress-Archive -Path "$output/*" -DestinationPath "artifacts/Semojum-$Runtime.zip" -Force
    dotnet build installer/Semojum.Installer.wixproj @versionArgs -c Release "-p:AppDir=$output" -o artifacts/installer
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
    Copy-Item "artifacts/installer/Semojum-$Runtime.msi" "artifacts/Semojum-$Runtime.msi" -Force
    Write-Host "Ready: artifacts/Semojum-$Runtime.zip, artifacts/Semojum-$Runtime.msi"
} finally { Pop-Location }
