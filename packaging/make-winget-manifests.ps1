# Generates the three winget manifest files for a release.
#
#   .\packaging\make-winget-manifests.ps1 -Version 1.0 -Owner yourname -Repo notepadx
#
# The MSI is used rather than the bare exe so winget gets real install and uninstall
# integration, and so the package appears in Apps & features.
#
# Output lands in dist\winget\<version>\ ready to be copied into a fork of
# microsoft/winget-pkgs under manifests\<letter>\<Publisher>\<Package>\<version>\.

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Owner = 'yuuge7',
    [string]$Repo = 'NotepadX',
    [string]$Publisher = 'yuuge7',
    [string]$MsiPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if ($Version -notmatch '^\d+\.\d+(\.\d+)?$') { throw "Version must look like 1.4, got '$Version'" }
$fullVersion = if ($Version -match '^\d+\.\d+$') { "$Version.0" } else { $Version }

if (-not $MsiPath) { $MsiPath = Join-Path $root "dist\x64\NotepadX-v$Version-win-x64.msi" }
if (-not (Test-Path $MsiPath)) { throw "No MSI at $MsiPath. Run packaging\build-msi.ps1 first." }

$sha = (Get-FileHash $MsiPath -Algorithm SHA256).Hash

# winget matches installed packages by ProductCode, and WiX mints a fresh one per build,
# so it has to be read back out of the package rather than hard-coded.
function Get-MsiProductCode([string]$path) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.GetType().InvokeMember(
        'OpenDatabase', 'InvokeMethod', $null, $installer, @($path, 0))
    $view = $database.GetType().InvokeMember(
        'OpenView', 'InvokeMethod', $null, $database,
        @("SELECT Value FROM Property WHERE Property = 'ProductCode'"))
    $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
    $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
    $code = $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, 1)
    $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null) | Out-Null
    return $code
}

$productCode = Get-MsiProductCode (Resolve-Path $MsiPath).Path
$packageId = "$Publisher.NotepadX"
$url = "https://github.com/$Owner/$Repo/releases/download/v$Version/$(Split-Path $MsiPath -Leaf)"
$outDir = Join-Path $root "dist\winget\$Version"
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

$manifestVersion = '1.6.0'

@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$manifestVersion.schema.json
PackageIdentifier: $packageId
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $manifestVersion
"@ | Set-Content (Join-Path $outDir "$packageId.yaml") -Encoding utf8

@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$manifestVersion.schema.json
PackageIdentifier: $packageId
PackageVersion: $Version
PackageLocale: en-US
Publisher: $Publisher
PublisherUrl: https://github.com/$Owner
PublisherSupportUrl: https://github.com/$Owner/$Repo/issues
PackageName: NotepadX
PackageUrl: https://github.com/$Owner/$Repo
License: MIT
LicenseUrl: https://github.com/$Owner/$Repo/blob/main/LICENSE
ShortDescription: The Windows 11 Notepad experience on Windows 10 too, without the AI features.
Description: |-
  A text editor with tabs, dark mode, session restore and an inline find and replace bar,
  running the same on Windows 10 and Windows 11. Offline by design: no account, no
  telemetry, no network calls and no AI features. Ships as a single executable that needs
  no .NET runtime installed.
Moniker: notepadx
Tags:
- editor
- notepad
- text
- text-editor
- offline
ReleaseNotesUrl: https://github.com/$Owner/$Repo/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $manifestVersion
"@ | Set-Content (Join-Path $outDir "$packageId.locale.en-US.yaml") -Encoding utf8

@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$manifestVersion.schema.json
PackageIdentifier: $packageId
PackageVersion: $Version
MinimumOSVersion: 10.0.17763.0
InstallerType: wix
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
ProductCode: '$productCode'
FileExtensions:
- txt
- log
- md
- ini
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: $manifestVersion
"@ | Set-Content (Join-Path $outDir "$packageId.installer.yaml") -Encoding utf8

Write-Host "wrote manifests to $outDir" -ForegroundColor Green
Get-ChildItem $outDir | ForEach-Object { "  $($_.Name)" }
Write-Host ""
Write-Host "ProductCode: $productCode"
Write-Host "SHA256:      $sha"
Write-Host ""
Write-Host "Validate locally with:  winget validate --manifest `"$outDir`"" -ForegroundColor Cyan
