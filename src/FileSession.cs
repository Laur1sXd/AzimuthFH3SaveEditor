using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace AzimuthFH3;

internal enum SaveKind { PackedProfile, Flags, Database, State }
internal sealed class SaveDocument
{
    public required string Name;
    public required byte[] Plain;
    public required SaveKind Kind;
    public bool Encrypted;
    public byte[] DatabaseHeader = [];
}

public sealed class SaveSession : IDisposable
{
    public string DataFolder { get; }
    public string? SourceFile { get; }
    public bool IsSingleFile => SourceFile != null;
    public string Revision => Directory.GetParent(DataFolder)?.Name ?? "";
    public string DisplayName => SourceFile == null ? Revision : Path.GetFileName(SourceFile);
    public string Work { get; }
    readonly Dictionary<string, byte[]> snapshot = new(StringComparer.Ordinal);
    readonly List<SaveDocument> documents = [];
    readonly List<(uint Hash, byte[] Data)> sections = [];
    string? manifestText;
    string ManifestPath => Path.Combine(Directory.GetParent(DataFolder)!.FullName, "commit");
    string databasePath = "";
    bool databaseChanged;
    PropertyBag? profile;
    BinaryXml? state;
    SqliteConnection? database;
    public bool HasProfile => profile != null;
    public bool HasState => state != null;
    public bool HasDatabase => database != null;
    public bool HasFlags { get; private set; }
    public PropertyBag Profile => profile ?? throw new InvalidOperationException("No profile is loaded.");
    public BinaryXml State => state ?? throw new InvalidOperationException("No SaveState is loaded.");
    public SqliteConnection Database => database ?? throw new InvalidOperationException("No database is loaded.");
    public List<string> Tables { get; } = [];
    public bool Dirty { get; set; }
    public uint VersionFlags { get; set; }
    public string ManifestStatus => manifestText == null ? "Standalone save files" : $"Manifest verified · {snapshot.Count} files";
    public bool CanSaveRevision => manifestText != null && Directory.GetParent(Directory.GetParent(DataFolder)!.FullName)?.Name.Equals("revisions", StringComparison.OrdinalIgnoreCase) == true && ulong.TryParse(Revision, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
    const uint ProfileHash = 0x4a8bf2b6, StateHash = 0xb60261fc, BinaryHash = 0x986ca5cb, DbHash = 0xa9f1f729;
    static readonly byte[] SqliteMagic = Encoding.ASCII.GetBytes("SQLite format 3\0");
    static readonly string[] KnownNames = ["ProfileData", "VersionFlags", "PlayerDatabase", "SaveState"];

    public static string Resolve(string input)
    {
        string path = Path.GetFullPath(input.Trim().Trim('"'));
        if (File.Exists(path)) return Path.GetDirectoryName(path)!;
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("Select a save file or save folder.");
        bool IsData(string p) => Directory.Exists(p) && Directory.EnumerateFiles(p).Any(f => KnownNames.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));
        if (IsData(path)) return path;
        if (IsData(Path.Combine(path, "data"))) return Path.Combine(path, "data");
        var candidates = Directory.EnumerateFiles(path, "ProfileData", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(path, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part.Equals(BackupLocation.FolderName, StringComparison.OrdinalIgnoreCase)))
            .Select(Path.GetDirectoryName).OfType<string>()
            .OrderByDescending(p => ulong.TryParse(Directory.GetParent(p)?.Name, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rev) ? rev : 0UL)
            .ThenByDescending(p => File.GetLastWriteTimeUtc(Path.Combine(p, "ProfileData"))).ToArray();
        if (candidates.Length == 0) throw new InvalidDataException("No supported save files were found. Use Open single file to choose a renamed or exported file.");
        return candidates[0];
    }

