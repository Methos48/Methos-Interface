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
        var decompiledFunctions = ReadFunctions(decompiledSource);
        var brokenFunctions = decompiledFunctions.Values
            .Where(function =>
            {
                if (function.Body.Contains("Failed to format", StringComparison.OrdinalIgnoreCase) ||
                    function.Body.Contains("Failed to decompile", StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(function.Body, @"\\b(?!self\\b)[A-Za-z_]\\w*\\.super\\s*\\(",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return true;
                if (!referenceFunctions.TryGetValue(function.Name, out var reference)) return false;
                int currentDepth = MaximumBraceDepth(function.Body);
                int referenceDepth = MaximumBraceDepth(reference.Body);
                return currentDepth >= 12 &&
                       currentDepth > referenceDepth + 5 &&
                       currentDepth >= referenceDepth * 2;
            })
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

    public static string RestoreInvalidTrailingReturns(string decompiledSource, string referenceSource,
        out IReadOnlyList<string> repairedFunctions)
    {
        ArgumentNullException.ThrowIfNull(decompiledSource);
        ArgumentNullException.ThrowIfNull(referenceSource);
        var repaired = new List<string>();
        IReadOnlyList<UnrealFunctionSource> currentFunctions;
        IReadOnlyList<UnrealFunctionSource> referenceFunctions;
        try
        {
            currentFunctions = UnrealScriptSourceIndex.ParseFunctions(decompiledSource);
            referenceFunctions = UnrealScriptSourceIndex.ParseFunctions(referenceSource);
        }
        catch (InvalidDataException)
        {
            repairedFunctions = repaired;
            return decompiledSource;
        }

        var referenceGroups = referenceFunctions.GroupBy(function => function.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var currentGroups = currentFunctions.GroupBy(function => function.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var edits = new List<(int Start, int Length)>();

        foreach (var function in currentFunctions)
        {
            if (!currentGroups.TryGetValue(function.Name, out var currentMatches) || currentMatches.Length != 1 ||
                !referenceGroups.TryGetValue(function.Name, out var referenceMatches) || referenceMatches.Length != 1)
                continue;

            string header = decompiledSource.Substring(function.DeclarationOffset,
                function.BodyOffset - function.DeclarationOffset);
            Match signature = Regex.Match(header,
                @"\b(?:function|event)\s+(?<returnType>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)?)\s+" +
                Regex.Escape(function.Name) + @"\s*\(",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!signature.Success || signature.Groups["returnType"].Value.Equals("void", StringComparison.OrdinalIgnoreCase))
                continue;

            string referenceBody = referenceMatches[0].Body.TrimEnd();
            if (Regex.IsMatch(referenceBody, @"\breturn\s*;\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                continue;

            Match trailingReturn = Regex.Match(function.Body, @"(?<statement>\breturn\s*;)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!trailingReturn.Success) continue;

            edits.Add((function.BodyOffset + trailingReturn.Groups["statement"].Index,
                trailingReturn.Groups["statement"].Length));
            repaired.Add(function.Name);
        }

        string result = decompiledSource;
        foreach (var edit in edits.OrderByDescending(item => item.Start))
            result = result.Remove(edit.Start, edit.Length);
        repairedFunctions = repaired;
        return result;
    }

    private sealed record FunctionBlock(string Name, int Start, int Length, string Text, string Body);

    private static int MaximumBraceDepth(string source)
    {
        int depth = 0, maximum = 0;
        bool inString = false, inLineComment = false, inBlockComment = false;
        char quote = '\0';

        for (int i = 0; i < source.Length; i++)
        {
            char current = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\n') inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }
            if (inString)
            {
                if (current == '\\')
                {
                    i++;
                    continue;
                }
                if (current == quote) inString = false;
                continue;
            }
            if (current == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }
            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }
            if (current is '"' or '\'')
            {
                inString = true;
                quote = current;
                continue;
            }

            if (current == '{')
            {
                depth++;
                maximum = Math.Max(maximum, depth);
            }
            else if (current == '}')
            {
                depth--;
            }
        }
        return maximum;
    }

    private static Dictionary<string, FunctionBlock> ReadFunctions(string source)
    {
        var result = new Dictionary<string, FunctionBlock>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<UnrealFunctionSource> functions;
        try
        {
            functions = UnrealScriptSourceIndex.ParseFunctions(source);
        }
        catch (InvalidDataException)
        {
            return ReadFunctionsLegacy(source);
        }

        foreach (var function in functions)
        {
            int lineStart = source.LastIndexOf('\n', Math.Max(0, function.DeclarationOffset - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            int start = lineStart;
            while (start < function.DeclarationOffset && char.IsWhiteSpace(source[start])) start++;

            int end = function.BodyOffset + function.BodyLength + 1;
            if (end > source.Length || end <= start) continue;
            result[function.Name] = new FunctionBlock(function.Name, start, end - start,
                source[start..end], source.Substring(function.BodyOffset, function.BodyLength));
        }
        return result;
    }

    private static Dictionary<string, FunctionBlock> ReadFunctionsLegacy(string source)
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
