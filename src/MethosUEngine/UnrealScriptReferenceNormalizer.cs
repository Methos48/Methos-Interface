using System.Text.RegularExpressions;

namespace MethosUEngine;

/// <summary>
/// Restores numeric enum arguments emitted by UELib by consulting declared
/// UnrealScript enum types and their ordinal values. No global literal replacement.
/// </summary>
public static class UnrealScriptReferenceNormalizer
{
    private static readonly Regex EnumPattern = new(
        @"\benum\s+(?<name>[A-Za-z_]\w*)\s*\{(?<body>[\s\S]*?)\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FunctionPattern = new(
        @"\b(?:function|event)\b[^;{}]*?\b(?<name>[A-Za-z_]\w*)\s*\((?<parameters>[^()]*)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ParameterPattern = new(
        @"(?<type>(?:[A-Za-z_]\w*\.)*[A-Za-z_]\w*)\s+[A-Za-z_]\w*\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private sealed record EnumParameter(int Index, IReadOnlyDictionary<int, string> Values);

    public static string RestoreEnumArguments(string decompiledSource, string referenceSource, out int restored)
    {
        ArgumentNullException.ThrowIfNull(decompiledSource);
        ArgumentNullException.ThrowIfNull(referenceSource);
        restored = 0;

        var enums = ReadEnums(referenceSource);
        var signatures = ReadEnumParameters(referenceSource, enums);
        if (signatures.Count == 0) return decompiledSource;

        var mask = BuildCodeMask(decompiledSource);
        var edits = new List<(int Start, int Length, string Value)>();
        for (int i = 0; i < decompiledSource.Length;)
        {
            if (!mask[i] || !(char.IsLetter(decompiledSource[i]) || decompiledSource[i] == '_'))
            {
                i++;
                continue;
            }

            int nameStart = i++;
            while (i < decompiledSource.Length && mask[i] &&
                   (char.IsLetterOrDigit(decompiledSource[i]) || decompiledSource[i] == '_')) i++;
            string name = decompiledSource[nameStart..i];
            if (!signatures.TryGetValue(name, out var parameters)) continue;

            int open = i;
            while (open < decompiledSource.Length && char.IsWhiteSpace(decompiledSource[open])) open++;
            if (open >= decompiledSource.Length || decompiledSource[open] != '(') continue;
            int close = FindMatchingParen(decompiledSource, open);
            if (close < 0) continue;
            var args = SplitArguments(decompiledSource, open + 1, close);

            foreach (var parameter in parameters)
            {
                if (parameter.Index >= args.Count) continue;
                var range = args[parameter.Index];
                string raw = decompiledSource[range.Start..range.End].Trim();
                if (!int.TryParse(raw, out int ordinal) ||
                    !parameter.Values.TryGetValue(ordinal, out string? enumName)) continue;
                int start = range.Start;
                while (start < range.End && char.IsWhiteSpace(decompiledSource[start])) start++;
                edits.Add((start, raw.Length, enumName));
            }
            i = close + 1;
        }

        string result = decompiledSource;
        foreach (var edit in edits.OrderByDescending(x => x.Start))
            result = result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value);
        restored = edits.Count;
        return result;
    }

    private static Dictionary<string, Dictionary<int, string>> ReadEnums(string source)
    {
        var result = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in EnumPattern.Matches(source))
        {
            string body = Regex.Replace(match.Groups["body"].Value, @"/\*[\s\S]*?\*/|//[^\r\n]*", "");
            var values = new Dictionary<int, string>();
            int next = 0;
            foreach (string entry in body.Split(','))
            {
                Match member = Regex.Match(entry.Trim(), @"^(?<name>[A-Za-z_]\w*)(?:\s*=\s*(?<value>-?\d+))?$");
                if (!member.Success) continue;
                int value = member.Groups["value"].Success ? int.Parse(member.Groups["value"].Value) : next;
                values[value] = member.Groups["name"].Value;
                next = value + 1;
            }
            if (values.Count > 0) result[match.Groups["name"].Value] = values;
        }
        return result;
    }

    private static Dictionary<string, List<EnumParameter>> ReadEnumParameters(
        string source, IReadOnlyDictionary<string, Dictionary<int, string>> enums)
    {
        var result = new Dictionary<string, List<EnumParameter>>(StringComparer.OrdinalIgnoreCase);
        foreach (Match function in FunctionPattern.Matches(source))
        {
            string[] parts = function.Groups["parameters"].Value.Split(',');
            var parameters = new List<EnumParameter>();
            for (int index = 0; index < parts.Length; index++)
            {
                Match parameter = ParameterPattern.Match(parts[index].Trim());
                if (!parameter.Success) continue;
                string type = parameter.Groups["type"].Value.Split('.').Last();
                if (enums.TryGetValue(type, out var values))
                    parameters.Add(new EnumParameter(index, values));
            }
            if (parameters.Count == 0) continue;
            string name = function.Groups["name"].Value;
            if (!result.TryGetValue(name, out var list)) result[name] = list = new();
            list.AddRange(parameters);
        }
        return result;
    }

    private static List<(int Start, int End)> SplitArguments(string source, int start, int end)
    {
        var result = new List<(int Start, int End)>();
        int segment = start, depth = 0;
        for (int i = start; i < end; i++)
        {
            char c = source[i];
            if (c is '"' or '\'')
            {
                char quote = c;
                while (++i < end)
                {
                    if (source[i] == '\\' && i + 1 < end) { i++; continue; }
                    if (source[i] == quote) break;
                }
                continue;
            }
            if (i + 1 < end && c == '/' && source[i + 1] == '/')
            {
                i += 2;
                while (i < end && source[i] != '\n') i++;
                continue;
            }
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (c == ',' && depth == 0) { result.Add((segment, i)); segment = i + 1; }
        }
        if (segment < end || result.Count > 0) result.Add((segment, end));
        return result;
    }

    private static int FindMatchingParen(string source, int open)
    {
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            char c = source[i];
            if (c is '"' or '\'')
            {
                char quote = c;
                while (++i < source.Length)
                {
                    if (source[i] == '\\') { i++; continue; }
                    if (source[i] == quote) break;
                }
                continue;
            }
            if (i + 1 < source.Length && c == '/' && source[i + 1] == '/')
            {
                i += 2;
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i;
        }
        return -1;
    }

    private static bool[] BuildCodeMask(string source)
    {
        var mask = new bool[source.Length];
        for (int i = 0; i < source.Length;)
        {
            char c = source[i];
            if (c is '"' or '\'')
            {
                char quote = c;
                while (++i < source.Length)
                {
                    if (source[i] == '\\' && i + 1 < source.Length) { i++; continue; }
                    if (source[i++] == quote) break;
                }
                continue;
            }
            if (i + 1 < source.Length && c == '/' && source[i + 1] == '/')
            {
                i += 2;
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }
            if (i + 1 < source.Length && c == '/' && source[i + 1] == '*')
            {
                i += 2;
                int end = source.IndexOf("*/", i, StringComparison.Ordinal);
                i = end < 0 ? source.Length : end + 2;
                continue;
            }
            mask[i++] = true;
        }
        return mask;
    }
}