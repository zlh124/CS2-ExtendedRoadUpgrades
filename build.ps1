#Requires -Version 5.1
<#
.SYNOPSIS
    Builds Extended Road Upgrades against a local Cities: Skylines II installation.

.DESCRIPTION
    The official CSII modding toolchain (Mod.props / Mod.targets) ships inside the game folder and is
    normally located through user-scoped environment variables that are written by the in-game toolchain
    setup. This script works out every path by itself, so the mod can be built (and deployed to the local
    Mods folder) even when those variables were never set.

    Two build modes are used:

      * full toolchain  - a Unity editor and the generated "Unity Mods project" are available. The build
                          then runs the official Mod Post Processor (Burst code extraction) and lets the
                          toolchain deploy the result.
      * minimal         - no Unity editor / Unity Mods project on the machine. The mod is still compiled and
                          deployed, only the Burst post-processing step is skipped. This mod contains no
                          Burst code, so its behaviour is identical; the game loads the assembly without the
                          optional "<name>_win_x86_64.dll" Burst library.

.PARAMETER CsiiGameDir
    Cities: Skylines II installation folder (the one containing Cities2.exe).

.PARAMETER Configuration
    Build configuration, Debug or Release. Defaults to Release.

.PARAMETER NoDeploy
    Build only: skip copying the result into the game's local Mods folder.

.PARAMETER Publish
    Publish the mod to PDX Mods after building. Requires the complete toolchain (Unity editor + Unity Mods
    project) and an authenticated PDX account (either the one stored by the game, or the credentials in
    Properties\pdx_account.txt).

      Update     - upload a new version of the mod referenced by <ModId> in Properties\PublishConfiguration.xml
      Publish    - create the mod entry described by Properties\PublishConfiguration.xml
      NewVersion - create a new version entry for the mod described by Properties\PublishConfiguration.xml

.EXAMPLE
    .\build.ps1

.EXAMPLE
    .\build.ps1 -Configuration Debug -CsiiGameDir "D:\SteamLibrary\steamapps\common\Cities Skylines II"

.EXAMPLE
    .\build.ps1 -Publish Update
#>
[CmdletBinding()]
param(
    [string]$CsiiGameDir,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$NoDeploy,
    [ValidateSet('Update', 'Publish', 'NewVersion')][string]$Publish,
    [string[]]$BuildArguments = @()
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Write-Step([string]$text) {
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Write-Warn([string]$text) {
    Write-Host "WARNING: $text" -ForegroundColor Yellow
}

function Get-UserEnvironmentValue([string]$name) {
    return [System.Environment]::GetEnvironmentVariable($name, 'User')
}

# ---------------------------------------------------------------------------------------------
# Locate the game
# ---------------------------------------------------------------------------------------------
function Get-SteamLibraries {
    $libraries = New-Object System.Collections.Generic.List[string]

    $steamPath = $null
    foreach ($view in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
        if (-not (Test-Path $view)) { continue }
        $value = (Get-ItemProperty -Path $view -Name SteamPath -ErrorAction SilentlyContinue).SteamPath
        if ($value) { $steamPath = $value; break }
    }

    if (-not $steamPath) { return $libraries }

    $steamPath = $steamPath -replace '/', '\'
    if (Test-Path $steamPath) { $libraries.Add($steamPath) }

    $vdf = Join-Path $steamPath 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        foreach ($match in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
            $path = $match.Groups[1].Value -replace '\\\\', '\'
            if ((Test-Path $path) -and -not $libraries.Contains($path)) { $libraries.Add($path) }
        }
    }

    return $libraries
}

function Resolve-CsiiGameDir([string]$explicit) {
    if ($explicit) {
        if (Test-Path (Join-Path $explicit 'Cities2.exe')) { return (Resolve-Path $explicit).Path }
        throw "Cities2.exe not found in '$explicit'."
    }

    $candidates = New-Object System.Collections.Generic.List[string]
    if ($env:CSII_GAMEPATH) { $candidates.Add($env:CSII_GAMEPATH) }
    foreach ($library in Get-SteamLibraries) { $candidates.Add((Join-Path $library 'steamapps\common\Cities Skylines II')) }
    $candidates.Add('C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II')
    $candidates.Add('C:\XboxGames\Cities- Skylines II - PC Edition\Content')

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path (Join-Path $candidate 'Cities2.exe'))) { return (Resolve-Path $candidate).Path }
    }

    throw 'Could not locate Cities: Skylines II. Pass -CsiiGameDir "<game folder>" or set CSII_GAMEPATH.'
}

