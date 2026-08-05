# Release strategy

## Versioning

Use semantic-style versions that `System.Version` can parse (`1.2.3`).
Clients order releases by parsed version number.

## Packaging

Ship each program release as a ZIP of the files that should land under the agent install directory.
Prefer a flat or relative folder layout that matches how the app runs on disk.

## Publish discipline

1. Upload as draft and smoke-test the package hash in Admin.
2. Publish only when ready for all assigned groups.
3. Watch **Rollouts** for `Installed` / `Failed` feedback.

## Schema upgrades

Server and Admin use `EnsureCreated` for the MVP.
After entity changes, delete the local `desktopops.db` (and recreate demo data) before restarting.

## Future channels

Post-MVP candidates: signed packages, staged rings, Velopack/MSI installers, and agent self-update.
