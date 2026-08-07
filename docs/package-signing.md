# Package integrity and signing

## What ships today

Every release ZIP is hashed (**SHA-256**) on upload. Clients verify the hash before install. Failed checks are reported on **Rollouts**.

That protects against bit-flip / tampering **after** the package is stored, assuming the Server and API key are trusted.

Optionally, each ZIP can also carry a **detached CMS/PKCS#7** signature (`.p7s`). Agents verify the signature **in addition to** SHA-256 when a signature URL is returned.

## Authenticode (Agent installer)

Sign the Velopack build with your code-signing certificate:

```powershell
# Certificate must be in the Windows cert store (CurrentUser or LocalMachine\My)
.\tools\pack-agent.ps1 -Version 0.3.1 -CertThumbprint "YOURTHUMBPRINT"

# Or sign arbitrary files after the fact
.\tools\sign-file.ps1 -CertThumbprint "YOURTHUMBPRINT" -Path .\artifacts\agent-releases\Setup.exe
```

What `-CertThumbprint` does in `pack-agent.ps1`:

1. Cleans the publish folder, then publishes
2. Validates thumbprint (40 hex chars) and timestamp URL
3. Signs `*.exe` in the publish folder before `vpk pack`
4. Signs all `*.exe` under the Velopack output folder after pack

Velopack `--signTemplate` is **not** used (avoids `cmd.exe` injection). Prefer post-pack `signtool` signing.

Requirements: Windows SDK **signtool**, certificate with private key, outbound access to the timestamp URL (default DigiCert).

| Artifact | Sign with |
|----------|-----------|
| `DesktopOps.Agent.exe` / Velopack `Setup.exe` | Authenticode (`pack-agent.ps1` / `sign-file.ps1`) |
| Optional: published program EXEs inside ZIPs | Same org certificate via `sign-file.ps1` |
| Program ZIP packages | Detached CMS (below) |

## Detached CMS signatures (program ZIPs)

On-disk layout next to the ZIP:

- `{slug}/{version}.zip`
- `{slug}/{version}.zip.p7s` (detached PKCS#7)

`ReleasePackage.SignaturePath` stores the relative `.p7s` path (null = unsigned / legacy).

### Admin / Server upload

```json
"Signing": {
  "CertificateThumbprint": "YOUR40HEXTHUMBPRINT",
  "RequireSignature": false
}
```

- When `CertificateThumbprint` is set, Admin and `POST /api/releases` auto-sign the ZIP after save
- Or upload a pre-made `.p7s` (Admin file picker / form field `signature`) — **validated** as a detached CMS signature over the ZIP (and must match `CertificateThumbprint` when that is configured)
- When `RequireSignature` is true, unsigned uploads and **publish** (Admin UI and `POST /api/releases/{id}/publish`) are rejected
- Failed signature steps delete orphaned ZIP/`.p7s` artifacts from storage (best-effort)
- Keep Admin and Server `Signing:RequireSignature` aligned (Production templates both default to `true`)

Certificate must be in **CurrentUser\My** or **LocalMachine\My** with a private key (same store pattern as Authenticode).

### Agent verification

Assignment / update payloads include optional `signatureUrl` (`GET /api/packages/{id}/signature?clientId=…`).

```json
"Agent": {
  "TrustedCmsThumbprints": "AABBCC…,DDEEFF…",
  "RequirePackageCmsSignature": false
}
```

| Setting | Behavior |
|---------|----------|
| Signature present | Download `.p7s`, verify CMS over ZIP bytes after SHA-256 |
| `TrustedCmsThumbprints` set | Signer thumbprint must be in the allowlist; invalid/empty list fails |
| `TrustedCmsThumbprints` empty and CMS not required | Cryptographic CMS check only (any valid signer) |
| `RequirePackageCmsSignature=true` | Fail when unsigned **or** when `TrustedCmsThumbprints` is empty |

Failures report deployment status **Failed** with `Package signature verification failed.`

### API

| Method | Path | Notes |
|--------|------|-------|
| GET | `/api/packages/{id}` | ZIP (unchanged) |
| GET | `/api/packages/{id}/signature` | Detached `.p7s`; same agent `clientId` / assignment rules |

## Support / SLA story (product)

See [Commercial support](commercial-support.md).
