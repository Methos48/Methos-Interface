using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace MethosUEngine;

/// <summary>
/// Lineage II standard file layer for protocol 111 (XOR) and 413 (RSA + zlib).
/// The 413 implementation follows the public-domain open-l2encdec format:
/// 28-byte UTF-16LE header, 128-byte RSA blocks, 20-byte tail with CRC32.
/// Decode tries both the modern and legacy 413 private-key sets.
/// </summary>
public sealed class Lineage2StandardCodec : IProtectionCodec
{
    private const int HeaderSize = 28;
    private const int TailSize = 20;
    private const int RsaBlockSize = 128;
    private const int RsaPayloadSize = 124;
    private const uint CrcPolynomial = 0xEDB88320;

    private const string ModernModulus =
        "75b4d6de5c016544068a1acf125869f43d2e09fc55b8b1e289556daf9b8757635593446288b3653da1ce91c87bb1a5c18f16323495c55d7d72c0890a83f69bfd1fd9434eb1c02f3e4679edfa43309319070129c267c85604d87bb65bae205de3707af1d2108881abb567c3b3d069ae67c3a4c6a3aa93d26413d4c66094ae2039";
    private const string ModernPublicExponent =
        "30b4c2d798d47086145c75063c8e841e719776e400291d7838d3e6c4405b504c6a07f8fca27f32b86643d2649d1d5f124cdd0bf272f0909dd7352fe10a77b34d831043d9ae541f8263c6fe3d1c14c2f04e43a7253a6dda9a8c1562cbd493c1b631a1957618ad5dfe5ca28553f746e2fc6f2db816c7db223ec91e955081c1de65";
    private const string ModernPrivateExponent = "1d";
    private const string Legacy413Modulus =
        "97df398472ddf737ef0a0cd17e8d172f0fef1661a38a8ae1d6e829bc1c6e4c3cfc19292dda9ef90175e46e7394a18850b6417d03be6eea274d3ed1dde5b5d7bde72cc0a0b71d03608655633881793a02c9a67d9ef2b45eb7c08d4be329083ce450e68f7867b6749314d40511d09bc5744551baa86a89dc38123dc1668fd72d83";
    private const string Legacy413PrivateExponent = "35";

    private readonly int _protocol;
    private readonly bool _verifyChecksumOnDecode;
    public string LayerId => $"lineage2-{_protocol}";
    public bool VerifyChecksumOnDecode => _verifyChecksumOnDecode;

    public Lineage2StandardCodec(int protocol, bool verifyChecksumOnDecode = false)
    {
        if (protocol is not (111 or 413))
            throw new ArgumentOutOfRangeException(nameof(protocol), "Only verified protocol implementations 111 and 413 are currently registered.");
        _protocol = protocol;
        _verifyChecksumOnDecode = verifyChecksumOnDecode;
    }

    public byte[] Decode(ReadOnlyMemory<byte> input, string originalFileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = input.ToArray();
        ValidateEnvelope(bytes, _verifyChecksumOnDecode);
        if (_protocol == 111)
        {
            byte[] plain = bytes.AsSpan(HeaderSize, bytes.Length - HeaderSize - TailSize).ToArray();
            for (int i = 0; i < plain.Length; i++) plain[i] ^= 0xAC;
            return plain;
        }

        byte[] encrypted = bytes.AsSpan(HeaderSize, bytes.Length - HeaderSize - TailSize).ToArray();
        if (encrypted.Length == 0 || encrypted.Length % RsaBlockSize != 0)
            throw new InvalidDataException("Protocol 413 payload is not a whole number of 128-byte RSA blocks.");

        Exception? modernError = null;
        try { return Decode413(encrypted, ModernModulus, ModernPrivateExponent, cancellationToken); }
        catch (Exception ex) when (ex is InvalidDataException or CryptographicException or IOException or ArgumentException)
        { modernError = ex; }

        try { return Decode413(encrypted, Legacy413Modulus, Legacy413PrivateExponent, cancellationToken); }
        catch (Exception ex) when (ex is InvalidDataException or CryptographicException or IOException or ArgumentException)
        { throw new InvalidDataException("Protocol 413 was detected, but neither the modern nor legacy RSA key produced a valid zlib stream.", new AggregateException(modernError!, ex)); }
    }

