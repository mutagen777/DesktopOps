# Agent installer (Velopack)

Customer installs of `DesktopOps.Agent` use [Velopack](https://docs.velopack.io/) (Setup.exe + auto-update hooks).

## Build an installer

Prerequisites: .NET 8 SDK, Windows.

```powershell
# once
dotnet tool install -g vpk

.\tools\pack-agent.ps1 -Version 0.3.0

# with Authenticode (certificate thumbprint from certmgr)
.\tools\pack-agent.ps1 -Version 0.3.0 -CertThumbprint "YOURTHUMBPRINT"
```

Output (default):

- `artifacts/agent-publish/` — `dotnet publish` output
- `artifacts/agent-releases/` — Velopack `Setup.exe`, nupkg, portable package

Install on a workstation with `Setup.exe`. The Agent registers Velopack hooks on startup (`VelopackApp.Build().Run()`).

## Hosting updates

1. Publish a new version with a higher `-Version`.
2. Host the contents of `artifacts/agent-releases/` on an HTTPS file share or static web folder.
3. Point clients at that feed (Velopack update URL — configure per Velopack docs / your distribution process).

For **managed app** updates (business programs), keep using DesktopOps Server releases. Velopack is for the **Agent binary** install channel.

## Relation to `_agent_` self-update

DesktopOps still supports updating the agent via program slug `_agent_` (ZIP through the Server). Prefer **one** channel in production:

| Channel | Use when |
|---------|----------|
| Velopack | Standard customer installers / IT deployment |
| `_agent_` ZIP | Lab / environments already shipping agent updates only through DesktopOps |

Disable DesktopOps self-update if Velopack owns agent updates:

```json
"Agent": { "AllowSelfUpdate": false }
```

Details for the ZIP path: [Agent self-update](agent-self-update.md).

## Notes

- Pack script targets `win-x64` + framework-dependent (`net8.0-x64-desktop` prerequisite).
- Code-sign with `-CertThumbprint` on `pack-agent.ps1`, or see [Package signing](package-signing.md).
