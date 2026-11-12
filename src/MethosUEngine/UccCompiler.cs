using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MethosUEngine;

public sealed record UccCompileResult(
    byte[] PackageBytes,
    string CompilerOutput,
    string ProfileDescription,
    int NameCount,
    int ImportCount,
    int ExportCount);

/// <summary>
/// Runs the Interlude UI-script compiler from the bundled, user-provided UCC runtime.
/// All compilation happens in a temporary staging directory; the input package is never modified.
/// </summary>
public static class UccCompiler
{
    // UELib can emit these two bytecode tokens as control characters in function identifiers.
    // Rename each token consistently in staged copies only; never alter the user's extracted sources.
    private static readonly IReadOnlyDictionary<char, string> RecoveredIdentifiers =
        new Dictionary<char, string>
        {
            ['\u0011'] = "MethosRecoveredFunction_17",
            ['\u001D'] = "MethosRecoveredFunction_29"
        };

    // UELib can emit a class-scope prototype for a function whose actual implementation
    // belongs to a state. Interlude UCC treats that pair as a conflicting declaration.
    // Remove only those redundant class-scope prototypes from the temporary staging copy;
    // the state implementation and all call sites remain unchanged.
    private static string NormalizeRecoveredStateFunctionPrototypes(string source, out int removed)
    {
        removed = 0;
        string[] lines = source.Replace("\\r\\n", "\\n").Replace('\\r', '\\n').Split('\\n');
        var stateFunctionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int braceDepth = 0;
        int activeStateDepth = -1;
        bool inBlockComment = false;

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string line = lines[lineIndex];
            if (activeStateDepth < 0 && braceDepth == 0 &&
                Regex.IsMatch(line, @"^\\s*state\\s+\\w+\\b.*\\{", RegexOptions.IgnoreCase))
            {
                activeStateDepth = braceDepth + CountCodeBraces(line, ref inBlockComment);
                continue;
            }

            if (activeStateDepth >= 0)
            {
                Match function = Regex.Match(line,
                    @"^\\s*(?:(?:simulated|static|final|native|iterator|latent)\\s+)*(?:function|event)\\s+(?:(?:coerce|optional)\\s+)*(?:\\w+\\s+)?(?<name>\\w+)\\s*\\(",
                    RegexOptions.IgnoreCase);
                if (function.Success)
                    stateFunctionNames.Add(function.Groups["name"].Value);
            }

            int delta = CountCodeBraces(line, ref inBlockComment);
            braceDepth += delta;
            if (activeStateDepth >= 0 && braceDepth < activeStateDepth)
                activeStateDepth = -1;
        }

        if (stateFunctionNames.Count == 0) return source;

