using System.Globalization;
using System.Text;

namespace AzimuthFH3;

public sealed class ProfileValue
{
    internal int Offset, Length;
    internal byte[]? Replacement;
    public string Name { get; init; } = "";
    public int Code { get; init; }
    public string Value { get; private set; } = "";
    public string Type => Code switch { 0 => "bool", 1 => "uint8", 2 => "uint16", 3 => "uint32", 4 => "uint64", 5 => "int8", 6 => "int16", 7 => "int32", 8 => "int64", 9 => "float32", 10 => "float64", 11 => "string", 12 => "wide string", 17 => "uint32 (type 17)", _ => "unknown" };
    internal void Initialize(string value) => Value = value;
    public void Set(string text)
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms, Encoding.UTF8, true);
        var c = CultureInfo.InvariantCulture; var s = text.Trim();
        switch (Code)
        {
            case 0: w.Write(s.ToLowerInvariant() switch { "true" or "1" => (byte)1, "false" or "0" => (byte)0, _ => throw new FormatException("Enter true, false, 1, or 0.") }); break;
            case 1: w.Write(byte.Parse(s, c)); break;
            case 2: w.Write(ushort.Parse(s, c)); break;
            case 3: case 17: w.Write(uint.Parse(s, c)); break;
            case 4: w.Write(ulong.Parse(s, c)); break;
            case 5: w.Write(sbyte.Parse(s, c)); break;
            case 6: w.Write(short.Parse(s, c)); break;
            case 7: w.Write(int.Parse(s, c)); break;
            case 8: w.Write(long.Parse(s, c)); break;
            case 9: var f = float.Parse(s, c); if (!float.IsFinite(f)) throw new FormatException("Enter a finite number."); w.Write(f); break;
            case 10: var d = double.Parse(s, c); if (!double.IsFinite(d)) throw new FormatException("Enter a finite number."); w.Write(d); break;
            case 11: var b = new UTF8Encoding(false, true).GetBytes(text); w.Write(b.Length); w.Write(b); break;
            case 12: var u = new UnicodeEncoding(false, false, true).GetBytes(text); w.Write(u.Length / 2); w.Write(u); break;
            default: throw new InvalidDataException("Unsupported property type.");
        }
        Replacement = ms.ToArray(); Value = Code is 11 or 12 ? text : s;
    }
}

public sealed class PropertyBag
{
    readonly byte[] original;
    public List<ProfileValue> Values { get; } = [];
    int nodes;
    public PropertyBag(byte[] data)
    {
        original = data;
        using var r = new BinaryReader(new MemoryStream(data));
        ReadBag(r, "", 0);
    }
    static int Count(BinaryReader r, int max)
    {
        var n = r.ReadInt32(); if (n < 0 || n > max) throw new InvalidDataException("Invalid property count or string length."); return n;
    }
    static byte[] Bytes(BinaryReader r, int n)
    {
        var b = r.ReadBytes(n); if (b.Length != n) throw new EndOfStreamException(); return b;
    }
    static string ReadString(BinaryReader r, bool wide = false)
    {
        int n = Count(r, 1_000_000); return (wide ? (Encoding)new UnicodeEncoding(false, false, true) : new UTF8Encoding(false, true)).GetString(Bytes(r, checked(n * (wide ? 2 : 1))));
    }
    void ReadBag(BinaryReader r, string parent, int depth)
    {
        if (depth > 64) throw new InvalidDataException("Property tree is too deep.");
        int count = Count(r, 100_000);
        for (int i = 0; i < count; i++)
        {
            if (++nodes > 100_000) throw new InvalidDataException("Too many profile nodes.");
            r.ReadByte(); r.ReadInt32(); // CHashName case sensitivity and hash bit count
            string name = ReadString(r); string path = parent.Length == 0 ? name : parent + "/" + name;
            int type = r.ReadInt32();
            if (type == 15) { ReadBag(r, path, depth + 1); continue; }
            int offset = checked((int)r.BaseStream.Position);
            object value = type switch
            {
                0 or 1 => r.ReadByte(), 2 => r.ReadUInt16(), 3 or 17 => r.ReadUInt32(), 4 => r.ReadUInt64(),
                5 => r.ReadSByte(), 6 => r.ReadInt16(), 7 => r.ReadInt32(), 8 => r.ReadInt64(),
                9 => r.ReadSingle(), 10 => r.ReadDouble(), 11 => ReadString(r), 12 => ReadString(r, true),
                _ => throw new InvalidDataException($"Unsupported type {type} at {path}. No save has been changed.")
            };
            var leaf = new ProfileValue { Name = path, Code = type, Offset = offset, Length = checked((int)r.BaseStream.Position - offset) };
            leaf.Initialize(value is float f ? f.ToString("R", CultureInfo.InvariantCulture) : value is double d ? d.ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture)!);
            Values.Add(leaf);
        }
    }
    public byte[] Build()
    {
        using var ms = new MemoryStream(); int p = 0;
        foreach (var v in Values.Where(v => v.Replacement != null).OrderBy(v => v.Offset))
        {
            ms.Write(original.AsSpan(p, v.Offset - p)); ms.Write(v.Replacement!); p = v.Offset + v.Length;
        }
        ms.Write(original.AsSpan(p)); return ms.ToArray();
    }
}

