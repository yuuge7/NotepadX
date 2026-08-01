# Creates the self-signed code-signing certificate used to sign NotepadX releases.
#
# Run this ONCE. The resulting .pfx is the identity of every build you ship — if you lose
# it, users get a different publisher on the next release and Windows treats the app as a
# brand new unknown program. Back it up (see SIGNING-KEY.md).
#
#   .\tools\new-signing-key.ps1
#   .\tools\new-signing-key.ps1 -Subject "CN=Your Name" -Years 10 -Force

param(
    [string]$Subject = 'CN=NotepadX',
    [int]$Years = 10,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$pfx = Join-Path $root 'NotepadX-CodeSigning.pfx'
$cer = Join-Path $root 'NotepadX-CodeSigning.cer'
$b64 = Join-Path $root 'NotepadX-CodeSigning.pfx.base64.txt'
$pwdFile = Join-Path $root 'NotepadX-CodeSigning.password.txt'

if ((Test-Path $pfx) -and -not $Force) {
    Write-Host "A key already exists at $pfx" -ForegroundColor Yellow
    Write-Host "Overwriting it means future releases are signed by a different publisher." -ForegroundColor Yellow
    Write-Host "Re-run with -Force only if you really mean to replace it." -ForegroundColor Yellow
    return
}

# 24 random characters, no ambiguous glyphs
$alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
$bytes = New-Object byte[] 24
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$password = -join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
$secure = ConvertTo-SecureString -String $password -AsPlainText -Force

Write-Host "creating certificate $Subject" -ForegroundColor Cyan
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -KeyExportPolicy Exportable `
    -KeyUsage DigitalSignature `
    -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddYears($Years)

Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $secure | Out-Null
Export-Certificate  -Cert $cert -FilePath $cer | Out-Null

[IO.File]::WriteAllText($b64, [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfx)))
[IO.File]::WriteAllText($pwdFile, $password)

Write-Host ''
Write-Host 'created:' -ForegroundColor Green
Write-Host "  $pfx        private key + certificate (NEVER commit)"
Write-Host "  $cer        public certificate only (safe to share)"
Write-Host "  $b64        base64 of the .pfx, for the GitHub secret"
Write-Host "  $pwdFile    the password (NEVER commit)"
Write-Host ''
Write-Host "thumbprint: $($cert.Thumbprint)" -ForegroundColor Cyan
Write-Host "password:   $password" -ForegroundColor Cyan
Write-Host ''
Write-Host 'Back these up now. Read SIGNING-KEY.md for moving the key to another machine.' -ForegroundColor Yellow
