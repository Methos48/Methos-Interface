using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MethosUEngine;

/// <summary>
/// Initial native UnrealScript bytecode reader. It decodes straight-line assignments
/// and return statements emitted by the current compiler subset.
/// </summary>
public static class UnrealScriptFunctionDecompiler
{
    private readonly record struct DecodedExpression(string Text, byte Precedence);
    private const byte EX_LocalVariable = 0x00;
    private const byte EX_InstanceVariable = 0x01;
    private const byte EX_DefaultVariable = 0x02;
    private const byte EX_Return = 0x04;
    private const byte EX_Let = 0x0F;
    private const byte EX_EndFunctionParms = 0x16;
    private const byte EX_ExtendedNative = 0x60;
    private const byte EX_FirstNative = 0x70;
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
        Func<int, string>? nameResolver = null, Func<int, string>? objectResolver = null,
        Func<ushort, UnrealScriptNativeFunction?>? nativeResolver = null)
    {
        ArgumentNullException.ThrowIfNull(bytecode);
        if (bytecode.Length < 2 || bytecode[^1] != EX_EndOfScript)
            throw new InvalidDataException("El bytecode no termina con EndOfScript.");

        int position = 0;
        var statements = new List<string>();
        bool sawReturn = false;
        while (position < bytecode.Length)
        {
            byte token = ReadByte(bytecode, ref position);
            if (token == EX_EndOfScript)
            {
                if (position != bytecode.Length)
                    throw new InvalidDataException("Hay bytes después de EndOfScript.");
                break;
            }
            if (sawReturn)
                throw new NotSupportedException("El bytecode contiene instrucciones después de return.");

            if (token == EX_ReturnNothing)
            {
                statements.Add("return;");
                sawReturn = true;
            }
            else if (token == EX_Return)
            {
                var expression = ReadLiteral(bytecode, ref position, nameResolver, objectResolver, nativeResolver);
                statements.Add($"return {expression.Text};");
                sawReturn = true;
            }
            else if (token == EX_Let)
            {
                var left = ReadLiteral(bytecode, ref position, nameResolver, objectResolver, nativeResolver);
                var right = ReadLiteral(bytecode, ref position, nameResolver, objectResolver, nativeResolver);
                statements.Add($"{left.Text} = {right.Text};");
            }
            else
            {
                throw new NotSupportedException($"Instrucción de bytecode no admitida: 0x{token:X2}.");
            }
        }

        if (!sawReturn)
            throw new InvalidDataException("El bytecode no contiene una instrucción return.");
        return string.Join(Environment.NewLine, statements);
    }

    public static string DecompileExport(UnrealPackageIndex package, int exportReference,
        int scriptOffsetRelative, int scriptSize, Func<int, string>? objectResolver = null,
        Func<ushort, UnrealScriptNativeFunction?>? nativeResolver = null) =>
        DecompileExport(package, exportReference, scriptOffsetRelative,
            scriptSize, scriptSize, objectResolver, nativeResolver);

    public static string DecompileExport(UnrealPackageIndex package, int exportReference,
        int scriptOffsetRelative, int declaredScriptSize, int actualScriptSize,
        Func<int, string>? objectResolver = null,
        Func<ushort, UnrealScriptNativeFunction?>? nativeResolver = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (exportReference < 1 || exportReference > package.Exports.Count)
            throw new ArgumentOutOfRangeException(nameof(exportReference));
        var export = package.Exports[exportReference - 1];
        if (export.SerialSize <= 0) throw new InvalidDataException("El export no contiene datos serializados.");
        if (scriptOffsetRelative < 4 || declaredScriptSize < 0 || actualScriptSize < 0)
            throw new InvalidDataException("La posición o tamaño del bytecode no son válidos.");
        byte[] payload = package.CopyOriginalSlice(export.SerialOffset, export.SerialSize);
        int sizeOffset = scriptOffsetRelative - 4;
        if (sizeOffset > payload.Length - 4 ||
            BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(sizeOffset, 4)) != declaredScriptSize)
            throw new InvalidDataException("El tamaño declarado del bytecode no coincide con el campo serializado.");
        if (scriptOffsetRelative > payload.Length - actualScriptSize)
            throw new InvalidDataException("El bytecode real excede el tamaño del export.");
        byte[] bytecode = payload.AsSpan(scriptOffsetRelative, actualScriptSize).ToArray();
        return DecompileReturnOnly(bytecode, index =>
        {
            if (index < 0 || index >= package.Names.Count)
                throw new InvalidDataException($"Índice de nombre inválido en bytecode: {index}.");
            return package.Names[index];
        }, objectResolver ?? package.ResolveObjectName, nativeResolver);
    }

    private static DecodedExpression ReadLiteral(byte[] data, ref int position, Func<int, string>? nameResolver,
        Func<int, string>? objectResolver, Func<ushort, UnrealScriptNativeFunction?>? nativeResolver)
    {
        byte token = ReadByte(data, ref position);
        if (token >= EX_FirstNative || (token >= EX_ExtendedNative && token < EX_FirstNative))
            return ReadNativeExpression(data, ref position, token, nameResolver, objectResolver, nativeResolver);
        switch (token)
        {
            case EX_LocalVariable:
            case EX_InstanceVariable:
            case EX_DefaultVariable:
            {
                int reference = ReadInt32(data, ref position);
                string variable = objectResolver?.Invoke(reference)
                    ?? throw new NotSupportedException("No se proporcionó un resolvedor para variables UnrealScript.");
                return new DecodedExpression(token == EX_DefaultVariable ? "default." + variable : variable, 0);
            }
            case EX_IntZero: return new DecodedExpression("0", 0);
            case EX_IntOne: return new DecodedExpression("1", 0);
            case EX_True: return new DecodedExpression("true", 0);
            case EX_False: return new DecodedExpression("false", 0);
            case EX_Self: return new DecodedExpression("self", 0);
            case EX_NoObject: return new DecodedExpression("none", 0);
            case EX_IntConst:
                return new DecodedExpression(ReadInt32(data, ref position).ToString(CultureInfo.InvariantCulture), 0);
            case EX_FloatConst:
            {
                float value = BitConverter.Int32BitsToSingle(ReadInt32(data, ref position));
                return new DecodedExpression(value.ToString("R", CultureInfo.InvariantCulture) + "f", 0);
            }
            case EX_StringConst:
                return new DecodedExpression(Quote(ReadAnsiString(data, ref position)), 0);
            case EX_UnicodeStringConst:
                return new DecodedExpression(Quote(ReadUnicodeString(data, ref position)), 0);
            case EX_NameConst:
            {
                int index = ReadInt32(data, ref position);
                string name = nameResolver?.Invoke(index)
                    ?? throw new NotSupportedException("No se proporcionó un resolvedor para constantes Name.");
                if (name.Contains("'")) throw new NotSupportedException("Name literal con apóstrofo no admitido en este subconjunto.");
                return new DecodedExpression("'" + name + "'", 0);
            }
            default:
                throw new NotSupportedException($"Expresión de bytecode no admitida: 0x{token:X2}.");
        }
    }

    private static DecodedExpression ReadNativeExpression(byte[] data, ref int position, byte firstToken,
        Func<int, string>? nameResolver, Func<int, string>? objectResolver,
        Func<ushort, UnrealScriptNativeFunction?>? nativeResolver)
    {
        ushort token = firstToken >= EX_FirstNative
            ? firstToken
            : (ushort)(((firstToken - EX_ExtendedNative) << 8) | ReadByte(data, ref position));
        var native = nativeResolver?.Invoke(token)
            ?? throw new NotSupportedException($"No hay metadatos para la función nativa 0x{token:X}.");
        string result;
        switch (native.Kind)
        {
            case UnrealScriptNativeFunctionKind.Operator:
            {
                var left = ReadLiteral(data, ref position, nameResolver, objectResolver, nativeResolver);
                var right = ReadLiteral(data, ref position, nameResolver, objectResolver, nativeResolver);
                ExpectEndFunctionParms(data, ref position);
                string leftText = left.Precedence > native.Precedence ? $"({left.Text})" : left.Text;
                string rightText = right.Precedence >= native.Precedence ? $"({right.Text})" : right.Text;
                result = $"{leftText} {native.Name} {rightText}";
                break;
            }
            case UnrealScriptNativeFunctionKind.PreOperator:
            {
                var operand = ReadLiteral(data, ref position, nameResolver, objectResolver, nativeResolver);
                ExpectEndFunctionParms(data, ref position);
                string operandText = operand.Precedence > native.Precedence ? $"({operand.Text})" : operand.Text;
                result = native.Name.All(char.IsLetter) ? native.Name + " " + operandText : native.Name + operandText;
                break;
            }
            case UnrealScriptNativeFunctionKind.PostOperator:
            {
                var operand = ReadLiteral(data, ref position, nameResolver, objectResolver, nativeResolver);
                ExpectEndFunctionParms(data, ref position);
                string operandText = operand.Precedence < native.Precedence ? $"({operand.Text})" : operand.Text;
                result = native.Name.All(char.IsLetter) ? operandText + " " + native.Name : operandText + native.Name;
                break;
            }
            case UnrealScriptNativeFunctionKind.Function:
            {
                var arguments = new List<string>();
                while (true)
                {
                    if (position >= data.Length) throw new InvalidDataException("Llamada nativa sin EndFunctionParms.");
                    if (data[position] == EX_EndFunctionParms)
                    {
                        position++;
                        break;
                    }
                    arguments.Add(ReadLiteral(data, ref position, nameResolver, objectResolver, nativeResolver).Text);
                }
                result = $"{native.Name}({string.Join(", ", arguments)})";
                break;
            }
            default:
                throw new NotSupportedException($"Tipo de función nativa no admitido para 0x{token:X}.");
        }
        byte precedence = native.Kind == UnrealScriptNativeFunctionKind.Function
            ? (byte)0 : native.Precedence;
        return new DecodedExpression(result, precedence);
    }

    private static void ExpectEndFunctionParms(byte[] data, ref int position)
    {
        if (ReadByte(data, ref position) != EX_EndFunctionParms)
            throw new InvalidDataException("La expresión nativa no termina con EndFunctionParms.");
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
        if (value == "\"") return "\"\"\"";
        if (value.Contains('\r') || value.Contains('\n'))
            throw new NotSupportedException("Cadena con saltos de línea no admitida en este subconjunto.");
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
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
