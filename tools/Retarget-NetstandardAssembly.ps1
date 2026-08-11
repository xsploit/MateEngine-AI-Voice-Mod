[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$CecilPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\Managed\Unity.Cecil.dll',
    [string]$FrameworkPath = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2',
    [string[]]$SearchDirectories = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

foreach ($path in @($InputPath, $CecilPath, (Join-Path $FrameworkPath 'mscorlib.dll'), (Join-Path $FrameworkPath 'System.dll'), (Join-Path $FrameworkPath 'System.Core.dll'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required retargeting input is missing: $path" }
}

[void][Reflection.Assembly]::LoadFrom($CecilPath)
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
foreach ($directory in @((Split-Path -Parent (Resolve-Path -LiteralPath $InputPath).Path), $FrameworkPath) + $SearchDirectories) {
    if (-not [string]::IsNullOrWhiteSpace($directory) -and (Test-Path -LiteralPath $directory -PathType Container)) {
        $resolver.AddSearchDirectory((Resolve-Path -LiteralPath $directory).Path)
    }
}
$reader = [Mono.Cecil.ReaderParameters]::new()
$reader.AssemblyResolver = $resolver
$target = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($InputPath, $reader)
$framework = @{}
foreach ($name in @('mscorlib', 'System', 'System.Core')) {
    $definition = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $FrameworkPath "$name.dll"))
    $types = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($type in $definition.MainModule.Types) { [void]$types.Add($type.FullName) }
    $framework[$name] = [pscustomobject]@{
        Definition = $definition
        Types = $types
        Reference = [Mono.Cecil.AssemblyNameReference]::Parse($definition.Name.FullName)
    }
}

try {
    $references = @{}
    foreach ($name in @('mscorlib', 'System', 'System.Core')) {
        $existing = $target.MainModule.AssemblyReferences | Where-Object { $_.Name -eq $name } | Select-Object -First 1
        if (-not $existing) {
            $existing = $framework[$name].Reference
            $target.MainModule.AssemblyReferences.Add($existing)
        }
        $references[$name] = $existing
    }

    $unresolved = [System.Collections.Generic.List[string]]::new()
    $retargetScopes = @('netstandard', 'Microsoft.Win32.Registry')
    foreach ($type in $target.MainModule.GetTypeReferences()) {
        if (-not $type.Scope -or $retargetScopes -notcontains $type.Scope.Name) { continue }
        $owner = $null
        foreach ($name in @('mscorlib', 'System', 'System.Core')) {
            if ($framework[$name].Types.Contains($type.FullName)) { $owner = $name; break }
        }
        if (-not $owner) { $unresolved.Add($type.FullName); continue }
        $type.Scope = $references[$owner]
    }
    if ($unresolved.Count -gt 0) { throw 'Could not retarget framework types: ' + (($unresolved | Sort-Object -Unique) -join ', ') }

    $obsolete = @($target.MainModule.AssemblyReferences | Where-Object { $retargetScopes -contains $_.Name })
    foreach ($reference in $obsolete) { [void]$target.MainModule.AssemblyReferences.Remove($reference) }
    $directory = Split-Path -Parent $OutputPath
    if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
    $target.Write($OutputPath)
}
finally {
    $target.Dispose()
    foreach ($entry in $framework.Values) { $entry.Definition.Dispose() }
    $resolver.Dispose()
}
