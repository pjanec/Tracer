<#
.SYNOPSIS
    Builds the TracerViewer self-contained distribution package.

.DESCRIPTION
    1. Builds the Vue SPA (pnpm run build).
    2. Publishes the .NET OfflineViewer as a self-contained single-file exe for win-x64.
    3. Verifies expected files are present in the output folder.
    4. Generates README.txt.
    5. Zips the output to dist/TracerViewer.zip.

.EXAMPLE
    .\build-viewer-distribution.ps1
    .\build-viewer-distribution.ps1 -Configuration Debug -OutputDir "my-dist/TracerViewer"
#>
param(
    [string]$Configuration = "Release",
    [string]$OutputDir     = "dist/TracerViewer"
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot

Write-Host "=== Building Tracer Viewer Distribution ===" -ForegroundColor Cyan

# 1. Build the Vue SPA
Write-Host "--- Step 1: Building Vue SPA ---"
Push-Location (Join-Path $RepoRoot "tracer-viewer")
try {
    & pnpm install --frozen-lockfile
    if ($LASTEXITCODE -ne 0) { throw "pnpm install failed (exit $LASTEXITCODE)" }
    & pnpm run build
    if ($LASTEXITCODE -ne 0) { throw "pnpm run build failed (exit $LASTEXITCODE)" }
} finally {
    Pop-Location
}

# 2. Publish the .NET project
Write-Host "--- Step 2: Publishing .NET OfflineViewer ---"
& dotnet publish (Join-Path $RepoRoot "src/Tracer.OfflineViewer") `
    -c $Configuration `
    -r win-x64 `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o (Join-Path $RepoRoot $OutputDir)
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

# 3. Verify expected files
Write-Host "--- Step 3: Verifying output ---"
$expected = @(
    "tracer-viewer.exe",
    "wwwroot/index.html"
)
foreach ($file in $expected) {
    $fullPath = Join-Path $RepoRoot $OutputDir $file
    if (-not (Test-Path $fullPath)) {
        throw "Distribution missing required file: $file (expected at: $fullPath)"
    }
}

# 4. Generate README.txt
Write-Host "--- Step 4: Writing README.txt ---"
$readme = @"
Tracer Offline Viewer
=====================

To open a Tracer bundle:

  1. Double-click tracer-viewer.exe
  2. When the browser opens, paste the path to your .tracerbundle file or directory
     and click Open.

Or from the command line:
  tracer-viewer.exe "C:\path\to\session.tracerbundle"

No installation required. This folder is portable -- copy it to any Windows 10/11
machine and run tracer-viewer.exe directly. No .NET installation needed.
"@
$readme | Set-Content (Join-Path $RepoRoot $OutputDir "README.txt") -Encoding UTF8

# 5. Zip
Write-Host "--- Step 5: Creating TracerViewer.zip ---"
$zipPath = Join-Path $RepoRoot "$OutputDir.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $RepoRoot $OutputDir "*") -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "=== Distribution built successfully ===" -ForegroundColor Green
Write-Host "  Folder: $OutputDir"
Write-Host "  ZIP:    $OutputDir.zip"
