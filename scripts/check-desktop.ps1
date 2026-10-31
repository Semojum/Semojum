param(
    [Parameter(Mandatory)]
    [ValidateSet('board', 'institution', 'editor', 'appearance')]
    [string[]]$Area,
    [switch]$NoBuild,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutputRoot) { $OutputRoot = Join-Path $root 'artifacts/desktop-checks' }
Push-Location $root
try {
    if (-not $NoBuild) {
        dotnet build src/Semojum.Desktop/Semojum.Desktop.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
    }
    $app = Join-Path $root 'src/Semojum.Desktop/bin/Release/net10.0-windows10.0.19041.0/Semojum.Mockup.dll'
    if (-not (Test-Path -LiteralPath $app -PathType Leaf)) { throw "Desktop build not found: $app" }
    foreach ($name in $Area) {
        $output = Join-Path $OutputRoot $name
        dotnet $app --check-area $name $output
        if ($LASTEXITCODE -ne 0) { throw "Desktop check '$name' failed. See $output/failure.txt." }
    }
} finally { Pop-Location }
