using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MethosUEngine;

public sealed record UnrealScriptVariableReference(byte Token, int ObjectReference);

/// <summary>
/// First native UnrealScript code generator. This deliberately accepts only a single
/// return statement with a literal expression; unsupported bodies fail closed.
/// Token IDs are the UE2 bytecode map used by the classic package profile.
/// </summary>
public static class UnrealScriptFunctionCompiler
{
    private const byte EX_LocalVariable = 0x00;
    private const byte EX_InstanceVariable = 0x01;
    private const byte EX_DefaultVariable = 0x02;
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

    private static readonly Regex ReturnExpression = new(
        @"^return\s+(?<expr>.+?)\s*;$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex NumericLiteral = new(
        @"^[+-]?(?:0[xX][0-9a-fA-F]+|(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?[fF]?)$",
        RegexOptions.CultureInvariant);

    public static byte[] CompileReturnOnlyBody(string body, Func<string, int>? nameIndexResolver = null,
        Func<string, UnrealScriptVariableReference>? variableResolver = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        string clean = StripComments(body).Trim();
        if (Regex.IsMatch(clean, @"^return\s*;$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return new[] { EX_ReturnNothing, EX_EndOfScript };

        Match match = ReturnExpression.Match(clean);
        if (!match.Success)
            throw new NotSupportedException("Este primer compilador solo admite cuerpos con una única instrucción return y una expresión literal.");

        using var output = new MemoryStream();
        output.WriteByte(EX_Return);
        WriteLiteral(output, match.Groups["expr"].Value.Trim(), nameIndexResolver, variableResolver);
        output.WriteByte(EX_EndOfScript);
        return output.ToArray();
    }

    private static void WriteLiteral(Stream output, string expression, Func<string, int>? nameIndexResolver,
        Func<string, UnrealScriptVariableReference>? variableResolver)
    {
        if (expression.Equals("true", StringComparison.OrdinalIgnoreCase))
        { output.WriteByte(EX_True); return; }
        if (expression.Equals("false", StringComparison.OrdinalIgnoreCase))
        { output.WriteByte(EX_False); return; }
        if (expression.Equals("none", StringComparison.OrdinalIgnoreCase))
        { output.WriteByte(EX_NoObject); return; }
        if (expression.Equals("self", StringComparison.OrdinalIgnoreCase))
        { output.WriteByte(EX_Self); return; }

        if (expression.Length >= 2 && expression[0] == '"' && expression[^1] == '"')
        {
            string value = DecodeEscapes(expression[1..^1]);
            if (value.Any(c => c > 0x7F))
            {
                output.WriteByte(EX_UnicodeStringConst);
                output.Write(Encoding.Unicode.GetBytes(value + "\0"));
            }
            else
            {
                output.WriteByte(EX_StringConst);
                output.Write(Encoding.Latin1.GetBytes(value + "\0"));
            }
            return;
        }

        if (expression.Length >= 2 && expression[0] == '\'' && expression[^1] == '\'')
        {
            if (nameIndexResolver == null)
                throw new NotSupportedException("Una constante Name requiere resolver el índice de nombre del paquete.");
            int index = nameIndexResolver(DecodeEscapes(expression[1..^1]));
            output.WriteByte(EX_NameConst);
            WriteInt32(output, index);
            return;
        }

        if (Regex.IsMatch(expression, @"^(?:default\\.)?[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (variableResolver == null)
                throw new NotSupportedException($"La variable '{expression}' requiere resolver su referencia de objeto.");
            var variable = variableResolver(expression);
            if (variable.Token is not (EX_LocalVariable or EX_InstanceVariable or EX_DefaultVariable))
                throw new InvalidDataException($"Token de variable no válido: 0x{variable.Token:X2}.");
            output.WriteByte(variable.Token);
            WriteInt32(output, variable.ObjectReference);
            return;
        }

        if (!NumericLiteral.IsMatch(expression))
            throw new NotSupportedException($"Expresión literal todavía no implementada: {expression}");

        bool forceFloat = expression.EndsWith("f", StringComparison.OrdinalIgnoreCase);
        string numeric = forceFloat ? expression[..^1] : expression;
        if (numeric.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            numeric.StartsWith("+0x", StringComparison.OrdinalIgnoreCase) ||
            numeric.StartsWith("-0x", StringComparison.OrdinalIgnoreCase))
        {
            bool negative = numeric.StartsWith("-", StringComparison.Ordinal);
            string hex = numeric.TrimStart('+', '-')[2..];
            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint raw))
                throw new InvalidDataException($"Literal hexadecimal inválido o demasiado grande: {expression}");
            long signed = negative ? -(long)raw : raw;
            if (signed < int.MinValue || signed > int.MaxValue)
                throw new InvalidDataException($"Literal hexadecimal fuera de rango Int32: {expression}");
            WriteIntLiteral(output, (int)signed);
            return;
        }

        if (forceFloat || numeric.Contains('.') || numeric.Contains("e", StringComparison.OrdinalIgnoreCase))
        {
            if (!float.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                throw new InvalidDataException($"Literal float inválido: {expression}");
            output.WriteByte(EX_FloatConst);
            WriteInt32(output, BitConverter.SingleToInt32Bits(value));
            return;
        }

        if (!int.TryParse(numeric, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
            throw new InvalidDataException($"Literal int fuera de rango: {expression}");
        WriteIntLiteral(output, integer);
    }

    private static void WriteIntLiteral(Stream output, int value)
    {
        if (value == 0) { output.WriteByte(EX_IntZero); return; }
        if (value == 1) { output.WriteByte(EX_IntOne); return; }
        output.WriteByte(EX_IntConst);
        WriteInt32(output, value);
    }

    private static string StripComments(string source)
    {
        var output = new StringBuilder(source.Length);
        int i = 0;
        while (i < source.Length)
        {
            if (source[i] == '"' && i + 2 < source.Length &&
                source[i + 1] == '"' && source[i + 2] == '"')
            {
                output.Append("\"\"\"");
                i += 3;
                continue;
            }
            if (source[i] is '"' or '\'')
            {
                char quote = source[i];
                output.Append(source[i++]);
                while (i < source.Length)
                {
                    char c = source[i++];
                    output.Append(c);
                    if (c == quote) break;
                }
                continue;
            }
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                i += 2;
                while (i < source.Length && source[i] != '\n') i++;
                output.Append(' ');
                continue;
            }
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                i += 2;
                int commentDepth = 1;
                while (i < source.Length && commentDepth > 0)
                {
                    if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
                    { commentDepth++; i += 2; continue; }
                    if (source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/')
                    { commentDepth--; i += 2; continue; }
                    if (source[i] == '\n') output.Append('\n');
                    i++;
                }
                if (commentDepth != 0) throw new InvalidDataException("Comentario de bloque sin cerrar.");
                output.Append(' ');
                continue;
            }
            output.Append(source[i++]);
        }
        return output.ToString();
    }

    private static string DecodeEscapes(string value) => value;

    private static void WriteInt32(Stream output, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        output.Write(buffer);
    }
}
