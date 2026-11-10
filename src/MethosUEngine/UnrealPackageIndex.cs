using System.Buffers.Binary;
using System.Text;

namespace MethosUEngine;

/// <summary>
/// Strict reader and lossless table round-tripper for classic UE1/UE2 packages.
/// The initial target is the classic UE1/UE2 package layout; Lineage II client profiles are detected by their actual version/licensee pair.
/// Object payloads are deliberately kept opaque until their class serializer is known.
/// </summary>
public sealed class UnrealPackageIndex
{
    public const uint PackageTag = 0x9E2A83C1;
    private const int FixedHeaderSize = 36;
    private const int GuidSize = 16;
    private readonly byte[] _data;

    private UnrealPackageIndex(byte[] data, PackageSummary summary,
        List<string> names, List<ImportEntry> imports, List<ExportEntry> exports,
        int nameEnd, int importEnd, int exportEnd, int generationCountOffset,
        int generationRecordsOffset)
    {
        _data = data;
        Summary = summary;
        Names = names;
        Imports = imports;
        Exports = exports;
        NameTableEnd = nameEnd;
        ImportTableEnd = importEnd;
        ExportTableEnd = exportEnd;
        GenerationCountOffset = generationCountOffset;
        GenerationRecordsOffset = generationRecordsOffset;
    }

    public PackageSummary Summary { get; }
    public IReadOnlyList<string> Names { get; }
    public IReadOnlyList<ImportEntry> Imports { get; }
    public IReadOnlyList<ExportEntry> Exports { get; }
    internal int NameTableEnd { get; }
    internal int ImportTableEnd { get; }
    internal int ExportTableEnd { get; }
    private int GenerationCountOffset { get; }
    private int GenerationRecordsOffset { get; }

    public static UnrealPackageIndex Open(string path) => Parse(File.ReadAllBytes(path));

