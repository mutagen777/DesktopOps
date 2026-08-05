# Agent self-update

The tray agent can update **itself** when a published program with slug `_agent_` is assigned to the user.

## How it works

1. Admin creates a program with slug **`_agent_`** (name e.g. `DesktopOps Agent`)
2. Assign it to the same groups that should receive agent updates
3. Upload a ZIP of a **published** agent build (folder contents of `dotnet publish`, including `DesktopOps.Agent.exe`) and publish the release
4. Agent compares the release version to its assembly version
5. On **Install pending updates**, the agent:
   - downloads and hash-verifies the ZIP
   - extracts to `%LocalAppData%\DesktopOps\AgentUpdate\extract`
   - starts `apply-update.cmd` (waits for exit, `xcopy` into the agent folder, restarts the EXE)
   - shuts down

## Requirements

- Agent must run as a **published** `DesktopOps.Agent.exe` (not `dotnet run` — self-update is skipped then)
- Version strings must parse as `System.Version` and be **greater** than the running assembly version
- Optional config: `Agent:AllowSelfUpdate` (default `true`)

## Packaging tip

```powershell
dotnet publish src/DesktopOps.Agent/DesktopOps.Agent.csproj -c Release -o .\publish\agent
Compress-Archive -Path .\publish\agent\* -DestinationPath .\publish\desktopops-agent-1.1.0.zip
```

Bump the agent project version before publishing so clients detect the upgrade.

## Safety

- The agent is never removed via the normal “unassign → delete files” path
- Self-update does not use the shared Programs install directory for the agent binaries
- Failed hash checks are reported as `Failed` on Rollouts
