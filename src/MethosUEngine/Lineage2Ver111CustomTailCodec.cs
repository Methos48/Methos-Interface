namespace MethosUEngine;

/// <summary>
/// Original protocol-111 envelope metadata for files whose 20-byte trailer is not
/// the stock CRC trailer. The original header and trailer are retained verbatim.
/// </summary>
public sealed class Lineage2CustomTailProfile
{
    public byte[] Header { get; }
    public byte[] Tail { get; }
    public bool IncludeTailInPayload { get; }
    public string Origin { get; }
    public bool CanReencode => Header.Length == 28 &&
        (IncludeTailInPayload || Tail.Length == 20);

    public Lineage2CustomTailProfile(ReadOnlySpan<byte> header, ReadOnlySpan<byte> tail,
        bool includeTailInPayload = false, string origin = "original-envelope")
    {
        if (header.Length != 28)
            throw new ArgumentException("Lineage2Ver111 header must contain exactly 28 bytes.", nameof(header));
        if (!includeTailInPayload && tail.Length != 20)
            throw new ArgumentException("Custom protocol-111 trailer must contain exactly 20 bytes.", nameof(tail));
        if (includeTailInPayload && !tail.IsEmpty)
            throw new ArgumentException("A profile that includes the trailer in the payload must not also store a separate trailer.", nameof(tail));
        Header = header.ToArray();
        Tail = tail.ToArray();
        IncludeTailInPayload = includeTailInPayload;
        Origin = string.IsNullOrWhiteSpace(origin) ? "imported" : origin.Trim();
    }

    public static Lineage2CustomTailProfile FromEnvelope(ReadOnlySpan<byte> envelope,
        bool includeTailInPayload = false, string origin = "original-envelope")
    {
        const int headerSize = 28;
        const int tailSize = 20;
        byte[] expected = System.Text.Encoding.Unicode.GetBytes("Lineage2Ver111");
        if (envelope.Length < headerSize + (includeTailInPayload ? 0 : tailSize) ||
            !envelope[..headerSize].SequenceEqual(expected))
            throw new InvalidDataException("Not a supported Lineage2Ver111 envelope for custom-trailer preservation.");
        byte[] tail = includeTailInPayload ? Array.Empty<byte>() : envelope[^tailSize..].ToArray();
        return new Lineage2CustomTailProfile(envelope[..headerSize], tail, includeTailInPayload, origin);
    }
}

/// <summary>
/// Protocol-111 XOR codec for custom/legacy trailers. Unlike the stock codec, it
/// preserves the exact original 20-byte trailer instead of inventing a replacement.
/// </summary>
public sealed class Lineage2Ver111CustomTailCodec : IProtectionCodec
{
    private const int HeaderSize = 28;
    private const int TailSize = 20;
    private readonly Lineage2CustomTailProfile? _profile;

    public string LayerId => "lineage2-111-custom-tail";

    public Lineage2Ver111CustomTailCodec(Lineage2CustomTailProfile? profile = null) =>
        _profile = profile;

    public byte[] Decode(ReadOnlyMemory<byte> input, string originalFileName,
        CancellationToken cancellationToken = default)
    {
        byte[] bytes = input.ToArray();
        ValidateHeader(bytes);
        bool includeTail = _profile?.IncludeTailInPayload ?? false;
        int tailLength = includeTail ? 0 : TailSize;
        if (bytes.Length < HeaderSize + tailLength)
            throw new InvalidDataException("Custom Lineage2Ver111 file is too short.");

        int payloadLength = bytes.Length - HeaderSize - tailLength;
        byte[] plain = bytes.AsSpan(HeaderSize, payloadLength).ToArray();
        for (int i = 0; i < plain.Length; i++)
        {
            if ((i & 0xFFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            plain[i] ^= 0xAC;
        }
        return plain;
    }

    public byte[] Encode(ReadOnlyMemory<byte> input, string originalFileName,
        CancellationToken cancellationToken = default)
    {
        Lineage2CustomTailProfile profile = _profile ??
            throw new NotSupportedException("The original Lineage2Ver111 trailer profile is required; no replacement trailer will be invented.");
        if (!profile.CanReencode)
            throw new InvalidDataException("The original Lineage2Ver111 trailer profile is incomplete.");

        byte[] encrypted = input.ToArray();
        for (int i = 0; i < encrypted.Length; i++)
        {
            if ((i & 0xFFFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            encrypted[i] ^= 0xAC;
        }

        int tailLength = profile.IncludeTailInPayload ? 0 : profile.Tail.Length;
        byte[] output = new byte[HeaderSize + encrypted.Length + tailLength];
        profile.Header.CopyTo(output, 0);
        encrypted.CopyTo(output, HeaderSize);
        if (tailLength != 0)
            profile.Tail.CopyTo(output, output.Length - tailLength);
        return output;
    }

    private static void ValidateHeader(ReadOnlySpan<byte> data)
    {
        byte[] expected = System.Text.Encoding.Unicode.GetBytes("Lineage2Ver111");
        if (data.Length < HeaderSize || !data[..HeaderSize].SequenceEqual(expected))
            throw new InvalidDataException("Expected UTF-16LE Lineage2Ver111 header.");
    }
}
