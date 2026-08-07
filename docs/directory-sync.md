# Directory group sync

DesktopOps can resolve Windows SIDs and import members from **Windows/AD** or **Microsoft Entra ID (Graph)** into Admin **Gruppen**.

## What it does

- When you add a user (`DOMAIN\user`, `samAccountName`, or UPN), Admin tries to resolve the **Windows SID** (on-prem / hybrid) when available
- **Aus AD synchronisieren** / Sync from directory loads enabled users from a linked group and upserts them into the DesktopOps group
- **SIDs auflösen** fills missing SIDs for existing members
- Optional field `ActiveDirectoryGroup` stores the directory group reference for the next sync (Windows name, `entra:…`, or Entra object id)
- The server matches assignments by **username or SID** (agents that send `WindowsSid` still get releases if the SAM name differs)

## Group reference formats

| Reference | Backend |
|-----------|---------|
| `DOMAIN\SecurityGroup` or local group name | Windows / AD (`AccountManagement`) |
| Entra object id (GUID) | Microsoft Graph |
| `entra:DisplayName` or `entra:mailNickname` | Microsoft Graph |
| Plain name when Admin has **no** Windows lookup but Entra is configured | Microsoft Graph |

On Windows with both backends configured, plain names go to AD; use a GUID or `entra:` for cloud groups.

## Scheduled sync

Background sync runs inside **DesktopOps.Admin** when enabled:

```json
"DirectorySync": {
  "Enabled": true,
  "IntervalMinutes": 60,
  "Entra": {
    "TenantId": "",
    "ClientId": "",
    "ClientSecret": ""
  }
}
```

- Syncs every N minutes (minimum 5) for all groups that have `ActiveDirectoryGroup` set
- Same upsert rules as the manual button (adds members / fills SIDs; does **not** remove users who left the directory group)
- Empty but resolvable groups still store `ActiveDirectoryGroup`; missing groups / directory errors return a distinct error
- Per-group lock prevents concurrent manual + scheduled sync collisions
- Requires at least one backend: Windows directory reachability and/or configured Entra app credentials

## Entra ID (Microsoft Graph)

1. Register an app in Entra ID (Azure portal → App registrations)
2. Create a **client secret** (or use certificate-based auth in a future hardening pass — secret is supported today)
3. Grant **application** permissions (admin consent):
   - `GroupMember.Read.All` (or `Group.Read.All`)
   - `User.Read.All` (or `Directory.Read.All`)
4. Set `DirectorySync:Entra:TenantId`, `ClientId`, and `ClientSecret` on Admin (prefer secret store / env vars in production)

Member mapping:

- `UserName` → `onPremisesSamAccountName`, else UPN / mail / object id
- `WindowsSid` → `onPremisesSecurityIdentifier` when hybrid identity sync provides it
- Membership uses **transitive** Graph members (nested groups)

Packages: `Azure.Identity`, `Microsoft.Graph`.

## Windows / AD requirements

- Admin on **Windows** for AD/local group sync
- Machine must reach the domain (or use a local group on non-domain PCs)
- Process identity needs permission to read the directory group
- Package: `System.DirectoryServices.AccountManagement`

Non-Windows Admin hosts can still sync via Entra when configured; otherwise directory lookup is a no-op.

## How to test

1. Open Admin → **Gruppen**
2. Create a group; set **AD-Gruppe** to e.g. `DOMAIN\SomeSecurityGroup` or `entra:MyCloudGroup` / object id
3. Click **Aus AD synchronisieren**
4. Members appear with a green **SID** badge when an on-prem SID was resolved
5. Assign a program to the group; run the agent as one of those users — assignments should appear
6. Negative: wrong group name → status message, no crash
7. Scheduled: set `DirectorySync:Enabled` true, wait for interval, confirm logs `Scheduled directory sync finished`

Without a domain, you can still add local users; SID resolution uses `NTAccount` translation when possible.

## Limits

- No automatic removal of members who left the directory group (safe default)
- Nested AD expansion uses `GetMembers(recursive: true)` — large groups may be slow
- Entra uses transitive members via Graph — large groups may be slow / throttled
- Ambiguous Entra display names require the object id (`entra:<guid>` or bare GUID)
