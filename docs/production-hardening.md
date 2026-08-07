# Production hardening

Checklist before selling or deploying DesktopOps to a real customer (self-hosted, single tenant).

## 1. Environment

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
```

Production templates:

- `src/DesktopOps.Server/appsettings.Production.json`
- `src/DesktopOps.Admin/appsettings.Production.json`

Prefer secrets via environment variables (do not commit real keys):

| Setting | Env var |
|---------|---------|
| API key | `Security__ApiKey` (agent) |
| Admin API key | `Security__AdminApiKey` (management) |
| SQL connection | `ConnectionStrings__DesktopOps` |
| Package storage | `Storage__RootPath` |
| Admin AD groups | `Security__ADGroup`, `Security__DeveloperADGroup` |
| Entra Graph sync (optional) | `DirectorySync__Entra__TenantId`, `DirectorySync__Entra__ClientId`, `DirectorySync__Entra__ClientSecret` |
| Package CMS signing (optional) | `Signing__CertificateThumbprint`, Agent `Agent__TrustedCmsThumbprints` |

In **Production**, the apps refuse to start when:

- Server: `Security:ApiKey` / `Security:AdminApiKey` missing, too short (&lt;32), placeholder-like, or identical; or connection string still looks like the local demo DB
- Admin: `Security:Enabled` is false / AD groups missing, or demo connection string

## 2. Authentication

| Surface | Required setting |
|---------|------------------|
| Agent / Updates API | Server `Security:ApiKey` + matching Agent `Agent:ApiKey` |
| Admin UI | `Security:Enabled=true` + Manager/Developer AD groups |

See [Admin authentication](admin-auth.md) and [Server API](server-api.md).

## 3. HTTPS

- Terminate TLS at IIS / reverse proxy, or bind Kestrel to a certificate
- Keep `UseHttpsRedirection` (already enabled)
- Set `AllowedHosts` to the real host names (see Production templates)

Example Kestrel (optional in `appsettings.Production.json`):

```json
"Kestrel": {
  "Endpoints": {
    "Https": {
      "Url": "https://0.0.0.0:443",
      "Certificate": {
        "Path": "certs/desktopops.pfx",
        "Password": "via-env-or-secret-store"
      }
    }
  }
}
```

## 4. SQL Server (recommended)

```json
"Database": { "Provider": "SqlServer" },
"ConnectionStrings": {
  "DesktopOps": "Server=...;Database=DesktopOps;Trusted_Connection=True;TrustServerCertificate=True"
}
```

SQLite remains fine for labs only. Point **Admin and Server** at the same database and `Storage:RootPath`.

## 5. Backup

Use `tools/backup-desktopops.ps1`:

```powershell
.\tools\backup-desktopops.ps1 `
  -StorageRoot "D:\DesktopOps\storage" `
  -SqliteDbPath "" `
  -BackupRoot "D:\DesktopOps\backups" `
  -SqlServerConnectionString "Server=...;Database=DesktopOps;Trusted_Connection=True;TrustServerCertificate=True"
```

Schedule daily (Task Scheduler). Keep package storage and DB backups aligned (same timestamp folder).

## 6. Monitoring

Server health (no API key required):

| URL | Meaning |
|-----|---------|
| `GET /health` | Process up (liveness, no dependency checks) |
| `GET /health/ready` | Database + package storage writable |
| `GET /` | Product info + `apiKeyRequired` |

Probe `/health/ready` from your monitoring stack (or a simple scheduled `Invoke-WebRequest`).

Serilog already writes console logs on the Server — ship them to your SIEM/file collector in production.

## 7. Smoke test

1. Start Server + Admin with `ASPNETCORE_ENVIRONMENT=Production` and Production config.
2. Confirm Admin requires Windows auth / AD groups.
3. Confirm Agent without API key gets `401`.
4. Confirm Agent with API key can register and download.
5. Confirm `/health/ready` returns Healthy.
6. Run a backup once and restore to a lab instance.

## Out of scope for this checklist

Detached CMS package signing, commercial contracts, and multi-tenant SaaS are covered in:

- [Package signing](package-signing.md)
- [Commercial support](commercial-support.md)
- [Self-hosted deployment](self-hosted.md)
- [Legal notices](legal-notices.md)
