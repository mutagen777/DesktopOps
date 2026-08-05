# Server API

Base URL example: `https://localhost:7022`

## Admin

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/programs` | List programs |
| POST | `/api/programs` | Create program (`name`, `slug`, `shortName?`, `description?`) |
| GET | `/api/groups` | List groups with members |
| POST | `/api/groups` | Create group (`name`, `description?`, `userNames[]`) |
| POST | `/api/groups/{groupId}/members` | Add member (`userName`, `windowsSid?`) |
| POST | `/api/assignments` | Assign program to group |
| GET | `/api/releases` | List releases |
| POST | `/api/releases` | Multipart upload (`programId`, `version`, `package`, `releaseNotes?`, `isMandatory?`) |
| POST | `/api/releases/{id}/publish` | Publish release |
| GET | `/api/rollouts` | Recent deployment events |

Upload response includes `packageHash` (SHA-256 hex) and `packageSize`.

## Client / agent

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/clients/register` | Register or refresh a client |
| GET | `/api/clients/{id}/programs` | Assigned programs for the client user |
| GET | `/api/clients/{id}/assignments` | Assigned programs with latest published release |
| GET | `/api/clients/{id}/updates?programSlug=` | Published releases for one program |
| POST | `/api/clients/{id}/events` | Report deployment status |
| GET | `/api/packages/{releaseId}` | Download package bytes |

### Register body

```json
{
  "programSlug": "_agent_",
  "userName": "alice",
  "machineName": "PC-01",
  "currentVersion": "0.3.0",
  "windowsSid": "S-1-5-21-..."
}
```

Use `programSlug: "_agent_"` (or omit / empty on the library side) for the tray agent.
Embedded apps set their own product slug.

### Assignment item

```json
{
  "id": "...",
  "name": "Sample App",
  "slug": "desktopops-sample",
  "shortName": "Sample",
  "latestRelease": {
    "id": "...",
    "version": "1.2.0",
    "releaseNotes": "...",
    "isMandatory": false,
    "packageHash": "abc123...",
    "packageSize": 12345,
    "packageUrl": "/api/packages/..."
  }
}
```

### Event body

```json
{
  "releaseId": "...",
  "status": "Installed",
  "message": "Installed by DesktopOps Agent.",
  "installedVersion": "1.2.0"
}
```

## Auth note

MVP endpoints are open for local demos. Harden with Windows Authentication or an identity provider before production exposure.
