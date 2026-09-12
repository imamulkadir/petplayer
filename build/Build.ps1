<#
.SYNOPSIS
    Builds and publishes Pet Player into a ready-to-run folder.

.DESCRIPTION
    Restores NuGet packages, builds in Release configuration, publishes the
    application (self-contained by default so the target machine needs no
    .NET runtime installed), verifies the bundled LibVLC runtime and icon
    made it into the output, and stages everything under dist\PetPlayer.

.PARAMETER Runtime
    The .NET runtime identifier to publish for. Default: win-x64.

.PARAMETER FrameworkDependent
    Publish a framework-dependent build instead of self-contained. Produces a
    much smaller output, but the target machine must already have the
    .NET 8 Desktop Runtime installed.

.PARAMETER Configuration
    Build configuration. Default: Release.

.EXAMPLE
    .\build\Build.ps1

.EXAMPLE
    .\build\Build.ps1 -FrameworkDependent
#>
[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "PetPlayer\PetPlayer.csproj"
$distRoot = Join-Path $repoRoot "dist"
$outputPath = Join-Path $distRoot "PetPlayer"

Write-Host "== Pet Player build ==" -ForegroundColor Cyan
Write-Host "Project:       $projectPath"
Write-Host "Configuration: $Configuration"
Write-Host "Runtime:       $Runtime"
Write-Host "Self-contained: $(-not $FrameworkDependent)"
Write-Host ""

# 1. Restore
Write-Host "-> Restoring NuGet packages..." -ForegroundColor Cyan
dotnet restore $projectPath
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

# 2. Build (fail fast before attempting a publish)
Write-Host "-> Building ($Configuration)..." -ForegroundColor Cyan
dotnet build $projectPath -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }

# 3. Clean previous output
if (Test-Path $outputPath) {
    Write-Host "-> Cleaning previous output at $outputPath..." -ForegroundColor Cyan
    Remove-Item -Recurse -Force $outputPath
}
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

# 4. Publish
Write-Host "-> Publishing..." -ForegroundColor Cyan
$publishArgs = @(
    "publish", $projectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", ([string](-not $FrameworkDependent)).ToLowerInvariant(),
    "-o", $outputPath,
    "-p:PublishSingleFile=false"
)
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# 5. Verify the bundled LibVLC runtime and icon made it into the output.
Write-Host "-> Verifying bundled runtime and resources..." -ForegroundColor Cyan

$libvlcDll = Join-Path $outputPath "libvlc\$Runtime\libvlc.dll"
$libvlcCoreDll = Join-Path $outputPath "libvlc\$Runtime\libvlccore.dll"
$pluginsDir = Join-Path $outputPath "libvlc\$Runtime\plugins"
$exePath = Join-Path $outputPath "PetPlayer.exe"

$missing = @()
if (-not (Test-Path $libvlcDll)) { $missing += $libvlcDll }
if (-not (Test-Path $libvlcCoreDll)) { $missing += $libvlcCoreDll }
if (-not (Test-Path $pluginsDir)) { $missing += $pluginsDir }
if (-not (Test-Path $exePath)) { $missing += $exePath }

if ($missing.Count -gt 0) {
    Write-Host "Missing expected output files:" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    throw "Publish output is incomplete. See missing files above."
}

Write-Host "   libvlc.dll, libvlccore.dll, plugins\, and PetPlayer.exe are all present." -ForegroundColor Green

# 6. Report result
$sizeBytes = (Get-ChildItem -Recurse -File $outputPath | Measure-Object -Property Length -Sum).Sum
$sizeMb = [math]::Round($sizeBytes / 1MB, 1)

Write-Host ""
Write-Host "== Build complete ==" -ForegroundColor Green
Write-Host "Output: $outputPath"
Write-Host "Size:   $sizeMb MB"
Write-Host ""
Write-Host "Copy the 'PetPlayer' folder anywhere on the target machine and run PetPlayer.exe."
Write-Host "No installer, admin rights, or system-wide VLC/.NET install is required for a self-contained build."
