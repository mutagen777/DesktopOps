# Admin authentication test checklist

Windows Negotiate + AD group policies for `DesktopOps.Admin`.

## Configure

In `src/DesktopOps.Admin/appsettings.json` (or environment-specific override):

```json
"Security": {
  "Enabled": true,
  "ADGroup": "DOMAIN\\DesktopOps-Managers",
  "DeveloperADGroup": "DOMAIN\\DesktopOps-Developers"
}
```

Both group names are required when `Enabled` is `true`. They must match the **role claim** Windows issues for that group (compare with `whoami /groups` and the sidebar debug block).

Local demo keeps `"Enabled": false` — policies always succeed and the sidebar shows `Auth aus`.

## Checklist

### Setup

- [ ] Create (or reuse) two AD security groups: managers and developers
- [ ] Put a **manager-only** account in `ADGroup` only
- [ ] Put a **developer** account in `DeveloperADGroup` (optionally also in managers)
- [ ] Set `Security.Enabled` to `true` and paste the exact group names
- [ ] Restart Admin

### Authentication

- [ ] Open Admin (prefer `https://localhost:7239` or IIS Express with Windows auth)
- [ ] Browser runs under the Windows test account (Edge/Chrome)
- [ ] Sidebar shows the Windows user name (not `Auth aus`)
- [ ] Sidebar **Rollen** list includes the configured group string(s), or you adjust config to match a listed claim

### Authorization matrix

| Page | Manager only | Developer |
|------|--------------|-----------|
| Übersicht | allowed | allowed |
| Gruppen | allowed | allowed |
| Zuweisungen | allowed | allowed |
| Rollouts | allowed | allowed |
| Programme | denied | allowed |
| Releases | denied | allowed |

- [ ] Manager-only: open `/programs` and `/releases` → “Keine Berechtigung…”
- [ ] Developer: create/upload/publish still works

### Negative cases

- [ ] Wrong / empty group name with `Enabled: true` → start fails if empty; wrong name → authenticated but denied
- [ ] User in neither group → denied on all gated pages
- [ ] Agent / `DesktopOps.Server` APIs still work without Windows login (Admin-only gate)

### Hosting notes

- **Kestrel** (`dotnet run`): Negotiate works on Windows for localhost; first visit may prompt once
- **IIS Express / IIS**: enable Windows Authentication, disable anonymous — closest to production
- Group claim format often looks like `DOMAIN\GroupName` or the group’s short name — copy from the sidebar, do not guess

## Sidebar debug

With `Security.Enabled = true`, the Admin footer shows:

- current Windows identity
- configured `ADGroup` / `DeveloperADGroup`
- role claims on the principal (truncated list)

Use that to align config with what Negotiate actually emits.
