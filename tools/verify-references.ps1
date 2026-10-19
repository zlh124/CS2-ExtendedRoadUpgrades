#Requires -Version 5.1
<#
.SYNOPSIS
    Verifies that every type/member referenced by a compiled mod assembly can be resolved
    against the currently installed game assemblies.

.DESCRIPTION
    Cities: Skylines II regularly changes the signatures of the methods mods call. A mod compiled against
    an older game still builds (or keeps loading) but fails at runtime with a MissingMethodException as
    soon as one of those methods is invoked.

    This script resolves every reference of a mod assembly (Cecil is used to read the metadata, so nothing
    is executed) and reports the ones that no longer exist in the game.

.PARAMETER Assembly
    Mod assembly to check (e.g. bin\Release\net48\ExtendedRoadUpgrades.dll).

.PARAMETER CsiiGameDir
    Cities: Skylines II installation folder. Auto-detected from the Steam registry when omitted.

.PARAMETER SearchDirectories
    Extra folders containing assemblies the mod references (for example a folder with 0Harmony.dll).

.EXAMPLE
    .\tools\verify-references.ps1 -Assembly .\bin\Release\net48\ExtendedRoadUpgrades.dll
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Assembly,
    [string]$CsiiGameDir,
    [string[]]$SearchDirectories = @()
)

$ErrorActionPreference = 'Stop'

if (-not $CsiiGameDir) {
    $steamPath = (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction SilentlyContinue).SteamPath
    $candidates = @(
        'C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II',
        'C:\XboxGames\Cities- Skylines II - PC Edition\Content'
    )
    if ($steamPath) { $candidates = @((Join-Path ($steamPath -replace '/', '\') 'steamapps\common\Cities Skylines II')) + $candidates }
    $CsiiGameDir = $candidates | Where-Object { Test-Path (Join-Path $_ 'Cities2.exe') } | Select-Object -First 1
    if (-not $CsiiGameDir) { throw 'Could not locate the game, pass -CsiiGameDir.' }
}

$managed = Join-Path $CsiiGameDir 'Cities2_Data\Managed'
if (-not (Test-Path (Join-Path $managed 'Game.dll'))) { throw "Game assemblies not found in '$managed'." }

Add-Type -Path (Join-Path $managed 'Colossal.Mono.Cecil.dll') -ErrorAction SilentlyContinue

$resolver = New-Object Colossal.Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory($managed)
foreach ($dir in $SearchDirectories) { if (Test-Path $dir) { $resolver.AddSearchDirectory($dir) } }

$readerParameters = New-Object Colossal.Mono.Cecil.ReaderParameters
$readerParameters.AssemblyResolver = $resolver
$module = [Colossal.Mono.Cecil.AssemblyDefinition]::ReadAssembly($Assembly, $readerParameters)

$missing = New-Object System.Collections.Generic.List[string]
$checked = 0

function Test-Reference($reference, $context) {
    $script:checked++
    try {
        if ($null -eq $reference.Resolve()) {
            $script:missing.Add("UNRESOLVED $context -> $($reference.FullName)")
        }
    }
    catch {
        $script:missing.Add("MISSING    $context -> $($reference.FullName)")
    }
}

function Test-Types($types) {
    foreach ($type in $types) {
        if ($type.HasNestedTypes) { Test-Types $type.NestedTypes }
        if ($type.BaseType) { Test-Reference $type.BaseType "$($type.FullName) (base type)" }
        foreach ($implementation in $type.Interfaces) { Test-Reference $implementation.InterfaceType "$($type.FullName) (interface)" }

        foreach ($method in $type.Methods) {
            if (-not $method.HasBody) { continue }
            foreach ($instruction in $method.Body.Instructions) {
                $operand = $instruction.Operand
                $isReference = $operand -is [Colossal.Mono.Cecil.MethodReference] -or
                    $operand -is [Colossal.Mono.Cecil.FieldReference] -or
                    $operand -is [Colossal.Mono.Cecil.TypeReference]
                if (-not $isReference) { continue }

                # References declared by the mod itself are resolved through the game assemblies anyway.
                Test-Reference $operand "$($type.FullName)::$($method.Name)"
            }
        }
    }
}

Test-Types $module.MainModule.Types
$module.Dispose()

Write-Host "References checked: $checked"
if ($missing.Count -eq 0) {
    Write-Host 'RESULT: all references resolved' -ForegroundColor Green
    exit 0
}

Write-Host "RESULT: $($missing.Count) unresolved references" -ForegroundColor Red
$missing | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" }
exit 1
