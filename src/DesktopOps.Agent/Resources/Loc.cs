using System.Globalization;
using System.Resources;

namespace DesktopOps.Agent.Resources;

/// <summary>Localized UI strings based on the Windows UI culture.</summary>
public static class Loc
{
    private static readonly ResourceManager Manager =
        new("DesktopOps.Agent.Resources.Strings", typeof(Loc).Assembly);

    public static string Get(string name) =>
        Manager.GetString(name, CultureInfo.CurrentUICulture) ?? name;

    public static string Format(string name, params object[] args) =>
        string.Format(CultureInfo.CurrentUICulture, Get(name), args);
}
