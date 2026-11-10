using System.Text.RegularExpressions;

namespace MethosUEngine;

/// <summary>
/// Repairs only functions that UELib explicitly failed to decompile, using the
/// matching function from the exact Interlude reference source for that class.
/// Healthy decompiled functions (including user edits) are never replaced.
/// </summary>
public static class UnrealScriptFailedFunctionRepair
{
    private static readonly Regex FunctionHeader = new(
        @"\b(?:final\s+|static\s+|simulated\s+|native\s+|iterator\s+|latent\s+|exec\s+|event\s+|reliable\s+|unreliable\s+)*(?:function|event|operator|preoperator|postoperator)\b[^;{}]*?\b(?<name>[A-Za-z_]\w*)\s*\([^;{}]*\)\s*(?:const\s*)?(?:\{)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string RestoreFailedFunctions(string decompiledSource, string referenceSource,
        out IReadOnlyList<string> restoredFunctions)
    {
        ArgumentNullException.ThrowIfNull(decompiledSource);
        ArgumentNullException.ThrowIfNull(referenceSource);
        var restored = new List<string>();
        var referenceFunctions = ReadFunctions(referenceSource);
        var brokenFunctions = ReadFunctions(decompiledSource)
            .Where(function => function.Body.Contains("Failed to format", StringComparison.OrdinalIgnoreCase) ||
                               function.Body.Contains("Failed to decompile", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var edits = new List<(int Start, int Length, string Replacement)>();
        foreach (var broken in brokenFunctions)
        {
            if (!referenceFunctions.TryGetValue(broken.Name, out var reference)) continue;
            edits.Add((broken.Start, broken.Length, reference.Text));
            restored.Add(broken.Name);
        }

        string result = decompiledSource;
        foreach (var edit in edits.OrderByDescending(item => item.Start))
            result = result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);

        restoredFunctions = restored;
        return result;
    }

    private sealed record FunctionBlock(string Name, int Start, int Length, string Text, string Body);

    private static Dictionary<string, FunctionBlock> ReadFunctions(string source)
    {
        var result = new Dictionary<string, FunctionBlock>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in FunctionHeader.Matches(source))
        {
            int openBrace = source.IndexOf('{', match.Index + match.Length - 1);
            if (openBrace < 0) continue;
            int closeBrace = FindClosingBrace(source, openBrace);
            if (closeBrace < 0) continue;
            string name = match.Groups["name"].Value;
            int start = match.Index;
            int end = closeBrace + 1;
            result[name] = new FunctionBlock(name, start, end - start,
                source[start..end], source[openBrace..(closeBrace + 1)]);
        }
        return result;
    }

    private static int FindClosingBrace(string source, int openBrace)
    {
        int depth = 0;
        int position = openBrace;
        while (position < source.Length)
        {
            char current = source[position];
            if (current is '"' or '\'')
            {
                char quote = current;
                position++;
                while (position < source.Length)
                {
                    if (source[position] == '\\') { position += Math.Min(2, source.Length - position); continue; }
                    if (source[position++] == quote) break;
                }
                continue;
            }

            if (position + 1 < source.Length && current == '/' && source[position + 1] == '/')
            {
                position += 2;
                while (position < source.Length && source[position] != '\n') position++;
                continue;
            }
            if (position + 1 < source.Length && current == '/' && source[position + 1] == '*')
            {
                position += 2;
                int end = source.IndexOf("*/", position, StringComparison.Ordinal);
                position = end < 0 ? source.Length : end + 2;
                continue;
            }

            if (current == '{') depth++;
            else if (current == '}' && --depth == 0) return position;
            position++;
        }
        return -1;
    }
}