    /// <summary>
    /// Diagnostic/compatibility path for custom Lineage2Ver111 trailers.
    /// It validates the header and applies the 0xAC stream, but deliberately does not
    /// assert the standard CRC layout. includeTail=true also XORs the final 20 bytes.
    /// </summary>
    public byte[] Decode111Unchecked(ReadOnlyMemory<byte> input, bool includeTail = false)
    {
        if (_protocol != 111)
            throw new NotSupportedException("Unchecked payload decoding is available only for protocol 111.");
        byte[] bytes = input.ToArray();
        ValidateHeaderOnly(bytes);
        int length = bytes.Length - HeaderSize - (includeTail ? 0 : TailSize);
        if (length < 0) throw new InvalidDataException("Lineage2Ver111 file is too short.");
        byte[] plain = bytes.AsSpan(HeaderSize, length).ToArray();
        for (int i = 0; i < plain.Length; i++) plain[i] ^= 0xAC;
        return plain;
    }

    /// <summary>
    /// Rebuilds a custom protocol-111 envelope while preserving its original trailer.
    /// This does not claim to update a custom checksum; callers must validate the
    /// trailer semantics before allowing modified output to be saved.
    /// </summary>
    public byte[] Encode111PreservingTail(ReadOnlyMemory<byte> payload,
        ReadOnlyMemory<byte> originalEnvelope, bool includeTail = false)
    {
        if (_protocol != 111)
            throw new NotSupportedException("Trailer-preserving encoding is available only for protocol 111.");
        byte[] original = originalEnvelope.ToArray();
        ValidateHeaderOnly(original);
        int tailLength = includeTail ? 0 : TailSize;
        byte[] output = new byte[HeaderSize + payload.Length + tailLength];
        original.AsSpan(0, HeaderSize).CopyTo(output);
        byte[] encrypted = payload.ToArray();
        for (int i = 0; i < encrypted.Length; i++) encrypted[i] ^= 0xAC;
        encrypted.CopyTo(output, HeaderSize);
        if (!includeTail)
            original.AsSpan(original.Length - TailSize, TailSize).CopyTo(output.AsSpan(output.Length - TailSize));
        return output;
    }

