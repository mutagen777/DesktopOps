# Deployment model

DesktopOps targets single-tenant, self-hosted deployments inside a company network.

## Roles

- **Admin**: manages programs, groups, assignments, releases, and inspects rollout events
- **User**: receives only programs assigned through their group membership
- **Agent / client**: polls the server, installs packages, reports status

## Targeting

Users are identified by username (and optionally Windows SID on the member record).
Groups are maintained inside DesktopOps for the MVP (no AD sync yet).

## Release lifecycle

1. Upload ZIP → draft release with SHA-256
2. Publish → visible to assigned clients
3. Client downloads → hash verification
4. Install → `.dops` manifest + optional backup of previous version
5. Event → `Installed` / `Failed` visible on the Rollouts page

## Storage

- Database: SQLite file or SQL Server
- Packages: filesystem directory (`Storage:RootPath`)

Keep Admin and Server on the same database and storage paths when running both locally.

## Sample demo path

1. Start Server and Admin.
2. Create program `desktopops-sample`.
3. Create group `demo` with your Windows username.
4. Assign the program to the group.
5. Upload a ZIP and publish.
6. Run the Agent with matching `Agent:UserName` (or default `Environment.UserName`).
7. Tray → Check for updates → Install pending updates.
8. Confirm the event on **Rollouts**.
