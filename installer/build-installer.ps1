# WisprClone Installer Build Script
# This script builds the application in Release mode and creates the installer
#
# Prerequisites:
#   1. .NET 8 SDK installed
#   2. Inno Setup 6 installed (https://jrsoftware.org/isdl.php)
#
# Usage:
#   .\build-installer.ps1              # Build and create installer
#   .\build-installer.ps1 -SkipBuild   # Create installer using existing build
#   .\build-installer.ps1 -OpenOutput  # Open output folder when done

param(
    [switch]$SkipBuild,
    [switch]$OpenOutput
)

$ErrorActionPreference = "Stop"

# Get script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$projectDir = Join-Path $rootDir "src\WisprClone.Avalonia"
$outputDir = Join-Path $scriptDir "output"

# Inno Setup compiler path (default installation location)
$innoSetupPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)

# Common dotnet SDK locations
$dotnetPaths = @(
    "dotnet",  # Try PATH first
    "C:\Program Files\dotnet\dotnet.exe",
    "${env:ProgramFiles}\dotnet\dotnet.exe",
    "${env:USERPROFILE}\.dotnet\dotnet.exe"
)

function Find-InnoSetup {
    foreach ($path in $innoSetupPaths) {
        if (Test-Path $path) {
            return $path
        }
    }
    return $null
}

function Find-DotNet {
    foreach ($path in $dotnetPaths) {
        try {
            $result = & $path --version 2>$null
            if ($LASTEXITCODE -eq 0) {
                return $path
            }
        } catch {
            continue
        }
    }
    return $null
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  WisprClone Installer Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check for Inno Setup
$innoCompiler = Find-InnoSetup
if (-not $innoCompiler) {
    Write-Host "ERROR: Inno Setup 6 not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install Inno Setup 6 from:" -ForegroundColor Yellow
    Write-Host "https://jrsoftware.org/isdl.php" -ForegroundColor White
    Write-Host ""
    exit 1
}

Write-Host "Found Inno Setup at: $innoCompiler" -ForegroundColor Green

# Step 1: Build the application
if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "Step 1: Building WisprClone (Release, self-contained)..." -ForegroundColor Cyan
    Write-Host ""

    # Find dotnet SDK
    $dotnetExe = Find-DotNet
    if (-not $dotnetExe) {
        Write-Host "ERROR: .NET SDK not found!" -ForegroundColor Red
        Write-Host ""
        Write-Host "Please install .NET 8 SDK from:" -ForegroundColor Yellow
        Write-Host "https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor White
        Write-Host ""
        Write-Host "Or if you have Visual Studio installed, run this script from" -ForegroundColor Yellow
        Write-Host "Developer PowerShell for VS 2022" -ForegroundColor White
        Write-Host ""
        exit 1
    }

    Write-Host "Using .NET SDK: $dotnetExe" -ForegroundColor Green

    Push-Location $projectDir
    try {
        # Clean previous build
        if (Test-Path "bin\Release") {
            Write-Host "Cleaning previous build..." -ForegroundColor Gray
            Remove-Item -Recurse -Force "bin\Release" -ErrorAction SilentlyContinue
        }

        # Build and publish
        $publishArgs = @(
            "publish",
            "-c", "Release",
            "-r", "win-x64",
            "--self-contained", "true",
            "-p:PublishSingleFile=false",
            "-p:DebugType=none",
            "-p:DebugSymbols=false"
        )

        & $dotnetExe @publishArgs

        if ($LASTEXITCODE -ne 0) {
            throw "Build failed with exit code $LASTEXITCODE"
        }

        Write-Host ""
        Write-Host "Build completed successfully!" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
}
else {
    Write-Host ""
    Write-Host "Step 1: Skipping build (using existing files)..." -ForegroundColor Yellow
}

# Verify publish output exists
$publishDir = Join-Path $projectDir "bin\Release\net8.0\win-x64\publish"
if (-not (Test-Path $publishDir)) {
    Write-Host ""
    Write-Host "ERROR: Publish directory not found at:" -ForegroundColor Red
    Write-Host $publishDir -ForegroundColor White
    Write-Host ""
    Write-Host "Please run the script without -SkipBuild flag." -ForegroundColor Yellow
    exit 1
}

$exePath = Join-Path $publishDir "WisprClone.exe"
if (-not (Test-Path $exePath)) {
    Write-Host ""
    Write-Host "ERROR: WisprClone.exe not found in publish directory!" -ForegroundColor Red
    exit 1
}

# Get file count and size
$files = Get-ChildItem -Path $publishDir -Recurse -File
$totalSize = ($files | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ""
Write-Host "Published files: $($files.Count) files, $([math]::Round($totalSize, 2)) MB" -ForegroundColor Gray

# Step 2: Create the installer
Write-Host ""
Write-Host "Step 2: Creating installer with Inno Setup..." -ForegroundColor Cyan
Write-Host ""

$issFile = Join-Path $scriptDir "WisprClone.iss"
if (-not (Test-Path $issFile)) {
    Write-Host "ERROR: Inno Setup script not found at:" -ForegroundColor Red
    Write-Host $issFile -ForegroundColor White
    exit 1
}

# Ensure output directory exists
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Run Inno Setup compiler
& $innoCompiler $issFile

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ERROR: Installer creation failed!" -ForegroundColor Red
    exit 1
}

# Find the created installer
$installer = Get-ChildItem -Path $outputDir -Filter "WisprClone-Setup-*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($installer) {
    $installerSize = [math]::Round($installer.Length / 1MB, 2)
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  Installer created successfully!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Output: $($installer.FullName)" -ForegroundColor White
    Write-Host "Size:   $installerSize MB" -ForegroundColor White
    Write-Host ""

    if ($OpenOutput) {
        Write-Host "Opening output folder..." -ForegroundColor Gray
        Start-Process explorer.exe -ArgumentList $outputDir
    }
}
else {
    Write-Host ""
    Write-Host "WARNING: Could not find the created installer file." -ForegroundColor Yellow
}

Write-Host "Done!" -ForegroundColor Green
Write-Host ""
