using System.Security.Cryptography;
using System.Text.Json;

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
        string? header, string? detail, int? unrealVersion = null, int? licenseeVersion = null)
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
                diagnostic = detail,
                reproducible = false,
                reproducibility_note = "Perfil registrado; la recompilación debe verificar motor, protocolo y parámetros antes de codificar."
            },
            security = new
            {
                secrets_saved = false,
                original_file_preserved = true
            }
        };
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
