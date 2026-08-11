[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$CecilPath = 'C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\Managed\Unity.Cecil.dll',
    [string[]]$SearchDirectories = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

foreach ($path in @($InputPath, $CecilPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required managed-fallback input is missing: $path" }
}

[void][Reflection.Assembly]::LoadFrom($CecilPath)
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
foreach ($directory in @((Split-Path -Parent (Resolve-Path -LiteralPath $InputPath).Path)) + $SearchDirectories) {
    if (-not [string]::IsNullOrWhiteSpace($directory) -and (Test-Path -LiteralPath $directory -PathType Container)) {
        $resolver.AddSearchDirectory((Resolve-Path -LiteralPath $directory).Path)
    }
}
$reader = [Mono.Cecil.ReaderParameters]::new()
$reader.AssemblyResolver = $resolver
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($InputPath, $reader)

try {
    $algorithm = $assembly.MainModule.GetType('uLipSync.Algorithm')
    if (-not $algorithm) { throw 'uLipSync.Algorithm was not found.' }
    $patched = 0
    foreach ($managed in @($algorithm.Methods | Where-Object { $_.Name.EndsWith('$BurstManaged', [StringComparison]::Ordinal) })) {
        $baseName = $managed.Name.Substring(0, $managed.Name.Length - '$BurstManaged'.Length)
        $target = $algorithm.Methods | Where-Object {
            if ($_.Name -ne $baseName -or $_.Parameters.Count -ne $managed.Parameters.Count) { return $false }
            for ($i = 0; $i -lt $_.Parameters.Count; $i++) {
                if ($_.Parameters[$i].ParameterType.FullName -ne $managed.Parameters[$i].ParameterType.FullName) { return $false }
            }
            return $_.ReturnType.FullName -eq $managed.ReturnType.FullName
        } | Select-Object -First 1
        if (-not $target) { throw "Could not locate the Burst wrapper for $($managed.Name)." }

        $target.Body = [Mono.Cecil.Cil.MethodBody]::new($target)
        $il = $target.Body.GetILProcessor()
        for ($i = 0; $i -lt $target.Parameters.Count; $i++) {
            switch ($i) {
                0 { $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0)); break }
                1 { $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_1)); break }
                2 { $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_2)); break }
                3 { $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_3)); break }
                default { $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ldarg, $target.Parameters[$i])) }
            }
        }
        $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Call, $managed))
        $il.Append($il.Create([Mono.Cecil.Cil.OpCodes]::Ret))
        $patched++
    }
    if ($patched -lt 10) { throw "Only $patched uLipSync Burst wrappers were patched." }

    # Current uLipSync uses NativeArray(NativeArray, Allocator), an overload that
    # does not exist in Mate Engine's older UnityEngine.CoreModule. Replace those
    # constructor calls with a tiny managed allocate-and-copy helper injected into
    # uLipSync itself. The MFCC implementation is unchanged; this only bridges the
    # Unity player API difference.
    $copyConstructorInstructions = [System.Collections.Generic.List[object]]::new()
    $inPlaceCopyConstructorCalls = [System.Collections.Generic.List[object]]::new()
    $allocationConstructor = $null
    foreach ($method in $algorithm.Methods) {
        if (-not $method.HasBody) { continue }
        foreach ($instruction in $method.Body.Instructions) {
            if ($instruction.OpCode.Code -ne [Mono.Cecil.Cil.Code]::Newobj -and
                $instruction.OpCode.Code -ne [Mono.Cecil.Cil.Code]::Call) { continue }
            $constructor = $instruction.Operand
            if (-not $constructor -or $constructor.Name -ne '.ctor') { continue }
            if (-not $constructor -or -not $constructor.DeclaringType.FullName.StartsWith('Unity.Collections.NativeArray`1', [StringComparison]::Ordinal)) { continue }
            if ($constructor.Parameters.Count -eq 2 -and
                $constructor.Parameters[0].ParameterType.FullName.StartsWith('Unity.Collections.NativeArray`1', [StringComparison]::Ordinal) -and
                $constructor.Parameters[1].ParameterType.FullName -eq 'Unity.Collections.Allocator') {
                if ($instruction.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Newobj) {
                    $copyConstructorInstructions.Add($instruction)
                }
                else {
                    $inPlaceCopyConstructorCalls.Add([pscustomobject]@{ Method = $method; Instruction = $instruction })
                }
            }
            elseif ($constructor.Parameters.Count -eq 3 -and
                    $constructor.Parameters[0].ParameterType.FullName -eq 'System.Int32' -and
                    $constructor.Parameters[1].ParameterType.FullName -eq 'Unity.Collections.Allocator') {
                $allocationConstructor = $constructor
            }
        }
    }
    $existingCopyHelper = $algorithm.Methods | Where-Object { $_.Name -eq 'CopyNativeArrayCompat' } | Select-Object -First 1
    $copyPatchCount = $copyConstructorInstructions.Count + $inPlaceCopyConstructorCalls.Count
    if ($copyPatchCount -eq 0 -and -not $existingCopyHelper) { throw 'No incompatible or previously patched NativeArray copy path was found.' }
    if ($copyPatchCount -gt 0 -or $existingCopyHelper) {
        if (-not $allocationConstructor) { throw 'The compatible NativeArray allocation constructor was not found.' }

        $copyConstructor = if ($copyConstructorInstructions.Count -gt 0) { $copyConstructorInstructions[0].Operand } elseif ($inPlaceCopyConstructorCalls.Count -gt 0) { $inPlaceCopyConstructorCalls[0].Instruction.Operand } else { $null }
        $nativeArrayType = if ($copyConstructor) { $copyConstructor.DeclaringType } else { $existingCopyHelper.ReturnType }
        $allocatorType = if ($copyConstructor) { $copyConstructor.Parameters[1].ParameterType } else { $existingCopyHelper.Parameters[1].ParameterType }
        $copyHelper = $existingCopyHelper
        if (-not $copyHelper) {
            $helperAttributes = [Mono.Cecil.MethodAttributes]::Private -bor [Mono.Cecil.MethodAttributes]::Static -bor [Mono.Cecil.MethodAttributes]::HideBySig
            $copyHelper = [Mono.Cecil.MethodDefinition]::new('CopyNativeArrayCompat', $helperAttributes, $nativeArrayType)
            $copyHelper.Parameters.Add([Mono.Cecil.ParameterDefinition]::new('source', [Mono.Cecil.ParameterAttributes]::None, $nativeArrayType))
            $copyHelper.Parameters.Add([Mono.Cecil.ParameterDefinition]::new('allocator', [Mono.Cecil.ParameterAttributes]::None, $allocatorType))
            $algorithm.Methods.Add($copyHelper)
        }

        # Rebuild the helper every run so packages produced by older versions of
        # this script are upgraded too. Use only APIs present in Mate Engine's
        # older Unity player; its NativeArray lacks both the copy constructor and
        # the newer static Copy overload.
        $copyHelper.Body = [Mono.Cecil.Cil.MethodBody]::new($copyHelper)
        $copyHelper.Body.InitLocals = $true
        $copyLocal = [Mono.Cecil.Cil.VariableDefinition]::new($nativeArrayType)
        $indexLocal = [Mono.Cecil.Cil.VariableDefinition]::new($assembly.MainModule.TypeSystem.Int32)
        $copyHelper.Body.Variables.Add($copyLocal)
        $copyHelper.Body.Variables.Add($indexLocal)

        $getLength = $null
        $getItem = $null
        $setItem = $null
        foreach ($type in $assembly.MainModule.Types) {
            foreach ($method in $type.Methods) {
                if (-not $method.HasBody) { continue }
                foreach ($instruction in $method.Body.Instructions) {
                    $candidate = $instruction.Operand
                    if (-not ($candidate -is [Mono.Cecil.MethodReference]) -or $candidate.DeclaringType.FullName -ne $nativeArrayType.FullName) { continue }
                    if ($candidate.Name -eq 'get_Length' -and -not $getLength) { $getLength = $candidate }
                    elseif ($candidate.Name -eq 'get_Item' -and -not $getItem) { $getItem = $candidate }
                    elseif ($candidate.Name -eq 'set_Item' -and -not $setItem) { $setItem = $candidate }
                }
            }
        }
        if (-not $getLength -or -not $getItem -or -not $setItem) { throw 'Could not reuse Mate Engine-compatible NativeArray indexer references.' }

        $helperIl = $copyHelper.Body.GetILProcessor()
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarga, $copyHelper.Parameters[0]))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $getLength))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_1))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_1))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Newobj, $allocationConstructor))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Stloc_0))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_0))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Stloc_1))
        $check = $helperIl.Create([Mono.Cecil.Cil.OpCodes]::Ldloc_1)
        $loop = $helperIl.Create([Mono.Cecil.Cil.OpCodes]::Ldloca, $copyLocal)
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Br, $check))
        $helperIl.Append($loop)
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Ldloc_1))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarga, $copyHelper.Parameters[0]))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Ldloc_1))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $getItem))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $setItem))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Ldloc_1))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_1))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Add))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Stloc_1))
        $helperIl.Append($check)
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Ldarga, $copyHelper.Parameters[0]))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Call, $getLength))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Blt, $loop))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Ldloc_0))
        $helperIl.Append($helperIl.Create([Mono.Cecil.Cil.OpCodes]::Ret))

        foreach ($instruction in $copyConstructorInstructions) {
            $instruction.OpCode = [Mono.Cecil.Cil.OpCodes]::Call
            $instruction.Operand = $copyHelper
        }
        foreach ($entry in $inPlaceCopyConstructorCalls) {
            $entry.Instruction.OpCode = [Mono.Cecil.Cil.OpCodes]::Call
            $entry.Instruction.Operand = $copyHelper
            $processor = $entry.Method.Body.GetILProcessor()
            $processor.InsertAfter($entry.Instruction, $processor.Create([Mono.Cecil.Cil.OpCodes]::Stobj, $nativeArrayType))
        }
    }

    $directory = Split-Path -Parent $OutputPath
    if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
    $assembly.Write($OutputPath)
    Write-Host "Patched $patched uLipSync analyzer methods to managed fallback and $copyPatchCount NativeArray copy calls for Mate Engine."
}
finally {
    $assembly.Dispose()
    $resolver.Dispose()
}
