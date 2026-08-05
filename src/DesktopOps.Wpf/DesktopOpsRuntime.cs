using DesktopOps.Diagnostics;
using DesktopOps.Updates;

namespace DesktopOps.Wpf;

public sealed class DesktopOpsRuntime
{
    internal DesktopOpsRuntime(
        IDiagnosticsService diagnostics,
        IUpdateService updates)
    {
        Diagnostics = diagnostics;
        Updates = updates;
    }

    public IDiagnosticsService Diagnostics { get; }

    public IUpdateService Updates { get; }
}
