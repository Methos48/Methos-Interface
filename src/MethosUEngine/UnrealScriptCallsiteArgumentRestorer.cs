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

        var currentCalls = ReadCalls(decompiledSource);
        var referenceCalls = ReadCalls(referenceSource);
        var edits = new List<(int Start, int Length, string Value)>();

        foreach (var entry in currentCalls)
        {
            if (!referenceCalls.TryGetValue(entry.Key, out var referenceSites)) continue;
            var currentSites = entry.Value;
            int count = Math.Min(currentSites.Count, referenceSites.Count);
            for (int occurrence = 0; occurrence < count; occurrence++)
            {
                var currentArguments = currentSites[occurrence].Arguments;
                var referenceArguments = referenceSites[occurrence].Arguments;
                if (currentArguments.Count != referenceArguments.Count) continue;

                for (int argumentIndex = 0; argumentIndex < currentArguments.Count; argumentIndex++)
                {
                    var currentArgument = currentArguments[argumentIndex];
                    string currentValue = currentArgument.Text.Trim();
                    string originalValue = referenceArguments[argumentIndex].Text.Trim();
                    if (!int.TryParse(currentValue, out _) ||
                        !Regex.IsMatch(originalValue, @"^(?:[A-Za-z_]\w*\.)*[A-Za-z_]\w*$",
                            RegexOptions.CultureInvariant))
                        continue;

                    int start = currentArgument.Start;
                    while (start < currentArgument.End && char.IsWhiteSpace(decompiledSource[start])) start++;
                    edits.Add((start, currentValue.Length, originalValue));
                }
            }
        }

        string result = decompiledSource;
        foreach (var edit in edits.OrderByDescending(item => item.Start))
            result = result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value);
        restored = edits.Count;
        return result;
    }


    public static string RestoreSymbolicAssignments(string decompiledSource, string referenceSource, out int restored)
    {
        ArgumentNullException.ThrowIfNull(decompiledSource);
        ArgumentNullException.ThrowIfNull(referenceSource);
        restored = 0;

        IReadOnlyList<UnrealFunctionSource> currentFunctions;
        IReadOnlyList<UnrealFunctionSource> referenceFunctions;
        try
        {
            currentFunctions = UnrealScriptSourceIndex.ParseFunctions(decompiledSource);
            referenceFunctions = UnrealScriptSourceIndex.ParseFunctions(referenceSource);
        }
        catch (InvalidDataException)
        {
            return decompiledSource;
        }

        var currentGroups = currentFunctions.GroupBy(function => function.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var referenceGroups = referenceFunctions.GroupBy(function => function.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var edits = new List<(int Start, int Length, string Value)>();

        foreach (var currentFunction in currentFunctions)
        {
            if (!currentGroups.TryGetValue(currentFunction.Name, out var currentMatches) || currentMatches.Length != 1 ||
                !referenceGroups.TryGetValue(currentFunction.Name, out var referenceMatches) || referenceMatches.Length != 1)
                continue;

            var referenceAssignments = ReadAssignments(referenceMatches[0].Body);
            var currentAssignments = ReadAssignments(currentFunction.Body);
            var referenceByLeft = referenceAssignments.GroupBy(item => item.Left, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Select(item => item.Right).ToArray(),
                    StringComparer.OrdinalIgnoreCase);
            var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var assignment in currentAssignments)
            {
                int occurrence = occurrences.TryGetValue(assignment.Left, out int seen) ? seen : 0;
                occurrences[assignment.Left] = occurrence + 1;
                if (!referenceByLeft.TryGetValue(assignment.Left, out var referenceValues) ||
                    occurrence >= referenceValues.Length)
                    continue;

                string originalValue = referenceValues[occurrence];
                bool restore = false;
                if (int.TryParse(assignment.Right, out _))
                {
                    restore = Regex.IsMatch(originalValue, @"^(?:[A-Za-z_]\w*\.)*[A-Za-z_]\w*$",
                        RegexOptions.CultureInvariant);
                }
                else
                {
                    Match currentCast = Regex.Match(assignment.Right,
                        @"^(?<type>(?:[A-Za-z_]\w*\.)*[A-Za-z_]\w*)\s*\((?<args>[^;{}]*)\)$",
                        RegexOptions.CultureInvariant);
                    Match originalCast = Regex.Match(originalValue,
                        @"^(?<type>(?:[A-Za-z_]\w*\.)*[A-Za-z_]\w*)\s*\((?<args>[^;{}]*)\)$",
                        RegexOptions.CultureInvariant);
                    restore = currentCast.Success && originalCast.Success &&
                        IsPrimitiveCastType(currentCast.Groups["type"].Value) &&
                        !IsPrimitiveCastType(originalCast.Groups["type"].Value) &&
                        NormalizeExpression(currentCast.Groups["args"].Value) ==
                        NormalizeExpression(originalCast.Groups["args"].Value);
                }
                if (!restore) continue;

                edits.Add((currentFunction.BodyOffset + assignment.RightStart,
                    assignment.RightLength, originalValue));
            }
        }

        string result = decompiledSource;
        foreach (var edit in edits.OrderByDescending(item => item.Start))
            result = result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value);
        restored = edits.Count;
        return result;
    }

    private static bool IsPrimitiveCastType(string typeName)
    {
        string simpleName = typeName.Split('.').Last();
        return simpleName.Equals("byte", StringComparison.OrdinalIgnoreCase) ||
               simpleName.Equals("int", StringComparison.OrdinalIgnoreCase) ||
               simpleName.Equals("float", StringComparison.OrdinalIgnoreCase) ||
               simpleName.Equals("bool", StringComparison.OrdinalIgnoreCase) ||
               simpleName.Equals("string", StringComparison.OrdinalIgnoreCase) ||
               simpleName.Equals("name", StringComparison.OrdinalIgnoreCase) ||
               simpleName.Equals("object", StringComparison.OrdinalIgnoreCase) ||
               simpleName.Equals("vector", StringComparison.OrdinalIgnoreCase) ||
               simpleName.Equals("rotator", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeExpression(string expression) =>
        Regex.Replace(expression, @"\s+", "");

    private sealed record Assignment(string Left, string Right, int RightStart, int RightLength);

    private static List<Assignment> ReadAssignments(string body)
    {
        const string pattern = @"(?<left>[A-Za-z_]\w*(?:\s*\[[^\]]+\])?(?:\s*\.\s*[A-Za-z_]\w*)*)\s*=\s*(?<right>-?\d+|(?:[A-Za-z_]\w*\.)*[A-Za-z_]\w*(?:\s*\([^;{}]*\))?)\s*;";
        var mask = BuildCodeMask(body);
        var assignments = new List<Assignment>();
        foreach (Match match in Regex.Matches(body, pattern, RegexOptions.CultureInvariant))
        {
            int leftStart = match.Groups["left"].Index;
            int rightStart = match.Groups["right"].Index;
            if (!mask[leftStart] || !mask[rightStart]) continue;
            assignments.Add(new Assignment(Regex.Replace(match.Groups["left"].Value, @"\s+", ""),
                match.Groups["right"].Value, rightStart, match.Groups["right"].Length));
        }
        return assignments;
    }

    public static string DescribeCallSites(string source, string functionName)
    {
        var calls = ReadCalls(source);
        if (!calls.TryGetValue(functionName, out var sites)) return "(none)";
        return string.Join(" | ", sites.Select(site =>
            functionName + "(" + string.Join(", ", site.Arguments.Select(argument => argument.Text.Trim())) + ")"));
    }

    private static Dictionary<string, List<CallSite>> ReadCalls(string source)
    {
        var result = new Dictionary<string, List<CallSite>>(StringComparer.OrdinalIgnoreCase);
        var mask = BuildCodeMask(source);
        foreach (Match match in Regex.Matches(source, @"\b(?<name>[A-Za-z_]\w*)\s*\(",
            RegexOptions.CultureInvariant))
        {
            int nameStart = match.Groups["name"].Index;
            if (!mask[nameStart]) continue;
            if (Regex.IsMatch(source[..nameStart], @"\b(function|event)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) continue;

            string name = match.Groups["name"].Value;
            int open = source.IndexOf('(', nameStart + name.Length);
            if (open < 0) continue;
            int close = FindMatchingParen(source, open);
            if (close < 0) continue;

            var ranges = SplitArguments(source, open + 1, close);
            var args = ranges.Select(range =>
                new CallArgument(range.Start, range.End, source[range.Start..range.End])).ToList();
            if (!result.TryGetValue(name, out var list)) result[name] = list = new();
            list.Add(new CallSite(name, args));
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

            mask[i] = true;
        }

        return mask;
    }
}
