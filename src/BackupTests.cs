namespace AzimuthFH3;

internal static class BackupTests
{
    public static void Run(string fixture, string destination, Action<bool, string> check)
    {
        string? oldOverride = Environment.GetEnvironmentVariable("AZIMUTH_FH3_BACKUPS");
        Environment.SetEnvironmentVariable("AZIMUTH_FH3_BACKUPS", null);
        try
        {
            string root = Path.Combine(destination, "backup-location");
            string title = Path.Combine(root, "data", "titles", "4CE1DFAB");
            string revisions = Path.Combine(title, "saves", "player-1", "User_test", "revisions");
            string[] names = ["0000000000000001", "0000000000000002"];
            void CopyRevision(string target)
            {
                Directory.CreateDirectory(Path.Combine(target, "data"));
                foreach (string p in Directory.GetFiles(fixture)) File.Copy(p, Path.Combine(target, "data", Path.GetFileName(p)));
                File.Copy(Path.Combine(Directory.GetParent(fixture)!.FullName, "commit"), Path.Combine(target, "commit"));
            }
            foreach (string name in names) CopyRevision(Path.Combine(revisions, name));
            string latestData = Path.Combine(revisions, names[1], "data");
            byte[] original = File.ReadAllBytes(Path.Combine(latestData, "ProfileData"));
            byte[] originalCommit = File.ReadAllBytes(Path.Combine(Directory.GetParent(latestData)!.FullName, "commit"));
            string expectedRoot = Path.Combine(title, "saves_backup");
            using (var session = new SaveSession(title))
            {
                session.Profile.Values.Single(v => v.Name == "Main/Credits").Set("345679");
                string backup = session.SaveAll();
                check(Path.GetDirectoryName(backup) == expectedRoot, "Save all stores backups in data/titles/4CE1DFAB/saves_backup");
                check(File.ReadAllBytes(Path.Combine(backup, "ProfileData")).AsSpan().SequenceEqual(original)
                    && File.ReadAllBytes(Path.Combine(backup, "commit")).AsSpan().SequenceEqual(originalCommit), "Title-folder backups retain the exact original profile and commit");
            }
            string firstData = Path.Combine(revisions, names[0], "data");
            using (var session = new SaveSession(Path.Combine(firstData, "ProfileData")))
            {
                session.Profile.Values.Single(v => v.Name == "Main/Credits").Set("345680");
                string backup = session.SaveAll();
                check(Path.GetDirectoryName(backup) == expectedRoot, "Single-file saves use the same title-level saves_backup folder");
                string exportFolder = Path.Combine(root, "exports"); Directory.CreateDirectory(exportFolder);
                string export = Path.Combine(exportFolder, "copied-profile.bin"); File.Copy(Path.Combine(fixture, "ProfileData"), export);
                string exportBackup = session.SaveSingleAs(export);
                check(Path.GetDirectoryName(exportBackup) == expectedRoot, "Save as outside the title keeps backups with the loaded FH3 title");
            }
            CopyRevision(Path.Combine(expectedRoot, "ffffffffffffffff"));
            using (var session = new SaveSession(title))
                check(session.DataFolder == latestData, "Automatic save discovery ignores saves_backup even when its revision number is higher");
            string[] afterNames = Directory.GetDirectories(revisions).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;
            check(afterNames.SequenceEqual(names), "Backing up and editing leave both active revision folder names unchanged");
            string standaloneFolder = Path.Combine(root, "standalone"); Directory.CreateDirectory(standaloneFolder);
            string flags = Path.Combine(standaloneFolder, "VersionFlags"); File.WriteAllBytes(flags, BitConverter.GetBytes(4u));
            using (var session = new SaveSession(flags))
            {
                session.VersionFlags = 5; string backup = session.SaveAll();
                check(Path.GetDirectoryName(backup) == Path.Combine(standaloneFolder, "saves_backup"), "Standalone files without an Azimuth title use an adjacent saves_backup folder");
            }
        }
        finally { Environment.SetEnvironmentVariable("AZIMUTH_FH3_BACKUPS", oldOverride); }
    }
}
