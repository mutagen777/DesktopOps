# Package integrity and signing

## What ships today

Every release ZIP is hashed (**SHA-256**) on upload. Clients verify the hash before install. Failed checks are reported on **Rollouts**.

That protects against bit-flip / tampering **after** the package is stored, assuming the Server and API key are trusted.

## Authenticode (recommended for sale)

Sign customer-facing binaries before distribution:

| Artifact | Sign with |
|----------|-----------|
| `DesktopOps.Agent.exe` / Velopack `Setup.exe` | Authenticode code-signing certificate |
| Optional: published program EXEs inside ZIPs | Same org certificate |

Example (after `pack-agent.ps1`):

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a `
  .\artifacts\agent-releases\Setup.exe
```

Velopack also supports `--signTemplate` during `vpk pack` — see [Agent installer](agent-installer.md).

## Future: signed release packages

Not implemented yet. A later hardening step can add:

1. Detached signature (e.g. `.sig` / CMS) next to the ZIP
2. Admin upload of signature or automatic signing on Server with a HSM/cert
3. Agent verification of signature **in addition to** SHA-256

Until then: treat Server authenticity (HTTPS + API key + locked-down Admin AD) as the trust root.

## Support / SLA story (product)

Ship a short commercial appendix (edit for your company):

| Tier | Response | Coverage |
|------|----------|----------|
| Standard | Next business day | Self-hosted install, Agent updates, Admin usage |
| Priority | Same business day | + production incidents, health probes |
| Critical | 4h (business hours) | + emergency hotfix guidance |

Include:

- Supported OS: Windows 10/11 (Agent), Windows Server 2019+ (Server/Admin)
- Supported DB: SQL Server (production), SQLite (lab only)
- How to open a ticket + attach Agent **Diagnose exportieren** ZIP
- Explicit exclusions: customer network, AD misconfiguration, unsigned third-party payloads

Template wording lives in [Commercial support](commercial-support.md).
