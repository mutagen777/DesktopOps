# License signing keys (local)

- `dev-public.pem` — may be committed; must match `LicensingPublicKeys.DefaultRsaPublicKeyPem`
- `dev-private.pem` — **gitignored**; used only by `tools/issue-license.ps1`

Generate or rotate:

```powershell
.\tools\issue-license.ps1 -InitKeys
```

Then copy the printed public PEM into `src/DesktopOps.Licensing/LicensingPublicKeys.cs`.
