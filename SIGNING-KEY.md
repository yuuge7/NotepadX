# Signing key

NotepadX releases are signed with a self-signed code-signing certificate. Signing does not
remove the SmartScreen prompt (only a paid EV/OV certificate from a public CA does), but it
does give every build a stable publisher identity, proves the binary was not modified after
you built it, and lets anyone verify a download really came from you.

**The key is the identity of the app.** Replace it and every future release looks like it
came from a different publisher.

---

## Files this key produced

Created by `tools\new-signing-key.ps1` in the repository root:

| File | Contains | Committed? |
| --- | --- | --- |
| `NotepadX-CodeSigning.pfx` | Private key + certificate | **Never** — in `.gitignore` |
| `NotepadX-CodeSigning.password.txt` | The `.pfx` password | **Never** — in `.gitignore` |
| `NotepadX-CodeSigning.pfx.base64.txt` | Base64 of the `.pfx`, for the GitHub secret | **Never** — in `.gitignore` |
| `NotepadX-CodeSigning.cer` | Public certificate only | Not committed by default; safe to publish if you want users to be able to install it as trusted |

The certificate also lives in the Windows certificate store at
`Cert:\CurrentUser\My`, which is what makes local signing work without pointing at the file.

---

## Back it up before anything else

Losing the `.pfx` means you cannot sign a continuation of the same identity ever again.

1. Copy `NotepadX-CodeSigning.pfx` **and** the password to somewhere durable and private —
   a password manager entry with a file attachment is ideal. Two locations, not one.
2. Do not put it in the repository, in a public cloud folder, or in a chat message.
3. Record the thumbprint alongside it so you can confirm you restored the right key:

```powershell
Get-PfxCertificate -FilePath .\NotepadX-CodeSigning.pfx | Select-Object Thumbprint, Subject, NotAfter
```

---

## Using the key on another machine

Copy `NotepadX-CodeSigning.pfx` to the new machine, then:

```powershell
# Import into the current user's personal store, key marked exportable so it can move again
$pw = Read-Host 'pfx password' -AsSecureString
Import-PfxCertificate -FilePath .\NotepadX-CodeSigning.pfx `
                      -CertStoreLocation Cert:\CurrentUser\My `
                      -Password $pw `
                      -Exportable
```

Confirm it landed:

```powershell
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Format-List Subject, Thumbprint, NotAfter
```

Then sign a build the same way as anywhere else:

```powershell
.\build.ps1 -Sign
```

`build.ps1 -Sign` looks for `NotepadX-CodeSigning.pfx` in the repository root first, and
falls back to the code-signing certificate in `Cert:\CurrentUser\My`.

### Regenerating the base64 for CI

If you no longer have `NotepadX-CodeSigning.pfx.base64.txt`:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('NotepadX-CodeSigning.pfx')) |
    Set-Content NotepadX-CodeSigning.pfx.base64.txt -NoNewline
```

---

## Wiring it into GitHub Actions

The release workflow signs automatically when both secrets exist, and skips signing
silently when they do not — so a fork with no key still produces working builds.

In the repository: **Settings → Secrets and variables → Actions → New repository secret**

| Secret name | Value |
| --- | --- |
| `SIGNING_CERT_BASE64` | Entire contents of `NotepadX-CodeSigning.pfx.base64.txt` |
| `SIGNING_CERT_PASSWORD` | Contents of `NotepadX-CodeSigning.password.txt` |

The workflow writes the decoded `.pfx` to a temporary file, signs, and deletes it in a step
that runs even if signing fails.

---

## Verifying a signature

Anyone can check a downloaded build:

```powershell
Get-AuthenticodeSignature .\NotepadX.exe | Format-List Status, SignerCertificate
```

`Status` reads `UnknownError` for a self-signed certificate the machine does not trust yet —
that is expected and still tells you the file is intact and who signed it. Compare
`SignerCertificate.Thumbprint` against the published thumbprint.

To make Windows report `Valid` on your own machines, install the public certificate into
Trusted Publishers (requires administrator, and only do this for a key you control):

```powershell
Import-Certificate -FilePath .\NotepadX-CodeSigning.cer `
                   -CertStoreLocation Cert:\LocalMachine\TrustedPublisher
```

---

## Renewing before expiry

The certificate is valid for ten years from creation. To check:

```powershell
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Select-Object Subject, NotAfter
```

Signatures that were timestamped at signing time — the workflow and `build.ps1 -Sign` both
timestamp — stay valid after the certificate expires. Only new signatures need a new key.