// BXML v2 structure follows Nenkai's MIT-licensed ForzaTools.BinaryXML.
// Preserve original string indexes and append edited strings so shared values stay independent.
public sealed class StateProperty
{
    public string Object { get; init; } = "";
    public string Property { get; init; } = "";
    public string Value { get => Attribute.Value; set => Attribute.Value = value; }
    internal BAttribute Attribute { get; init; } = null!;
}
internal sealed class BAttribute { public int NameIndex, ValueIndex; public string Value = "", Original = ""; }
internal sealed class BNode { public byte Flags; public int NameIndex; public List<BAttribute> Attributes = []; public List<BNode> Children = []; }

public sealed class BinaryXml
{
    readonly byte[] original;
    readonly List<string> strings = [];
    readonly List<byte[]> encodedStrings = [];
    readonly BNode root;
    readonly byte version, rootFlag;
    int nodeCount;
    public List<StateProperty> Properties { get; } = [];
    public BinaryXml(byte[] data)
    {
        original = data; using var r = new BinaryReader(new MemoryStream(data));
        if (r.ReadUInt32() != 0x4c4d5842) throw new InvalidDataException("SaveState is not BXML.");
        version = r.ReadByte(); if (version != 2) throw new InvalidDataException("Only BXML version 2 is supported.");
        int count = r.ReadInt32(), size = r.ReadInt32();
        if (count < 1 || count > 1_000_000 || size < 0 || size > data.Length - 13) throw new InvalidDataException("Invalid BXML string table.");
        for (int i = 0; i < count; i++)
        {
            int length = r.ReadUInt16(); var b = r.ReadBytes(length); if (b.Length != length) throw new EndOfStreamException();
            strings.Add(new UTF8Encoding(false, true).GetString(b)); encodedStrings.Add(b);
        }
        if (r.BaseStream.Position != 13L + size) throw new InvalidDataException("BXML string table size mismatch.");
        rootFlag = r.ReadByte(); root = ReadNode(r, 0);
        if (r.BaseStream.Position != data.Length) throw new InvalidDataException("Unexpected BXML tail.");
        Collect(root, "");
    }
    int ReadIndex(BinaryReader r)
    {
        int i = strings.Count > 65535 ? r.ReadInt32() : strings.Count > 255 ? r.ReadUInt16() : r.ReadByte();
        if (i < 0 || i >= strings.Count) throw new InvalidDataException("BXML index out of range."); return i;
    }
    BNode ReadNode(BinaryReader r, int depth)
    {
        if (depth > 128 || ++nodeCount > 200_000) throw new InvalidDataException("BXML tree is too large.");
        var n = new BNode { Flags = r.ReadByte(), NameIndex = ReadIndex(r) };
        if ((n.Flags & ~7) != 0) throw new InvalidDataException("Unknown BXML node flags.");
        if ((n.Flags & 2) != 0)
        {
            int count = r.ReadByte();
            for (int i = 0; i < count; i++) { int key = ReadIndex(r), val = ReadIndex(r); n.Attributes.Add(new BAttribute { NameIndex = key, ValueIndex = val, Original = strings[val], Value = strings[val] }); }
        }
        if ((n.Flags & 4) != 0) { int count = r.ReadUInt16(); for (int i = 0; i < count; i++) n.Children.Add(ReadNode(r, depth + 1)); }
        return n;
    }
    void Collect(BNode n, string owner)
    {
        string? Attr(string name) => n.Attributes.FirstOrDefault(a => strings[a.NameIndex] == name)?.Value;
        if (strings[n.NameIndex] == "Object") owner = Attr("Id") ?? owner;
        if (strings[n.NameIndex] == "Property")
        {
            var a = n.Attributes.FirstOrDefault(a => strings[a.NameIndex] == "Value");
            if (a != null) Properties.Add(new StateProperty { Object = owner, Property = Attr("Name") ?? "", Attribute = a });
        }
        foreach (var child in n.Children) Collect(child, owner);
    }
    public byte[] Build()
    {
        if (Properties.All(p => p.Attribute.Value == p.Attribute.Original)) return original.ToArray();
        var all = strings.ToList(); var encoded = encodedStrings.ToList(); var replacements = new Dictionary<BAttribute, int>();
        foreach (var p in Properties.Where(p => p.Attribute.Value != p.Attribute.Original))
        {
            byte[] b = new UTF8Encoding(false, true).GetBytes(p.Value); if (b.Length > ushort.MaxValue) throw new InvalidDataException("SaveState value is too long.");
            replacements[p.Attribute] = all.Count; all.Add(p.Value); encoded.Add(b);
        }
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms, Encoding.UTF8, true);
        w.Write(0x4c4d5842u); w.Write(version); w.Write(all.Count); w.Write(encoded.Sum(b => checked(2 + b.Length)));
        foreach (var b in encoded) { w.Write((ushort)b.Length); w.Write(b); }
        w.Write(rootFlag);
        void Index(int i) { if (all.Count > 65535) w.Write(i); else if (all.Count > 255) w.Write((ushort)i); else w.Write((byte)i); }
        void Node(BNode n)
        {
            w.Write(n.Flags); Index(n.NameIndex);
            if ((n.Flags & 2) != 0) { w.Write((byte)n.Attributes.Count); foreach (var a in n.Attributes) { Index(a.NameIndex); Index(replacements.GetValueOrDefault(a, a.ValueIndex)); } }
            if ((n.Flags & 4) != 0) { w.Write((ushort)n.Children.Count); foreach (var c in n.Children) Node(c); }
        }
        Node(root); return ms.ToArray();
    }
}
