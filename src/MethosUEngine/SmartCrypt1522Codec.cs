using System.Text;

namespace MethosUEngine;

/// <summary>
/// SmartCrypt per-file state. For the FF ED FA CE variant, the 24-byte seed block
/// is embedded in the encrypted file itself, so the key arrays can be reconstructed
/// without dsetup.u when the external-key flag bit is clear.
/// </summary>
public sealed class SmartCryptKeyProfile
{
    private static readonly byte[] DefaultMarker = { 0xFF, 0xED, 0xFA, 0xCE };

    public byte[] Key6 { get; }
    public byte[] Key16 { get; }
    public byte Flag { get; }
    public byte Seed0 { get; }
    public byte Seed1 { get; }
    public uint GlobalKey { get; }
    public string Origin { get; }
    public byte[] SeedBlock { get; }
    public byte[] Marker { get; }
    public int Lineage2Version { get; }
    public bool CanReencode => SeedBlock.Length == 24 && Marker.Length == 4;

    public bool UsesC5Variant => Flag != 0;

    public SmartCryptKeyProfile(ReadOnlySpan<byte> key6, ReadOnlySpan<byte> key16,
        byte flag, uint globalKey = 0, byte seed0 = 0, byte seed1 = 0,
        string origin = "runtime-capture", ReadOnlySpan<byte> seedBlock = default,
        ReadOnlySpan<byte> marker = default, int lineage2Version = 111)
    {
        if (key6.Length != 6) throw new ArgumentException("SmartCrypt key6 must contain exactly 6 bytes.", nameof(key6));
        if (key16.Length != 16) throw new ArgumentException("SmartCrypt key16 must contain exactly 16 bytes.", nameof(key16));
        if (!seedBlock.IsEmpty && seedBlock.Length != 24)
            throw new ArgumentException("SmartCrypt seed block must contain exactly 24 bytes.", nameof(seedBlock));
        if (!marker.IsEmpty && marker.Length != 4)
            throw new ArgumentException("SmartCrypt marker must contain exactly 4 bytes.", nameof(marker));
        Key6 = key6.ToArray();
        Key16 = key16.ToArray();
        Flag = flag;
        GlobalKey = globalKey;
        Seed0 = seed0;
        Seed1 = seed1;
        Origin = string.IsNullOrWhiteSpace(origin) ? "imported" : origin.Trim();
        SeedBlock = seedBlock.ToArray();
        Marker = marker.IsEmpty ? DefaultMarker.ToArray() : marker.ToArray();
        Lineage2Version = lineage2Version;
    }

    public static SmartCryptKeyProfile FromEncryptedFile(ReadOnlySpan<byte> data,
        uint? externalGlobalKey = null, string origin = "file-header")
    {
        if (data.Length < 0x400 || !SmartCrypt1522Codec.HasHeader(data))
            throw new InvalidDataException("File does not contain a supported SmartCrypt 1.5.2.2 FF ED FA CE header.");

        byte[] seed = data.Slice(4, 24).ToArray();
        byte state0 = seed[0];
        byte state1 = seed[1];
        byte[] state = seed.ToArray();

        // The NPK initializer only mixes the external dsetup.u CRC key when bit 0
        // of state[1] is set and the external-key activation flag is enabled.
        if ((state1 & 1) != 0)
        {
            if (!externalGlobalKey.HasValue)
                throw new InvalidDataException("This SmartCrypt file requests the external dsetup.u key; a matching global key is required.");
            byte[] global = BitConverter.GetBytes(externalGlobalKey.Value);
            for (int i = 0; i < 6; i++)
                state[2 + i] ^= global[i & 3];
        }

        // Marker CE FA ED FF selects state[0] + 1 for the six-byte key seed.
        byte keyXor = unchecked((byte)(state0 + 1));
        for (int i = 0; i < 6; i++)
            state[2 + i] ^= keyXor;

        byte[] key6 = state.AsSpan(2, 6).ToArray();
        byte[] key16 = state.AsSpan(8, 16).ToArray();
        for (int i = 0; i < 16; i++)
            key16[i] ^= key6[i % 6];

        int version = state0 switch
        {
            1 or 0x6F => 111,
            2 or 0x79 => 121,
            3 => 413,
            _ => throw new InvalidDataException($"Unsupported SmartCrypt Lineage II version selector: 0x{state0:X2}.")
        };

        return new SmartCryptKeyProfile(key6, key16, 0, externalGlobalKey ?? 0,
            state0, state1, origin, seed, data[..4], version);
    }

    public byte[] BuildLineageHeader() =>
        Encoding.Unicode.GetBytes($"Lineage2Ver{Lineage2Version:D3}");

