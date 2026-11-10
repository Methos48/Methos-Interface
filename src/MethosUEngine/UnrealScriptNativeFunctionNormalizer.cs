using System.Text.Json;

namespace MethosUEngine;

/// <summary>
/// Converts UELib's synthetic __NFUN_token__(...) calls back to UnrealScript
/// native function calls and operators using the Interlude Core/Engine/NWindow registry.
/// </summary>
public static class UnrealScriptNativeFunctionNormalizer
{
    private sealed record NativeEntry(int NativeToken, string Name, bool IsOperator);

    public static string Normalize(string source, string registryPath, out int normalized, out int unresolved)
    {
        ArgumentNullException.ThrowIfNull(source);
        normalized = 0;
        unresolved = 0;
        if (!File.Exists(registryPath)) return source;

        var entries = JsonSerializer.Deserialize<List<NativeEntry>>(
            File.ReadAllText(registryPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

        var registry = entries
            .Where(item => item.NativeToken > 0 && !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.NativeToken)
            .ToDictionary(group => group.Key, group => group.First());

        return NormalizeRange(source, registry, ref normalized, ref unresolved);
    }

    private static string NormalizeRange(string source, IReadOnlyDictionary<int, NativeEntry> registry,
        ref int normalized, ref int unresolved)
    {
        var output = new System.Text.StringBuilder(source.Length);
        int position = 0;

        while (position < source.Length)
        {
            if (source[position] == '"' || source[position] == '\'')
            {
                CopyQuoted(source, ref position, output);
                continue;
            }

            if (position + 1 < source.Length && source[position] == '/' &&
                (source[position + 1] == '/' || source[position + 1] == '*'))
            {
                CopyComment(source, ref position, output);
                continue;
            }

            if (!TryReadWrapper(source, position, out int token, out int openParen, out int closeParen))
            {
                output.Append(source[position++]);
                continue;
            }

            string rawArguments = source.Substring(openParen + 1, closeParen - openParen - 1);
            var arguments = SplitArguments(rawArguments)
                .Select(argument => NormalizeRange(argument, registry, ref normalized, ref unresolved))
                .ToArray();

            if (!registry.TryGetValue(token, out var entry))
            {
                unresolved++;
                output.Append(source, position, closeParen - position + 1);
            }
            else
            {
                output.Append(FormatCall(entry, arguments));
                normalized++;
            }

            position = closeParen + 1;
        }

        return output.ToString();
    }

    private static bool TryReadWrapper(string source, int start, out int token, out int openParen, out int closeParen)
    {
        token = 0;
        openParen = closeParen = -1;
        const string prefix = "__NFUN_";
        if (!source.AsSpan(start).StartsWith(prefix, StringComparison.Ordinal)) return false;

        int cursor = start + prefix.Length;
        int digitsStart = cursor;
        while (cursor < source.Length && char.IsAsciiDigit(source[cursor])) cursor++;
        if (cursor == digitsStart || cursor + 2 >= source.Length ||
            source[cursor] != '_' || source[cursor + 1] != '_' || source[cursor + 2] != '(')
            return false;
        if (!int.TryParse(source.AsSpan(digitsStart, cursor - digitsStart), out token)) return false;

        openParen = cursor + 2;
        closeParen = FindMatchingParen(source, openParen);
        return closeParen >= 0;
    }

    private static int FindMatchingParen(string source, int openParen)
    {
        int depth = 0;
        int position = openParen;
        while (position < source.Length)
        {
            char current = source[position];
            if (current == '"' || current == '\'')
            {
                position++;
                while (position < source.Length)
                {
                    if (source[position] == '\\') { position += Math.Min(2, source.Length - position); continue; }
                    if (source[position++] == current) break;
                }
                continue;
            }

            if (position + 1 < source.Length && current == '/' &&
                (source[position + 1] == '/' || source[position + 1] == '*'))
            {
                if (source[position + 1] == '/')
                {
                    position += 2;
                    while (position < source.Length && source[position] != '\n') position++;
                }
                else
                {
                    position += 2;
                    int end = source.IndexOf("*/", position, StringComparison.Ordinal);
                    position = end < 0 ? source.Length : end + 2;
                }
                continue;
            }

            if (current == '(') depth++;
            else if (current == ')' && --depth == 0) return position;
            position++;
        }
        return -1;
    }

    private static string[] SplitArguments(string arguments)
    {
        var result = new List<string>();
        int start = 0, depth = 0, position = 0;
        while (position < arguments.Length)
        {
            char current = arguments[position];
            if (current == '"' || current == '\'')
            {
                char quote = current;
                position++;
                while (position < arguments.Length)
                {
                    if (arguments[position] == '\\') { position += Math.Min(2, arguments.Length - position); continue; }
                    if (arguments[position++] == quote) break;
                }
                continue;
            }

            if (position + 1 < arguments.Length && current == '/' &&
                (arguments[position + 1] == '/' || arguments[position + 1] == '*'))
            {
                if (arguments[position + 1] == '/')
                {
                    position += 2;
                    while (position < arguments.Length && arguments[position] != '\n') position++;
                }
                else
                {
                    position += 2;
                    int end = arguments.IndexOf("*/", position, StringComparison.Ordinal);
                    position = end < 0 ? arguments.Length : end + 2;
                }
                continue;
            }

            if (current == '(' || current == '[') depth++;
            else if (current == ')' || current == ']') depth--;
            else if (current == ',' && depth == 0)
            {
                result.Add(arguments[start..position].Trim());
                start = position + 1;
            }
            position++;
        }

        string last = arguments[start..].Trim();
        if (last.Length > 0 || result.Count > 0) result.Add(last);
        return result.ToArray();
    }

    private static string FormatCall(NativeEntry entry, string[] args)
    {
        if (!entry.IsOperator) return entry.Name + "(" + string.Join(", ", args) + ")";

        string name = entry.Name;
        if (name == "Not_PreBool" && args.Length == 1) return "(!" + args[0] + ")";
        if (name.StartsWith("Subtract_Pre", StringComparison.Ordinal) && args.Length == 1) return "(-" + args[0] + ")";
        if (name == "Complement_PreInt" && args.Length == 1) return "(~" + args[0] + ")";
        if (name.StartsWith("AddAdd_Pre", StringComparison.Ordinal) && args.Length == 1) return "(++" + args[0] + ")";
        if (name.StartsWith("SubtractSubtract_Pre", StringComparison.Ordinal) && args.Length == 1) return "(--" + args[0] + ")";
        if (name.StartsWith("AddAdd_", StringComparison.Ordinal) && args.Length == 1) return "(" + args[0] + "++)";
        if (name.StartsWith("SubtractSubtract_", StringComparison.Ordinal) && args.Length == 1) return "(" + args[0] + "--)";

        string? op = GetBinaryOperator(name);
        if (op != null && args.Length == 2) return "(" + args[0] + " " + op + " " + args[1] + ")";
        return entry.Name + "(" + string.Join(", ", args) + ")";
    }

    private static string? GetBinaryOperator(string name)
    {
        if (name.StartsWith("GreaterGreaterGreater_", StringComparison.Ordinal)) return ">>>";
        if (name.StartsWith("GreaterGreater_", StringComparison.Ordinal)) return ">>";
        if (name.StartsWith("LessLess_", StringComparison.Ordinal)) return "<<";
        if (name.StartsWith("AddEqual_", StringComparison.Ordinal)) return "+=";
        if (name.StartsWith("SubtractEqual_", StringComparison.Ordinal)) return "-=";
        if (name.StartsWith("MultiplyEqual_", StringComparison.Ordinal)) return "*=";
        if (name.StartsWith("DivideEqual_", StringComparison.Ordinal)) return "/=";
        if (name.StartsWith("EqualEqual_", StringComparison.Ordinal)) return "==";
        if (name.StartsWith("NotEqual_", StringComparison.Ordinal)) return "!=";
        if (name.StartsWith("ComplementEqual_", StringComparison.Ordinal)) return "~=";
        if (name.StartsWith("GreaterEqual_", StringComparison.Ordinal)) return ">=";
        if (name.StartsWith("LessEqual_", StringComparison.Ordinal)) return "<=";
        if (name.StartsWith("AndAnd_", StringComparison.Ordinal)) return "&&";
        if (name.StartsWith("OrOr_", StringComparison.Ordinal)) return "||";
        if (name.StartsWith("XorXor_", StringComparison.Ordinal)) return "^^";
        if (name.StartsWith("Greater_", StringComparison.Ordinal)) return ">";
        if (name.StartsWith("Less_", StringComparison.Ordinal)) return "<";
        if (name.StartsWith("Add_", StringComparison.Ordinal)) return "+";
        if (name.StartsWith("Subtract_", StringComparison.Ordinal)) return "-";
        if (name.StartsWith("Multiply_", StringComparison.Ordinal)) return "*";
        if (name.StartsWith("Divide_", StringComparison.Ordinal)) return "/";
        if (name.StartsWith("Percent_", StringComparison.Ordinal)) return "%";
        if (name.StartsWith("And_", StringComparison.Ordinal)) return "&";
        if (name.StartsWith("Or_", StringComparison.Ordinal)) return "|";
        if (name.StartsWith("Xor_", StringComparison.Ordinal)) return "^";
        if (name.StartsWith("Concat_", StringComparison.Ordinal)) return "$";
        if (name.StartsWith("At_", StringComparison.Ordinal)) return "@";
        if (name.StartsWith("Cross_", StringComparison.Ordinal)) return "cross";
        if (name.StartsWith("Dot_", StringComparison.Ordinal)) return "dot";
        if (name.StartsWith("MultiplyMultiply_", StringComparison.Ordinal)) return "**";
        return null;
    }

    private static void CopyQuoted(string source, ref int position, System.Text.StringBuilder output)
    {
        char quote = source[position];
        output.Append(source[position++]);
        while (position < source.Length)
        {
            char current = source[position++];
            output.Append(current);
            if (current == '\\' && position < source.Length) output.Append(source[position++]);
            else if (current == quote) break;
        }
    }

    private static void CopyComment(string source, ref int position, System.Text.StringBuilder output)
    {
        if (source[position + 1] == '/')
        {
            while (position < source.Length)
            {
                char current = source[position++];
                output.Append(current);
                if (current == '\n') break;
            }
            return;
        }

        output.Append(source[position++]);
        output.Append(source[position++]);
        while (position < source.Length)
        {
            if (position + 1 < source.Length && source[position] == '*' && source[position + 1] == '/')
            {
                output.Append("*/");
                position += 2;
                break;
            }
            output.Append(source[position++]);
        }
    }
}
