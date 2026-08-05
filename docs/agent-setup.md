# Agent setup

`DesktopOps.Agent` is a WPF tray application with no private UI frameworks.

## Run

```powershell
dotnet run --project src/DesktopOps.Agent/DesktopOps.Agent.csproj
```

## Configuration

`appsettings.json` next to the executable:

```json
{
  "Agent": {
    "ServerUri": "https://localhost:7022",
    "UserName": "",
    "InstallationDirectory": "",
    "PollIntervalMinutes": 30
  }
}
```

| Key | Default | Notes |
|-----|---------|-------|
| `ServerUri` | `https://localhost:7022` | DesktopOps.Server HTTPS URL |
| `UserName` | current Windows user | Must exist in a DesktopOps group |
| `InstallationDirectory` | `%LocalAppData%\DesktopOps\Programs` | Where ZIPs are extracted |
| `PollIntervalMinutes` | `30` | Background search interval |

## Tray actions

- **Check for updates** – register + evaluate assignments
- **Install pending updates** – backup / install / remove + report events
- **Export diagnostics** – writes a support ZIP via `DesktopOps.Diagnostics`
- **Exit** – stops the agent

## Prerequisites

1. DesktopOps.Server is running
2. Your username is a member of a group that has at least one assigned program with a published release
3. Dev HTTPS certificate is trusted for local demos (`dotnet dev-certs https --trust`)
