<#
.SYNOPSIS
    Builds and publishes self-contained VRCGT artifacts for one or more
    runtime identifiers (RIDs).

.DESCRIPTION
    1. Builds the VRCGT.Web React frontend (npm ci + npm run build).
       The output lands in src/VRCGT.Server/wwwroot/ which is already
       referenced by VRCGT.Launcher.csproj Content items and copied into
       every publish directory automatically.

    2. For each target RID, runs dotnet publish on VRCGT.Launcher in
       self-contained mode (--self-contained true), so the .NET runtime is
       bundled and end-users need no tooling installed.

    3. Writes VERSION.txt and README.txt into each artifact directory.

.PARAMETER Rids
    One or more .NET RIDs to target.
    Default: win-x64, linux-x64, osx-x64, osx-arm64

.PARAMETER Configuration
    MSBuild configuration.  Default: Release

.PARAMETER OutDir
    Root output directory for artifacts.  Default: artifacts/ at repo root

.PARAMETER SkipFrontend
    Skip the npm build step (wwwroot/ already up-to-date)

.EXAMPLE
    # Build all platforms
    .\scripts\build.ps1

.EXAMPLE
    # Build only Windows, skip npm rebuild
    .\scripts\build.ps1 -Rids win-x64 -SkipFrontend
#>

[CmdletBinding()]
param(
    [string[]] $Rids          = @('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64'),
    [string]   $Configuration = 'Release',
    [string]   $OutDir        = '',
    [switch]   $SkipFrontend
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---- Resolve paths -----------------------------------------------------------

$RepoRoot   = (Resolve-Path "$PSScriptRoot\..").Path
$WebDir     = Join-Path $RepoRoot 'src\VRCGT.Web'
$LaunchProj = Join-Path $RepoRoot 'src\VRCGT.Launcher\VRCGT.Launcher.csproj'
$CoreSrc    = Join-Path $RepoRoot 'src\VRCGT.Core\Class1.cs'

if ($OutDir -eq '') {
    $OutDir = Join-Path $RepoRoot 'artifacts'
}

# ---- Extract version from source --------------------------------------------

$VersionLine = Get-Content $CoreSrc | Select-String 'AppVersion\s*=\s*"([^"]+)"'
$Version = if ($VersionLine) { $VersionLine.Matches[0].Groups[1].Value } else { 'unknown' }

Write-Host ""
Write-Host "  VRCGT Build Script" -ForegroundColor Cyan
Write-Host "  --------------------------------------------------" -ForegroundColor Cyan
Write-Host "  Version   : $Version" -ForegroundColor Cyan
Write-Host "  Config    : $Configuration" -ForegroundColor Cyan
Write-Host "  Targets   : $($Rids -join ', ')" -ForegroundColor Cyan
Write-Host "  Output    : $OutDir" -ForegroundColor Cyan
Write-Host "  --------------------------------------------------" -ForegroundColor Cyan
Write-Host ""

# ---- Prerequisite checks ----------------------------------------------------

function Test-RequiredCommand([string]$cmd) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        Write-Error "'$cmd' not found in PATH. Please install it and re-run."
    }
}

Test-RequiredCommand 'dotnet'

if (-not $SkipFrontend) {
    Test-RequiredCommand 'node'
    Test-RequiredCommand 'npm'
}

# ---- Step 1 - Build frontend ------------------------------------------------

if (-not $SkipFrontend) {
    Write-Host "[1/2] Building VRCGT.Web frontend..." -ForegroundColor Yellow
    Push-Location $WebDir
    try {
        if (Test-Path 'package-lock.json') {
            npm ci --prefer-offline
        } else {
            npm install --prefer-offline
        }
        if ($LASTEXITCODE -ne 0) { throw 'npm install failed' }

        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'npm run build failed' }
    }
    finally {
        Pop-Location
    }
    Write-Host "    Frontend built." -ForegroundColor Green
} else {
    Write-Host "[1/2] Skipping frontend build (-SkipFrontend)." -ForegroundColor DarkGray
}

