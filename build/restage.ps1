<#
.SYNOPSIS
  Windows port of build/restage.sh: rebuild the mod and restage it into the local Vintage
  Story Mods folder for manual playtesting.

.DESCRIPTION
  Unlike package.sh (which zips a Release build for distribution), this copies straight
  into %APPDATA%\VintagestoryData\Mods\<modid> so the game picks it up on next launch.

  Always wipes the staged mod folder's assets\ before recopying, matching restage.sh --
  copying on top of an existing directory can leave a stale nested assets\assets\ tree.

  Configuration defaults to Release (the player-like build; VSImGui is excluded from it by
  a Condition in Mod.csproj, so a Release stage has zero ImGui presence). Pass -Configuration
  Debug to stage a build that includes the VSImGui live-tuning sliders (RegisterDebugSliders)
  -- required for the add-imgui-configlib-tuning task 5.1 investigation, since those sliders
  are #if DEBUG-gated AND the reference itself is Debug-only.

  VSImGui's overlay only actually renders on a machine with OpenGL >= 4.3. It draws nothing
  on Apple Silicon (OpenGL 4.1 over Metal) -- which is exactly why the Debug investigation
  runs on Windows. See VSAPI-NOTES.md "VSImGui debug overlay".

.PARAMETER Configuration
  Debug or Release. Defaults to Release.

.EXAMPLE
  # From the repo root, in a PowerShell window (Windows PowerShell 5.1 or PowerShell 7):
  .\build\restage.ps1 -Configuration Debug

  # If blocked by execution policy, unblock for this session only, then re-run:
  #   Set-ExecutionPolicy -Scope Process Bypass
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $RepoRoot

$ModInfo = 'src/Mod/modinfo.json'
if (-not (Test-Path $ModInfo)) {
    Write-Error "error: $ModInfo not found (has the Mod been scaffolded yet?)"
}

# Match restage.sh's modid extraction: read "modid" from modinfo.json, default to "scribe".
$ModId = (Get-Content $ModInfo -Raw | ConvertFrom-Json).modid
if ([string]::IsNullOrWhiteSpace($ModId)) { $ModId = 'scribe' }

$Stage = Join-Path $env:APPDATA "VintagestoryData\Mods\$ModId"
Write-Host "Restaging $ModId ($Configuration) -> $Stage"

dotnet build src/Mod/Mod.csproj --configuration $Configuration
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet build failed ($LASTEXITCODE)" }

New-Item -ItemType Directory -Force -Path $Stage | Out-Null
Copy-Item $ModInfo -Destination $Stage -Force
# Blanket copy of the build output DLLs (Scribe.dll + Scribe.Core.dll). The `gui` (LibGUI) hard
# dep, ConfigLib, and the game DLLs are all Private=false, so they never land in bin/ and are NOT
# staged here -- the separately-installed mods/game provide them at runtime (verified: no Gui.dll).
Copy-Item "src/Mod/bin/$Configuration/net10.0/*.dll" -Destination $Stage -Force

$StageAssets = Join-Path $Stage 'assets'
if (Test-Path $StageAssets) { Remove-Item $StageAssets -Recurse -Force }
if (Test-Path 'src/Mod/assets') {
    Copy-Item 'src/Mod/assets' -Destination $StageAssets -Recurse -Force
}

$FileCount = (Get-ChildItem $Stage -Recurse -File).Count
Write-Host "Staged: $FileCount files"
Write-Host "Fully quit and relaunch the game client to pick up the new build (lang/assets load once at boot, not per-world-join)."
