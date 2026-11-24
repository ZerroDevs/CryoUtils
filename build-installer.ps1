#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes the self-contained x64 CryoUtils build and packages it with Inno Setup.
.DESCRIPTION
    Reads the version from VERSION, runs dotnet publish into publish\win-x64,
    locates Inno Setup 6 (installing it with winget when absent), compiles
    Installer\Output\CryoUtils-Setup-<version>.exe, and packages the portable
    release\CryoUtils-<version>-win-x64.zip.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = $PSScriptRoot
$versionFile = Join-Path $repoRoot 'VERSION'
$projectFile = Join-Path $repoRoot 'CryoUtils.csproj'
$issPath = Join-Path $repoRoot 'Installer\CryoUtils.iss'
$publishDir = Join-Path $repoRoot 'publish\win-x64'

Write-Host 'CryoUtils release build' -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) {
    throw "VERSION file not found: $versionFile"
}

$Version = (Get-Content -LiteralPath $versionFile -Raw).Trim()
if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "VERSION file is empty: $versionFile"
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "VERSION must contain exactly three numeric parts, for example 0.0.100."
}

if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
    throw "Project file not found: $projectFile"
}
if (-not (Test-Path -LiteralPath $issPath -PathType Leaf)) {
    throw "Inno Setup script not found: $issPath"
}
foreach ($required in @('LICENSE', 'NOTICE')) {
    $requiredPath = Join-Path $repoRoot $required
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required release file not found: $requiredPath"
    }
}

$outputExe = Join-Path $repoRoot "Installer\Output\CryoUtils-Setup-$Version.exe"

Write-Host "Version: $Version"

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

Write-Host 'Publishing self-contained win-x64 build...'
& dotnet publish $projectFile -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedExe = Join-Path $publishDir 'CryoUtils.exe'
if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
    throw "Published application not found: $publishedExe"
}

function Find-Iscc {
    $candidates = @()

    $programFilesX86 = ${env:ProgramFiles(x86)}
    if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
        $candidates += Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe'
    }

    $programFiles = $env:ProgramFiles
    if (-not [string]::IsNullOrWhiteSpace($programFiles)) {
        $candidates += Join-Path $programFiles 'Inno Setup 6\ISCC.exe'
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    return $null
}

$iscc = Find-Iscc
if (-not $iscc) {
    Write-Host 'Inno Setup 6 not found. Installing with winget...' -ForegroundColor Yellow
    $winget = Get-Command 'winget.exe' -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw 'Inno Setup 6 is not installed and winget is unavailable. Install Inno Setup 6.3 or newer from https://jrsoftware.org/isdl.php and rerun this script.'
    }

    & $winget.Source install -e --id JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "winget failed to install Inno Setup 6 (exit code $LASTEXITCODE)."
    }

    $iscc = Find-Iscc
    if (-not $iscc) {
        throw 'Inno Setup 6 was installed but ISCC.exe could not be located. Open a new terminal and rerun this script.'
    }
}

Write-Host "Using Inno Setup compiler: $iscc"
$isccDir = Split-Path -Parent $iscc
$isccVersionCandidate = @(
    (Get-Item -LiteralPath $iscc).VersionInfo.ProductVersion,
    $(if (Test-Path -LiteralPath (Join-Path $isccDir 'unins000.exe')) { (Get-Item -LiteralPath (Join-Path $isccDir 'unins000.exe')).VersionInfo.ProductVersion })
) | Where-Object { $_ -and $_ -match '^(\d+)\.(\d+)\.(\d+)' -and "$($Matches[1]).$($Matches[2]).$($Matches[3])" -ne '0.0.0' } | Select-Object -First 1
if ($isccVersionCandidate -and $isccVersionCandidate -match '^(\d+)\.(\d+)\.(\d+)') {
    $isccVer = [version]"$($Matches[1]).$($Matches[2]).$($Matches[3])"
    if ($isccVer -lt [version]'6.3.0') {
        throw "Inno Setup 6.3.0 or newer is required for x64compatible architecture support (found $isccVer)."
    }
    Write-Host "Inno Setup version: $isccVer"
}

Write-Host 'Compiling installer...'
& $iscc "/DAppVersion=$Version" $issPath
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $outputExe -PathType Leaf)) {
    throw "Installer was not created: $outputExe"
}

$sizeMb = [math]::Round((Get-Item -LiteralPath $outputExe).Length / 1MB, 2)
Write-Host "Installer created: $outputExe ($sizeMb MB)" -ForegroundColor Green

$setupHash = (Get-FileHash -LiteralPath $outputExe -Algorithm SHA256).Hash.ToLowerInvariant()
"$setupHash  $(Split-Path -Leaf $outputExe)" | Set-Content -LiteralPath "$outputExe.sha256" -Encoding ascii

$releaseDir = Join-Path $repoRoot 'release'
$portableZip = Join-Path $releaseDir "CryoUtils-$Version-win-x64.zip"
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

Write-Host 'Creating portable zip...'
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $portableZip -Force
if (-not (Test-Path -LiteralPath $portableZip -PathType Leaf)) {
    throw "Portable archive was not created: $portableZip"
}

$zipHash = (Get-FileHash -LiteralPath $portableZip -Algorithm SHA256).Hash.ToLowerInvariant()
"$zipHash  $(Split-Path -Leaf $portableZip)" | Set-Content -LiteralPath "$portableZip.sha256" -Encoding ascii

$zipSizeMb = [math]::Round((Get-Item -LiteralPath $portableZip).Length / 1MB, 2)
Write-Host "Portable zip created: $portableZip ($zipSizeMb MB)" -ForegroundColor Green
