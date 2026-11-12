using System.Security.Cryptography;

namespace MethosUEngine;

/// <summary>A verified codec for one explicitly identified protection layer.</summary>
public interface IProtectionCodec
{
    string LayerId { get; }
    byte[] Decode(ReadOnlyMemory<byte> input, string originalFileName, CancellationToken cancellationToken = default);
    byte[] Encode(ReadOnlyMemory<byte> input, string originalFileName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runtime registry and ordered transform executor. No codec is inferred from a file
/// extension; callers must supply a plan and every layer must have a registered codec.
/// </summary>
public sealed class ProtectionCodecRuntime
{
    private readonly IReadOnlyDictionary<string, IProtectionCodec> _codecs;

    public static ProtectionCodecRuntime CreateDefault(ActiveAntiCheatRsaProfile? activeAntiCheatProfile = null,
        SmartCryptKeyProfile? smartCryptProfile = null,
        Lineage2CustomTailProfile? lineage2CustomTailProfile = null)
    {
        var codecs = new List<IProtectionCodec>
        {
            new Lineage2StandardCodec(111),
            new Lineage2StandardCodec(413),
            new Lineage2Ver111CustomTailCodec(lineage2CustomTailProfile)
        };
        codecs.Add(new SmartCrypt1522Codec(smartCryptProfile));
        if (activeAntiCheatProfile is not null)
            codecs.Add(new ActiveAntiCheatCodec(activeAntiCheatProfile));
        return new ProtectionCodecRuntime(codecs);
    }

    public ProtectionCodecRuntime(IEnumerable<IProtectionCodec> codecs)
    {
        ArgumentNullException.ThrowIfNull(codecs);
        var map = new Dictionary<string, IProtectionCodec>(StringComparer.OrdinalIgnoreCase);
        foreach (var codec in codecs)
        {
            if (string.IsNullOrWhiteSpace(codec.LayerId))
                throw new ArgumentException("A codec must declare a non-empty layer ID.", nameof(codecs));
            if (!map.TryAdd(codec.LayerId, codec))
                throw new ArgumentException($"Duplicate codec registration: {codec.LayerId}", nameof(codecs));
        }
        _codecs = map;
    }

    public IReadOnlyList<string> MissingCodecs(ProtectionPlan plan) =>
        plan.Layers.Where(x => x.Kind != ProtectionLayerKind.Unknown && !_codecs.ContainsKey(x.Id))
            .Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public byte[] Decode(ProtectionPlan plan, ReadOnlyMemory<byte> input, string originalFileName,
        CancellationToken cancellationToken = default)
    {
        ValidatePlan(plan);
        if (!plan.CanDecode)
            throw new NotSupportedException("The protection plan does not have a verified decoder/key profile. The input is left unchanged.");
        var missing = MissingCodecs(plan);
        if (missing.Count != 0)
            throw new NotSupportedException("No verified decoder is registered for: " + string.Join(", ", missing));

        byte[] current = input.ToArray();
        foreach (var layer in plan.Layers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = _codecs[layer.Id].Decode(current, originalFileName, cancellationToken);
        }
        return current;
    }

    public byte[] Encode(ProtectionPlan plan, ReadOnlyMemory<byte> editedPlaintext, string originalFileName,
        CancellationToken cancellationToken = default)
    {
        ValidatePlan(plan);
        if (!plan.CanEncode)
            throw new NotSupportedException("The protection plan does not have a verified encoder. No output file was produced.");
        var missing = MissingCodecs(plan);
        if (missing.Count != 0)
            throw new NotSupportedException("No verified encoder is registered for: " + string.Join(", ", missing));

        byte[] current = editedPlaintext.ToArray();
        foreach (var layer in plan.Layers.Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = _codecs[layer.Id].Encode(current, originalFileName, cancellationToken);
        }

        byte[] verified = Decode(plan, current, originalFileName, cancellationToken);
        if (!verified.AsSpan().SequenceEqual(editedPlaintext.Span))
            throw new InvalidDataException("Re-encoded output failed the full decode round-trip check. No output file should be saved.");
        return current;
    }

    /// <summary>Writes to a new destination atomically; never overwrites the source.</summary>
    public static async Task WriteNewOutputAsync(string sourcePath, string destinationPath,
        ReadOnlyMemory<byte> output, CancellationToken cancellationToken = default)
    {
        string source = Path.GetFullPath(sourcePath);
        string destination = Path.GetFullPath(destinationPath);
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Output must not overwrite the imported original.");
        if (File.Exists(destination))
            throw new IOException("Output already exists; choose a new destination.");

        string? directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(directory)) throw new IOException("Invalid output directory.");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, output.ToArray(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void ValidatePlan(ProtectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Layers.Count == 0 || plan.Layers.Any(x => x.Kind == ProtectionLayerKind.Unknown))
            throw new NotSupportedException("Unknown protection layers cannot be decoded or encoded.");
        if (plan.Layers.Any(x => string.IsNullOrWhiteSpace(x.Id)))
            throw new InvalidOperationException("Protection plan contains an invalid layer ID.");
    }
}
