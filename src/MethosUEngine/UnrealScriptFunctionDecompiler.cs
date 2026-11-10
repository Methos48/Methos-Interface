using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MethosUEngine;

/// <summary>
/// First native UnrealScript bytecode reader. It intentionally decodes the same
/// literal-return subset emitted by UnrealScriptFunctionCompiler.
/// </summary>
public static class UnrealScriptFunctionDecompiler
{
    private const byte EX_Return = 0x04;
    private const byte EX_IntConst = 0x1D;
    private const byte EX_FloatConst = 0x1E;
    private const byte EX_StringConst = 0x1F;
    private const byte EX_NameConst = 0x21;
    private const byte EX_IntZero = 0x25;
    private const byte EX_IntOne = 0x26;
    private const byte EX_True = 0x27;
    private const byte EX_False = 0x28;
    private const byte EX_Self = 0x17;
    private const byte EX_NoObject = 0x2A;
    private const byte EX_ReturnNothing = 0x3A;
    private const byte EX_UnicodeStringConst = 0x34;
    private const byte EX_EndOfScript = 0x47;

    public static string DecompileReturnOnly(byte[] bytecode,
        Func<int, string>? nameResolver = null) =>
        DecompileReturnOnly(bytecode, out _, nameResolver);

    public static string DecompileReturnOnly(byte[] bytecode, out int consumedBytes,
        Func<int, string>? nameResolver = null)
    {
        ArgumentNullException.ThrowIfNull(bytecode);
        consumedBytes = 0;
        if (bytecode.Length < 2)
            throw new InvalidDataException("El bytecode es demasiado corto.");

        int position = 0;
        byte statement = ReadByte(bytecode, ref position);
        string result;
        if (statement == EX_ReturnNothing)
        {
            result = "return;";
        }
        else if (statement == EX_Return)
        {
            string expression = ReadLiteral(bytecode, ref position, nameResolver);
            result = $"return {expression};";
        }
        else
        {
            throw new NotSupportedException($"Instrucción inicial de bytecode no admitida: 0x{statement:X2}.");
        }

        if (ReadByte(bytecode, ref position) != EX_EndOfScript)
            throw new NotSupportedException("La función contiene instrucciones adicionales fuera del subconjunto de descompilación actual.");
        consumedBytes = position;
        return result;
    }

    public static string DecompileExport(UnrealPackageIndex package, int exportReference,
        int scriptOffsetRelative, int maxScriptSpan) =>
        DecompileExport(package, exportReference, scriptOffsetRelative, maxScriptSpan, out _);

    public static string DecompileExport(UnrealPackageIndex package, int exportReference,
        int scriptOffsetRelative, int maxScriptSpan, out int consumedBytes)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (exportReference < 1 || exportReference > package.Exports.Count)
            throw new ArgumentOutOfRangeException(nameof(exportReference));
        var export = package.Exports[exportReference - 1];
        if (export.SerialSize <= 0) throw new InvalidDataException("El export no contiene datos serializados.");
        if (scriptOffsetRelative < 4 || maxScriptSpan < 0)
            throw new InvalidDataException("La posición o límite del bytecode no son válidos.");
        byte[] payload = package.CopyOriginalSlice(export.SerialOffset, export.SerialSize);
        int sizeOffset = scriptOffsetRelative - 4;
        if (sizeOffset > payload.Length - 4)
            throw new InvalidDataException("El campo de tamaño virtual del bytecode está fuera del export.");
        int virtualScriptSize = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(sizeOffset, 4));
        if (virtualScriptSize < 0)
            throw new InvalidDataException("El tamaño virtual del bytecode es negativo.");
        if (scriptOffsetRelative > payload.Length)
            throw new InvalidDataException("La posición del bytecode excede el export.");

        int available = payload.Length - scriptOffsetRelative;
        if (maxScriptSpan > 0) available = Math.Min(available, maxScriptSpan);
        byte[] bytecode = payload.AsSpan(scriptOffsetRelative, available).ToArray();
        return DecompileReturnOnly(bytecode, out consumedBytes, index =>
        {
            if (index < 0 || index >= package.Names.Count)
                throw new InvalidDataException($"Índice de nombre inválido en bytecode: {index}.");
            return package.Names[index];
        });
    }

    private static string ReadLiteral(byte[] data, ref int position, Func<int, string>? nameResolver)
    {
        byte token = ReadByte(data, ref position);
        switch (token)
        {
            case EX_IntZero: return "0";
            case EX_IntOne: return "1";
            case EX_True: return "true";
            case EX_False: return "false";
            case EX_Self: return "self";
            case EX_NoObject: return "none";
            case EX_IntConst:
                return ReadInt32(data, ref position).ToString(CultureInfo.InvariantCulture);
            case EX_FloatConst:
            {
                float value = BitConverter.Int32BitsToSingle(ReadInt32(data, ref position));
                return value.ToString("R", CultureInfo.InvariantCulture) + "f";
            }
            case EX_StringConst:
                return Quote(ReadAnsiString(data, ref position));
            case EX_UnicodeStringConst:
                return Quote(ReadUnicodeString(data, ref position));
            case EX_NameConst:
            {
                int index = ReadInt32(data, ref position);
                string name = nameResolver?.Invoke(index)
                    ?? throw new NotSupportedException("No se proporcionó un resolvedor para constantes Name.");
                return "'" + name.Replace("\\\\", "\\\\\\\\").Replace("'", "\\\\'") + "'";
            }
            default:
                throw new NotSupportedException($"Expresión de bytecode no admitida: 0x{token:X2}.");
        }
    }

    private static string ReadAnsiString(byte[] data, ref int position)
    {
        int start = position;
        while (position < data.Length && data[position] != 0) position++;
        if (position >= data.Length) throw new InvalidDataException("StringConst sin terminador nulo.");
        string value = Encoding.Latin1.GetString(data, start, position - start);
        position++;
        return value;
    }

    private static string ReadUnicodeString(byte[] data, ref int position)
    {
        int start = position;
        while (position + 1 < data.Length && (data[position] != 0 || data[position + 1] != 0)) position += 2;
        if (position + 1 >= data.Length) throw new InvalidDataException("UnicodeStringConst sin terminador nulo.");
        string value = Encoding.Unicode.GetString(data, start, position - start);
        position += 2;
        return value;
    }

    private static string Quote(string value)
    {
        string escaped = value.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
        return "\"" + escaped + "\"";
    }

    private static byte ReadByte(byte[] data, ref int position)
    {
        if ((uint)position >= (uint)data.Length) throw new EndOfStreamException("Bytecode truncado.");
        return data[position++];
    }

    private static int ReadInt32(byte[] data, ref int position)
    {
        if (position > data.Length - 4) throw new EndOfStreamException("Constante Int32 truncada.");
        int value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(position, 4));
        position += 4;
        return value;
    }
}
