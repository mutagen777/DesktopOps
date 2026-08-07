# Legal notices (template)

> **Not legal advice.** Have a lawyer review before offering DesktopOps commercially. This file is a checklist and placeholder wording only.

## Software license

The repository ships under the **MIT License** (`LICENSE`). MIT allows commercial sale of copies and services, and also allows others to use the code freely if they receive it.

If you need a proprietary commercial license (no MIT redistribution by the customer’s competitors, escrow, etc.), replace or dual-license **before** first sale and keep customer contracts aligned.

## Typical contract pack

| Document | Purpose |
|----------|---------|
| License / EULA | What the customer may install and copy |
| Support / SLA | Response times — see [Commercial support](commercial-support.md) |
| DPA (AVV) | Processing of personal data if you host or access their systems |
| Order form | Scope, price, term, named environments |

## GDPR / DSGVO (self-hosted)

When the **customer** hosts DesktopOps:

- Customer is typically **controller** for usernames, SIDs, machine names, rollout events
- You are **processor** only if you operate the system or can access production data (managed service)
- Minimize data: avoid storing unnecessary PII in release notes / diagnostics
- Document retention for deployment events and logs

When you offer **hosted** DesktopOps for a customer, execute a DPA and define sub-processors (cloud, email, monitoring).

## Liability (product note)

MIT disclaims warranty. Commercial contracts usually add:

- Cap on liability (e.g. fees paid in last 12 months)
- Exclusion of indirect damages
- Customer responsibility for backups and AD configuration

## Hosting / operations

If you host:

- State region (e.g. EU) and backup locations
- Incident notification process
- Exit: DB + storage export within N days after contract end

## Checklist before first invoice

- [ ] License model decided (MIT-as-is vs commercial EULA) — product seats: [Licensing](licensing.md)
- [ ] Support tiers priced — [Commercial support](commercial-support.md)
- [ ] Signing key pair replaced for production licenses (`LicensingPublicKeys` + private key offline)
- [ ] DPA template ready if you touch personal data
- [ ] Production hardening verified at customer — [Production hardening](production-hardening.md)
- [ ] Signing process for Agent installer — [Package signing](package-signing.md)
