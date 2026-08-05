# Directory group sync

DesktopOps can resolve Windows SIDs and import members from an AD or local security group into Admin **Gruppen**.

## What it does

- When you add a user (`DOMAIN\user` or `samAccountName`), Admin tries to resolve the **Windows SID**
- **Aus AD synchronisieren** loads enabled users from a security group (recursive) and upserts them into the DesktopOps group
- **SIDs auflösen** fills missing SIDs for existing members
- Optional field `ActiveDirectoryGroup` is stored on the group for the next sync
- The server matches assignments by **username or SID** (agents that send `WindowsSid` still get releases if the SAM name differs)

## Requirements

- Admin runs on **Windows**
- Machine must reach the domain (or use a local group on non-domain PCs)
- Process identity needs permission to read the directory group

Package: `System.DirectoryServices.AccountManagement`.

## How to test

1. Open Admin → **Gruppen**
2. Create a group; optionally set **AD-Gruppe** to e.g. `DOMAIN\SomeSecurityGroup`
3. Click **Aus AD synchronisieren**
4. Members appear with a green **SID** badge when resolution succeeded
5. Assign a program to the group; run the agent as one of those users — assignments should appear
6. Negative: wrong group name → status message, no crash

Without a domain, you can still add local users; SID resolution uses `NTAccount` translation when possible.

## Limits (MVP)

- No scheduled / background AD sync
- No Entra ID (cloud-only) Graph sync
- Nested group expansion uses `GetMembers(recursive: true)` — large groups may be slow
- Non-Windows Admin hosts get a no-op directory lookup
