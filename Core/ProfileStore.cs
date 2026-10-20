using System.Text.Json;

namespace NothingOnWindows.Core;

public sealed record EqProfile(string Name, EqBand[] Bands)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Bands == null || Bands.Length != 8 || Bands.Any(b => b == null || !b.IsValid))
            throw new InvalidDataException("The profile needs a name and eight valid EQ bands (±6 dB).");
    }
}

public static class ProfileStore
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NothingOnWindows", "profiles.json");
    public static List<EqProfile> Load()
    {
        if (!File.Exists(FilePath)) return [];
        var profiles = JsonSerializer.Deserialize<List<EqProfile>>(File.ReadAllText(FilePath)) ?? [];
        foreach (var p in profiles) p.Validate();
        return profiles;
    }
    public static void Save(List<EqProfile> profiles)
    {
        foreach (var p in profiles) p.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
}
