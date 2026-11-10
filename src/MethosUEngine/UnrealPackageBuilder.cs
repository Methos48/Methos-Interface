using System.Buffers.Binary;
using System.Text;

namespace MethosUEngine;

/// <summary>
/// Appends new names, imports, exports and opaque serialized payloads to a classic
/// UE package without moving any pre-existing object bytes.
/// </summary>
public sealed class UnrealPackageBuilder
{
    private const uint DefaultNameFlags = 0x04070010;
    private readonly UnrealPackageIndex _source;
    private readonly List<(string Name, uint Flags)> _newNames = new();
    private readonly List<ImportEntry> _newImports = new();
    private readonly List<PendingExport> _newExports = new();
    private readonly Dictionary<int, byte[]> _replacements = new();

    public UnrealPackageBuilder(UnrealPackageIndex source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public int NameCount => _source.Names.Count + _newNames.Count;
    public int ImportCount => _source.Imports.Count + _newImports.Count;
    public int ExportCount => _source.Exports.Count + _newExports.Count;

    public int AddName(string name, uint flags = DefaultNameFlags)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("El nombre no puede estar vacío.", nameof(name));
        for (int i = 0; i < _source.Names.Count; i++)
            if (string.Equals(_source.Names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        for (int i = 0; i < _newNames.Count; i++)
            if (string.Equals(_newNames[i].Name, name, StringComparison.OrdinalIgnoreCase)) return _source.Names.Count + i;
        _newNames.Add((name, flags));
        return _source.Names.Count + _newNames.Count - 1;
    }

    /// <summary>Returns the Unreal object reference (negative import index).</summary>
    public int AddImport(int classPackageName, int className, int outerReference, int objectName)
    {
        CheckName(classPackageName, nameof(classPackageName));
        CheckName(className, nameof(className));
        CheckName(objectName, nameof(objectName));
        CheckObjectReference(outerReference, ImportCount, ExportCount, nameof(outerReference));
        var entry = new ImportEntry(classPackageName, className, outerReference, objectName);
        for (int i = 0; i < _source.Imports.Count; i++)
            if (_source.Imports[i] == entry) return -(i + 1);
        for (int i = 0; i < _newImports.Count; i++)
            if (_newImports[i] == entry) return -(_source.Imports.Count + i + 1);
        _newImports.Add(entry);
        return -ImportCount;
    }

    /// <summary>Replaces an existing export's serialized payload without moving any other payload.</summary>
    public void ReplaceExportPayload(int exportReference, byte[] serializedPayload)
    {
        ArgumentNullException.ThrowIfNull(serializedPayload);
        if (exportReference < 1 || exportReference > _source.Exports.Count)
            throw new ArgumentOutOfRangeException(nameof(exportReference), "Solo se pueden reemplazar exports que ya existen en el paquete original.");
        _replacements[exportReference] = serializedPayload.ToArray();
    }

    /// <summary>Returns the Unreal object reference (positive export index).</summary>
    public int AddExport(int classReference, int superReference, int outerReference,
        int objectName, uint objectFlags, byte[] serializedPayload)
    {
        ArgumentNullException.ThrowIfNull(serializedPayload);
        CheckName(objectName, nameof(objectName));
        CheckObjectReference(classReference, ImportCount, ExportCount + 1, nameof(classReference));
        CheckObjectReference(superReference, ImportCount, ExportCount + 1, nameof(superReference));
        CheckObjectReference(outerReference, ImportCount, ExportCount + 1, nameof(outerReference));
        _newExports.Add(new PendingExport(classReference, superReference, outerReference,
            objectName, objectFlags, serializedPayload.ToArray()));
        return ExportCount;
    }

    public byte[] Build()
    {
        using var output = new MemoryStream(checked(_source.OriginalLength + 4096));
        output.Write(_source.CopyOriginalSlice(0, _source.OriginalLength));

        var replacementOffsets = new Dictionary<int, int>();
        foreach (var replacement in _replacements.OrderBy(item => item.Key))
        {
            if (replacement.Value.Length == 0) replacementOffsets[replacement.Key] = 0;
            else
            {
                replacementOffsets[replacement.Key] = checked((int)output.Position);
                output.Write(replacement.Value);
            }
        }

        var payloadOffsets = new List<int>(_newExports.Count);
        foreach (var export in _newExports)
        {
            if (export.Payload.Length == 0) payloadOffsets.Add(0);
            else
            {
                payloadOffsets.Add(checked((int)output.Position));
                output.Write(export.Payload);
            }
        }

        int nameOffset = checked((int)output.Position);
        output.Write(_source.CopyOriginalSlice(_source.Summary.NameOffset,
            GetTableLength(_source.Summary.NameOffset, _source.NameTableEnd)));
        foreach (var name in _newNames) WriteName(output, name.Name, name.Flags);

        int importOffset = checked((int)output.Position);
        output.Write(_source.CopyOriginalSlice(_source.Summary.ImportOffset,
            GetTableLength(_source.Summary.ImportOffset, _source.ImportTableEnd)));
        foreach (var import in _newImports) WriteImport(output, import);

        int exportOffset = checked((int)output.Position);
        for (int i = 0; i < _source.Exports.Count; i++)
        {
            int reference = i + 1;
            ExportEntry entry = _source.Exports[i];
            if (_replacements.TryGetValue(reference, out byte[]? replacement))
                entry = entry with { SerialSize = replacement.Length, SerialOffset = replacement.Length == 0 ? 0 : replacementOffsets[reference] };
            WriteExport(output, entry);
        }
        for (int i = 0; i < _newExports.Count; i++)
        {
            var pending = _newExports[i];
            WriteExport(output, new ExportEntry(pending.ClassReference, pending.SuperReference,
                pending.OuterReference, pending.ObjectName, pending.ObjectFlags,
                pending.Payload.Length, payloadOffsets[i]));
        }

        byte[] result = output.ToArray();
        WriteI32(result, 12, NameCount);
        WriteI32(result, 16, nameOffset);
        WriteI32(result, 20, ExportCount);
        WriteI32(result, 24, exportOffset);
        WriteI32(result, 28, ImportCount);
        WriteI32(result, 32, importOffset);

        if (_source.Summary.GenerationCount > 0)
        {
            int generationOffset = 56 + (_source.Summary.GenerationCount - 1) * 8;
            WriteI32(result, generationOffset, ExportCount);
            WriteI32(result, generationOffset + 4, NameCount);
        }

        var verified = UnrealPackageIndex.Parse(result);
        if (verified.Summary.NameCount != NameCount ||
            verified.Summary.ImportCount != ImportCount ||
            verified.Summary.ExportCount != ExportCount)
            throw new InvalidDataException("El paquete reconstruido no conserva las cantidades de las tablas.");
        for (int i = 0; i < _source.Names.Count; i++)
            if (!string.Equals(verified.Names[i], _source.Names[i], StringComparison.Ordinal))
                throw new InvalidDataException($"Cambió el nombre original de índice {i}.");
        for (int i = 0; i < _source.Imports.Count; i++)
            if (verified.Imports[i] != _source.Imports[i])
                throw new InvalidDataException($"Cambió el import original de índice {i}.");
        for (int i = 0; i < _source.Exports.Count; i++)
            if (verified.Exports[i] != _source.Exports[i])
                throw new InvalidDataException($"Cambió el export original de índice {i}.");
        return result;
    }

    public void Write(string destination)
    {
        byte[] bytes = Build();
        string fullPath = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + ".methos-tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            _ = UnrealPackageIndex.Open(temporary);
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private int GetTableLength(int start, int end) => checked(end - start);

    private void CheckName(int index, string parameter)
    {
        if (index < 0 || index >= NameCount)
            throw new ArgumentOutOfRangeException(parameter, $"Índice de nombre {index} fuera de rango ({NameCount}).");
    }

    private static void CheckObjectReference(int reference, int imports, int exports, string parameter)
    {
        if (reference < -imports || reference > exports)
            throw new ArgumentOutOfRangeException(parameter, $"Referencia {reference} fuera de rango.");
    }

    private static void WriteName(Stream stream, string name, uint flags)
    {
        if (name.All(c => c <= 0xFF))
        {
            byte[] bytes = Encoding.Latin1.GetBytes(name + "\0");
            WriteCompact(stream, bytes.Length);
            stream.Write(bytes);
        }
        else
        {
            byte[] bytes = Encoding.Unicode.GetBytes(name + "\0");
            WriteCompact(stream, -(bytes.Length / 2));
            stream.Write(bytes);
        }
        WriteU32(stream, flags);
    }

    private static void WriteImport(Stream stream, ImportEntry entry)
    {
        WriteCompact(stream, entry.ClassPackage);
        WriteCompact(stream, entry.ClassName);
        WriteI32(stream, entry.Outer);
        WriteCompact(stream, entry.ObjectName);
    }

    private static void WriteExport(Stream stream, ExportEntry entry)
    {
        WriteCompact(stream, entry.ClassRef);
        WriteCompact(stream, entry.SuperRef);
        WriteI32(stream, entry.Outer);
        WriteCompact(stream, entry.ObjectName);
        WriteU32(stream, entry.ObjectFlags);
        WriteCompact(stream, entry.SerialSize);
        if (entry.SerialSize > 0) WriteCompact(stream, entry.SerialOffset);
    }

    private static void WriteCompact(Stream stream, int value)
    {
        if (value == int.MinValue) throw new ArgumentOutOfRangeException(nameof(value));
        bool negative = value < 0;
        uint magnitude = (uint)Math.Abs((long)value);
        byte first = (byte)(magnitude & 0x3F);
        if (negative) first |= 0x80;
        magnitude >>= 6;
        if (magnitude != 0) first |= 0x40;
        stream.WriteByte(first);
        while (magnitude != 0)
        {
            byte next = (byte)(magnitude & 0x7F);
            magnitude >>= 7;
            if (magnitude != 0) next |= 0x80;
            stream.WriteByte(next);
        }
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteI32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteI32(byte[] data, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);

    private sealed record PendingExport(int ClassReference, int SuperReference,
        int OuterReference, int ObjectName, uint ObjectFlags, byte[] Payload);
}
