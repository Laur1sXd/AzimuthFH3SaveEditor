using System.Globalization;

namespace AzimuthFH3;

internal static class FileTests
{
    public static void Run(string fixture, string destination, Action<bool, string> check)
    {
        string root = Path.Combine(destination, "individual-files"); Directory.CreateDirectory(root);
        string? oldBackup = Environment.GetEnvironmentVariable("AZIMUTH_FH3_BACKUPS");
        Environment.SetEnvironmentVariable("AZIMUTH_FH3_BACKUPS", Path.Combine(destination, "test-backups"));
        try
        {
            string profile = Path.Combine(root, "any-player-save.bin"); File.Copy(Path.Combine(fixture, "ProfileData"), profile);
            string flags = Path.Combine(root, "another-player-flags.bin"); File.Copy(Path.Combine(fixture, "VersionFlags"), flags);
            byte[] originalProfile = File.ReadAllBytes(profile); string savedCopy = Path.Combine(root, "renamed-copy.save");
            using (var s = new SaveSession(profile))
            {
                check(s.IsSingleFile && s.HasProfile && s.HasDatabase && s.HasState && !s.HasFlags, "A renamed encrypted ProfileData opens alone without VersionFlags");
                var value = s.Profile.Values.Single(p => p.Name == "Main/Credits"); value.Set((uint.Parse(value.Value, CultureInfo.InvariantCulture) + 11).ToString(CultureInfo.InvariantCulture));
                s.SaveSingleAs(savedCopy);
                check(File.ReadAllBytes(profile).AsSpan().SequenceEqual(originalProfile), "Save As leaves the original standalone file unchanged");
                using var copy = new SaveSession(savedCopy);
                check(copy.Profile.Values.Single(p => p.Name == "Main/Credits").Value == value.Value, "Save As supports arbitrary filenames and reopens edited encrypted content");
                string backup = s.SaveAll();
                check(backup.Length > 0 && File.ReadAllBytes(Path.Combine(backup, Path.GetFileName(profile))).AsSpan().SequenceEqual(originalProfile), "In-place Save creates a byte-identical backup of the original file");
                using var reopened = new SaveSession(profile);
                check(reopened.Profile.Values.Single(p => p.Name == "Main/Credits").Value == value.Value, "In-place Save writes the edited standalone file");
                var beforeRepeat = File.ReadAllBytes(profile); s.SaveAll();
                check(File.ReadAllBytes(profile).AsSpan().SequenceEqual(beforeRepeat), "Repeated Save retains the same encrypted bytes");
                string plain = Path.Combine(root, "decrypted-profile.bin"); File.WriteAllBytes(plain, s.BuildPlain());
                using var plaintext = new SaveSession(plain); var p = plaintext.Profile.Values.First(v => v.Code == 3); p.Set("123"); plaintext.SaveAll();
                using var rePlain = new SaveSession(plain); check(rePlain.Profile.Values.Single(v => v.Name == p.Name).Value == "123", "Decrypted packed profiles remain decrypted after editing and saving");
                string dbPath = Path.Combine(root, "career.sqlite"); File.WriteAllBytes(dbPath, s.DatabaseBytes());
                using var db = new SaveSession(dbPath); check(db.HasDatabase && !db.HasProfile && !db.HasFlags, "Standalone SQLite opens without any sibling save files");
                var table = db.ReadTable("Career_Garage"); long row = Convert.ToInt64(table.Rows[0]["__az_rowid"]); db.UpdateCell("Career_Garage", row, "PartsValue", 12345L); db.SaveAll();
                using var dbAgain = new SaveSession(dbPath); check(Convert.ToInt64(dbAgain.ReadTable("Career_Garage").Rows[0]["PartsValue"]) == 12345L, "Standalone SQLite changes survive Save and reopen");
                string statePath = Path.Combine(root, "save-state.bxml"); File.WriteAllBytes(statePath, s.State.Build());
                using var state = new SaveSession(statePath); var prop = state.State.Properties.First(); prop.Value = "edited-test"; state.SaveAll();
                using var stateAgain = new SaveSession(statePath); check(stateAgain.State.Properties.First().Value == "edited-test", "Standalone BXML SaveState changes survive Save and reopen");
            }
            using (var s = new SaveSession(flags))
            {
                check(s.HasFlags && !s.HasProfile && !s.HasState && !s.HasDatabase, "Encrypted VersionFlags opens independently of ProfileData");
                s.VersionFlags = 123; s.SaveAll();
                using var again = new SaveSession(flags); check(again.VersionFlags == 123, "Standalone encrypted VersionFlags supports in-place Save");
            }
            string rawFlags = Path.Combine(root, "flags.raw"); File.WriteAllBytes(rawFlags, BitConverter.GetBytes(4u));
            using (var s = new SaveSession(rawFlags)) { s.VersionFlags = 456; s.SaveAll(); }
            check(File.ReadAllBytes(rawFlags).AsSpan().SequenceEqual(BitConverter.GetBytes(456u)), "Plain VersionFlags remains a four-byte file when saved");
            string data = Path.Combine(root, "managed-revision", "data"); Directory.CreateDirectory(data);
            foreach (var p in Directory.EnumerateFiles(fixture)) File.Copy(p, Path.Combine(data, Path.GetFileName(p)));
            string commit = Path.Combine(Directory.GetParent(data)!.FullName, "commit"); File.Copy(Path.Combine(Directory.GetParent(fixture)!.FullName, "commit"), commit);
            byte[] untouchedFlags = File.ReadAllBytes(Path.Combine(data, "VersionFlags"));
            using (var s = new SaveSession(Path.Combine(data, "ProfileData"))) { s.Profile.Values.Single(p => p.Name == "Main/Credits").Set("345678"); s.SaveAll(); }
            using (var s = new SaveSession(data))
            {
                check(s.ManifestStatus.StartsWith("Manifest verified") && s.Profile.Values.Single(p => p.Name == "Main/Credits").Value == "345678", "Saving one file in an Azimuth revision updates and verifies its commit manifest");
                check(File.ReadAllBytes(Path.Combine(data, "VersionFlags")).AsSpan().SequenceEqual(untouchedFlags), "Saving one revision file preserves its unopened siblings");
            }
            string changed = Path.Combine(root, "externally-changed.flags"); File.WriteAllBytes(changed, BitConverter.GetBytes(1u));
            using (var s = new SaveSession(changed))
            {
                File.WriteAllBytes(changed, BitConverter.GetBytes(2u)); bool refused = false;
                try { s.SaveAll(); } catch (IOException) { refused = true; }
                check(refused && BitConverter.ToUInt32(File.ReadAllBytes(changed)) == 2u, "Save refuses to overwrite changes made by another process");
            }
            string unknown = Path.Combine(root, "unsupported.dat"); File.WriteAllBytes(unknown, [1, 2, 3, 4, 5, 6, 7]); bool rejected = false;
            try { using var s = new SaveSession(unknown); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "Unsupported formats are rejected without modifying the file");
        }
        finally { Environment.SetEnvironmentVariable("AZIMUTH_FH3_BACKUPS", oldBackup); }
    }
}
