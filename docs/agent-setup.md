# Agent setup

`DesktopOps.Agent` is a WPF tray application inspired by Autoupdater client behaviour (without private UI frameworks).

## Run

```powershell
dotnet run --project src/DesktopOps.Agent/DesktopOps.Agent.csproj
```

## Behaviour (Autoupdater-like)

| Action | Behaviour |
|--------|-----------|
| Startup | Search assigned programs; **auto-install** if updates exist (configurable) |
| Tray double-click / **Updates suchen...** | Search dialog → update dialog with list + **Ausführen** |
| Background poll | Discovers updates and shows a balloon (remind again after 30 minutes) |
| **Konfiguration** | Install folder, last search, programme list (latest vs installed) |
| **Programme neuinstallieren** | Removes local `.dops` installs (not the agent) and opens the update dialog |
| Progress | Per-step title + progress bar while installing |

UI settings are stored in `%LocalAppData%\DesktopOps\agent-ui.json`:

- `AutoInstallOnStartup` (default `true`)
- `NotifyWhenUpdatesAvailable` (default `true`)

## Configuration

`appsettings.json` next to the executable:

```json
{
  "Agent": {
    "ServerUri": "https://localhost:7022",
    "UserName": "",
    "InstallationDirectory": "",
    "PollIntervalMinutes": 30,
    "ApiKey": "",
    "AllowSelfUpdate": true
  }
}
```

| Key | Default | Notes |
|-----|---------|-------|
| `ServerUri` | `https://localhost:7022` | DesktopOps.Server HTTPS URL |
| `UserName` | current Windows user | Must exist in a DesktopOps group |
| `InstallationDirectory` | `%LocalAppData%\DesktopOps\Programs` | Where ZIPs are extracted |
| `PollIntervalMinutes` | `30` | Background search interval |
| `ApiKey` | empty | Must match server `Security:ApiKey` when that is set |
| `AllowSelfUpdate` | `true` | Apply published `_agent_` releases (published EXE only) |

Self-update details: [Agent self-update](agent-self-update.md).

UI language follows the Windows display language (English / German / …). See [Localization](localization.md).

Installer builds: [Agent installer (Velopack)](agent-installer.md).

## Tray menu

- **Configuration** / **Konfiguration**
- **Search for updates…** / **Updates suchen…**
- **Last search** (info)
- **Reinstall programs** / **Programme neuinstallieren**
- **Export diagnostics** / **Diagnose exportieren**
- **Version** (info)
- **Exit** / **Beenden**

## Prerequisites

- DesktopOps.Server reachable
- User is a member of a group that has program assignments
- For self-update: run a published `DesktopOps.Agent.exe`, not `dotnet run`
