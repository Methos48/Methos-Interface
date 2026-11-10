using System.Text.RegularExpressions;

namespace MethosUEngine;

/// <summary>
/// Restores symbolic arguments by comparing each decompiled call with the same
/// call occurrence in the original reference source. Only integer literals
/// paired with a symbolic identifier are changed.
/// </summary>
public static class UnrealScriptCallsiteArgumentRestorer
{
    private sealed record CallArgument(int Start, int End, string Text);
    private sealed record CallSite(string Name, List<CallArgument> Arguments);

    public static string RestoreSymbolicArguments(string decompiledSource, string referenceSource, out int restored)
    {
        ArgumentNullException.ThrowIfNull(decompiledSource);
        ArgumentNullException.ThrowIfNull(referenceSource);
        restored = 0;

        var referenceCalls = ReadCalls(referenceSource);
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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

            int open = i;
            while (open < decompiledSource.Length && char.IsWhiteSpace(decompiledSource[open])) open++;
            if (open >= decompiledSource.Length || decompiledSource[open] != '(') continue;
            if (Regex.IsMatch(decompiledSource[..nameStart], @"\b(function|event)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) continue;

            int close = FindMatchingParen(decompiledSource, open);
            if (close < 0) continue;
            var currentArgs = SplitArguments(decompiledSource, open + 1, close);
            int occurrence = occurrences.TryGetValue(name, out int seen) ? seen : 0;
            occurrences[name] = occurrence + 1;

            if (!referenceCalls.TryGetValue(name, out var candidates) || occurrence >= candidates.Count)
            {
                i = close + 1;
                continue;
            }

            var referenceArgs = candidates[occurrence].Arguments;
            if (referenceArgs.Count != currentArgs.Count)
            {
                i = close + 1;
                continue;
            }

            for (int argIndex = 0; argIndex < currentArgs.Count; argIndex++)
            {
                var range = currentArgs[argIndex];
                string current = decompiledSource[range.Start..range.End].Trim();
                string original = referenceArgs[argIndex].Text.Trim();
                if (!int.TryParse(current, out _) ||
                    !Regex.IsMatch(original, @"^(?:[A-Za-z_]\w*\.)*[A-Za-z_]\w*$",
                        RegexOptions.CultureInvariant)) continue;

                int start = range.Start;
                while (start < range.End && char.IsWhiteSpace(decompiledSource[start])) start++;
                edits.Add((start, current.Length, original));
            }

            i = close + 1;
        }

        string result = decompiledSource;
        foreach (var edit in edits.OrderByDescending(x => x.Start))
        {
            result = result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value);
            restored++;
        }
        return result;
    }

    private static Dictionary<string, List<CallSite>> ReadCalls(string source)
    {
        var result = new Dictionary<string, List<CallSite>>(StringComparer.OrdinalIgnoreCase);
        var mask = BuildCodeMask(source);
        for (int i = 0; i < source.Length;)
        {
            if (!mask[i] || !(char.IsLetter(source[i]) || source[i] == '_'))
            {
                i++;
                continue;
            }

            int start = i++;
            while (i < source.Length && mask[i] &&
                   (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
            string name = source[start..i];
            int open = i;
            while (open < source.Length && char.IsWhiteSpace(source[open])) open++;
            if (open >= source.Length || source[open] != '(') continue;
            if (Regex.IsMatch(source[..start], @"\b(function|event)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) continue;

            int close = FindMatchingParen(source, open);
            if (close < 0) continue;
            var ranges = SplitArguments(source, open + 1, close);
            var args = ranges.Select(r => new CallArgument(r.Start, r.End, source[r.Start..r.End])).ToList();
            if (!result.TryGetValue(name, out var list)) result[name] = list = new();
            list.Add(new CallSite(name, args));
            i = close + 1;
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
            if (i + 1 < end && c == '/' && source[i + 1] == '*')
            {
                i += 2;
                int commentEnd = source.IndexOf("*/", i, StringComparison.Ordinal);
                if (commentEnd < 0 || commentEnd >= end) { i = end; break; }
                i = commentEnd + 1;
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
                    if (source[i] == '\\' && i + 1 < source.Length) { i++; continue; }
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
            if (i + 1 < source.Length && c == '/' && source[i + 1] == '*')
            {
                i += 2;
                int commentEnd = source.IndexOf("*/", i, StringComparison.Ordinal);
                if (commentEnd < 0) return -1;
                i = commentEnd + 1;
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
                int commentEnd = source.IndexOf("*/", i, StringComparison.Ordinal);
                i = commentEnd < 0 ? source.Length : commentEnd + 2;
                continue;
            }
            mask[i++] = true;
        }
        return mask;
    }
}
