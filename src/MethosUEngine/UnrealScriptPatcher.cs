using System.Buffers.Binary;

namespace MethosUEngine;

/// <summary>
/// Replaces the bytecode section inside an existing UE1/UE2 UStruct export.
/// UELib supplies the script offset relative to the object's serialized payload.
/// </summary>
public static class UnrealScriptPatcher
{
    public static byte[] ReplaceBytecode(byte[] exportPayload, ushort packageVersion,
        int scriptOffsetRelative, int originalScriptSize, byte[] newBytecode)
    {
        ArgumentNullException.ThrowIfNull(exportPayload);
        ArgumentNullException.ThrowIfNull(newBytecode);
        if (packageVersion >= 639)
            throw new NotSupportedException("Este parcheador inicial solo admite el formato UE1/UE2 anterior a DataScriptSize.");
        if (scriptOffsetRelative < 4 || originalScriptSize < 0)
            throw new InvalidDataException("La posición o el tamaño del bytecode original no son válidos.");

        int sizeFieldOffset = scriptOffsetRelative - 4;
        if (sizeFieldOffset > exportPayload.Length - 4)
            throw new InvalidDataException("El campo de tamaño del bytecode está fuera del objeto.");
        int storedSize = BinaryPrimitives.ReadInt32LittleEndian(exportPayload.AsSpan(sizeFieldOffset, 4));
        if (storedSize != originalScriptSize)
            throw new InvalidDataException($"El tamaño del bytecode no coincide: el paquete declara {storedSize}, el lector reporta {originalScriptSize}. Se cancela la modificación.");

        if (scriptOffsetRelative > exportPayload.Length - originalScriptSize)
            throw new InvalidDataException("El bytecode original excede los límites del objeto.");

        int suffixOffset = checked(scriptOffsetRelative + originalScriptSize);
        using var output = new MemoryStream(checked(exportPayload.Length - originalScriptSize + newBytecode.Length));
        output.Write(exportPayload, 0, sizeFieldOffset);
        WriteInt32(output, newBytecode.Length);
        output.Write(newBytecode);
        output.Write(exportPayload, suffixOffset, exportPayload.Length - suffixOffset);
        return output.ToArray();
    }

    public static void ReplaceExportBytecode(UnrealPackageIndex package, UnrealPackageBuilder builder,
        int exportReference, int scriptOffsetRelative, int originalScriptSize, byte[] newBytecode)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(builder);
        if (exportReference < 1 || exportReference > package.Exports.Count)
            throw new ArgumentOutOfRangeException(nameof(exportReference));
        var export = package.Exports[exportReference - 1];
        if (export.SerialSize <= 0)
            throw new InvalidDataException("El export no contiene datos serializados.");
        byte[] payload = package.CopyOriginalSlice(export.SerialOffset, export.SerialSize);
        byte[] patched = ReplaceBytecode(payload, package.Summary.Version,
            scriptOffsetRelative, originalScriptSize, newBytecode);
        builder.ReplaceExportPayload(exportReference, patched);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }
}
