namespace DesktopOps.Server.Data;

public sealed class StorageOptions
{
    public string RootPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "storage");
}
