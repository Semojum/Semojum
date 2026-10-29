param([Parameter(Mandatory = $true)][ValidateSet('Register', 'Unregister')][string]$Action)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'smj-association-core.ps1')

# Only HKCU is opened. The Windows default choice and machine-wide associations are never changed.
class CurrentUserAssociationStore {
    [bool] KeyExists([string]$path) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path)
        if ($null -eq $key) { return $false }; $key.Dispose(); return $true
    }
    [bool] IsEmptyTree([string]$path) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path)
        if ($null -eq $key) { return $true }
        try {
            if ($key.ValueCount -gt 0) { return $false }
            foreach ($name in $key.GetSubKeyNames()) { if (-not $this.IsEmptyTree("$path\$name")) { return $false } }
            return $true
        } finally { $key.Dispose() }
    }
    [bool] ValueExists([string]$path, [string]$name) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path)
        if ($null -eq $key) { return $false }
        try { return @($key.GetValueNames() | Where-Object { $_ -ieq $name }).Count -gt 0 } finally { $key.Dispose() }
    }
    [object] Read([string]$path, [string]$name) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path)
        try { return $key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } finally { $key.Dispose() }
    }
    [string] Kind([string]$path, [string]$name) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path)
        try { return $key.GetValueKind($name).ToString() } finally { $key.Dispose() }
    }
    [void] WriteString([string]$path, [string]$name, [string]$value) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($path)
        try { $key.SetValue($name, $value, [Microsoft.Win32.RegistryValueKind]::String) } finally { $key.Dispose() }
    }
    [void] WriteNone([string]$path, [string]$name) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($path)
        try { $key.SetValue($name, [byte[]]@(), [Microsoft.Win32.RegistryValueKind]::None) } finally { $key.Dispose() }
    }
    [void] RemoveValue([string]$path, [string]$name) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($path, $true)
        if ($null -ne $key) { try { $key.DeleteValue($name, $false) } finally { $key.Dispose() } }
    }
}

$executable = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'Semojum.Mockup.exe'))
$store = [CurrentUserAssociationStore]::new()
if ($Action -eq 'Register') {
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Semojum.Mockup.exe was not found beside this script: $executable" }
    Register-SmjAssociation $store $executable
    Write-Host 'Registered Semojum as an Open with candidate for .smj. Choose the default app in Windows Settings if desired.'
} else {
    Unregister-SmjAssociation $store $executable
    Write-Host 'Removed matching Semojum association values if present. Windows default-app choices were not changed.'
}
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class SemojumAssociationNotify { [DllImport("shell32.dll")] public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2); }'
[SemojumAssociationNotify]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