        var output = new StringBuilder(source.Length);
        braceDepth = 0;
        inBlockComment = false;
        foreach (string line in lines)
        {
            Match prototype = Regex.Match(line,
                @"^\\s*(?:(?:simulated|static|final|native|iterator|latent)\\s+)*(?:function|event)\\s+(?:(?:coerce|optional)\\s+)*(?:\\w+\\s+)?(?<name>\\w+)\\s*\\([^;]*\\)\\s*;\\s*$",
                RegexOptions.IgnoreCase);
            if (braceDepth == 0 && prototype.Success &&
                stateFunctionNames.Contains(prototype.Groups["name"].Value))
            {
                removed++;
                continue;
            }

            output.AppendLine(line);
            braceDepth += CountCodeBraces(line, ref inBlockComment);
        }
        return output.ToString();
    }

    private static int CountCodeBraces(string line, ref bool inBlockComment)
    {
        int delta = 0;
        bool inString = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            char next = i + 1 < line.Length ? line[i + 1] : '\\0';
            if (inBlockComment)
            {
                if (ch == '*' && next == '/') { inBlockComment = false; i++; }
                continue;
            }
            if (!inString && ch == '/' && next == '/') break;
            if (!inString && ch == '/' && next == '*') { inBlockComment = true; i++; continue; }
            if (ch == '"' && (i == 0 || line[i - 1] != '\\\\')) { inString = !inString; continue; }
            if (inString) continue;
            if (ch == '{') delta++;
            else if (ch == '}') delta--;
        }
        return delta;
    }

    // Some UELib decompilations preserve a local struct below the fields that use it.
    // Interlude UCC resolves struct types in declaration order, so move this recovered
    // ChatWnd-local struct above its first use in the staged copy.
    private static string NormalizeRecoveredStructOrder(string source, out bool changed)
    {
        changed = false;
        var declaration = Regex.Match(source,
            @"(?ms)^[\t ]*struct\s+ChatFilterInfo\s*\{.*?^[\t ]*\};[\t ]*\r?\n?");
        if (!declaration.Success) return source;

        string structText = declaration.Value.Trim();
        source = source.Remove(declaration.Index, declaration.Length);
        var classLine = Regex.Match(source, @"(?m)^\s*class\s+ChatWnd\b[^;]*;[\t ]*\r?\n?");
        if (!classLine.Success) return source.Insert(declaration.Index, declaration.Value);

        source = source.Insert(classLine.Index + classLine.Length, Environment.NewLine + structText + Environment.NewLine);
        changed = true;
        return source;
    }

    // UELib sometimes loses closing braces when it reconstructs UnrealScript control flow.
    // Before a new top-level function, close any blocks left open by the previous function.
    // This runs only on temporary compiler staging copies and never changes the user's sources.
    private static string NormalizeRecoveredFunctionBlocks(string source, out int insertedClosures)
    {
        insertedClosures = 0;
        var output = new StringBuilder(source.Length + 256);
        int depth = 0;
        bool inBlockComment = false;

        foreach (string line in source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (Regex.IsMatch(line, @"^[\t ]*(?:(?:simulated|static|final|native|iterator|latent)\s+)*(?:function|event)\b",
                    RegexOptions.IgnoreCase) && depth > 0)
            {
                output.AppendLine(new string('}', depth));
                insertedClosures += depth;
                depth = 0;
            }

            bool inString = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                char next = i + 1 < line.Length ? line[i + 1] : '\0';
                if (inBlockComment)
                {
                    if (ch == '*' && next == '/') { inBlockComment = false; i++; }
                    continue;
                }
                if (!inString && ch == '/' && next == '/') break;
                if (!inString && ch == '/' && next == '*') { inBlockComment = true; i++; continue; }
                if (ch == '"' && (i == 0 || line[i - 1] != '\\')) { inString = !inString; continue; }
                if (inString) continue;
                if (ch == '{') depth++;
                else if (ch == '}') depth = Math.Max(0, depth - 1);
            }
            output.AppendLine(line);
        }

        if (depth > 0)
        {
            output.AppendLine(new string('}', depth));
            insertedClosures += depth;
        }
        return output.ToString();
    }

    public static bool IsAvailable(string runtimeRoot)
    {
        string system = Path.Combine(runtimeRoot, "Interlude", "System");
        return File.Exists(Path.Combine(system, "ucc.exe")) &&
               File.Exists(Path.Combine(system, "UIScript.ini")) &&
               File.Exists(Path.Combine(system, "Core.u")) &&
               File.Exists(Path.Combine(system, "Engine.u")) &&
               File.Exists(Path.Combine(system, "NWindow.u"));
    }

    public static UccCompileResult CompileInterface(
        string runtimeRoot,
        string sourceDirectory,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        if (!IsAvailable(runtimeRoot))
            throw new FileNotFoundException("No se encontró el runtime UCC de Interlude dentro de Methos U Editor.");

        string[] sourceFiles = Directory.GetFiles(sourceDirectory, "*.uc", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(sourceDirectory, path)
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment.Equals("backups", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourceFiles.Length == 0)
            throw new InvalidOperationException($"No se encontraron fuentes .uc en {sourceDirectory}.");

        string[] duplicates = sourceFiles
            .GroupBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
            throw new InvalidDataException("Hay nombres de clase .uc duplicados: " + string.Join(", ", duplicates));

        string temporaryRoot = Path.Combine(Path.GetTempPath(), "MethosU_UCC_" + Guid.NewGuid().ToString("N"));
        try
        {
            string sourceSystem = Path.Combine(runtimeRoot, "Interlude", "System");
            string stagedSystem = Path.Combine(temporaryRoot, "Interlude", "System");
            string stagedClasses = Path.Combine(temporaryRoot, "Interlude", "Interface", "Classes");
            Directory.CreateDirectory(stagedSystem);
            Directory.CreateDirectory(stagedClasses);

            foreach (string file in Directory.GetFiles(sourceSystem))
                File.Copy(file, Path.Combine(stagedSystem, Path.GetFileName(file)), overwrite: true);

            int repairedIdentifiers = 0;
            foreach (string file in sourceFiles)
            {
                string source = File.ReadAllText(file, Encoding.Default);
                foreach (var pair in RecoveredIdentifiers)
                {
                    int count = source.Count(ch => ch == pair.Key);
                    if (count > 0)
                    {
                        source = source.Replace(pair.Key.ToString(), pair.Value);
                        repairedIdentifiers += count;
                    }
                }
                if (Path.GetFileName(file).Equals("ChatWnd.uc", StringComparison.OrdinalIgnoreCase))
                {
                    source = NormalizeRecoveredStructOrder(source, out bool movedStruct);
                    if (movedStruct) repairedIdentifiers++;
                }
                source = NormalizeRecoveredStateFunctionPrototypes(source, out int removedStatePrototypes);
                repairedIdentifiers += removedStatePrototypes;
                source = NormalizeRecoveredFunctionBlocks(source, out int insertedClosures);
                repairedIdentifiers += insertedClosures;
                File.WriteAllText(Path.Combine(stagedClasses, Path.GetFileName(file)), source, new UTF8Encoding(false));
            }

            string compilerPath = Path.Combine(stagedSystem, "ucc.exe");
            var startInfo = new ProcessStartInfo
            {
                FileName = compilerPath,
                WorkingDirectory = stagedSystem,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("make");
            startInfo.ArgumentList.Add("-NoBind");

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                throw new InvalidOperationException("No se pudo iniciar el compilador UCC de Interlude.");

            process.StandardInput.WriteLine("N");
            process.StandardInput.Close();
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();

            TimeSpan maxWait = timeout ?? TimeSpan.FromMinutes(2);
            if (!process.WaitForExit((int)maxWait.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException($"UCC superó el límite de {maxWait.TotalMinutes:0.#} minutos.");
            }
            Task.WaitAll(standardOutput, standardError);

            string compilerOutput = standardOutput.Result + Environment.NewLine + standardError.Result;
            compilerOutput += Environment.NewLine + $"METHOS_SOURCE_REPAIRS={repairedIdentifiers}";
            if (process.ExitCode != 0 ||
                !Regex.IsMatch(compilerOutput, @"Success\s*-\s*0 error\(s\)", RegexOptions.IgnoreCase))
            {
                string detail = compilerOutput.Length > 7000
                    ? compilerOutput[^7000..]
                    : compilerOutput;
                throw new InvalidOperationException(
                    $"UCC no pudo compilar Interface.u (código {process.ExitCode}).{Environment.NewLine}{detail}");
            }

            string outputPath = Path.Combine(stagedSystem, "Interface.u");
            if (!File.Exists(outputPath))
                throw new FileNotFoundException("UCC informó éxito, pero no generó Interface.u.", outputPath);

            byte[] packageBytes = File.ReadAllBytes(outputPath);
            if (packageBytes.Length < 65536)
                throw new InvalidDataException("El Interface.u generado por UCC es demasiado pequeño.");

            UnrealPackageIndex index = UnrealPackageIndex.Parse(packageBytes);
            if (index.Names.Count < 100 || index.Imports.Count < 1 || index.Exports.Count < 100)
                throw new InvalidDataException("El Interface.u generado no contiene tablas de paquete plausibles.");

            return new UccCompileResult(
                packageBytes,
                compilerOutput,
                index.Summary.ProfileDescription,
                index.Names.Count,
                index.Imports.Count,
                index.Exports.Count);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, recursive: true);
            }
            catch
            {
                // Temporary compiler logs are disposable; cleanup must not hide the actual result.
            }
        }
    }
}