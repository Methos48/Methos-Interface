using System.Numerics;
using System.Security.Cryptography;

namespace MethosUEngine;

/// <summary>
/// RSA material captured from a client using ActiveAntiCheatCrypt.
/// N, D and the optional public exponent are stored as unsigned little-endian integers.
/// </summary>
public sealed class ActiveAntiCheatRsaProfile
{
    public BigInteger Modulus { get; }
    public BigInteger PrivateExponent { get; }
    public BigInteger? PublicExponent { get; }
    public string Origin { get; }

    public ActiveAntiCheatRsaProfile(
        ReadOnlySpan<byte> modulusLittleEndian,
        ReadOnlySpan<byte> privateExponentLittleEndian,
        ReadOnlySpan<byte> publicExponentLittleEndian = default,
        string origin = "imported")
    {
        Modulus = new BigInteger(modulusLittleEndian, isUnsigned: true, isBigEndian: false);
        PrivateExponent = new BigInteger(privateExponentLittleEndian, isUnsigned: true, isBigEndian: false);
        PublicExponent = publicExponentLittleEndian.IsEmpty
            ? null
            : new BigInteger(publicExponentLittleEndian, isUnsigned: true, isBigEndian: false);
        Origin = string.IsNullOrWhiteSpace(origin) ? "imported" : origin.Trim();

        if (Modulus <= BigInteger.Zero || Modulus.IsEven)
            throw new ArgumentException("RSA modulus must be positive and odd.", nameof(modulusLittleEndian));
        if (PrivateExponent <= BigInteger.Zero)
            throw new ArgumentException("RSA private exponent must be positive.", nameof(privateExponentLittleEndian));
        if (PublicExponent.HasValue && PublicExponent.Value <= BigInteger.One)
            throw new ArgumentException("RSA public exponent must be greater than one.", nameof(publicExponentLittleEndian));
    }

    public static ActiveAntiCheatRsaProfile ParseLog(string text,
        ReadOnlySpan<byte> publicExponentLittleEndian = default, string origin = "imported")
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] modulus = ParseHexComponent(text, "N_LE");
        byte[] privateExponent = ParseHexComponent(text, "D_LE");
        return new ActiveAntiCheatRsaProfile(modulus, privateExponent,
            publicExponentLittleEndian, origin);
    }

    private static byte[] ParseHexComponent(string text, string name)
    {
        string needle = name + "=";
        int start = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            throw new InvalidDataException($"RSA profile is missing {name}.");
        start += needle.Length;
        int end = start;
        while (end < text.Length && Uri.IsHexDigit(text[end])) end++;
        if (end == start || ((end - start) & 1) != 0)
            throw new InvalidDataException($"RSA profile component {name} is not valid hexadecimal.");
        try { return Convert.FromHexString(text.AsSpan(start, end - start)); }
        catch (FormatException ex) { throw new InvalidDataException($"RSA profile component {name} is not valid hexadecimal.", ex); }
    }
}

/// <summary>
/// ActiveAntiCheatCrypt container layer: RSA-2048/PKCS#1 v1.5 transports a 20-byte
/// RC4 key; the remaining payload is RC4 encrypted. Decoding requires the matching
/// client's private RSA profile. Encoding additionally requires the matching public exponent.
/// </summary>
public sealed class ActiveAntiCheatCodec : IProtectionCodec
{
    public const int RsaBlockOffset = 0x14;
    public const int RsaBlockLength = 256;
    public const int PayloadOffset = 0x114;
    public const int Rc4KeyLength = 20;
    private static readonly byte[] Magic = "ActiveAnticheatCrypt"u8.ToArray();
    private readonly ActiveAntiCheatRsaProfile _profile;

