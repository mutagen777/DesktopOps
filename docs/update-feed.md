# Client update flow

## Embedded app (`DesktopOps.Updates`)

1. Configure `UpdateOptions` (`ServerUri`, `ProgramSlug`, `CurrentVersion`, `UserName`).
2. `RegisterClientAsync()`
3. `CheckForUpdatesAsync()` against `/api/clients/{id}/updates`
4. `PrepareUpdateAsync()` downloads the package and verifies `PackageHash` when present
5. Host app applies the package (restart / installer) and calls `ReportStatusAsync`

## Tray agent (`DesktopOps.Agent`)

1. Registers as `_agent_`
2. Loads all assignments via `/api/clients/{id}/assignments`
3. Compares with local `.dops` manifests (`ProgramInstallService`)
4. Produces candidates: Add / Update / Delete
5. On install: backup (update), remove old files, download ZIP, verify hash, extract, write `.dops`
6. Reports deployment events and can export a diagnostics ZIP

## Local artifacts

| Artifact | Meaning |
|----------|---------|
| `{slug}.dops` | Installed program manifest |
| `{slug}-{version}-backup.zip` | Previous version backup |
| `%LocalAppData%\DesktopOps\Updates` | Download cache |
| `%LocalAppData%\DesktopOps\Diagnostics` | Agent / app logs and exports |

## Hash verification

The server stores SHA-256 of the uploaded package.
Clients refuse to install when the downloaded file hash does not match.