    public bool VerifyTailChecksum(ReadOnlySpan<byte> input)
    {
        if (input.Length < HeaderSize + TailSize) return false;
        byte[] data = input.ToArray();
        try { ValidateHeaderOnly(data); }
        catch (InvalidDataException) { return false; }
        uint stored = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - TailSize + 12, 4));
        uint actual = Crc32(data.AsSpan(0, data.Length - TailSize));
        return stored == actual;
    }

    private void ValidateHeaderOnly(byte[] data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("File is too short to contain a Lineage II header.");
        string expected = $"Lineage2Ver{_protocol:D3}";
        for (int i = 0; i < expected.Length; i++)
            if (data[i * 2] != (byte)expected[i] || data[i * 2 + 1] != 0)
                throw new InvalidDataException($"Expected UTF-16LE Lineage2Ver{_protocol:D3} header.");
    }

    public byte[] Encode(ReadOnlyMemory<byte> input, string originalFileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] payload;
        if (_protocol == 111)
        {
            payload = input.ToArray();
            for (int i = 0; i < payload.Length; i++) payload[i] ^= 0xAC;
        }
        else
        {
            byte[] compressed = CompressWithLength(input.Span);
            byte[] padded = AddRsaPadding(compressed);
            payload = TransformRsaBlocks(padded, ModernModulus, ModernPublicExponent, cancellationToken);
        }

        byte[] output = new byte[HeaderSize + payload.Length + TailSize];
        Encoding.Unicode.GetBytes($"Lineage2Ver{_protocol:D3}").CopyTo(output, 0);
        payload.CopyTo(output, HeaderSize);
        uint crc = Crc32(output.AsSpan(0, output.Length - TailSize));
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(output.Length - TailSize + 12, 4), crc);
        return output;
    }

    private void ValidateEnvelope(byte[] data, bool verifyChecksum)
    {
        if (data.Length < HeaderSize + TailSize)
            throw new InvalidDataException("File is too short to contain a Lineage II header and tail.");
        string expected = $"Lineage2Ver{_protocol:D3}";
        for (int i = 0; i < expected.Length; i++)
            if (data[i * 2] != (byte)expected[i] || data[i * 2 + 1] != 0)
                throw new InvalidDataException($"Expected UTF-16LE Lineage2Ver{_protocol:D3} header.");
        if (verifyChecksum)
        {
            uint stored = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - TailSize + 12, 4));
            uint actual = Crc32(data.AsSpan(0, data.Length - TailSize));
            if (stored != actual)
                throw new InvalidDataException($"Lineage II tail checksum mismatch (stored {stored:X8}, calculated {actual:X8}).");
        }
    }

    private static byte[] Decode413(byte[] encrypted, string modulusHex, string privateExponentHex, CancellationToken token)
    {
        BigInteger modulus = FromHex(modulusHex);
        BigInteger exponent = FromHex(privateExponentHex);
        using var compressed = new MemoryStream();
        for (int offset = 0; offset < encrypted.Length; offset += RsaBlockSize)
        {
            token.ThrowIfCancellationRequested();
            var cipher = new BigInteger(encrypted.AsSpan(offset, RsaBlockSize), isUnsigned: true, isBigEndian: true);
            if (cipher >= modulus) throw new InvalidDataException("RSA block is outside the selected modulus.");
            BigInteger plain = BigInteger.ModPow(cipher, exponent, modulus);
            byte[] block = ToFixedBigEndian(plain, RsaBlockSize);
            int count = Math.Min((int)block[3], RsaPayloadSize);
            int start = RsaBlockSize - Align4(count);
            compressed.Write(block, start, count);
        }

        byte[] packed = compressed.ToArray();
        if (packed.Length < 4) throw new InvalidDataException("Missing protocol 413 decompressed-size prefix.");
        uint expectedLength = BinaryPrimitives.ReadUInt32LittleEndian(packed.AsSpan(0, 4));
        if (expectedLength > 512 * 1024 * 1024) throw new InvalidDataException("Protocol 413 decoded size exceeds safety limit.");
        using var input = new MemoryStream(packed, 4, packed.Length - 4, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(expectedLength > int.MaxValue ? 0 : (int)expectedLength);
        zlib.CopyTo(output);
        if (output.Length != expectedLength)
            throw new InvalidDataException($"Protocol 413 size mismatch: expected {expectedLength}, got {output.Length}.");
        return output.ToArray();
    }

    private static byte[] CompressWithLength(ReadOnlySpan<byte> input)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(input);
        byte[] stream = compressed.ToArray();
        byte[] result = new byte[4 + stream.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), (uint)input.Length);
        stream.CopyTo(result, 4);
        return result;
    }

    private static byte[] AddRsaPadding(byte[] input)
    {
        int blocks = (input.Length + RsaPayloadSize - 1) / RsaPayloadSize;
        byte[] output = new byte[blocks * RsaBlockSize];
        int consumed = 0;
        for (int i = 0; i < blocks; i++)
        {
            int count = Math.Min(input.Length - consumed, RsaPayloadSize);
            int aligned = Align4(count);
            int offset = i * RsaBlockSize;
            output[offset + 3] = (byte)count;
            Buffer.BlockCopy(input, consumed, output, offset + RsaBlockSize - aligned, count);
            consumed += count;
        }
        return output;
    }

    private static byte[] TransformRsaBlocks(byte[] input, string modulusHex, string publicExponentHex, CancellationToken token)
    {
        BigInteger modulus = FromHex(modulusHex);
        BigInteger exponent = FromHex(publicExponentHex);
        byte[] output = new byte[input.Length];
        for (int offset = 0; offset < input.Length; offset += RsaBlockSize)
        {
            token.ThrowIfCancellationRequested();
            var block = new BigInteger(input.AsSpan(offset, RsaBlockSize), isUnsigned: true, isBigEndian: true);
            byte[] encrypted = ToFixedBigEndian(BigInteger.ModPow(block, exponent, modulus), RsaBlockSize);
            Buffer.BlockCopy(encrypted, 0, output, offset, RsaBlockSize);
        }
        return output;
    }

    private static BigInteger FromHex(string hex) => new(Convert.FromHexString(hex), isUnsigned: true, isBigEndian: true);

    private static byte[] ToFixedBigEndian(BigInteger value, int size)
    {
        byte[] raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length > size) throw new CryptographicException("RSA result exceeds block size.");
        byte[] output = new byte[size];
        Buffer.BlockCopy(raw, 0, output, size - raw.Length, raw.Length);
        return output;
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? CrcPolynomial : 0);
        }
        return ~crc;
    }
}