# ---- Step 2 - Publish per RID -----------------------------------------------

Write-Host "[2/2] Publishing VRCGT.Launcher..." -ForegroundColor Yellow

$Succeeded = [System.Collections.Generic.List[string]]::new()
$Failed    = [System.Collections.Generic.List[string]]::new()

foreach ($rid in $Rids) {
    $ridOut = Join-Path $OutDir $rid
    Write-Host ""
    Write-Host "  -> $rid" -ForegroundColor Cyan

    $publishArgs = @(
        'publish'
        $LaunchProj
        '--configuration', $Configuration
        '--runtime',       $rid
        '--self-contained', 'true'
        '/p:SkipWebFrontend=true'
        '/p:PublishReadyToRun=false'
        '--output', $ridOut
        '--nologo'
        '-v', 'minimal'
    )

    dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Warning "  Publish FAILED for $rid (exit $LASTEXITCODE)"
        $Failed.Add($rid)
        continue
    }

    # ---- VERSION.txt ---------------------------------------------------------
    $builtAt    = Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'
    $versionTxt = "VRCGT $Version`nRuntime target: $rid`nBuilt: $builtAt`nConfiguration: $Configuration`n"
    Set-Content -Path (Join-Path $ridOut 'VERSION.txt') -Value $versionTxt -Encoding UTF8

    # ---- README.txt ----------------------------------------------------------
    $isWin = $rid.StartsWith('win-')
    $exe   = if ($isWin) { 'VRCGT.Launcher.exe' } else { 'VRCGT.Launcher' }

    if ($isWin) {
        $readmeTxt = @(
            "VRCGT $Version -- Windows ($rid)"
            "================================"
            ""
            "How to run"
            "----------"
            "Double-click VRCGT.Launcher.exe"
            "  -- or --"
            "Open a terminal in this folder and run:"
            "    .\VRCGT.Launcher.exe"
            ""
            "The app opens your default browser automatically to the local dashboard."
            "To stop, press Ctrl+C in the terminal, or close the terminal window."
            ""
            "Data is stored in:"
            "    %LOCALAPPDATA%\VRCGT\"
            ""
            "No installation, database server, or Node.js required."
        )
    } else {
        $dataDir = if ($rid.StartsWith('osx-')) {
            '~/Library/Application Support/VRCGT/'
        } else {
            '~/.local/share/VRCGT/'
        }
        $readmeTxt = @(
            "VRCGT $Version -- $rid"
            "================================"
            ""
            "How to run"
            "----------"
            "Open a terminal in this folder and run:"
            ""
            "    chmod +x $exe"
            "    ./$exe"
            ""
            "The app opens your default browser automatically to the local dashboard."
            "To stop, press Ctrl+C."
            ""
            "Data is stored in:"
            "    $dataDir"
            ""
            "No installation, database server, or Node.js required."
        )
    }

    Set-Content -Path (Join-Path $ridOut 'README.txt') -Value ($readmeTxt -join "`n") -Encoding UTF8

    Write-Host "    Published -> $ridOut" -ForegroundColor Green
    $Succeeded.Add($rid)
}

# ---- Summary ----------------------------------------------------------------

Write-Host ""
Write-Host "  --------------------------------------------------" -ForegroundColor Cyan
Write-Host "  Build summary" -ForegroundColor Cyan
foreach ($rid in $Succeeded) {
    Write-Host "    OK : $rid" -ForegroundColor Green
}
foreach ($rid in $Failed) {
    Write-Host "    FAIL: $rid" -ForegroundColor Red
}
Write-Host "  Output: $OutDir" -ForegroundColor Cyan
Write-Host "  --------------------------------------------------" -ForegroundColor Cyan
Write-Host ""

if ($Failed.Count -gt 0) {
    Write-Error "Build completed with $($Failed.Count) failure(s): $($Failed -join ', ')"
}
