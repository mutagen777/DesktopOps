using DesktopOps.Diagnostics;
using DesktopOps.Updates;

namespace DesktopOps.Wpf;

public sealed class DesktopOpsWpfOptions
{
    public DiagnosticsOptions Diagnostics { get; } = new();

    public UpdateOptions Updates { get; } = new();

    public Action<Exception, string>? OnExceptionCaptured { get; set; }
}
