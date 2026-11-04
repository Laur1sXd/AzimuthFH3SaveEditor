using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace AzimuthFH3;

internal static class SelfTest
{
    public static int Run(string source, string destination)
    {
        var checks = new List<string>();
        void Check(bool pass, string name) { if (!pass) throw new Exception("FAILED: " + name); checks.Add(name); }
        string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
        try
        {
            source = SaveSession.Resolve(source); destination = Path.GetFullPath(destination); Directory.CreateDirectory(destination);
            var before = Directory.EnumerateFiles(source).ToDictionary(p => Path.GetFileName(p), Hash);
            var fixture = Path.Combine(destination, "revisions", "00000000000002e2", "data"); Directory.CreateDirectory(fixture);
            foreach (var f in Directory.EnumerateFiles(source)) File.Copy(f, Path.Combine(fixture, Path.GetFileName(f)), true);
            File.Copy(Path.Combine(Directory.GetParent(source)!.FullName, "commit"), Path.Combine(Directory.GetParent(fixture)!.FullName, "commit"), true);
            int values, properties, tables;
            using (var s = new SaveSession(fixture))
            {
                values = s.Profile.Values.Count; properties = s.State.Properties.Count; tables = s.Tables.Count;
                Check(values > 400 && properties > 1000 && tables > 20, "Real FH3 profile, BXML and SQLite parse successfully");
                s.CheckDatabase(); Check(true, "SQLite integrity_check returns ok");
                var originalPlain = File.ReadAllBytes(Path.Combine(s.Work, "ProfileData.plain"));
                Check(s.BuildPlain().AsSpan().SequenceEqual(originalPlain), "No-edit packed profile is byte-identical");
                var encrypted = s.BuildEncrypted();
                Check(encrypted.All(p => p.Value.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(fixture, p.Key)))), "No-edit output preserves every encrypted save byte");
                byte[] cipher = File.ReadAllBytes(Path.Combine(fixture, "ProfileData"));
                Check(Fh3Crypto.Encrypt(originalPlain, cipher).AsSpan().SequenceEqual(cipher), "Built-in crypto reproduces the original encrypted profile byte for byte");
            }
            string changedPath;
            string expectedCredit, expectedString, expectedWide, stateObject, stateName, expectedState, dbColumn; long dbRow; object expectedDb; uint expectedFlags;
            Dictionary<string, string> unchangedValues;
            using (var s = new SaveSession(fixture))
            {
                var credit = s.Profile.Values.Single(p => p.Name == "Main/Credits");
                bool rejected = false; try { credit.Set("4294967296"); } catch (OverflowException) { rejected = true; }
                Check(rejected && credit.Replacement == null, "uint32 overflow is rejected without changing the property");
                unchangedValues = s.Profile.Values.ToDictionary(p => p.Name, p => p.Value);
                expectedCredit = (uint.Parse(credit.Value, CultureInfo.InvariantCulture) + 123u).ToString(CultureInfo.InvariantCulture); credit.Set(expectedCredit);
                var narrow = s.Profile.Values.First(v => v.Code == 11); expectedString = narrow.Value + " test"; narrow.Set(expectedString);
                var wide = s.Profile.Values.First(v => v.Code == 12); expectedWide = wide.Value + " Ω"; wide.Set(expectedWide);
                unchangedValues.Remove(credit.Name); unchangedValues.Remove(narrow.Name); unchangedValues.Remove(wide.Name);
                var state = s.State.Properties.First(p => p.Property == "Completed" && p.Value == "false"); stateObject = state.Object; stateName = state.Property; expectedState = "true"; state.Value = expectedState;
                var aliases = s.State.Properties.Count(p => p.Value == "false"); var bxml = new BinaryXml(s.State.Build());
                Check(bxml.Properties.Count(p => p.Value == "false") == aliases, "BXML editing leaves other references to a shared string unchanged");
                var dt = s.ReadTable("Career_Garage");
                dbColumn = dt.Columns.Cast<System.Data.DataColumn>().First(c => c.ColumnName != "__az_rowid" && c.DataType == typeof(long) && !c.ColumnName.Contains("Id", StringComparison.OrdinalIgnoreCase)).ColumnName;
                dbRow = Convert.ToInt64(dt.Rows[0]["__az_rowid"]); expectedDb = Convert.ToInt64(dt.Rows[0][dbColumn]) + 1;
                s.UpdateCell("Career_Garage", dbRow, dbColumn, expectedDb);
                expectedFlags = s.VersionFlags + 1; s.VersionFlags = expectedFlags;
                changedPath = s.SaveNewRevision();
                Check(Path.GetFileName(changedPath) == "00000000000002e3", "New revision uses the next hexadecimal revision number");
                bool overwriteRejected = false; try { s.SaveToNewFolder(changedPath); } catch (IOException) { overwriteRejected = true; }
                Check(overwriteRejected, "Existing destination revisions cannot be overwritten");
            }
            using (var edited = new SaveSession(Path.Combine(changedPath, "data")))
            {
                Check(edited.Profile.Values.Single(v => v.Name == "Main/Credits").Value == expectedCredit, "Edited credits survive encryption and reopening");
                Check(edited.Profile.Values.First(v => v.Code == 11).Value == expectedString && edited.Profile.Values.First(v => v.Code == 12).Value == expectedWide, "Variable-length narrow and Unicode strings survive rebuilding");
                Check(unchangedValues.All(p => edited.Profile.Values.Single(v => v.Name == p.Key).Value == p.Value), "All untouched profile properties retain their values");
                Check(edited.State.Properties.Single(p => p.Object == stateObject && p.Property == stateName).Value == expectedState, "Edited BXML property survives encryption and reopening");
                var dt = edited.ReadTable("Career_Garage");
                Check(Convert.ToInt64(dt.Rows.Cast<System.Data.DataRow>().Single(r => Convert.ToInt64(r["__az_rowid"]) == dbRow)[dbColumn]) == Convert.ToInt64(expectedDb), "SQLite cell edit survives encryption and reopening");
                Check(edited.VersionFlags == expectedFlags, "VersionFlags edit survives encryption and reopening");
                Check(edited.ManifestStatus.StartsWith("Manifest verified"), "Generated commit hashes and sizes verify for every file");
            }
            using (var stale = new SaveSession(fixture))
            {
                bool refused = false; try { stale.SaveNewRevision(); } catch (IOException) { refused = true; }
                Check(refused, "Installing edits from a stale revision is refused");
            }
            FileTests.Run(fixture, destination, Check);
            BackupTests.Run(fixture, destination, Check);
            CryptoTests.Run(fixture, Check);
            Check(before.All(p => Hash(Path.Combine(source, p.Key!)) == p.Value), "Live source save files remain byte-identical");
            var report = new { result = "PASS", profileValues = values, stateProperties = properties, databaseTables = tables, checks };
            File.WriteAllText(Path.Combine(destination, "validation.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(report)); return 0;
        }
        catch (Exception e)
        {
            Directory.CreateDirectory(destination); File.WriteAllText(Path.Combine(destination, "validation-error.txt"), e.ToString()); Console.Error.WriteLine(e); return 1;
        }
    }
}