    public static UnrealPackageIndex Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < FixedHeaderSize) throw new InvalidDataException("El archivo no contiene el encabezado Unreal completo.");

        uint tag = U32(data, 0);
        if (tag != PackageTag) throw new InvalidDataException($"Firma Unreal incorrecta: 0x{tag:X8}.");

        ushort version = U16(data, 4);
        ushort licensee = U16(data, 6);
        if (version < 68 || version > 194)
            throw new NotSupportedException($"La versión {version}/{licensee} no pertenece todavía al perfil clásico UE1/UE2 implementado.");

        uint flags = U32(data, 8);
        int nameCount = Count(data, 12, "nombres");
        int nameOffset = Offset(data, 16, "nombres", data.Length);
        int exportCount = Count(data, 20, "exports");
        int exportOffset = Offset(data, 24, "exports", data.Length);
        int importCount = Count(data, 28, "imports");
        int importOffset = Offset(data, 32, "imports", data.Length);

        int generationCountOffset = checked(FixedHeaderSize + GuidSize);
        RequireRange(data, FixedHeaderSize, GuidSize + 4, "GUID y generaciones");
        int generationCount = Count(data, generationCountOffset, "generaciones");
        int generationRecordsOffset = generationCountOffset + 4;
        RequireRange(data, generationRecordsOffset, checked(generationCount * 8), "registros de generación");

        // Tables are parsed independently at their absolute offsets. This is intentional:
        // Unreal permits table order to vary within a package.
        var names = new List<string>(nameCount);
        int p = nameOffset;
        for (int i = 0; i < nameCount; i++)
        {
            int length = ReadCompact(data, ref p);
            if (length == 0) names.Add(string.Empty);
            else if (length > 0)
            {
                RequireRange(data, p, length, $"nombre {i}");
                if (data[p + length - 1] != 0) throw new InvalidDataException($"El nombre {i} no termina en cero.");
                names.Add(Encoding.Latin1.GetString(data, p, length - 1));
                p += length;
            }
            else
            {
                int units = checked(-length);
                int bytes = checked(units * 2);
                RequireRange(data, p, bytes, $"nombre Unicode {i}");
                if (U16(data, p + bytes - 2) != 0) throw new InvalidDataException($"El nombre Unicode {i} no termina en cero.");
                names.Add(Encoding.Unicode.GetString(data, p, bytes - 2));
                p += bytes;
            }
            RequireRange(data, p, 4, $"flags del nombre {i}");
            p += 4;
        }
        int nameEnd = p;

        var imports = new List<ImportEntry>(importCount);
        p = importOffset;
        for (int i = 0; i < importCount; i++)
        {
            int classPackage = ReadCompact(data, ref p);
            int className = ReadCompact(data, ref p);
            int outer = ReadI32(data, ref p);
            int objectName = ReadCompact(data, ref p);
            CheckNameIndex(classPackage, names.Count, $"import {i} paquete-clase");
            CheckNameIndex(className, names.Count, $"import {i} clase");
            CheckNameIndex(objectName, names.Count, $"import {i} nombre");
            imports.Add(new ImportEntry(classPackage, className, outer, objectName));
        }
        int importEnd = p;

        var exports = new List<ExportEntry>(exportCount);
        p = exportOffset;
        for (int i = 0; i < exportCount; i++)
        {
            int classRef = ReadCompact(data, ref p);
            int superRef = ReadCompact(data, ref p);
            int outer = ReadI32(data, ref p);
            int objectName = ReadCompact(data, ref p);
            RequireRange(data, p, 4, $"flags del export {i}");
            uint objectFlags = U32(data, p); p += 4;
            int serialSize = ReadCompact(data, ref p);
            int serialOffset = serialSize > 0 ? ReadCompact(data, ref p) : 0;

            CheckObjectRef(classRef, importCount, exportCount, $"export {i} clase");
            CheckObjectRef(superRef, importCount, exportCount, $"export {i} superclase");
            CheckObjectRef(outer, importCount, exportCount, $"export {i} contenedor");
            CheckNameIndex(objectName, names.Count, $"export {i} nombre");
            if (serialSize < 0) throw new InvalidDataException($"Tamaño negativo en export {i}.");
            if (serialSize > 0) RequireRange(data, serialOffset, serialSize, $"datos del export {i}");
            exports.Add(new ExportEntry(classRef, superRef, outer, objectName, objectFlags, serialSize, serialOffset));
        }
        int exportEnd = p;

        return new UnrealPackageIndex(data, new PackageSummary(version, licensee, flags,
            nameCount, nameOffset, exportCount, exportOffset, importCount, importOffset,
            generationCount, data.Length), names, imports, exports, nameEnd, importEnd,
            exportEnd, generationCountOffset, generationRecordsOffset);
    }

    /// <summary>
    /// Rebuilds the three index tables at EOF while preserving every original byte
    /// and every object payload offset. No object data is interpreted or moved.
    /// </summary>
    public byte[] CreateLosslessRoundTrip()
    {
        using var output = new MemoryStream(_data.Length + (NameTableEnd - Summary.NameOffset)
            + (ImportTableEnd - Summary.ImportOffset) + (ExportTableEnd - Summary.ExportOffset));
        output.Write(_data, 0, _data.Length);

        int newNameOffset = checked((int)output.Position);
        output.Write(_data, Summary.NameOffset, NameTableEnd - Summary.NameOffset);
        int newImportOffset = checked((int)output.Position);
        output.Write(_data, Summary.ImportOffset, ImportTableEnd - Summary.ImportOffset);
        int newExportOffset = checked((int)output.Position);
        output.Write(_data, Summary.ExportOffset, ExportTableEnd - Summary.ExportOffset);

        byte[] result = output.ToArray();
        W32(result, 16, checked((uint)newNameOffset));
        W32(result, 24, checked((uint)newExportOffset));
        W32(result, 32, checked((uint)newImportOffset));

        if (Summary.GenerationCount > 0)
        {
            int last = checked(GenerationRecordsOffset + (Summary.GenerationCount - 1) * 8);
            W32(result, last, checked((uint)Summary.ExportCount));
            W32(result, last + 4, checked((uint)Summary.NameCount));
        }
        return result;
    }

    public static void WriteLosslessRoundTrip(string source, string destination)
    {
        var package = Open(source);
        byte[] rebuilt = package.CreateLosslessRoundTrip();
        // Verify the rebuilt file before it is allowed to reach the destination.
        var verified = Parse(rebuilt);
        if (verified.Summary.NameCount != package.Summary.NameCount ||
            verified.Summary.ImportCount != package.Summary.ImportCount ||
            verified.Summary.ExportCount != package.Summary.ExportCount ||
            !verified.Names.SequenceEqual(package.Names) ||
            !verified.Imports.SequenceEqual(package.Imports) ||
            !verified.Exports.SequenceEqual(package.Exports))
            throw new InvalidDataException("La reconstrucción no conserva las tablas originales.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        File.WriteAllBytes(destination, rebuilt);
    }

    private static int Count(byte[] data, int offset, string label)
    {
        RequireRange(data, offset, 4, label);
        int value = I32(data, offset);
        if (value < 0 || value > 2_000_000) throw new InvalidDataException($"Cantidad inválida de {label}: {value}.");
        return value;
    }

    private static int Offset(byte[] data, int offset, string label, int fileLength)
    {
        RequireRange(data, offset, 4, label);
        uint value = U32(data, offset);
        if (value > fileLength) throw new InvalidDataException($"Offset fuera del archivo en tabla de {label}: {value}.");
        return checked((int)value);
    }

    public static int DecodeCompactIndex(byte[] data, ref int position) => ReadCompact(data, ref position);

    private static int ReadCompact(byte[] data, ref int position)
    {
        RequireRange(data, position, 1, "índice compacto");
        byte first = data[position++];
        bool negative = (first & 0x80) != 0;
        bool more = (first & 0x40) != 0;
        uint value = (uint)(first & 0x3F);
        int shift = 6;
        int bytes = 1;
        while (more)
        {
            if (bytes >= 5) throw new InvalidDataException("Índice compacto demasiado largo.");
            RequireRange(data, position, 1, "índice compacto");
            byte next = data[position++];
            bool fifthByte = bytes == 4;
            if (fifthByte && (next & 0xF0) != 0)
                throw new InvalidDataException("El quinto byte del índice compacto contiene bits reservados.");
            value |= (uint)(next & (fifthByte ? 0x0F : 0x7F)) << shift;
            bytes++;
            if (fifthByte && (next & 0x80) != 0)
                throw new InvalidDataException("El índice compacto continúa después del quinto byte.");
            shift += 7;
            more = (next & 0x80) != 0;
        }
        if (value > int.MaxValue) throw new InvalidDataException("Índice compacto fuera del rango Int32.");
        if (negative && value == 0) throw new InvalidDataException("Índice compacto contiene cero negativo.");
        return negative ? -(int)value : (int)value;
    }

    private static int ReadI32(byte[] data, ref int position)
    {
        RequireRange(data, position, 4, "referencia de objeto");
        int value = I32(data, position); position += 4; return value;
    }

    private static void CheckNameIndex(int value, int count, string label)
    {
        if (value < 0 || value >= count) throw new InvalidDataException($"{label}: índice de nombre {value} fuera de rango ({count}).");
    }

    private static void CheckObjectRef(int value, int imports, int exports, string label)
    {
        if (value < -imports || value > exports) throw new InvalidDataException($"{label}: referencia {value} fuera de rango (imports {imports}, exports {exports}).");
    }

    private static void RequireRange(byte[] data, int offset, int length, string label)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new InvalidDataException($"Datos truncados o fuera del archivo al leer {label} (offset {offset}, tamaño {length}).");
    }

    internal byte[] CopyOriginalSlice(int offset, int length) => _data.AsSpan(offset, length).ToArray();
    internal int OriginalLength => _data.Length;

    private static ushort U16(byte[] d, int p) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(p, 2));
    private static uint U32(byte[] d, int p) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p, 4));
    private static int I32(byte[] d, int p) => BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p, 4));
    private static void W32(byte[] d, int p, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(p, 4), v);
}

public sealed record PackageSummary(ushort Version, ushort LicenseeVersion, uint Flags,
    int NameCount, int NameOffset, int ExportCount, int ExportOffset, int ImportCount,
    int ImportOffset, int GenerationCount, int FileLength)
{
    public string ProfileDescription => (Version, LicenseeVersion) switch
    {
        (111, _) => "Lineage II: paquete nativo v111",
        (121, _) => "Lineage II: paquete nativo v121",
        (123, 0) => "Lineage II: paquete compilado v123/0",
        (126, 0) => "UE2 v126/0 genérico; no clasificado como perfil L2 estándar",
        _ => "Paquete Unreal clásico; perfil de juego pendiente de identificar"
    };
}

public sealed record ImportEntry(int ClassPackage, int ClassName, int Outer, int ObjectName);
public sealed record ExportEntry(int ClassRef, int SuperRef, int Outer, int ObjectName,
    uint ObjectFlags, int SerialSize, int SerialOffset);
