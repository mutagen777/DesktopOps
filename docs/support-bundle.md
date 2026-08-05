# Support bundle

The diagnostics export is designed to give support teams a predictable ZIP artifact that can be attached to tickets or customer emails.

## Bundle contents

- `app-info.json`
- `environment.json`
- `exceptions.json`
- `logs/desktopops.log`
- optional registered attachments

## Typical use

1. initialize `DesktopOpsWpfBootstrapper`
2. register extra files if the app has local settings or domain-specific exports
3. call `ExportDiagnosticsAsync()` when a user reports an issue
4. attach the ZIP to a support case

## Privacy note

Environment variables are excluded by default.
Enable them only when the app owner has a clear operational need and an explicit data handling policy.
