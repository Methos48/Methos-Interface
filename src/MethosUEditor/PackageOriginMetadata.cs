using System.Security.Cryptography;
using System.Text.Json;
using MethosUEngine;

namespace MethosUEditor;

/// <summary>
/// Guarda el perfil de origen de cada paquete para que la recompilación
/// pueda exigir el mismo protocolo y nunca elegir otro silenciosamente.
/// No almacena claves ni secretos.
/// </summary>
internal static class PackageOriginMetadata
{
    private const int SchemaVersion = 1;

    public static string Save(string sourcePath, byte[] originalBytes, string outputDirectory,
        string fileKind, string detectionStatus, string? family, int? protocol,
        string? header, string? detail, int? unrealVersion = null, int? licenseeVersion = null,
        ProtectionPipelineInspection? protection = null)
    {
        Directory.CreateDirectory(outputDirectory);
        string metadataDirectory = Path.Combine(outputDirectory, ".methos");
        Directory.CreateDirectory(metadataDirectory);
        string hash = Convert.ToHexString(SHA256.HashData(originalBytes));
        var document = new
        {
            schema = "methos.package-origin",
            schema_version = SchemaVersion,
            created_utc = DateTimeOffset.UtcNow,
            source = new
            {
                file_name = Path.GetFileName(sourcePath),
                extension = Path.GetExtension(sourcePath).ToLowerInvariant(),
                size_bytes = originalBytes.LongLength,
                sha256 = hash,
                kind = fileKind
            },
            encoding = new
            {
                detection_status = detectionStatus,
                family,
                protocol_version = protocol,
                header,
                original_filename_required = protocol == 121 || protocol == 821,
                unreal_package_version = unrealVersion,
                unreal_licensee_version = licenseeVersion,
                diagnostic = protection?.Plan.Explanation ?? detail,
                reproducible = protection?.Plan.CanEncode ?? !protocol.HasValue,
                reproducibility_note = protection is null
                    ? "No se adjuntó una inspección de capas; la compilación debe volver a detectar y verificar el perfil original."
                    : protection.Plan.CanEncode
                        ? "Todas las capas detectadas tienen perfil original y superaron la prueba de ida y vuelta."
                        : "El paquete puede inspeccionarse, pero Methos debe detener la compilación hasta recuperar un perfil de codificación verificable.",
                layers = protection is null ? Array.Empty<object>() : protection.Plan.Layers.Select(layer => (object)new
                {
                    kind = layer.Kind.ToString(),
                    id = layer.Id,
                    status = layer.Status,
                    decode_available = layer.DecodeAvailable,
                    encode_available = layer.EncodeAvailable,
                    evidence = layer.Evidence
                }).ToArray(),
                smartcrypt_profile = protection?.SmartCryptProfile is { } smart ? new
                {
                    marker_hex = Convert.ToHexString(smart.Marker),
                    seed_block_hex = Convert.ToHexString(smart.SeedBlock),
                    seed0 = smart.Seed0,
                    seed1 = smart.Seed1,
                    flag = smart.Flag,
                    lineage2_version = smart.Lineage2Version,
                    origin = smart.Origin
                } : null,
                lineage2_profile = protection?.Lineage2CustomTailProfile is { } l2 ? new
                {
                    header_hex = Convert.ToHexString(l2.Header),
                    include_terminal_bytes_in_payload = l2.IncludeTailInPayload,
                    terminal_bytes_hex = Convert.ToHexString(l2.Tail),
                    origin = l2.Origin
                } : null,
                decoded_payload_sha256 = protection is null ? null :
                    Convert.ToHexString(SHA256.HashData(protection.DecodedPayload))
            },
            security = new
            {
                secrets_saved = false,
                per_file_profile_saved = protection?.SmartCryptProfile is not null,
                original_terminal_metadata_saved = protection?.Lineage2CustomTailProfile is not null,
                original_file_preserved = true
            }
        };
        string originalsDirectory = Path.Combine(metadataDirectory, "originals");
        Directory.CreateDirectory(originalsDirectory);
        string immutableOriginal = Path.Combine(originalsDirectory, Path.GetFileName(sourcePath));
        if (!File.Exists(immutableOriginal))
        {
            string originalTemporary = immutableOriginal + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(originalTemporary, originalBytes);
                File.Move(originalTemporary, immutableOriginal, overwrite: false);
            }
            finally { if (File.Exists(originalTemporary)) File.Delete(originalTemporary); }
        }
        else
        {
            string savedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(immutableOriginal)));
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(savedHash)))
                throw new InvalidDataException("Ya existe una copia original inmutable diferente para este paquete.");
        }
        string destination = Path.Combine(metadataDirectory, "origin.json");
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return destination;
    }

    public static JsonDocument? TryLoadAndValidate(string metadataPath, string sourcePath)
    {
        if (!File.Exists(metadataPath)) return null;
        var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
        try
        {
            var root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "methos.package-origin" ||
                root.GetProperty("schema_version").GetInt32() != SchemaVersion)
                throw new InvalidDataException("El manifiesto Methos no tiene un formato compatible.");
            string expected = root.GetProperty("source").GetProperty("sha256").GetString() ?? "";
            if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
                throw new InvalidDataException("El SHA-256 guardado en el manifiesto no es válido.");
            string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath)));
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected), Convert.FromHexString(actual)))
                throw new InvalidDataException("El archivo de origen no coincide con el hash del manifiesto.");
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }
}
