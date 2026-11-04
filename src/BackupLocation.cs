namespace AzimuthFH3;

internal static class BackupLocation
{
    public const string FolderName = "saves_backup";

    public static string ForSave(string destinationFolder, string sourceFolder)
    {
        string? custom = Environment.GetEnvironmentVariable("AZIMUTH_FH3_BACKUPS");
        if (!string.IsNullOrWhiteSpace(custom)) return Path.GetFullPath(custom);
        string? title = FindTitle(destinationFolder) ?? FindTitle(sourceFolder);
        return Path.Combine(title ?? Path.GetFullPath(destinationFolder), FolderName);
    }

    static string? FindTitle(string folder)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(folder)); current != null; current = current.Parent)
        {
            if (current.Name.Equals("4CE1DFAB", StringComparison.OrdinalIgnoreCase)
                && current.Parent?.Name.Equals("titles", StringComparison.OrdinalIgnoreCase) == true
                && current.Parent.Parent?.Name.Equals("data", StringComparison.OrdinalIgnoreCase) == true)
                return current.FullName;
        }
        return null;
    }
}
