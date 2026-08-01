# Builds a self-contained NotepadX that runs on Windows 10 (1809+) and Windows 11
# with no .NET runtime installed on the target machine.
#
#   .\build.ps1                    -> single-file exe in .\dist\x64
#   .\build.ps1 -Arch arm64        -> ARM64 build
#   .\build.ps1 -Framework         -> small build that needs the .NET 10 Desktop Runtime
#   .\build.ps1 -Portable          -> also drops the marker that keeps data next to the exe
#   .\build.ps1 -Sign              -> Authenticode-sign the result (see SIGNING-KEY.md)
#   .\build.ps1 -Version 1.4       -> stamp a version into the binary

[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86', 'arm64')][string]$Arch = 'x64',
    [switch]$Framework,
    [switch]$Portable,
    [switch]$Sign,
    [string]$Version,
    [string]$PfxPath,
    [string]$PfxPassword
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$rid = "win-$Arch"
$out = Join-Path $PSScriptRoot "dist\$Arch"

if (-not (Test-Path 'Assets\app.ico')) {
    powershell -ExecutionPolicy Bypass -File 'tools\make-icon.ps1'
}

$publishArgs = @(
    'publish', 'NotepadX.csproj',
    '-c', 'Release',
    '-r', $rid,
    '-o', $out,
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=none',
    '--nologo'
)

if ($Version) {
    if ($Version -notmatch '^\d+\.\d+(\.\d+)?$') { throw "Version must look like 1.4 or 1.4.0, got '$Version'" }
    $full = if ($Version -match '^\d+\.\d+$') { "$Version.0" } else { $Version }
    $publishArgs += "-p:Version=$full"
    $publishArgs += "-p:FileVersion=$full.0"
    $publishArgs += "-p:InformationalVersion=$full"
}

if ($Framework) { $publishArgs += '--self-contained:false' } else { $publishArgs += '--self-contained:true' }

Write-Host "publishing $rid -> $out" -ForegroundColor Cyan
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $out 'NotepadX.exe'

if ($Portable) {
    Set-Content -Path (Join-Path $out 'NotepadX.portable') -Value '' -Encoding utf8
    Write-Host 'portable marker written: settings and recovery data stay in .\Data' -ForegroundColor Yellow
}

if ($Sign) {
    # Prefer the .pfx sitting in the repo root; fall back to the certificate store.
    if (-not $PfxPath) { $PfxPath = Join-Path $PSScriptRoot 'NotepadX-CodeSigning.pfx' }

    $cert = $null
    if (Test-Path $PfxPath) {
        if (-not $PfxPassword) {
            $pwdFile = Join-Path $PSScriptRoot 'NotepadX-CodeSigning.password.txt'
            if (Test-Path $pwdFile) { $PfxPassword = (Get-Content $pwdFile -Raw).Trim() }
        }
        if (-not $PfxPassword) { $PfxPassword = Read-Host 'pfx password' }

        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
            $PfxPath, $PfxPassword,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    } else {
        $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Select-Object -First 1
        if (-not $cert) { throw "No signing key found. Run tools\new-signing-key.ps1 or see SIGNING-KEY.md." }
    }

    Write-Host "signing with $($cert.Subject) [$($cert.Thumbprint)]" -ForegroundColor Cyan
    $result = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert `
        -HashAlgorithm SHA256 `
        -TimestampServer 'http://timestamp.digicert.com'

    if ($result.Status -eq 'HashMismatch' -or $result.Status -eq 'NotSigned') {
        throw "signing failed: $($result.Status) $($result.StatusMessage)"
    }
    Write-Host "signature: $($result.Status)" -ForegroundColor Green
}

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
Set-Content -Path "$exe.sha256" -Value "$hash  NotepadX.exe" -Encoding ascii

$size = [Math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "done: $exe ($size MB)" -ForegroundColor Green
Write-Host "sha256: $hash"
