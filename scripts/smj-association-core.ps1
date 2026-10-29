Set-StrictMode -Version Latest

function Get-SmjAssociationValues([string]$Executable) {
    if (-not [System.IO.Path]::IsPathRooted($Executable) -or $Executable.IndexOfAny([char[]]@('"', "`r", "`n")) -ge 0) {
        throw 'The executable path must be absolute and cannot contain quotes or line breaks.'
    }
    $prog = 'Software\Classes\Semojum.WorkFile.1'
    return @(
        @{ Path = $prog; Name = ''; Value = 'Semojum work file' },
        @{ Path = "$prog\DefaultIcon"; Name = ''; Value = '"' + $Executable + '",0' },
        @{ Path = "$prog\shell\open\command"; Name = ''; Value = '"' + $Executable + '" "%1"' }
    )
}

function Register-SmjAssociation($Store, [string]$Executable) {
    $values = @(Get-SmjAssociationValues $Executable)
    $prog = 'Software\Classes\Semojum.WorkFile.1'
    $openWith = 'Software\Classes\.smj\OpenWithProgids'
    $command = "$prog\shell\open\command"
    # An existing ProgID belongs to another registration unless its command is ours.
    if ($Store.KeyExists($prog) -and -not $Store.ValueExists($command, '') -and -not $Store.IsEmptyTree($prog)) { throw 'The Semojum file handler already exists without a matching command; no changes were made.' }
    foreach ($entry in $values) {
        if ($Store.ValueExists($entry.Path, $entry.Name) -and ($Store.Kind($entry.Path, $entry.Name) -ne 'String' -or $Store.Read($entry.Path, $entry.Name) -ine $entry.Value)) {
            throw 'An existing Semojum file handler belongs to a different installation or was modified; no changes were made.'
        }
    }
    foreach ($entry in @(@{ Path = $prog; Name = 'AllowSilentDefaultTakeOver' }, @{ Path = $openWith; Name = 'Semojum.WorkFile.1' })) {
        if ($Store.ValueExists($entry.Path, $entry.Name) -and ($Store.Kind($entry.Path, $entry.Name) -ne 'None' -or @($Store.Read($entry.Path, $entry.Name)).Count -ne 0)) {
            throw 'An existing file association has a different value; no changes were made.'
        }
    }
    # Establish ownership first so a partial failure can be safely retried.
    $Store.WriteString($values[2].Path, $values[2].Name, $values[2].Value)
    foreach ($entry in $values[0..1]) { $Store.WriteString($entry.Path, $entry.Name, $entry.Value) }
    $Store.WriteNone($prog, 'AllowSilentDefaultTakeOver')
    $Store.WriteNone($openWith, 'Semojum.WorkFile.1')
}

function Unregister-SmjAssociation($Store, [string]$Executable) {
    $values = @(Get-SmjAssociationValues $Executable)
    $prog = 'Software\Classes\Semojum.WorkFile.1'
    $openWith = 'Software\Classes\.smj\OpenWithProgids'
    $command = "$prog\shell\open\command"
    if (-not $Store.ValueExists($command, '') -or $Store.Kind($command, '') -ne 'String' -or $Store.Read($command, '') -ine $values[2].Value) { return }
    if ($Store.ValueExists($openWith, 'Semojum.WorkFile.1') -and $Store.Kind($openWith, 'Semojum.WorkFile.1') -eq 'None' -and @($Store.Read($openWith, 'Semojum.WorkFile.1')).Count -eq 0) {
        $Store.RemoveValue($openWith, 'Semojum.WorkFile.1')
    }
    if ($Store.ValueExists($prog, 'AllowSilentDefaultTakeOver') -and $Store.Kind($prog, 'AllowSilentDefaultTakeOver') -eq 'None' -and @($Store.Read($prog, 'AllowSilentDefaultTakeOver')).Count -eq 0) {
        $Store.RemoveValue($prog, 'AllowSilentDefaultTakeOver')
    }
    foreach ($entry in $values) {
        if ($Store.ValueExists($entry.Path, $entry.Name) -and $Store.Kind($entry.Path, $entry.Name) -eq 'String' -and $Store.Read($entry.Path, $entry.Name) -ieq $entry.Value) {
            $Store.RemoveValue($entry.Path, $entry.Name)
        }
    }
}
