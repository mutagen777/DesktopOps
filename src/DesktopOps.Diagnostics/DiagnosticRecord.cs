namespace DesktopOps.Diagnostics;

public sealed record DiagnosticRecord(
    DateTimeOffset Timestamp,
    string Level,
    string Category,
    int? EventId,
    string Message,
    string? Exception);
