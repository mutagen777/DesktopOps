# Directory group sync

DesktopOps can resolve Windows SIDs and import members from an AD or local security group into Admin **Gruppen**.

## What it does

- When you add a user (`DOMAIN\user` or `samAccountName`), Admin tries to resolve the **Windows SID**
- **Aus AD synchronisieren** / Sync from AD loads enabled users from a security group (recursive) and upserts them into the DesktopOps group
- **SIDs auflösen** fills missing SIDs for existing members
- Optional field `ActiveDirectoryGroup` is stored on the group for the next sync
- The server matches assignments by **username or SID** (agents that send `WindowsSid` still get releases if the SAM name differs)

## Scheduled sync

Background sync runs inside **DesktopOps.Admin** when enabled:

```json
"DirectorySync": {
  "Enabled": true,
  "IntervalMinutes": 60
}
```

- Syncs every N minutes (minimum 5) for all groups that have `ActiveDirectoryGroup` set
- Same upsert rules as the manual button (adds members / fills SIDs; does **not** remove users who left the AD group)
- Requires Windows + directory reachability; no-ops cleanly otherwise
- Production template enables it by default — turn off if Admin cannot reach AD

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
7. Scheduled: set `DirectorySync:Enabled` true, wait for interval, confirm logs `Scheduled directory sync finished`

Without a domain, you can still add local users; SID resolution uses `NTAccount` translation when possible.

## Limits

- No automatic removal of members who left the AD group (safe default)
- No Entra ID (cloud-only) Graph sync yet
- Nested group expansion uses `GetMembers(recursive: true)` — large groups may be slow
- Non-Windows Admin hosts get a no-op directory lookup