    public ActiveAntiCheatCodec(ActiveAntiCheatRsaProfile profile) =>
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));

    public string LayerId => "active-anticheat-crypt";

    public static bool HasHeader(ReadOnlySpan<byte> data) =>
        data.Length >= Magic.Length && data[..Magic.Length].SequenceEqual(Magic);

    public byte[] Decode(ReadOnlyMemory<byte> input, string originalFileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> data = input.Span;
        if (data.Length <= PayloadOffset || !HasHeader(data))
            throw new InvalidDataException("File is not an ActiveAntiCheatCrypt container.");

        ReadOnlySpan<byte> encryptedKey = data.Slice(RsaBlockOffset, RsaBlockLength);
        BigInteger cipher = new(encryptedKey, isUnsigned: true, isBigEndian: true);
        if (cipher >= _profile.Modulus)
            throw new InvalidDataException("ActiveAntiCheatCrypt RSA block is outside the modulus.");

        BigInteger messageValue = BigInteger.ModPow(cipher, _profile.PrivateExponent, _profile.Modulus);
        byte[] block = ToFixedBigEndian(messageValue, RsaBlockLength);
        byte[] rc4Key = ExtractRc4Key(block);

        byte[] plaintext = data[PayloadOffset..].ToArray();
        Rc4Transform(plaintext, rc4Key);
        cancellationToken.ThrowIfCancellationRequested();
        return plaintext;
    }

    public byte[] Encode(ReadOnlyMemory<byte> input, string originalFileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_profile.PublicExponent is not BigInteger publicExponent)
            throw new NotSupportedException("ActiveAntiCheatCrypt re-encoding requires the matching RSA public exponent; it has not been verified or supplied.");

        byte[] rc4Key = RandomNumberGenerator.GetBytes(Rc4KeyLength);
        byte[] message = new byte[Magic.Length + rc4Key.Length];
        Magic.CopyTo(message, 0);
        rc4Key.CopyTo(message, Magic.Length);

        byte[] encodedBlock = BuildPkcs1Type2Block(message);
        BigInteger messageValue = new(encodedBlock, isUnsigned: true, isBigEndian: true);
        BigInteger cipher = BigInteger.ModPow(messageValue, publicExponent, _profile.Modulus);
        byte[] encryptedKey = ToFixedBigEndian(cipher, RsaBlockLength);

        byte[] payload = input.ToArray();
        Rc4Transform(payload, rc4Key);
        byte[] output = new byte[PayloadOffset + payload.Length];
        Magic.CopyTo(output, 0);
        encryptedKey.CopyTo(output, RsaBlockOffset);
        payload.CopyTo(output, PayloadOffset);
        cancellationToken.ThrowIfCancellationRequested();
        return output;
    }

    private static byte[] ExtractRc4Key(ReadOnlySpan<byte> block)
    {
        if (block.Length != RsaBlockLength || block[0] != 0x00 || block[1] != 0x02)
            throw new InvalidDataException("ActiveAntiCheatCrypt RSA padding is invalid.");

        int separator = block[2..].IndexOf((byte)0x00);
        if (separator < 8)
            throw new InvalidDataException("ActiveAntiCheatCrypt RSA padding is too short.");
        separator += 2;
        ReadOnlySpan<byte> message = block[(separator + 1)..];

        if (message.Length == Magic.Length + Rc4KeyLength &&
            message[..Magic.Length].SequenceEqual(Magic))
            return message[Magic.Length..].ToArray();

        if (message.Length == Rc4KeyLength)
            return message.ToArray();

        throw new InvalidDataException($"Unexpected ActiveAntiCheatCrypt RSA message length: {message.Length}.");
    }

    private static byte[] BuildPkcs1Type2Block(ReadOnlySpan<byte> message)
    {
        int separator = RsaBlockLength - message.Length - 1;
        int paddingLength = separator - 2;
        if (paddingLength < 8)
            throw new InvalidDataException("RSA message is too long for PKCS#1 v1.5.");

        byte[] block = new byte[RsaBlockLength];
        block[0] = 0;
        block[1] = 2;
        RandomNumberGenerator.Fill(block.AsSpan(2, paddingLength));
        for (int i = 2; i < separator; i++)
            while (block[i] == 0)
                block[i] = RandomNumberGenerator.GetBytes(1)[0];
        block[separator] = 0;
        message.CopyTo(block.AsSpan(separator + 1));
        return block;
    }

    private static byte[] ToFixedBigEndian(BigInteger value, int length)
    {
        if (value < BigInteger.Zero)
            throw new InvalidDataException("RSA integer cannot be negative.");
        byte[] bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (bytes.Length > length)
            throw new InvalidDataException("RSA integer exceeds the expected block length.");
        byte[] result = new byte[length];
        bytes.CopyTo(result, length - bytes.Length);
        return result;
    }

    private static void Rc4Transform(Span<byte> data, ReadOnlySpan<byte> key)
    {
        if (key.IsEmpty)
            throw new InvalidDataException("RC4 key is empty.");
        Span<byte> state = stackalloc byte[256];
        for (int i = 0; i < state.Length; i++) state[i] = (byte)i;

        int j = 0;
        for (int i = 0; i < state.Length; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
        }

        int x = 0;
        j = 0;
        for (int k = 0; k < data.Length; k++)
        {
            x = (x + 1) & 0xFF;
            j = (j + state[x]) & 0xFF;
            (state[x], state[j]) = (state[j], state[x]);
            byte keyByte = state[(state[x] + state[j]) & 0xFF];
            data[k] ^= keyByte;
        }
    }
}
