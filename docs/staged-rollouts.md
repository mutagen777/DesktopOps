# Staged rollouts

Published releases can be limited to a **percentage of assigned users** before a full rollout.

## Field

`ReleasePackage.RolloutPercent` (0–100, default **100**).

Eligibility is stable: `SHA-256(lowercase(userName) + "|" + releaseId)` → bucket `0..99`. The user receives the release when `bucket < RolloutPercent`.

## Behaviour

- Assignments / update APIs return the **newest published** release for which the client user is eligible
- Users outside the current percent keep the previous eligible version (if any)
- Raising the percent later includes more users without republishing
- `0` means nobody; `100` means everyone assigned

## Admin

On **Releases**:

- Set **Rollout %** when uploading
- Edit percent in the table and click **OK** (works for drafts and published releases)
- Publishing uses the current percent value

## API

| Method | Path | Notes |
|--------|------|-------|
| POST | `/api/releases` | form field `rolloutPercent` |
| POST | `/api/releases/{id}/publish` | optional body `{ "rolloutPercent": 25 }` |
| PATCH | `/api/releases/{id}/rollout` | `{ "rolloutPercent": 50 }` |

## Suggested practice

1. Publish at **10–25%**, watch Rollouts / Failed
2. Increase to **50%**, then **100%**
3. Keep mandatory flag for critical fixes only after high confidence
