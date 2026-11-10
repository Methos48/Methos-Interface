using System.Text.RegularExpressions;

namespace MethosUEngine;

/// <summary>
/// Restores enum identifiers that UELib emits as numeric literals by aligning
/// one-argument calls with the matching original Interlude source class.
/// </summary>
public static class UnrealScriptReferenceNormalizer
{
    private static readonly Regex SingleArgumentCall = new(
        @"(?<name>\b[A-Za-z_]\w*)\s*\(\s*(?<argument>[A-Za-z_]\w*|\d+)\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string RestoreEnumArguments(string decompiledSource, string referenceSource, out int restored)
    {
        ArgumentNullException.ThrowIfNull(decompiledSource);
        ArgumentNullException.ThrowIfNull(referenceSource);
        restored = 0;

        var decompiledMask = BuildCodeMask(decompiledSource);
        var referenceMask = BuildCodeMask(referenceSource);
        var decompiledCalls = SingleArgumentCall.Matches(decompiledSource)
            .Cast<Match>().Where(match => decompiledMask[match.Index]).ToArray();
        var referenceCalls = SingleArgumentCall.Matches(referenceSource)
            .Cast<Match>().Where(match => referenceMask[match.Index]).ToArray();

        var edits = new List<(int Start, int Length, string Replacement)>();
        int count = Math.Min(decompiledCalls.Length, referenceCalls.Length);
        for (int i = 0; i < count; i++)
        {
            Match current = decompiledCalls[i];
            Match reference = referenceCalls[i];
            if (!current.Groups["name"].Value.Equals(reference.Groups["name"].Value,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            string currentArgument = current.Groups["argument"].Value;
            string referenceArgument = reference.Groups["argument"].Value;
            if (!int.TryParse(currentArgument, out _) ||
                !Regex.IsMatch(referenceArgument, @"^[A-Za-z_]\w*$", RegexOptions.CultureInvariant))
                continue;

            Group argumentGroup = current.Groups["argument"];
            edits.Add((argumentGroup.Index, argumentGroup.Length, referenceArgument));
        }

        string result = decompiledSource;
        foreach (var edit in edits.OrderByDescending(item => item.Start))
            result = result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Replacement);

        restored = edits.Count;
        return result;
    }

    private static bool[] BuildCodeMask(string source)
    {
        var mask = new bool[source.Length];
        int position = 0;
        while (position < source.Length)
        {
            char current = source[position];
            if (current is '"' or '\'')
            {
                char quote = current;
                position++;
                while (position < source.Length)
                {
                    if (source[position] == '\\')
                    {
                        position += Math.Min(2, source.Length - position);
                        continue;
                    }
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

            mask[position++] = true;
        }
        return mask;
    }
}
