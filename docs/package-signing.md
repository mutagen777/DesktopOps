# Package integrity and signing

## What ships today

Every release ZIP is hashed (**SHA-256**) on upload. Clients verify the hash before install. Failed checks are reported on **Rollouts**.

That protects against bit-flip / tampering **after** the package is stored, assuming the Server and API key are trusted.

## Authenticode (Agent installer)

Sign the Velopack build with your code-signing certificate:

```powershell
# Certificate must be in the Windows cert store (CurrentUser or LocalMachine\My)
.\tools\pack-agent.ps1 -Version 0.3.1 -CertThumbprint "YOURTHUMBPRINT"

# Or sign arbitrary files after the fact
.\tools\sign-file.ps1 -CertThumbprint "YOURTHUMBPRINT" -Path .\artifacts\agent-releases\Setup.exe
```

What `-CertThumbprint` does in `pack-agent.ps1`:

1. Signs `*.exe` in the publish folder before `vpk pack`
2. Passes Velopack `--signTemplate` so pack-produced binaries are signed
3. Re-signs `Setup.exe` / agent EXEs in the output folder

Requirements: Windows SDK **signtool**, certificate with private key, outbound access to the timestamp URL (default DigiCert).

| Artifact | Sign with |
|----------|-----------|
| `DesktopOps.Agent.exe` / Velopack `Setup.exe` | Authenticode (`pack-agent.ps1` / `sign-file.ps1`) |
| Optional: published program EXEs inside ZIPs | Same org certificate via `sign-file.ps1` |

## Future: signed release packages

Not implemented yet. A later hardening step can add:

1. Detached signature (e.g. `.sig` / CMS) next to the ZIP
2. Admin upload of signature or automatic signing on Server with a HSM/cert
3. Agent verification of signature **in addition to** SHA-256

Until then: treat Server authenticity (HTTPS + API key + locked-down Admin AD) as the trust root for program ZIPs.

## Support / SLA story (product)

See [Commercial support](commercial-support.md).
