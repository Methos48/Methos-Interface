using System.Text;

namespace MethosUEngine;

public enum ProtectionLayerKind
{
    SmartCrypt1522Candidate,
    Lineage2Standard,
    UnrealPackage,
    XdatPayload,
    Unknown
}

public sealed record ProtectionLayer(
    ProtectionLayerKind Kind,
    string Id,
    string Status,
    bool DecodeAvailable,
    bool EncodeAvailable,
    string Evidence);

public sealed record ProtectionPlan(
    string FileExtension,
    IReadOnlyList<ProtectionLayer> Layers,
    bool CanDecode,
    bool CanEncode,
    string Status,
    string Explanation);

/// <summary>
/// Fail-closed planner for ordered protection stages. Detection is not decryption.
/// </summary>
public static class ProtectionPipelinePlanner
{
    private static readonly byte[] SmartCrypt1522SamplePrefix = Convert.FromHexString("FFEDFACE0100");
    private const uint UnrealPackageTag = 0x9E2A83C1;

    public static ProtectionPlan Inspect(ReadOnlySpan<byte> data, string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        string extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (data.Length >= SmartCrypt1522SamplePrefix.Length &&
            data[..SmartCrypt1522SamplePrefix.Length].SequenceEqual(SmartCrypt1522SamplePrefix))
        {
            var layer = new ProtectionLayer(ProtectionLayerKind.SmartCrypt1522Candidate,
                "smartcrypt-1.5.2.2", "candidate-unverified", false, false,
                "Coincide con el prefijo de las muestras aportadas. No demuestra por sí solo que cualquier archivo con este prefijo use SmartCrypt.");
            return new(extension, new[] { layer }, false, false, "recognized-candidate",
                "Firma coincidente con las muestras SmartCrypt 1.5.2.2. Falta recuperar y probar el transformador y su inversa; no se intenta descifrar por suposición.");
        }

        if (TryReadLineage2Version(data, out int version))
        {
            var layer = new ProtectionLayer(ProtectionLayerKind.Lineage2Standard,
                $"lineage2-{version}", "identified-codec-not-verified", false, false,
                $"Cabecera Lineage2Ver{version:D3} reconocida. La identificación no equivale a tener un codec validado.");
            return new(extension, new[] { layer }, false, false, "recognized-encryption",
                $"Se identificó Lineage II versión {version:D3}, pero no se ejecutará hasta disponer de un codec probado.");
        }

        if (data.Length >= 4 && BitConverter.ToUInt32(data[..4]) == UnrealPackageTag)
        {
            var kind = extension == ".xdat" ? ProtectionLayerKind.XdatPayload : ProtectionLayerKind.UnrealPackage;
            var layer = new ProtectionLayer(kind, kind.ToString(), "signature-only", false, false,
                "Firma Unreal reconocida. La validación estructural específica todavía debe ejecutarse.");
            return new(extension, new[] { layer }, false, false, "plain-format-candidate",
                extension == ".xdat"
                    ? "La firma parece Unreal; esto no demuestra que sea un XDAT válido."
                    : "Firma de paquete Unreal reconocida; falta validar tablas, índices y offsets.");
        }

        var unknown = new ProtectionLayer(ProtectionLayerKind.Unknown, "unknown", "unsupported", false, false,
            "No hay una firma ni un codec verificado para este archivo.");
        return new(extension, new[] { unknown }, false, false, "unknown-or-unsupported",
            "Formato o protección desconocidos. El archivo se conserva intacto; no se adivinan algoritmo, clave ni versión.");
    }

    private static bool TryReadLineage2Version(ReadOnlySpan<byte> data, out int version)
    {
        version = 0;
        const int headerChars = 14;
        if (data.Length < headerChars * 2) return false;
        Span<char> chars = stackalloc char[headerChars];
        for (int i = 0; i < headerChars; i++)
        {
            if (data[i * 2 + 1] != 0) return false;
            chars[i] = (char)data[i * 2];
        }
        return chars[..11].SequenceEqual("Lineage2Ver") &&
            int.TryParse(chars[11..], out version);
    }
}