function Get-UnityEditorPath {
    # The toolchain stores the Unity version it was built against in CSII_UNITYVERSION, and Unity registers its
    # installation under "Unity Technologies\Installer\Unity <version>".
    $version = $env:CSII_UNITYVERSION
    $keys = New-Object System.Collections.Generic.List[string]

    if ($version) {
        $keys.Add("HKLM:\SOFTWARE\Unity Technologies\Installer\Unity $version")
        $keys.Add("HKCU:\SOFTWARE\Unity Technologies\Installer\Unity $version")
    }
    else {
        foreach ($hive in @('HKLM:\SOFTWARE\Unity Technologies\Installer', 'HKCU:\SOFTWARE\Unity Technologies\Installer')) {
            if (-not (Test-Path $hive)) { continue }
            foreach ($install in Get-ChildItem $hive -ErrorAction SilentlyContinue) { $keys.Add($install.PSPath) }
        }
    }

    foreach ($key in $keys) {
        if (-not (Test-Path $key)) { continue }
        $location = (Get-ItemProperty -Path $key -Name 'Location x64' -ErrorAction SilentlyContinue).'Location x64'
        if (-not $location) { continue }

        $candidate = Join-Path ($location -replace '/', '\') 'Editor'
        if (Test-Path (Join-Path $candidate 'Unity.exe')) { return $candidate }
        if (Test-Path $candidate) { return $candidate }
    }

    foreach ($hubRoot in @('C:\Program Files\Unity\Hub\Editor', 'D:\Unity\Hub\Editor')) {
        if (-not (Test-Path $hubRoot)) { continue }
        $installed = Get-ChildItem $hubRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
        if ($installed -and (Test-Path (Join-Path $installed.FullName 'Editor'))) { return (Join-Path $installed.FullName 'Editor') }
    }

    return $null
}

function Resolve-ToolchainPath([string]$environmentVariable, [string]$fallback) {
    $value = Get-UserEnvironmentValue $environmentVariable
    if ($value -and (Test-Path $value)) { return $value }
    return $fallback
}

$gameDir = Resolve-CsiiGameDir $CsiiGameDir
$managedPath = Resolve-ToolchainPath 'CSII_MANAGEDPATH' (Join-Path $gameDir 'Cities2_Data\Managed')
$toolchainPath = Resolve-ToolchainPath 'CSII_TOOLPATH' (Join-Path $gameDir 'Cities2_Data\Content\Game\.ModdingToolchain')
$userDataPath = Resolve-ToolchainPath 'CSII_USERDATAPATH' (Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II')
$localModsPath = Resolve-ToolchainPath 'CSII_LOCALMODSPATH' (Join-Path $userDataPath 'Mods')
$mscorlibPath = Resolve-ToolchainPath 'CSII_MSCORLIBPATH' (Join-Path $managedPath 'mscorlib.dll')
$postProcessorPath = Resolve-ToolchainPath 'CSII_MODPOSTPROCESSORPATH' (Join-Path $gameDir 'Cities2_Data\Content\Game\.ModdingToolchain\ModPostProcessor\ModPostProcessor.exe')
$publisherPath = Resolve-ToolchainPath 'CSII_MODPUBLISHERPATH' (Join-Path $gameDir 'Cities2_Data\Content\Game\.ModdingToolchain\ModPublisher\ModPublisher.exe')
$unityProjectPath = Get-UserEnvironmentValue 'CSII_UNITYMODPROJECTPATH'
$entitiesVersion = Get-UserEnvironmentValue 'CSII_ENTITIESVERSION'
$unityEditorPath = Get-UnityEditorPath
$hasUnityProject = $unityProjectPath -and (Test-Path $unityProjectPath)
$hasUnityEditor = [bool]$unityEditorPath

Write-Step "Game:      $gameDir"
Write-Step "Toolchain: $toolchainPath"

if (-not (Test-Path (Join-Path $managedPath 'Game.dll'))) {
    throw "Game assemblies not found in '$managedPath'. Is the game fully installed?"
}

if (-not (Test-Path (Join-Path $toolchainPath 'Mod.props'))) {
    Write-Warn "Modding toolchain not found in '$toolchainPath'. Run the game once and install the Modding toolchain from the in-game menu."
}

if (-not (Test-Path $userDataPath)) {
    Write-Warn "User data folder '$userDataPath' does not exist yet. Run the game once."
    $localModsPath = Join-Path $root '.tmp\deploy'
}

# The toolchain deploys the post-processed output itself, so -NoDeploy redirects it to a scratch folder
# instead of the game's local Mods folder.
if ($NoDeploy) {
    $localModsPath = Join-Path $root '.tmp\deploy'
}

$deployDir = Join-Path $localModsPath 'ExtendedRoadUpgrades'

# ---------------------------------------------------------------------------------------------
# Toolchain inputs (also read from user-scoped environment variables by Mod.props; they are passed
# explicitly so the build also works when only some of them are configured)
# ---------------------------------------------------------------------------------------------
$buildArgs = @(
    '-c', $Configuration
    '-nologo'
    "-p:CsiiToolchainPath=$toolchainPath"
    "-p:ManagedPath=$managedPath"
    "-p:MSCORLIBPath=$mscorlibPath"
    "-p:UserDataPath=$userDataPath"
    "-p:LocalModsPath=$localModsPath"
    "-p:ModPostProcessorPath=$postProcessorPath"
    "-p:ModPublisherPath=$publisherPath"
)

if ($unityProjectPath) { $buildArgs += "-p:UnityModProjectPath=$unityProjectPath" }
if ($entitiesVersion) { $buildArgs += "-p:EntitiesVersion=$entitiesVersion" }

# The .NET Framework 4.8 targeting pack is not installed on every modding machine (it ships with the
# Visual Studio "'.NET Framework 4.8 SDK" workload). When it is missing we compile against the game's own
# reference assemblies, which is what the mod is loaded against at runtime anyway.
$targetingPack = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\mscorlib.dll'
if (-not (Test-Path $targetingPack)) {
    Write-Warn '.NET Framework 4.8 targeting pack is missing; compiling against the game assemblies instead.'
    $buildArgs += '-p:AutomaticallyUseReferenceAssemblyPackages=false'
    $buildArgs += "-p:FrameworkPathOverride=$managedPath"
}

$fullToolchain = $hasUnityProject -and $hasUnityEditor -and (Test-Path $postProcessorPath)
if ($fullToolchain) {
    Write-Step "Unity:     $unityEditorPath"
    Write-Step "Project:   $unityProjectPath"
    Write-Step "Post proc: $postProcessorPath"
}
else {
    $reason = if (-not $hasUnityProject) { 'the Unity Mods project (CSII_UNITYMODPROJECTPATH) is missing' }
    elseif (-not $hasUnityEditor) { 'no Unity editor installation was found' }
    else { "the Mod Post Processor was not found ('$postProcessorPath')" }
    Write-Warn "Building without Mod Post Processor because $reason."
    Write-Warn 'Burst code extraction / platform libraries will be skipped; the mod itself is unaffected.'
    $buildArgs += '-p:SkipModTargets=true'
}

if ($BuildArguments.Count -gt 0) { $buildArgs += $BuildArguments }

# ---------------------------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------------------------
$logPath = Join-Path $root 'build.log'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
Push-Location $root
try {
    if ($dotnet) {
        Write-Step "dotnet build ($Configuration)"
        $buildOutput = & $dotnet.Source build (Join-Path $root 'ExtendedRoadUpgrades.csproj') @buildArgs 2>&1
    }
    else {
        $vswhere = Join-Path $toolchainPath 'vswhere.exe'
        $msbuild = $null
        if (Test-Path $vswhere) {
            $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        }
        if (-not $msbuild) { throw 'Neither "dotnet" nor MSBuild could be found.' }

        Write-Step "msbuild ($Configuration)"
        $msbuildArgs = @((Join-Path $root 'ExtendedRoadUpgrades.csproj'), "/p:Configuration=$Configuration") + ($buildArgs | Where-Object { $_ -ne '-c' -and $_ -ne $Configuration })
        $buildOutput = & $msbuild @msbuildArgs 2>&1
    }

    $buildExitCode = $LASTEXITCODE
    $buildOutput | Set-Content -Path $logPath -Encoding UTF8
    $buildOutput | ForEach-Object { Write-Host $_ }

    if ($buildExitCode -ne 0) { throw "Build failed with exit code $buildExitCode (see $logPath)." }
}
finally {
    Pop-Location
}

# ---------------------------------------------------------------------------------------------
# Deploy (the toolchain does this itself when it is complete)
# ---------------------------------------------------------------------------------------------
if ($NoDeploy) {
    Write-Step 'Deployment skipped (-NoDeploy).'
}
elseif (-not $fullToolchain) {
    $outDir = Join-Path $root "bin\$Configuration\net48"
    if (-not (Test-Path (Join-Path $outDir 'ExtendedRoadUpgrades.dll'))) {
        throw "Build output not found in '$outDir'."
    }

    Write-Step "Deploying to $deployDir"
    if (Test-Path $deployDir) { Remove-Item $deployDir -Recurse -Force }
    New-Item -ItemType Directory -Path $deployDir -Force | Out-Null
    Copy-Item (Join-Path $outDir 'ExtendedRoadUpgrades.dll') $deployDir
    Copy-Item (Join-Path $outDir '0Harmony.dll') $deployDir
    Copy-Item (Join-Path $root 'Icons') (Join-Path $deployDir 'Icons') -Recurse
}

# ---------------------------------------------------------------------------------------------
# Publish to PDX Mods
# ---------------------------------------------------------------------------------------------
if ($Publish) {
    if (-not $fullToolchain) {
        throw 'Publishing requires the complete modding toolchain (Unity editor + Unity Mods project + Mod Post Processor).'
    }

    if (-not (Test-Path $publisherPath)) {
        throw "Mod Publisher not found in '$publisherPath'. Reinstall the modding toolchain from the in-game menu."
    }

    # 'Update' keeps the existing mod entry (ModId in Properties\PublishConfiguration.xml) and does not rebuild,
    # 'Publish' creates the entry pointed to by the configuration file. Everything is rebuilt right before.
    $publishArgs = $buildArgs | Where-Object { $_ -notlike '-nologo' }
    $publishArgs += "-p:ModPublisherCommand=$Publish"

    Write-Step "dotnet publish ($Publish)"
    Push-Location $root
    try {
        $publishOutput = & $dotnet.Source publish (Join-Path $root 'ExtendedRoadUpgrades.csproj') @publishArgs 2>&1
        $publishExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    $publishLogPath = Join-Path $root 'publish.log'
    $publishOutput | Set-Content -Path $publishLogPath -Encoding UTF8
    $publishOutput | ForEach-Object { Write-Host $_ }

    if ($publishExitCode -ne 0) {
        throw "Publish failed with exit code $publishExitCode (see $publishLogPath). If the error mentions the PDX account, create 'Properties\pdx_account.txt' with your PDX email on the first line and your password on the second."
    }
}

Write-Step 'Done.'
