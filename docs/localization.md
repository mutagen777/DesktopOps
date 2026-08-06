# Localization

Admin and Agent UI language follows the system / browser culture.

Supported cultures (default **English**):

| Code | Language |
|------|----------|
| `en` | English |
| `de` | German |
| `fr` | French |
| `es` | Spanish |
| `it` | Italian |
| `ru` | Russian |

## Admin (`DesktopOps.Admin`)

- Uses ASP.NET Core request localization (`Accept-Language` from the browser, which usually matches Windows display language).
- Resources: `src/DesktopOps.Admin/Resources/Ui.resx` + `Ui.<culture>.resx`
- Default culture: `en`. Other languages when the request prefers a supported culture.

Override for a session (optional):

- Query: `?culture=fr&ui-culture=fr`
- Or cookie via the standard `CookieRequestCultureProvider` (enabled with `UseRequestLocalization`)

## Agent (`DesktopOps.Agent`)

- Uses `CultureInfo.CurrentUICulture` from Windows at startup.
- Resources: `src/DesktopOps.Agent/Resources/Strings.resx` + `Strings.<culture>.resx`
- Access via `DesktopOps.Agent.Resources.Loc`

Matching Windows UI culture → that language. Unsupported cultures fall back to English.

## Adding a language

1. Add `Ui.<culture>.resx` / `Strings.<culture>.resx` (copy from English, translate values). Optional helper: `tools/gen-extra-locales.mjs`.
2. Admin: add the culture code to the supported list in `Program.cs`.
3. Rebuild. Agent satellites are picked up automatically for matching `CurrentUICulture`.
