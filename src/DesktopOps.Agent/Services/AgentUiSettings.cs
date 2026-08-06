using System.IO;
using System.Text.Json;

namespace DesktopOps.Agent.Services;

public sealed class AgentUiSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public bool AutoInstallOnStartup { get; set; } = true;

    public bool NotifyWhenUpdatesAvailable { get; set; } = true;

    public DateTimeOffset? LastSearchUtc { get; set; }

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopOps",
        "agent-ui.json");

    public static AgentUiSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AgentUiSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AgentUiSettings>(json, JsonOptions) ?? new AgentUiSettings();
        }
        catch
        {
            return new AgentUiSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
