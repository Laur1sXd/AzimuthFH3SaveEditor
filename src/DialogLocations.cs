using System.Text.Json;

namespace AzimuthFH3;

internal sealed class DialogLocations
{
    public string? OpenFolder { get; set; }
    public string? OpenFileFolder { get; set; }
    public string? SaveFolder { get; set; }
    public string? SaveFileFolder { get; set; }
    public string? LastOpenLocation { get; set; }
    public string? LastSaveLocation { get; set; }

    static string SettingsPath => Environment.GetEnvironmentVariable("AZIMUTH_FH3_SETTINGS")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AzimuthFH3SaveEditor", "settings.json");

    public static DialogLocations Load()
    {
        try { return JsonSerializer.Deserialize<DialogLocations>(File.ReadAllText(SettingsPath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public string StartFolder(bool saving, bool singleFile, string? fallback = null)
    {
        string? preferred = saving ? (singleFile ? SaveFileFolder : SaveFolder) : (singleFile ? OpenFileFolder : OpenFolder);
        string? latest = saving ? LastSaveLocation : LastOpenLocation;
        string?[] choices = [preferred, latest, fallback];
        foreach (string? path in choices) if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) return path;
        foreach (string? path in choices)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            try
            {
                var parent = Directory.GetParent(path);
                while (parent != null) { if (parent.Exists) return parent.FullName; parent = parent.Parent; }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { }
        }
        return "";
    }

    public void Remember(string selectedPath, bool saving, bool singleFile)
    {
        string path = Path.GetFullPath(selectedPath);
        string folder = singleFile ? Path.GetDirectoryName(path)! : path;
        if (saving)
        {
            if (singleFile) SaveFileFolder = folder; else SaveFolder = folder;
            LastSaveLocation = folder;
        }
        else
        {
            if (singleFile) OpenFileFolder = folder; else OpenFolder = folder;
            LastOpenLocation = folder;
        }
        string settings = SettingsPath, temp = settings + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, settings, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
