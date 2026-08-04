# Builds the per-user MSI installer with the WiX toolset.
#
#   .\packaging\build-msi.ps1 -Version 1.0
#
# WiX is installed as a local dotnet tool on first run, so nothing has to be present
# on the machine beyond the .NET SDK.

param(
    [string]$Version = '1.0',
    [ValidateSet('x64', 'arm64')][string]$Arch = 'x64',
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if ($Version -notmatch '^\d+\.\d+(\.\d+)?$') { throw "Version must look like 1.4 or 1.4.0, got '$Version'" }
$msiVersion = if ($Version -match '^\d+\.\d+$') { "$Version.0" } else { $Version }

if (-not $ExePath) { $ExePath = Join-Path $root "dist\$Arch\NotepadX.exe" }
if (-not (Test-Path $ExePath)) { throw "No executable at $ExePath. Run .\build.ps1 first." }

# WiX wants an RTF licence; generate a minimal one from LICENSE so nothing extra is committed.
$licenseRtf = Join-Path $env:TEMP 'NotepadX-license.rtf'
$licenseText = (Get-Content (Join-Path $root 'LICENSE') -Raw) -replace '\\', '\\\\' -replace '([{}])', '\$1'
$licenseLines = $licenseText -split "`r?`n" | ForEach-Object { $_ + '\par' }
@"
{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}}
\f0\fs18
$($licenseLines -join "`r`n")
}
"@ | Set-Content -Path $licenseRtf -Encoding ascii

if (-not (Test-Path (Join-Path $root '.config\dotnet-tools.json'))) {
    Write-Host 'creating local tool manifest' -ForegroundColor Cyan
    dotnet new tool-manifest --force | Out-Null
}

# The extension version must track the toolset version exactly; leaving it unpinned
# resolves to a v7 package that a v5 toolset refuses to load.
$wixVersion = '5.0.2'

$hasWix = (dotnet tool list --local 2>$null | Select-String -Pattern '^\s*wix\s') -ne $null
if (-not $hasWix) {
    Write-Host 'installing the WiX toolset' -ForegroundColor Cyan
    dotnet tool install wix --local --version $wixVersion | Out-Null
}

dotnet tool restore | Out-Null

$installed = dotnet wix extension list -g 2>$null
foreach ($ext in 'WixToolset.UI.wixext', 'WixToolset.Util.wixext') {
    if (-not ($installed | Select-String -Pattern ([regex]::Escape($ext)))) {
        Write-Host "installing $ext" -ForegroundColor Cyan
        dotnet wix extension add -g "$ext/$wixVersion" | Out-Null
    }
}

$outDir = Join-Path $root "dist\$Arch"
$msi = Join-Path $outDir "NotepadX-v$Version-win-$Arch.msi"

Write-Host "building $msi" -ForegroundColor Cyan
dotnet wix build (Join-Path $PSScriptRoot 'wix\NotepadX.wxs') `
    -ext WixToolset.UI.wixext `
    -ext WixToolset.Util.wixext `
    -arch $Arch `
    -d "Version=$msiVersion" `
    -d "SourceExe=$ExePath" `
    -d "IconFile=$(Join-Path $root 'Assets\app.ico')" `
    -d "LicenseFile=$licenseRtf" `
    -o $msi

if ($LASTEXITCODE -ne 0) { throw "wix build failed with exit code $LASTEXITCODE" }

Remove-Item $licenseRtf -ErrorAction SilentlyContinue

$size = [Math]::Round((Get-Item $msi).Length / 1MB, 1)
Write-Host "done: $msi ($size MB)" -ForegroundColor Green
