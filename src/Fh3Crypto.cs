using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace AzimuthFH3;

/// <summary>In-process FH3 profile TFIT/CBC and CMAC. No process, console, or network calls.</summary>
internal static class Fh3Crypto
{
    const int HeaderSize = 36, DataSize = 512, RecordSize = 528;
    static readonly byte[] Direct = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
    static readonly byte[] Forward = [0, 5, 10, 15, 3, 4, 9, 14, 2, 7, 8, 13, 1, 6, 11, 12];
    static readonly byte[] Reverse = [0, 7, 10, 13, 1, 4, 11, 14, 2, 5, 8, 15, 3, 6, 9, 12];
    static readonly Lazy<Context> Tables = new(LoadConstants);
    sealed record Context(uint[] DecodeKeys, uint[] EncodeKeys, uint[] AuthKeys, uint[] Decode, uint[] Encode, uint[] Auth);

    static Context LoadConstants()
    {
        using var resource = typeof(Fh3Crypto).Assembly.GetManifestResourceStream("AzimuthFH3.Fh3Tfit.bin.gz") ?? throw new InvalidDataException("Embedded FH3 crypto constants are missing.");
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip);
        if (!reader.ReadBytes(8).AsSpan().SequenceEqual("AZFH3TF1"u8)) throw new InvalidDataException("Embedded FH3 crypto constants have an invalid header.");
        uint[] Read(int length) { var values = new uint[length]; for (int i = 0; i < length; i++) values[i] = reader.ReadUInt32(); return values; }
        var result = new Context(Read(68), Read(68), Read(52), Read(17 * 4096), Read(17 * 4096), Read(13 * 4096));
        if (gzip.ReadByte() != -1) throw new InvalidDataException("Unexpected FH3 crypto data.");
        return result;
    }

    // Round layouts are the documented Forza TFIT permutation; all arithmetic is uint32 XOR.
    static void Transform(ReadOnlySpan<byte> source, Span<byte> destination, uint[] table, uint[] keys, byte[] permutation)
    {
        Span<byte> state = stackalloc byte[16]; source[..16].CopyTo(state);
        Span<uint> words = stackalloc uint[4]; int rounds = keys.Length / 4;
        for (int round = 0; round < rounds; round++)
        {
            byte[] order = round < 2 || round == rounds - 1 ? Direct : permutation;
            for (int word = 0; word < 4; word++)
            {
                uint value = keys[round * 4 + word];
                for (int lane = 0; lane < 4; lane++)
                {
                    int index = order[word * 4 + lane]; value ^= table[round * 4096 + index * 256 + state[index]];
                }
                words[word] = value;
            }
            for (int word = 0; word < 4; word++) BinaryPrimitives.WriteUInt32LittleEndian(state[(word * 4)..], words[word]);
        }
        state.CopyTo(destination);
    }
    static void Double(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        int carry = 0;
        for (int i = 15; i >= 0; i--) { int value = source[i]; destination[i] = (byte)((value << 1) | carry); carry = value >> 7; }
        if (carry != 0) destination[15] ^= 0x87;
    }
    static void Authenticate(ReadOnlySpan<byte> message, Span<byte> tag)
    {
        Context t = Tables.Value;
        Span<byte> zero = stackalloc byte[16]; zero.Clear();
        Span<byte> subkey = stackalloc byte[16], first = stackalloc byte[16], second = stackalloc byte[16];
        Transform(zero, subkey, t.Auth, t.AuthKeys, Forward); Double(subkey, first); Double(first, second);
        Span<byte> chain = stackalloc byte[16], block = stackalloc byte[16]; chain.Clear();
        int blocks = Math.Max(1, (message.Length + 15) / 16);
        for (int n = 0; n < blocks; n++)
        {
            block.Clear(); int length = Math.Min(16, message.Length - n * 16);
            if (length > 0) message.Slice(n * 16, length).CopyTo(block);
            if (n == blocks - 1)
            {
                bool full = length == 16;
                if (!full) block[length] = 0x80;
                ReadOnlySpan<byte> key = full ? first : second;
                for (int i = 0; i < 16; i++) block[i] ^= key[i];
            }
            for (int i = 0; i < 16; i++) block[i] ^= chain[i];
            Transform(block, chain, t.Auth, t.AuthKeys, Forward);
        }
        chain.CopyTo(tag);
    }
    static int ValidateHeader(ReadOnlySpan<byte> file)
    {
        if (file.Length < HeaderSize || (file.Length - HeaderSize) % RecordSize != 0) throw new InvalidDataException("The encrypted FH3 save is truncated or has an invalid size.");
        int dataLength = checked((file.Length - HeaderSize) / RecordSize * DataSize);
        uint unused = BinaryPrimitives.ReadUInt32LittleEndian(file[16..]);
        if (unused >= DataSize || unused > dataLength) throw new InvalidDataException("The encrypted FH3 save has invalid padding.");
        Span<byte> message = stackalloc byte[24], tag = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(message, (uint)dataLength); file[..20].CopyTo(message[4..]); Authenticate(message, tag);
        if (!CryptographicOperations.FixedTimeEquals(tag, file.Slice(20, 16))) throw new InvalidDataException("The file is not a supported FH3 profile save, or its header is damaged.");
        return dataLength - (int)unused;
    }
    public static byte[] Decrypt(ReadOnlySpan<byte> file)
    {
        int length = ValidateHeader(file); var plain = new byte[length]; Context t = Tables.Value;
        Span<byte> previous = stackalloc byte[16], scratch = stackalloc byte[16], tag = stackalloc byte[16]; file[..16].CopyTo(previous);
        Span<byte> record = stackalloc byte[RecordSize]; int output = 0;
        for (int offset = HeaderSize; offset < file.Length; offset += RecordSize)
        {
            for (int block = 0; block < RecordSize; block += 16)
            {
                ReadOnlySpan<byte> cipher = file.Slice(offset + block, 16);
                Transform(cipher, scratch, t.Decode, t.DecodeKeys, Reverse);
                for (int i = 0; i < 16; i++) record[block + i] = (byte)(scratch[i] ^ previous[i]);
                cipher.CopyTo(previous);
            }
            Authenticate(record[..DataSize], tag);
            if (!CryptographicOperations.FixedTimeEquals(tag, record[DataSize..])) throw new InvalidDataException($"FH3 save authentication failed in data block {(offset - HeaderSize) / RecordSize + 1}. No file was changed.");
            int copy = Math.Min(DataSize, length - output); record[..copy].CopyTo(plain.AsSpan(output)); output += copy;
        }
        return plain;
    }
    public static byte[] Encrypt(ReadOnlySpan<byte> plain, ReadOnlySpan<byte> original)
    {
        ValidateHeader(original);
        int blocks = checked((int)(((long)plain.Length + DataSize - 1) / DataSize));
        var encoded = new byte[checked(HeaderSize + blocks * RecordSize)];
        original[..16].CopyTo(encoded); BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(16), (uint)(blocks * DataSize - plain.Length));
        Span<byte> message = stackalloc byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(message, (uint)(blocks * DataSize)); encoded.AsSpan(0, 20).CopyTo(message[4..]); Authenticate(message, encoded.AsSpan(20, 16));
        Context t = Tables.Value; Span<byte> previous = stackalloc byte[16], scratch = stackalloc byte[16], record = stackalloc byte[RecordSize];
        original[..16].CopyTo(previous);
        for (int n = 0; n < blocks; n++)
        {
            record.Clear(); plain.Slice(n * DataSize, Math.Min(DataSize, plain.Length - n * DataSize)).CopyTo(record);
            Authenticate(record[..DataSize], record[DataSize..]);
            for (int block = 0; block < RecordSize; block += 16)
            {
                for (int i = 0; i < 16; i++) scratch[i] = (byte)(record[block + i] ^ previous[i]);
                Span<byte> destination = encoded.AsSpan(HeaderSize + n * RecordSize + block, 16);
                Transform(scratch, destination, t.Encode, t.EncodeKeys, Forward); destination.CopyTo(previous);
            }
        }
        return encoded;
    }
}
