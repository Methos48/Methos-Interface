using System.Text.RegularExpressions;

namespace MethosUEngine;

/// <summary>
/// Removes UELib's orphaned switch-format diagnostic comments after the switch
/// body has already been emitted. These comments are not UnrealScript and do
/// not represent missing statements.
/// </summary>
public static class UnrealScriptDecompilerDiagnosticCleaner
{
    private static readonly Regex DiagnosticLine = new(
        @"^[ \\t]*// (?:Failed to format(?: remaining)? nests!:.*|[0-9]+ & Type:Switch Position:0x[0-9A-Fa-f]+)[ \\t]*(?:\\r?\\n|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    public static string RemoveOrphanedSwitchDiagnostics(string source, out int removedLines)
    {
        ArgumentNullException.ThrowIfNull(source);
        removedLines = 0;
        string result = DiagnosticLine.Replace(source, _ =>
        {
            removedLines++;
            return string.Empty;
        });
        return result;
    }
}
