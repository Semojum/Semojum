$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../scripts/smj-association-core.ps1')

class FakeAssociationStore {
    [hashtable]$Values = @{}
    [string] Key([string]$path, [string]$name) { return ($path + '|' + $name).ToLowerInvariant() }
    [bool] ValueExists([string]$path, [string]$name) { return $this.Values.ContainsKey($this.Key($path, $name)) }
    [bool] KeyExists([string]$path) { $prefix = ($path + '|').ToLowerInvariant(); return @($this.Values.Keys | Where-Object { $_.StartsWith($prefix) }).Count -gt 0 }
    [object] Read([string]$path, [string]$name) { return $this.Values[$this.Key($path, $name)].Value }
    [string] Kind([string]$path, [string]$name) { return $this.Values[$this.Key($path, $name)].Kind }
    [void] WriteString([string]$path, [string]$name, [string]$value) { $this.Values[$this.Key($path, $name)] = @{ Value = $value; Kind = 'String' } }
    [void] WriteNone([string]$path, [string]$name) { $this.Values[$this.Key($path, $name)] = @{ Value = [byte[]]@(); Kind = 'None' } }
    [void] RemoveValue([string]$path, [string]$name) { $this.Values.Remove($this.Key($path, $name)) }
    [bool] IsEmptyTree([string]$path) { $prefix = $path.ToLowerInvariant(); return @($this.Values.Keys | Where-Object { $_.StartsWith($prefix + '|') -or $_.StartsWith($prefix + '\') }).Count -eq 0 }
}
function Assert($condition, $message) { if (-not $condition) { throw "FAIL: $message" }; Write-Host "PASS: $message" }

$store = [FakeAssociationStore]::new()
$filePath = 'C:\Program Files\Semojum Portable\Semojum.Mockup.exe'
$otherPath = 'C:\Other Install\Semojum.Mockup.exe'
$classes = 'Software\Classes'
$extension = "$classes\.smj"
$openWith = "$extension\OpenWithProgids"
$prog = "$classes\Semojum.WorkFile.1"
$command = "$prog\shell\open\command"
$store.WriteString($extension, '', 'Other.Document.1')
$store.WriteNone($openWith, 'Other.Document.1')
Register-SmjAssociation $store $filePath
Assert ($store.Read($command, '') -ceq '"C:\Program Files\Semojum Portable\Semojum.Mockup.exe" "%1"') 'Executable and document argument are individually quoted'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class SmjCommandLineCheck {
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CommandLineToArgvW(string text, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    public static string[] Parse(string command) {
        int count; IntPtr pointer = CommandLineToArgvW(command, out count);
        if (pointer == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try { var args = new string[count]; for (int i = 0; i < count; i++) args[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size)); return args; }
        finally { LocalFree(pointer); }
    }
}
'@
$document = 'C:\Users\Example User\Documents\work file.smj'
$arguments = [SmjCommandLineCheck]::Parse($store.Read($command, '').Replace('%1', $document))
Assert ($arguments.Count -eq 2 -and $arguments[0] -ceq $filePath -and $arguments[1] -ceq $document) 'Windows parses a spaced document path as the single startup argument'
Assert ($store.Read($extension, '') -ceq 'Other.Document.1' -and $store.ValueExists($openWith, 'Other.Document.1')) 'Existing default and other handler remain'
Assert ($store.ValueExists($openWith, 'Semojum.WorkFile.1') -and $store.Kind($openWith, 'Semojum.WorkFile.1') -eq 'None') 'Semojum is registered as a candidate'
Register-SmjAssociation $store $filePath
$rejected = $false
try { Register-SmjAssociation $store $otherPath } catch { $rejected = $true }
Assert ($rejected -and $store.Read($command, '') -ceq '"C:\Program Files\Semojum Portable\Semojum.Mockup.exe" "%1"') 'Another installation cannot silently take over'
$store.WriteString($prog, 'ForeignValue', 'preserve me')
Unregister-SmjAssociation $store $otherPath
Assert ($store.ValueExists($openWith, 'Semojum.WorkFile.1')) 'Unregistering a different path preserves the current installation'
Unregister-SmjAssociation $store $filePath
Assert (-not $store.ValueExists($openWith, 'Semojum.WorkFile.1') -and -not $store.ValueExists($command, '')) 'Matching installation removes only its association'
Assert ($store.Read($extension, '') -ceq 'Other.Document.1' -and $store.ValueExists($openWith, 'Other.Document.1') -and $store.Read($prog, 'ForeignValue') -ceq 'preserve me') 'Unregister preserves foreign defaults, handlers and additions'
$clean = [FakeAssociationStore]::new()
Register-SmjAssociation $clean $filePath
Unregister-SmjAssociation $clean $filePath
Register-SmjAssociation $clean $filePath
Assert ($clean.ValueExists($command, '')) 'The same installation can register again after removal'
$foreign = [FakeAssociationStore]::new()
$foreign.WriteString($prog, 'ForeignValue', 'do not overwrite')
$rejected = $false
try { Register-SmjAssociation $foreign $filePath } catch { $rejected = $true }
Assert ($rejected -and -not $foreign.ValueExists($command, '') -and $foreign.Read($prog, 'ForeignValue') -ceq 'do not overwrite') 'An unrelated pre-existing ProgID is left untouched'
$changed = [FakeAssociationStore]::new()
Register-SmjAssociation $changed $filePath
$changed.WriteString($command, '', '"C:\Third Party\viewer.exe" "%1"')
Unregister-SmjAssociation $changed $filePath
Assert ($changed.Read($command, '') -ceq '"C:\Third Party\viewer.exe" "%1"' -and $changed.ValueExists($openWith, 'Semojum.WorkFile.1')) 'Removal preserves a handler changed by another application'
$collision = [FakeAssociationStore]::new()
$collision.WriteString($openWith, 'Semojum.WorkFile.1', 'foreign value')
$rejected = $false
try { Register-SmjAssociation $collision $filePath } catch { $rejected = $true }
Assert ($rejected -and $collision.Read($openWith, 'Semojum.WorkFile.1') -ceq 'foreign value' -and -not $collision.ValueExists($command, '')) 'Preflight rejects a conflicting candidate before writing anything'
Write-Host 'All file-association simulations passed.'
