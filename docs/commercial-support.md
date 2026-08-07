# Commercial support (template)

> **Not legal advice.** Adapt names, SLAs, and jurisdiction with your counsel before using in a contract.

## Scope

DesktopOps is licensed for **self-hosted, single-tenant** use at the Customer’s site (or Customer-controlled cloud).

### SKUs (seats)

| SKU | Seats | Notes |
|-----|-------|--------|
| Community | 3 | Free; soft overage warning |
| Team 25 / 50 / 100 | Pack size | Signed offline license |
| Enterprise | Custom / unlimited | + support hours |

Seat = unique assigned person; unlimited programs per person. See [Licensing](licensing.md).

Support covers:

- Installation guidance for Server, Admin, and Agent (Velopack)
- Configuration of API key, Admin AD groups, SQL Server, HTTPS
- Defects in the unmodified DesktopOps software
- Guidance using diagnostics export from the Agent

Support does **not** cover:

- Customer Active Directory / network / certificate PKI issues
- Custom forks or third-party modifications
- Content of customer application packages (malware, licensing of those apps)
- Guaranteed uptime of Customer infrastructure

## Severity

| Severity | Meaning | Target first response |
|----------|---------|------------------------|
| S1 | Production Agent fleet cannot update; data loss risk | 4 business hours |
| S2 | Major feature broken; workaround exists | 1 business day |
| S3 | Minor defect / how-to | 2 business days |

## Customer responsibilities

- Run Production with [Production hardening](production-hardening.md) completed
- Keep API keys and AD groups confidential
- Provide logs / diagnostics ZIP and environment details
- Apply updates / hotfixes within an agreed window

## Contact

- Email: `support@example.com`
- Hours: Mon–Fri 09:00–17:00 (Europe/Berlin), excluding public holidays

## Diagnostics

Ask customers to use Agent tray → **Export diagnostics** / **Diagnose exportieren** and attach the ZIP. See [Support bundle](support-bundle.md).
