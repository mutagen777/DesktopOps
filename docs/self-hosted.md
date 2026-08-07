# Self-hosted deployment (single tenant)

DesktopOps is sold and operated as **one deployment per customer organization** — not multi-tenant SaaS.

## Model

```text
[Customer AD / Windows users]
        │
        ▼
[DesktopOps.Admin] ──┐
                     ├──▶ [SQL Server + package storage]
[DesktopOps.Agent] ──┘         ▲
        │                      │
        └──── HTTPS + API key ─┘
              (DesktopOps.Server)
```

- **No shared database** across customers
- **No tenant IDs** in the schema
- Isolation = separate VM/app service + DB + storage + secrets

## Why not multi-tenant (yet)

Multi-tenant would need tenant-scoped programs/groups/packages, per-tenant keys, stronger authn/authz, and billing. That is intentionally out of MVP.

## Customer deliverables

1. Server + Admin binaries (or container/IIS site)
2. Agent `Setup.exe` (Velopack) — [Agent installer](agent-installer.md)
3. Production config checklist — [Production hardening](production-hardening.md)
4. Optional: support tier — [Commercial support](commercial-support.md)

## Ops checklist per customer

- [ ] Dedicated SQL database `DesktopOps`
- [ ] Dedicated `Storage:RootPath`
- [ ] Unique `Security:ApiKey`
- [ ] Customer AD groups for Admin Manager/Developer
- [ ] TLS certificate for Server/Admin hostnames
- [ ] Backup job (`tools/backup-desktopops.ps1`)
- [ ] Monitor `GET /health/ready`
- [ ] Document Agent install path for IT