    public static SmartCryptKeyProfile ParseLog(string text, string origin = "runtime-capture")
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] key6 = ParseHex(text, "KEY6", 6);
        byte[] key16 = ParseHex(text, "KEY16", 16);
        byte[] seed = ParseHex(text, "SEED_BLOCK", 24);
        byte[] marker = ParseHex(text, "MARKER", 4);
        byte[] flagBytes = ParseHex(text, "FLAG", 1);
        uint global = ParseUInt32Hex(text, "NPK_GLOBAL_KEY");
        int version = (int)ParseUInt32Hex(text, "LINEAGE2_VERSION");
        return new SmartCryptKeyProfile(key6, key16, flagBytes[0], global,
            seed[0], seed[1], origin, seed, marker, version);
    }

    private static byte[] ParseHex(string text, string name, int expectedBytes)
    {
        string needle = name + "=";
        int start = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (start < 0) throw new InvalidDataException($"SmartCrypt profile is missing {name}.");
        start += needle.Length;
        int end = start;
        while (end < text.Length && Uri.IsHexDigit(text[end])) end++;
        if (end - start != expectedBytes * 2)
            throw new InvalidDataException($"SmartCrypt profile {name} must contain {expectedBytes * 2} hexadecimal characters.");
        try { return Convert.FromHexString(text.AsSpan(start, end - start)); }
        catch (FormatException ex) { throw new InvalidDataException($"SmartCrypt profile {name} is invalid.", ex); }
    }

    private static uint ParseUInt32Hex(string text, string name)
    {
        string needle = name + "=";
        int start = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (start < 0) throw new InvalidDataException($"SmartCrypt profile is missing {name}.");
        start += needle.Length;
        int end = start;
        while (end < text.Length && Uri.IsHexDigit(text[end])) end++;
        if (end - start != 8 || !uint.TryParse(text.AsSpan(start, 8),
            System.Globalization.NumberStyles.HexNumber, null, out uint value))
            throw new InvalidDataException($"SmartCrypt profile {name} is invalid.");
        return value;
    }
}

/// <summary>
/// SmartCrypt 1.5.2.2 NPK ReadFile transform. The first 28 bytes are a per-file
/// seed block on disk and become a Lineage2Ver header in memory. XOR starts at 0x1C.
/// </summary>
public sealed class SmartCrypt1522Codec : IProtectionCodec
{
    private static readonly byte[] Magic = { 0xFF, 0xED, 0xFA, 0xCE };
    private readonly SmartCryptKeyProfile? _profile;

    public SmartCrypt1522Codec(SmartCryptKeyProfile? profile = null) => _profile = profile;

    public string LayerId => "smartcrypt-1.5.2.2";

    public static bool HasHeader(ReadOnlySpan<byte> data) =>
        data.Length >= Magic.Length && data[..Magic.Length].SequenceEqual(Magic);

    public byte[] Decode(ReadOnlyMemory<byte> input, string originalFileName,
        CancellationToken cancellationToken = default)
    {
        if (input.Length < 0x400 || !HasHeader(input.Span))
            throw new InvalidDataException("File is not a supported SmartCrypt 1.5.2.2 container.");

        SmartCryptKeyProfile profile = _profile ?? SmartCryptKeyProfile.FromEncryptedFile(input.Span);
        if (!profile.Marker.AsSpan().SequenceEqual(input.Span[..4]))
            throw new InvalidDataException("SmartCrypt profile marker does not match this file.");

        byte[] output = input.ToArray();
        byte[] header = profile.BuildLineageHeader();
        if (header.Length != 28)
            throw new InvalidDataException("SmartCrypt version header must be exactly 28 bytes.");
        header.CopyTo(output, 0);
        TransformPayload(output.AsSpan(0x1C), 0x1C, profile, cancellationToken);
        return output;
    }

    public byte[] Encode(ReadOnlyMemory<byte> input, string originalFileName,
        CancellationToken cancellationToken = default)
    {
        SmartCryptKeyProfile profile = _profile ??
            throw new NotSupportedException("SmartCrypt re-encoding requires the original per-file seed block stored in Methos metadata.");
        if (!profile.CanReencode || input.Length < 0x400)
            throw new InvalidDataException("SmartCrypt profile is incomplete or the edited file is too small.");

        byte[] expectedHeader = profile.BuildLineageHeader();
        if (input.Length < expectedHeader.Length || !input.Span[..expectedHeader.Length].SequenceEqual(expectedHeader))
            throw new InvalidDataException($"Edited intermediate must retain the original Lineage2Ver{profile.Lineage2Version:D3} header.");

        byte[] output = input.ToArray();
        profile.Marker.CopyTo(output, 0);
        profile.SeedBlock.CopyTo(output, 4);
        TransformPayload(output.AsSpan(0x1C), 0x1C, profile, cancellationToken);
        if (!HasHeader(output))
            throw new InvalidDataException("SmartCrypt re-encoding did not reproduce the original marker; refusing to emit a file.");
        return output;
    }

    private static void TransformPayload(Span<byte> data, uint absoluteFileOffset,
        SmartCryptKeyProfile profile, CancellationToken cancellationToken)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if ((i & 0xFFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            uint position = checked(absoluteFileOffset + (uint)i);
            byte value = data[i];
            value ^= profile.Key6[position % 6];
            value ^= profile.Key16[position & 0x0F];
            uint salt = (position ^ (profile.UsesC5Variant ? 0xC5u : 0x65u)) * 0x93u;
            data[i] = (byte)(value ^ (byte)salt);
        }
    }
}
