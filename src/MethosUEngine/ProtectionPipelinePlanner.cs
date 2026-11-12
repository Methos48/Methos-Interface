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
/// Fail-closed planner for the ordered protection stages used by Methos.
/// It identifies known signatures but never treats identification as decryption.
/// A codec becomes available only after a tested IProtectionCodec is registered.
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
            var layers = new[]
            {
                new ProtectionLayer(
                    ProtectionLayerKind.SmartCrypt1522Candidate,
                    "smartcrypt-1.5.2.2",
                    "candidate-unverified",
                    false,
                    false,
                    "Coincide con el prefijo de las muestras interface.u/interface.xdat aportadas. No demuestra por sí solo que cualquier archivo con este prefijo use SmartCrypt.")
            };
            return new(extension, layers, false, false, "recognized-candidate",
                "Se detectó una firma coincidente con las muestras SmartCrypt 1.5.2.2. Falta recuperar y probar el transformador y su operación inversa; no se intenta descifrar por suposición.");
        }

        if (TryReadLineage2Version(data, out int version))
        {
            var layer = new ProtectionLayer(
                ProtectionLayerKind.Lineage2Standard,
                $"lineage2-{version}",
                "identified-codec-not-verified",
                false,
                false,
                $"Cabecera Lineage2Ver{version:D3} reconocida. La identificación de versión no equivale a tener un descifrador/cifrador validado.");
            return new(extension, new[] { layer }, false, false, "recognized-encryption",
                $"Se identificó la capa Lineage II versión {version:D3}, pero no se ejecutará hasta disponer de un codec probado para esa versión.");
        }

        if (data.Length >= 4 && BitConverter.ToUInt32(data[..4]) == UnrealPackageTag)
        {
            var kind = extension == ".xdat" ? ProtectionLayerKind.XdatPayload : ProtectionLayerKind.UnrealPackage;
            var layer = new ProtectionLayer(kind, kind.ToString(), "signature-only", false, false,
                "Firma Unreal reconocida. La validación estructural específica del formato todavía debe ejecutarse.");
            return new(extension, new[] { layer }, false, false, "plain-format-candidate",
                extension == ".xdat"
                    ? "La firma parece corresponder a un paquete Unreal, no demuestra que sea un XDAT válido."
                    : "Firma de paquete Unreal reconocida; falta validar completamente tablas, índices y offsets.");
        }

        var unknown = new ProtectionLayer(ProtectionLayerKind.Unknown, "unknown", "unsupported", false, false,
            "No hay una firma ni un codec verificado para este archivo.");
        return new(extension, new[] { unknown }, false, false, "unknown-or-unsupported",
            "Formato o protección desconocidos. El archivo se conserva intacto; no se adivinan algoritmo, clave ni versión.");
    }

    private static bool TryReadLineage2Version(ReadOnlySpan<byte> data, out int version)
    {
        version = 0;
        const int headerChars = 14; // Lineage2Ver + three ASCII digits
        if (data.Length < headerChars * 2) return false;
        for (int i = 0; i < headerChars; i++)
        {
            if (data[i * 2 + 1] != 0) return false;
        }
        string header = Encoding.ASCII.GetString(data[..(headerChars * 2)]
            .Where((_, index) => index % 2 == 0).ToArray());
        if (!header.StartsWith("Lineage2Ver", StringComparison.Ordinal) ||
            header.Length != headerChars ||
            !int.TryParse(header.AsSpan(11, 3), out version))
            return false;
        return true;
    }
}