    public SaveSession(string input)
    {
        string full = Path.GetFullPath(input.Trim().Trim('"'));
        SourceFile = File.Exists(full) ? full : null;
        DataFolder = Resolve(full);
        Work = Path.Combine(Environment.GetEnvironmentVariable("AZIMUTH_FH3_WORK") ?? Path.GetTempPath(), "AzimuthFH3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Work);
        try
        {
            bool hasManifest = Path.GetFileName(DataFolder).Equals("data", StringComparison.OrdinalIgnoreCase) && File.Exists(ManifestPath);
            if (hasManifest)
            {
                manifestText = File.ReadAllText(ManifestPath);
                foreach (var p in ReadManifestFiles(DataFolder, manifestText)) snapshot.Add(p.Key, p.Value);
                if (SourceFile != null && !snapshot.ContainsKey(Path.GetFileName(SourceFile))) throw new InvalidDataException("The selected file is not listed in this revision's commit manifest.");
            }
            else if (SourceFile != null) snapshot.Add(Path.GetFileName(SourceFile), File.ReadAllBytes(SourceFile));
            else foreach (var p in Directory.EnumerateFiles(DataFolder)) snapshot.Add(Path.GetFileName(p), File.ReadAllBytes(p));
            var selected = SourceFile == null ? snapshot.Keys.Where(n => KnownNames.Contains(n, StringComparer.OrdinalIgnoreCase)).ToArray() : [Path.GetFileName(SourceFile)];
            foreach (string name in selected) LoadDocument(name, snapshot[name]);
            if (documents.Count == 0) throw new InvalidDataException("No supported FH3 save files were found.");
        }
        catch { Dispose(); throw; }
    }
    void LoadDocument(string name, byte[] bytes)
    {
        File.WriteAllBytes(Path.Combine(Work, "original-" + name), bytes);
        byte[] plain = bytes; bool encrypted = false;
        bool Recognized(byte[] b) => b.Length == 4 || b.AsSpan().StartsWith(SqliteMagic) || (b.Length > 8 && b.AsSpan(8).StartsWith(SqliteMagic)) || (b.Length >= 8 && (BitConverter.ToUInt32(b) == ProfileHash || BitConverter.ToUInt32(b) == 0x4c4d5842));
        if (!Recognized(plain))
        {
            if (bytes.Length < 36 || (bytes.Length - 36) % 528 != 0 || BitConverter.ToUInt32(bytes, 16) >= 512) throw Unsupported();
            plain = Fh3Crypto.Decrypt(bytes); encrypted = true;
        }
        SaveKind kind;
        byte[] dbHeader = [];
        if (plain.Length == 4)
        {
            if (HasFlags) throw new InvalidDataException("More than one VersionFlags file was found.");
            kind = SaveKind.Flags; VersionFlags = BitConverter.ToUInt32(plain); HasFlags = true;
        }
        else if (plain.Length >= 8 && BitConverter.ToUInt32(plain) == ProfileHash)
        {
            if (profile != null || state != null || database != null) throw new InvalidDataException("This folder contains overlapping packed and separate profile sections. Open the file individually.");
            kind = SaveKind.PackedProfile;
            using var r = new BinaryReader(new MemoryStream(plain));
            while (r.BaseStream.Position < r.BaseStream.Length)
            {
                uint hash = r.ReadUInt32(); int size = r.ReadInt32();
                if (size < 0 || size > r.BaseStream.Length - r.BaseStream.Position) throw new InvalidDataException("Invalid FH3 profile section length.");
                sections.Add((hash, r.ReadBytes(size)));
            }
            if (sections.Count != 4 || sections.Select(s => s.Hash).Distinct().Count() != 4 || !new[] { ProfileHash, StateHash, BinaryHash, DbHash }.All(h => sections.Any(s => s.Hash == h))) throw new InvalidDataException("This file does not have the expected four FH3 profile sections.");
            profile = new PropertyBag(Section(ProfileHash)); state = new BinaryXml(Section(StateHash)); OpenDatabase(Section(DbHash));
        }
        else if (plain.AsSpan().StartsWith(SqliteMagic) || (plain.Length > 8 && plain.AsSpan(8).StartsWith(SqliteMagic)))
        {
            kind = SaveKind.Database;
            if (!plain.AsSpan().StartsWith(SqliteMagic)) dbHeader = plain[..8];
            OpenDatabase(plain[dbHeader.Length..]);
        }
        else if (plain.Length > 4 && BitConverter.ToUInt32(plain) == 0x4c4d5842)
        {
            if (state != null) throw new InvalidDataException("More than one SaveState section was found.");
            kind = SaveKind.State; state = new BinaryXml(plain);
        }
        else throw Unsupported();
        File.WriteAllBytes(Path.Combine(Work, name + ".plain"), plain);
        documents.Add(new SaveDocument { Name = name, Plain = plain, Kind = kind, Encrypted = encrypted, DatabaseHeader = dbHeader });
    }
    static InvalidDataException Unsupported() => new("Unsupported save format. Open an FH3 ProfileData or VersionFlags file, a plain SQLite/PlayerDatabase file, or a BXML SaveState file. Filenames may be changed; the contents must use one of these formats.");
    byte[] Section(uint hash) => sections.Single(s => s.Hash == hash).Data;
    void OpenDatabase(byte[] bytes)
    {
        if (database != null) throw new InvalidDataException("More than one database section was found.");
        databasePath = Path.Combine(Work, "career.sqlite"); File.WriteAllBytes(databasePath, bytes);
        database = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString()); database.Open();
        using (var cmd = database.CreateCommand()) { cmd.CommandText = "PRAGMA journal_mode=DELETE"; cmd.ExecuteNonQuery(); }
        CheckDatabase();
        using (var cmd = database.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using var r = cmd.ExecuteReader(); while (r.Read()) Tables.Add(r.GetString(0));
        }
    }
    static Dictionary<string, byte[]> ReadManifestFiles(string folder, string manifest)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string line in manifest.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Skip(2))
        {
            string[] p = line.Split(' ', 3);
            if (p.Length != 3 || p[2].Length == 0 || p[2] != Path.GetFileName(p[2]) || p[2].Contains('/') || p[2].Contains('\\') || !files.TryAdd(p[2], File.ReadAllBytes(Path.Combine(folder, p[2])))) throw new InvalidDataException("Invalid commit file entry.");
        }
        ValidateManifest(manifest, files); return files;
    }
    public static void ValidateManifest(string manifest, IReadOnlyDictionary<string, byte[]> files)
    {
        var lines = manifest.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        if (lines.Length < 2 || lines[0] != "Azimuth complete save revision v2" || lines[1] != "blobs " + files.Count || lines.Length != files.Count + 2) throw new InvalidDataException("The save manifest is missing entries or has an unsupported header.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in lines.Skip(2))
        {
            var p = line.Split(' ', 3);
            if (p.Length != 3 || !seen.Add(p[2]) || !files.TryGetValue(p[2], out var data) || p[1] != data.Length.ToString(CultureInfo.InvariantCulture) || !Convert.ToHexString(SHA256.HashData(data)).Equals(p[0], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Save manifest verification failed. Close the game and select a complete revision.");
        }
    }
    public static string Manifest(IReadOnlyDictionary<string, byte[]> files) => "Azimuth complete save revision v2\nblobs " + files.Count + "\n" + string.Concat(files.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Convert.ToHexString(SHA256.HashData(p.Value)).ToLowerInvariant() + " " + p.Value.Length.ToString(CultureInfo.InvariantCulture) + " " + p.Key + "\n"));
    public static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    public DataTable ReadTable(string name)
    {
        if (!Tables.Contains(name)) throw new ArgumentException("Unknown table.");
        DataTable Read(string sql) { using var cmd = Database.CreateCommand(); cmd.CommandText = sql; using var r = cmd.ExecuteReader(); var dt = new DataTable(); dt.Load(r); return dt; }
        try { return Read($"SELECT rowid AS __az_rowid, * FROM {Quote(name)}"); }
        catch (SqliteException) { return Read($"SELECT * FROM {Quote(name)}"); }
    }
    public void UpdateCell(string table, long row, string column, object? value)
    {
        if (!Tables.Contains(table)) throw new ArgumentException("Unknown table.");
        using var tx = Database.BeginTransaction(); using var cmd = Database.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = $"UPDATE {Quote(table)} SET {Quote(column)}=$value WHERE rowid=$row";
        cmd.Parameters.AddWithValue("$value", value ?? DBNull.Value); cmd.Parameters.AddWithValue("$row", row);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("The database row was not uniquely updated.");
        tx.Commit(); databaseChanged = true; Dirty = true;
    }
    public void CheckDatabase()
    {
        using var cmd = Database.CreateCommand(); cmd.CommandText = "PRAGMA integrity_check";
        if (Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) != "ok") throw new InvalidDataException("SQLite integrity check failed.");
    }
    public byte[] DatabaseBytes()
    {
        if (!databaseChanged) return documents.FirstOrDefault(d => d.Kind == SaveKind.Database) is { } d ? d.Plain[d.DatabaseHeader.Length..].ToArray() : Section(DbHash).ToArray();
        CheckDatabase(); Database.Close();
        try { return File.ReadAllBytes(databasePath); } finally { Database.Open(); }
    }
    public byte[] BuildPlain()
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms, Encoding.UTF8, true);
        foreach (var (hash, data) in sections)
        {
            var body = hash switch { ProfileHash => Profile.Build(), StateHash => State.Build(), DbHash => DatabaseBytes(), _ => data };
            w.Write(hash); w.Write(body.Length); w.Write(body);
        }
        return ms.ToArray();
    }
    byte[] BuildDocument(SaveDocument d) => d.Kind switch
    {
        SaveKind.PackedProfile => BuildPlain(), SaveKind.Flags => BitConverter.GetBytes(VersionFlags),
        SaveKind.State => State.Build(), SaveKind.Database => [.. d.DatabaseHeader, .. DatabaseBytes()], _ => throw Unsupported()
    };
    byte[] Encode(SaveDocument d)
    {
        byte[] plain = BuildDocument(d);
        if (d.Kind == SaveKind.PackedProfile)
        {
            using var r = new BinaryReader(new MemoryStream(plain));
            while (r.BaseStream.Position < r.BaseStream.Length)
            {
                uint h = r.ReadUInt32(); int n = r.ReadInt32(); byte[] b = r.ReadBytes(n);
                if (b.Length != n) throw new InvalidDataException("Rebuilt profile section is truncated.");
                if (h == ProfileHash) _ = new PropertyBag(b); if (h == StateHash) _ = new BinaryXml(b);
            }
        }
        if (!d.Encrypted) return plain;
        if (plain.AsSpan().SequenceEqual(d.Plain)) return snapshot[d.Name].ToArray();
        byte[] output = Fh3Crypto.Encrypt(plain, snapshot[d.Name]);
        if (!plain.AsSpan().SequenceEqual(Fh3Crypto.Decrypt(output))) throw new InvalidDataException("Re-encryption verification failed. No save was written.");
        return output;
    }
    public Dictionary<string, byte[]> BuildEncrypted()
    {
        var files = snapshot.ToDictionary(p => p.Key, p => p.Value.ToArray(), StringComparer.Ordinal);
        foreach (var d in documents) files[d.Name] = Encode(d);
        return files;
    }
    void CheckSource()
    {
        foreach (var f in snapshot)
            if (!File.ReadAllBytes(Path.Combine(DataFolder, f.Key)).AsSpan().SequenceEqual(f.Value)) throw new IOException("The loaded save has changed on disk. Close the game and reopen it before saving.");
        if (manifestText != null && File.ReadAllText(ManifestPath) != manifestText) throw new IOException("The save's commit manifest has changed. Reopen the save before saving.");
    }
    public string SaveAll()
    {
        CheckSource(); var files = BuildEncrypted(); CheckSource();
        string backup = WriteFiles(DataFolder, files, manifestText != null);
        foreach (var d in documents) d.Plain = BuildDocument(d);
        foreach (var p in files) { snapshot[p.Key] = p.Value; File.WriteAllBytes(Path.Combine(Work, "original-" + p.Key), p.Value); }
        if (manifestText != null) manifestText = Manifest(files);
        Dirty = false; return backup;
    }
    public string SaveSingleAs(string destination)
    {
        if (!IsSingleFile) throw new InvalidOperationException("Use Save all to folder for a folder session.");
        destination = Path.GetFullPath(destination);
        if (destination.Equals(SourceFile, StringComparison.OrdinalIgnoreCase)) return SaveAll();
        var d = documents.Single(); var files = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [Path.GetFileName(destination)] = Encode(d) };
        return WriteFiles(Path.GetDirectoryName(destination)!, files, false);
    }
    public string SaveAllTo(string folder)
    {
        folder = Path.GetFullPath(folder);
        if (folder.Equals(DataFolder, StringComparison.OrdinalIgnoreCase)) return SaveAll();
        return WriteFiles(folder, BuildEncrypted(), manifestText != null && Path.GetFileName(folder).Equals("data", StringComparison.OrdinalIgnoreCase));
    }
    string WriteFiles(string folder, Dictionary<string, byte[]> updates, bool createManifest)
    {
        Directory.CreateDirectory(folder);
        string commit = Path.Combine(Directory.GetParent(folder)!.FullName, "commit");
        bool hasCommit = Path.GetFileName(folder).Equals("data", StringComparison.OrdinalIgnoreCase) && File.Exists(commit);
        Dictionary<string, byte[]> result = hasCommit ? ReadManifestFiles(folder, File.ReadAllText(commit)) : new(StringComparer.Ordinal);
        foreach (var p in updates) result[p.Key] = p.Value;
        var writes = updates.ToDictionary(p => Path.Combine(folder, p.Key), p => p.Value, StringComparer.OrdinalIgnoreCase);
        if (hasCommit || createManifest) { string m = Manifest(result); ValidateManifest(m, result); writes[commit] = new UTF8Encoding(false).GetBytes(m); }
        var changes = writes.Where(p => !File.Exists(p.Key) || !File.ReadAllBytes(p.Key).AsSpan().SequenceEqual(p.Value)).ToArray();
        if (changes.Length == 0) return "";
        string backupRoot = BackupLocation.ForSave(folder, DataFolder);
        string backup = changes.Any(p => File.Exists(p.Key)) ? Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]) : "";
        if (backup.Length > 0) Directory.CreateDirectory(backup);
        var old = new Dictionary<string, byte[]?>(); var applied = new List<string>(); var temps = new List<string>();
        foreach (var p in changes)
        {
            old[p.Key] = File.Exists(p.Key) ? File.ReadAllBytes(p.Key) : null;
            if (old[p.Key] is { } bytes) File.WriteAllBytes(Path.Combine(backup, Path.GetFileName(p.Key)), bytes);
        }
        if (backup.Length > 0) File.WriteAllText(Path.Combine(backup, "restore-info.txt"), "Original locations:\n" + string.Join("\n", old.Keys));
        try
        {
            foreach (var p in changes)
            {
                string tmp = Path.Combine(Path.GetDirectoryName(p.Key)!, ".fh3-write-" + Guid.NewGuid().ToString("N")); temps.Add(tmp);
                File.WriteAllBytes(tmp, p.Value); File.Move(tmp, p.Key, true); applied.Add(p.Key);
            }
        }
        catch
        {
            foreach (string p in applied.AsEnumerable().Reverse())
            {
                if (old[p] is { } bytes) { string restore = p + ".restore-" + Guid.NewGuid().ToString("N"); File.WriteAllBytes(restore, bytes); File.Move(restore, p, true); }
                else File.Delete(p);
            }
            throw;
        }
        finally { foreach (string tmp in temps) if (File.Exists(tmp)) File.Delete(tmp); }
        return backup;
    }
    public string SaveNewRevision()
    {
        if (!CanSaveRevision) throw new InvalidOperationException("Open a save inside a numbered Azimuth revision, or use Save / Save as.");
        string revisions = Directory.GetParent(Directory.GetParent(DataFolder)!.FullName)!.FullName;
        ulong highest = Directory.EnumerateDirectories(revisions).Select(Path.GetFileName).Select(n => ulong.TryParse(n, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0UL).Max();
        if (highest != ulong.Parse(Revision, NumberStyles.HexNumber, CultureInfo.InvariantCulture)) throw new IOException("A newer revision exists. Close FH3 and reopen the latest save before saving a new revision.");
        CheckSource(); return SaveToNewFolder(Path.Combine(revisions, checked(highest + 1).ToString("x16", CultureInfo.InvariantCulture)));
    }
    public string SaveToNewFolder(string target)
    {
        target = Path.GetFullPath(target);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Choose a new revision folder. Existing folders are never overwritten.");
        string parent = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("The destination's parent folder must exist.");
        var files = BuildEncrypted(); string staging = Path.Combine(parent, ".fh3-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(staging, "data"));
        try
        {
            foreach (var f in files) File.WriteAllBytes(Path.Combine(staging, "data", f.Key), f.Value);
            string manifest = Manifest(files); ValidateManifest(manifest, files);
            File.WriteAllText(Path.Combine(staging, "commit"), manifest, new UTF8Encoding(false)); Directory.Move(staging, target); return target;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
    public void Dispose()
    {
        database?.Dispose();
        try { if (Directory.Exists(Work)) Directory.Delete(Work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
