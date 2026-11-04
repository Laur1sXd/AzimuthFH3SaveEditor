namespace AzimuthFH3;

internal static class CryptoTests
{
    public static void Run(string fixture, Action<bool, string> check)
    {
        var original = File.ReadAllBytes(Path.Combine(fixture, "VersionFlags"));
        byte[] flags = Fh3Crypto.Decrypt(original);
        check(Fh3Crypto.Encrypt(flags, original).AsSpan().SequenceEqual(original), "Built-in crypto reproduces the original encrypted VersionFlags byte for byte");
        int[] lengths = [0, 1, 15, 16, 17, 511, 512, 513, 1023, 1024, 1025, 16385];
        foreach (int length in lengths)
        {
            var bytes = new byte[length]; new Random(length + 1871).NextBytes(bytes);
            byte[] encoded = Fh3Crypto.Encrypt(bytes, original);
            if (!Fh3Crypto.Decrypt(encoded).AsSpan().SequenceEqual(bytes)) throw new Exception("Crypto round trip failed for length " + length);
        }
        check(true, "Built-in crypto handles empty, exact-block, partial-block, and multiple-block payloads");
        bool Reject(byte[] bytes) { try { Fh3Crypto.Decrypt(bytes); return false; } catch (InvalidDataException) { return true; } }
        var changedHeader = original.ToArray(); changedHeader[0] ^= 1;
        check(Reject(changedHeader), "Changed encrypted headers fail authentication");
        var changedPayload = original.ToArray(); changedPayload[36] ^= 1;
        check(Reject(changedPayload), "Changed encrypted data fails block authentication");
        var changedTag = original.ToArray(); changedTag[^1] ^= 1;
        check(Reject(changedTag), "Changed encrypted authentication tags are rejected");
        check(Reject(original[..^1]), "Truncated encrypted files are rejected");
    }
}
