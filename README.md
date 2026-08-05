# DesktopOps for .NET

`DesktopOps` is a generic, self-hosted desktop deployment system for .NET.
It is inspired by mature internal updater architectures, but has no private company dependencies: public NuGet feeds only, SQLite by default, and a portable tray agent.

Admins manage programs, user groups, assignments, and releases. Assigned users receive updates through either:

- **DesktopOps.Agent** – tray client that installs assigned programs with hash verification and local backups
- **DesktopOps.Updates** – library embedded in your WPF app for in-app update checks

## Components

| Project | Role |
|---------|------|
| `DesktopOps.Server` | ASP.NET Core API, package store, rollout events |
| `DesktopOps.Admin` | Blazor admin portal |
| `DesktopOps.Agent` | WPF tray agent (poll, notify, install, backup, diagnostics) |
| `DesktopOps.Updates` | Client library (register, assignments, download, hash check) |
| `DesktopOps.Diagnostics` | Structured logs and support ZIP export |
| `DesktopOps.Wpf` | WPF bootstrap for host apps |
| `DesktopOps.Sample.Wpf` | Sample embedded client |

## Core workflow

1. Admin creates a program.
2. Admin creates a group and adds Windows usernames.
3. Admin assigns the program to the group.
4. Admin uploads a ZIP release (SHA-256 is stored) and publishes it.
5. Agent (or embedded client) registers with the server.
6. Assigned users see the release, download it, verify the hash, install it, and report status.

## Quick start

### Server and admin

```powershell
dotnet build DesktopOps.slnx
dotnet run --project src/DesktopOps.Server/DesktopOps.Server.csproj
dotnet run --project src/DesktopOps.Admin/DesktopOps.Admin.csproj
```

Point Admin and Server at the same SQLite file and storage root (see `appsettings.json`).
Delete `desktopops.db` after schema upgrades when using `EnsureCreated`.

### Tray agent

```powershell
dotnet run --project src/DesktopOps.Agent/DesktopOps.Agent.csproj
```

Configure `src/DesktopOps.Agent/appsettings.json`:

```json
{
  "Agent": {
    "ServerUri": "https://localhost:7022",
    "UserName": "alice",
    "PollIntervalMinutes": 30,
    "ApiKey": ""
  }
}
```

`UserName` must match a group member in the admin portal. Leave empty to use `Environment.UserName`.
Set `ApiKey` to the same value as server `Security:ApiKey` when API key auth is enabled.

Tray menu: check updates, install pending updates, export diagnostics, exit.

### Embedded WPF client

```csharp
using DesktopOps.Wpf;

Runtime = DesktopOpsWpfBootstrapper.Initialize(this, options =>
{
    options.Diagnostics.AppName = "My Product";
    options.Diagnostics.AppVersion = "1.0.0";
    options.Updates.CurrentVersion = new Version(1, 0, 0);
    options.Updates.ProgramSlug = "my-product";
    options.Updates.ServerUri = new Uri("https://desktopops-server.internal");
    options.Updates.ApiKey = "change-me"; // same as server Security:ApiKey when set
});
```

## Documentation

- [Architecture](docs/architecture.md)
- [Admin authentication](docs/admin-auth.md)
- [Directory group sync](docs/directory-sync.md)
- [Server API](docs/server-api.md)
- [Deployment model](docs/deployment-model.md)
- [Client update flow](docs/update-feed.md)
- [Agent setup](docs/agent-setup.md)
- [Agent self-update](docs/agent-self-update.md)
- [Staged rollouts](docs/staged-rollouts.md)
- [Support bundle](docs/support-bundle.md)
- [Release strategy](docs/release-strategy.md)

## MVP boundary

Included:

- self-hosted server (SQLite default, SQL Server via config)
- Blazor admin (programs, groups, assignments, releases, rollouts)
- package SHA-256 storage and client verification
- tray agent with install, backup, remove, diagnostics
- embedded update library for WPF apps

Not included yet:

- Scheduled / Entra ID (Graph) sync of end-user groups
- Velopack / MSI installer channel
- multi-tenant isolation
- delta updates
- signed packages

Staged percentage rollouts are supported — see [Staged rollouts](docs/staged-rollouts.md).

### Server API key

Protect `/api/*` with a shared key (`Security:ApiKey`). See [Server API](docs/server-api.md).

### Admin authentication

Windows Negotiate authentication can protect the Blazor admin (same model as typical intranet AD setups):

```json
"Security": {
  "Enabled": true,
  "ADGroup": "DOMAIN\\DesktopOps-Managers",
  "DeveloperADGroup": "DOMAIN\\DesktopOps-Developers"
}
```

| Policy | AD groups | Pages |
|--------|-----------|--------|
| Manager | `ADGroup` or `DeveloperADGroup` | Übersicht, Gruppen, Zuweisungen, Rollouts |
| Developer | `DeveloperADGroup` | Programme, Releases |

Local demo keeps `"Enabled": false` (policies always allow). Protect the agent-facing API separately with `Security:ApiKey` on the server.

See [Admin authentication](docs/admin-auth.md) for the enable/test checklist. With auth enabled, the Admin sidebar lists configured groups and role claims for debugging.

## Building

```powershell
dotnet build DesktopOps.slnx
```

Only `nuget.org` is configured (`nuget.config`). No private feeds required.

## Origin

Product architecture is derived from battle-tested desktop deployment patterns (central admin, SID/user targeting, hash-verified installs, local backups). The `autoupdater` production codebase remains separate and unchanged; DesktopOps is the generic open product line.
