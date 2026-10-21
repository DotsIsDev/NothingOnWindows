using System.Text.Json;

namespace NothingOnWindows.Core;

public sealed record AppPreferences
{
    public bool CloseToTray { get; init; }
    public string? LastDeviceId { get; init; }
    public string? LastDeviceModel { get; init; }
}

public sealed class PreferencesStore(string? path = null)
{
    private readonly string filePath = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NothingOnWindows", "settings.json");
    public AppPreferences Load() => File.Exists(filePath)
        ? JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(filePath)) ?? throw new InvalidDataException("The app settings file is empty.")
        : new AppPreferences();

    public void Save(AppPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        string temporary = filePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, filePath, true);
    }
}
