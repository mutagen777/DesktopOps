# DesktopOps licensing

## Product model

| Tier | Seats | Programs per user |
|------|-------|-------------------|
| **Community** (default) | 3 | Unlimited |
| **Team** | As licensed (e.g. 25 / 50 / 100) | Unlimited |
| **Enterprise** | Custom / unlimited (`maxSeats: -1`) | Unlimited |

A **seat** is a unique person (`UserGroupMember`) in a group that has at least one **program assignment**.  
Machines, clients, and number of programs per person do **not** count.

## Enforcement

| Situation | Behavior |
|-----------|----------|
| Community over 3 seats | Soft warning in Admin; product keeps working |
| Paid license expired or signature invalid | Hard block: no new assignments, no publish (existing installs continue) |
| No license file | Community defaults |

Offline only — no phone-home. License JSON is stored in the shared database (`LicenseStates`).

## Install a license (Admin)

1. Open **License** in Admin (Developer role).
2. Upload the signed `.json` / `.lic` file.
3. Status shows tier, seats used/max, customer, expiry.

Revert to Community with **Revert to Community**.

## License file format

Signed JSON envelope:

```json
{
  "payload": {
    "licenseId": "…",
    "tier": "team",
    "maxSeats": 25,
    "customer": "Contoso GmbH",
    "validUntilUtc": "2027-12-31T23:59:59Z"
  },
  "signature": "<base64 RSA-SHA256>"
}
```

- Omit `validUntilUtc` for perpetual licenses.
- `maxSeats: -1` or `null` = unlimited (Enterprise).
- Signature is RSA-SHA256 (PKCS#1) over canonical camelCase JSON of `payload`.

Public key is embedded in `DesktopOps.Licensing` (`LicensingPublicKeys.DefaultRsaPublicKeyPem`).  
**Replace the key pair before issuing production licenses.**

## Issue a license (vendor)

```powershell
# Private key must exist (gitignored):
#   tools/licensing/dev-private.pem

.\tools\issue-license.ps1 `
  -Tier team `
  -MaxSeats 25 `
  -Customer "Contoso GmbH" `
  -ValidUntil "2027-12-31" `
  -OutFile ".\contoso-team-25.lic.json"
```

Generate a new key pair (updates public key file; paste public PEM into `LicensingPublicKeys.cs`):

```powershell
.\tools\issue-license.ps1 -InitKeys
```

## Legal

Community and paid seats assume a commercial EULA (or dual-license). The repo may still ship under MIT until counsel replaces it — see [Legal notices](legal-notices.md).
