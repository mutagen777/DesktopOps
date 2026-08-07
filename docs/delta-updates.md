# Package delta updates

DesktopOps can ship an optional **delta ZIP** next to a full release package. Agents that still have the matching base version reconstruct the full ZIP locally, verify per-file hashes from the delta manifest, then install as usual. Any failure falls back to the full package download (SHA-256 + CMS when configured).

When `RequirePackageCmsSignature` is enabled, agents **skip delta** and always use the full package so detached CMS verification still applies.

## Format

A delta is a ZIP of **changed files only**, plus a manifest:

| Entry | Purpose |
|-------|---------|
| `_desktopops-delta.json` | `baseVersion`, `targetVersion`, `targetPackageHash`, `deletes[]`, `targetEntryHashes` |
| *(other paths)* | Added or replaced files from the target package |

Removed files appear only in `deletes` (paths relative to the package root, `/` separators).

File name on disk: `{slug}/{version}.from-{baseVersion}.delta.zip`.

## Server / Admin

On release upload (Admin UI or `POST /api/releases`), DesktopOps looks for the highest **older published** version of the same program and builds a delta when:

- packages differ, and
- delta size is less than **80%** of the full package size

Fields on `ReleasePackages`: `DeltaPath`, `DeltaHash`, `DeltaSize`, `DeltaBaseVersion`.

| Action | How |
|--------|-----|
| Auto on upload | Built against previous version when beneficial |
| Manual | Admin **Generate delta**, or `POST /api/releases/{id}/delta` |
| Download | `GET /api/packages/{id}/delta?clientId=` (agent auth same as full package) |

Assignments / updates JSON include `deltaUrl`, `deltaHash`, `deltaSize`, `deltaBaseVersion` when present.

## Agent

| Setting | Default | Meaning |
|---------|---------|---------|
| `Agent:EnableDeltaUpdates` | `true` | Try delta before full download |
| `Agent:DeltaMaxSizeRatio` | `0.8` | Skip delta when advertised size ≥ this fraction of the full package |
| `Agent:RequirePackageCmsSignature` | `false` | When `true`, delta apply is disabled (full package + CMS only) |

Flow for an in-place update:

1. Backup install + snapshot ZIP to the update cache (delta base)
2. Download delta (if base version matches `deltaBaseVersion`), apply onto snapshot
3. Verify **per-file SHA-256** from `_desktopops-delta.json` (`targetEntryHashes`)
4. On any failure → download full ZIP and verify package SHA-256 (+ CMS if present)
5. Remove old install, install from reconstructed/full ZIP

Reconstructed ZIPs are **not** byte-identical to the published artifact, so full-package SHA-256 and detached CMS apply to the **full download path** only. Delta integrity is: delta SHA-256 + entry hashes in the signed-by-hash manifest inside the delta.

There are **no delta chains** in v1 — only one hop from `deltaBaseVersion` to the target. Clients on any other version always use the full package.

## Library (`DesktopOps.Updates`)

- `UpdateOptions.EnableDeltaUpdates` / `DeltaMaxSizeRatio`
- `UpdateRelease.DeltaUrl` / `DeltaHash` / `DeltaSize` / `DeltaBaseVersion`
- `PrepareUpdateAsync(release, basePackagePath, installedVersion, ct)`
- `PackageDeltaApplier.ApplyDelta(...)`
