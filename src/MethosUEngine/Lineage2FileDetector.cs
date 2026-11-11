using System.Buffers.Binary;
using System.Text;

namespace MethosUEngine;

/// <summary>
/// Identifies documented Lineage II file-encryption headers before a UE package
/// parser is allowed to inspect the payload. Detection is not decryption.
/// </summary>
public static class Lineage2FileDetector
{
    private const int HeaderBytes = 28;
    private const uint UnrealPackageTag = 0x9E2A83C1;
    private static readonly IReadOnlyDictionary<int, (string Family, bool NeedsFilename, bool EngineAvailable)> Protocols =
        new Dictionary<int, (string, bool, bool)>
        {
            [111] = ("XOR", false, true), [120] = ("XOR", false, true), [121] = ("XOR", true, true),
            [211] = ("BLOWFISH", false, true), [212] = ("BLOWFISH", false, true),
            [411] = ("RSA", false, true), [412] = ("RSA", false, true),
            [413] = ("RSA", false, true), [414] = ("RSA", false, true),
            [611] = ("LAMECRYPT_RSA", false, false), [612] = ("LAMECRYPT_RSA", false, false),
            [613] = ("LAMECRYPT_RSA", false, false), [614] = ("LAMECRYPT_RSA", false, false),
            [811] = ("LAMECRYPT_XOR", false, false), [820] = ("LAMECRYPT_XOR", false, false),
            [821] = ("LAMECRYPT_XOR", true, false), [911] = ("LAMECRYPT_BLOWFISH", false, false),
            [912] = ("LAMECRYPT_BLOWFISH", false, false)
        };

    public static Lineage2FileDetection Detect(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        string signature = Convert.ToHexString(data.AsSpan(0, Math.Min(32, data.Length)));
        if (data.Length >= HeaderBytes)
        {
            string header;
            try { header = Encoding.Unicode.GetString(data, 0, HeaderBytes); }
            catch (DecoderFallbackException) { header = string.Empty; }
            if (header.StartsWith("Lineage2Ver", StringComparison.Ordinal))
            {
                string suffix = header.Length == 14 ? header[11..] : string.Empty;
                if (suffix.Length != 3 || !suffix.All(char.IsAsciiDigit))
                    return new("malformed_header", null, null, "UNKNOWN", false, false, header, signature,
                        "La cabecera Lineage2Ver está dañada o no contiene tres dígitos de versión.");
                int version = int.Parse(suffix, System.Globalization.CultureInfo.InvariantCulture);
                if (!Protocols.TryGetValue(version, out var protocol))
                    return new("unsupported_version", true, version, "UNKNOWN", false, false, header, signature,
                        "Cifrado Lineage II reconocido, pero esta versión no está catalogada.");
                return new("recognized_encryption", true, version, protocol.Family,
                    protocol.NeedsFilename, protocol.EngineAvailable, header, signature,
                    (protocol.EngineAvailable
                        ? "Protocolo identificado; todavía debe descifrarse y validarse el paquete."
                        : "Variante LameCrypt identificada; falta integrar un motor compatible.")
                    + (protocol.NeedsFilename ? " Este protocolo requiere conservar el nombre original del archivo." : ""));
            }
        }

        if (data.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, 4)) == UnrealPackageTag)
        {
            ushort version = data.Length >= 6 ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4, 2)) : (ushort)0;
            ushort licensee = data.Length >= 8 ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6, 2)) : (ushort)0;
            return new("plain_candidate", false, null, "UNREAL", false, false, null, signature,
                $"Firma Unreal UE1/UE2 reconocida; versión de paquete {version}/{licensee}. Se validarán tablas y offsets.");
        }

        return new("unknown_or_truncated", null, null, "UNKNOWN", false, false, null, signature,
            "Firma no reconocida: podría ser cifrado u ofuscación personalizada, corrupción, archivo truncado o una variante distinta. No se asignó un protocolo por suposición.");
    }
}

public sealed record Lineage2FileDetection(
    string Status, bool? Encrypted, int? Version, string Family,
    bool FilenameRequired, bool DecoderAvailable, string? Header,
    string FirstBytesHex, string Detail);
