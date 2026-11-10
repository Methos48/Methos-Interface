using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using UELib;
using MethosUEngine;
using UELib.Core;

namespace MethosUEditor;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private readonly string docsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Methos U Editor");
    private string CompleteDir => Path.Combine(docsRoot, "u completo");
    private string PartsDir => Path.Combine(docsRoot, "u por partes");
    private const string OriginalInterfaceSha256 = "A3074E83A54D3C681BC67830D87BC38FDAB1D0A30EB77384DA2F615F619D5B71";
    private string UccRuntimeRoot => Path.Combine(AppContext.BaseDirectory, "UccRuntime");
    private string SourceCacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Methos U Editor", "SourceCache");
    private readonly ListBox files = new() { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended };
    private readonly TextBox log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Bottom, Height = 18 };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 24, Text = "Listo", TextAlign = ContentAlignment.MiddleLeft };
    private readonly List<string> inputs = new();
    private readonly Button decompile = new() { Text = "DESCOMPILAR", Height = 48, Width = 170 };
    private readonly Button compile = new() { Text = "COMPILAR", Height = 48, Width = 170 };

    public MainForm()
    {
        Text = "Methos U Editor";
        Width = 980; Height = 700; MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 247, 250);
        Directory.CreateDirectory(CompleteDir);
        Directory.CreateDirectory(PartsDir);
        BuildLayout();
        RefreshInputs();
        Log("Carpetas preparadas:");
        Log(CompleteDir);
        Log(PartsDir);
    }

    private void BuildLayout()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(18), BackColor = Color.FromArgb(32, 48, 68) };
        var title = new Label { Text = "METHOS U EDITOR", ForeColor = Color.White, Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true, Location = new Point(18, 12) };
        var subtitle = new Label { Text = "Editor de paquetes UnrealScript para Lineage II", ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 9), AutoSize = true, Location = new Point(20, 52) };
        top.Controls.Add(title); top.Controls.Add(subtitle);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(12), BackColor = Color.White };
        var addU = new Button { Text = "Agregar .U", Width = 112, Height = 42 };
        var addZip = new Button { Text = "Importar ZIP", Width = 112, Height = 42 };
        var addFolder = new Button { Text = "Agregar carpeta", Width = 130, Height = 42 };
        var openComplete = new Button { Text = "Abrir u completo", Width = 145, Height = 42 };
        var openParts = new Button { Text = "Abrir u por partes", Width = 155, Height = 42 };
        actions.Controls.AddRange(new Control[] { addU, addZip, addFolder, decompile, compile, openComplete, openParts });

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 430, Padding = new Padding(10) };
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        left.Controls.Add(files);
        left.Controls.Add(new Label { Text = "ARCHIVOS .U", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI", 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
        split.Panel1.Controls.Add(left);
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        right.Controls.Add(log);
        right.Controls.Add(new Label { Text = "REGISTRO DE ACTIVIDAD", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI", 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
        split.Panel2.Controls.Add(right);

        Controls.Add(split); Controls.Add(status); Controls.Add(progress); Controls.Add(actions); Controls.Add(top);

        addU.Click += (_, _) => AddUPackages();
        addZip.Click += (_, _) => ImportZip();
        addFolder.Click += (_, _) => AddFolder();
        openComplete.Click += (_, _) => OpenFolder(CompleteDir);
        openParts.Click += (_, _) => OpenFolder(PartsDir);
        decompile.Click += async (_, _) => await RunSelectedAsync(DecompileFile);
        compile.Click += async (_, _) => await RunSelectedAsync(CompileFolder);
    }

    private void RefreshInputs()
    {
        inputs.Clear();
        if (Directory.Exists(CompleteDir))
            inputs.AddRange(Directory.GetFiles(CompleteDir, "*.u", SearchOption.TopDirectoryOnly));
        files.Items.Clear();
        foreach (var f in inputs) files.Items.Add(Path.GetFileName(f));
    }

    private void AddUPackages()
    {
        using var d = new OpenFileDialog { Filter = "Paquetes Unreal (*.u)|*.u", Multiselect = true };
        if (d.ShowDialog() != DialogResult.OK) return;
        foreach (var src in d.FileNames)
        {
            var dst = Path.Combine(CompleteDir, Path.GetFileName(src));
            if (!SamePath(src, dst)) CopyWithBackup(src, dst);
        }
        RefreshInputs();
    }

    private void ImportZip()
    {
        using var d = new OpenFileDialog { Filter = "Archivo ZIP (*.zip)|*.zip" };
        if (d.ShowDialog() != DialogResult.OK) return;
        try
        {
            using var archive = ZipFile.OpenRead(d.FileName);
            string zipBase = Path.GetFileNameWithoutExtension(d.FileName);
            var interfaceSources = archive.Entries.Where(entry =>
            {
                string normalized = entry.FullName.Replace('\\', '/');
                return entry.Name.EndsWith(".uc", StringComparison.OrdinalIgnoreCase) &&
                    normalized.Contains("/Interface/Classes/", StringComparison.OrdinalIgnoreCase);
            }).ToArray();
            bool isInterfaceArchive = zipBase.Contains("interface", StringComparison.OrdinalIgnoreCase) ||
                interfaceSources.Length > 0;
            string targetName = isInterfaceArchive ? "Interface" : zipBase;
            string targetDir = Path.Combine(PartsDir, targetName);
            Directory.CreateDirectory(targetDir);

            var sourceEntries = interfaceSources.Length > 0
                ? interfaceSources
                : archive.Entries.Where(entry => entry.Name.EndsWith(".uc", StringComparison.OrdinalIgnoreCase)).ToArray();
            int sourceCount = 0;
            foreach (var entry in sourceEntries)
            {
                string name = Path.GetFileName(entry.Name);
                if (string.IsNullOrWhiteSpace(name)) continue;
                CopyEntryWithBackup(entry, Path.Combine(targetDir, name));
                sourceCount++;
            }

            var packageEntries = archive.Entries
                .Where(entry => entry.Name.EndsWith(".u", StringComparison.OrdinalIgnoreCase) &&
                    entry.FullName.Replace('\\', '/').Contains("/System/", StringComparison.OrdinalIgnoreCase))
                .GroupBy(entry => Path.GetFileName(entry.Name), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(entry =>
                    entry.FullName.Contains("/Testar/system/", StringComparison.OrdinalIgnoreCase)).First())
                .ToArray();
            int packageCount = 0;
            foreach (var entry in packageEntries)
            {
                string name = Path.GetFileName(entry.Name);
                if (string.IsNullOrWhiteSpace(name)) continue;
                CopyEntryWithBackup(entry, Path.Combine(CompleteDir, name));
                packageCount++;
            }

            RefreshInputs();
            Log($"ZIP importado: fuentes .uc={sourceCount} en '{targetName}'; paquetes .u={packageCount}. Ejecutables y DLL ignorados.");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Error al importar ZIP", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static void CopyEntryWithBackup(ZipArchiveEntry entry, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination)) BackupExistingFile(destination);
        using var input = entry.Open();
        using var output = File.Create(destination);
        input.CopyTo(output);
    }

    private void AddFolder()
    {
        using var d = new FolderBrowserDialog { Description = "Selecciona una carpeta con archivos .u o clases .uc" };
        if (d.ShowDialog() != DialogResult.OK) return;
        var uFiles = Directory.GetFiles(d.SelectedPath, "*.u", SearchOption.AllDirectories);
        var ucFiles = Directory.GetFiles(d.SelectedPath, "*.uc", SearchOption.AllDirectories);
        if (uFiles.Length == 0 && ucFiles.Length == 0)
        {
            MessageBox.Show("No se encontraron archivos .u ni .uc.");
            return;
        }

        foreach (var src in uFiles)
        {
            var dst = Path.Combine(CompleteDir, Path.GetFileName(src));
            if (!SamePath(src, dst)) CopyWithBackup(src, dst);
        }

        if (ucFiles.Length > 0)
        {
            var folderName = new DirectoryInfo(d.SelectedPath).Name;
            var targetName = folderName.Contains("interface", StringComparison.OrdinalIgnoreCase) ? "Interface" : folderName;
            var target = Path.Combine(PartsDir, targetName);
            Directory.CreateDirectory(target);
            foreach (var src in ucFiles)
            {
                var dst = Path.Combine(target, Path.GetFileName(src));
                if (!SamePath(src, dst)) CopyWithBackup(src, dst);
            }
        }

        RefreshInputs();
        Log($"Carpeta importada: {uFiles.Length} paquetes .u y {ucFiles.Length} clases .uc.");
    }

    private async Task RunSelectedAsync(Func<string, Task> action)
    {
        if (files.SelectedIndices.Count == 0) { MessageBox.Show("Selecciona uno o más archivos .u de la lista."); return; }
        var selected = files.SelectedIndices.Cast<int>().Where(i => i >= 0 && i < inputs.Count).Select(i => inputs[i]).ToList();
        SetBusy(true);
        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                progress.Value = (int)((i * 100.0) / selected.Count);
                status.Text = $"{i + 1}/{selected.Count}: {Path.GetFileName(selected[i])}";
                await action(selected[i]);
            }
            progress.Value = 100;
            status.Text = "Proceso finalizado";
        }
        catch (Exception ex) { Log("ERROR: " + ex); MessageBox.Show(ex.Message, "Methos U Editor", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    private Task DecompileFile(string path) => Task.Run(() =>
    {
        var baseName = Path.GetFileNameWithoutExtension(path);
        var output = Path.Combine(PartsDir, baseName);
        Directory.CreateDirectory(output);
        Log($"Descompilando {Path.GetFileName(path)}...");
        try
        {
            var index = UnrealPackageIndex.Open(path);
            Log($"Índice independiente UE2: {index.Summary.ProfileDescription}; nombres={index.Summary.NameCount}, imports={index.Summary.ImportCount}, exports={index.Summary.ExportCount}, generaciones={index.Summary.GenerationCount}.");
        }
        catch (Exception ex)
        {
            // UELib remains the compatibility reader while the native compiler is developed.
            Log($"Aviso del analizador independiente: {ex.Message}");
        }
        byte[] packageBytes = File.ReadAllBytes(path);
        if (TryRestoreExactSources(baseName, packageBytes, output))
            return;

        UnrealPackageIndex? nativeIndex = null;
        try { nativeIndex = UnrealPackageIndex.Parse(packageBytes); }
        catch (Exception ex) { Log($"Aviso: descompilador nativo no disponible para este paquete: {ex.Message}"); }

        using var package = UnrealLoader.LoadPackage(path, FileAccess.Read);
        Log($"Paquete detectado: versión {package.Summary.Version}, licencia {package.Summary.LicenseeVersion}.");
        package.InitializePackage();
        var classes = package.Objects.OfType<UClass>().Where(o => (int)o > 0).ToList();
        int ok = 0, failed = 0, repairedFunctions = 0, insertedClosures = 0, removedClosures = 0, nativeFunctions = 0;
        foreach (var obj in classes)
        {
            try
            {
                var source = obj.Decompile();
                var objectName = obj.Name?.ToString() ?? $"Class_{ok}";
                var safeName = string.Concat(objectName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

                // UELib can stop formatting nested scopes after a malformed bytecode token.
                // Repair only missing closing braces at the next function boundary, and report every change.
                try
                {
                    var repair = UnrealScriptSourceIndex.RepairMissingFunctionClosures(source);
                    if (repair.Repairs.Count > 0)
                    {
                        source = repair.Source;
                        repairedFunctions += repair.Repairs.Count;
                        foreach (var item in repair.Repairs)
                        {
                            insertedClosures += item.ClosuresInserted;
                            removedClosures += item.ClosuresRemoved;
                            Log($"Fuente reparada: {safeName}.{item.FunctionName} (línea {item.Line}); llaves insertadas={item.ClosuresInserted}; cierres sobrantes eliminados={item.ClosuresRemoved}.");
                        }
                    }
                }
                catch (Exception repairError)
                {
                    Log($"Aviso: no se pudo verificar/reparar la estructura de {safeName}.uc; se conserva la salida original. {repairError.Message}");
                }

                try
                {
                    var returnNormalization = UnrealScriptSourceIndex.RemoveRedundantTrailingReturns(source);
                    source = returnNormalization.Source;
                    if (returnNormalization.Repairs.Count > 0)
                        Log($"Retornos redundantes eliminados en {safeName}: {returnNormalization.Repairs.Count}.");
                }
                catch (Exception normalizationError)
                {
                    Log($"Aviso: no se pudo normalizar retornos en {safeName}.uc. {normalizationError.Message}");
                }

                if (nativeIndex != null)
                {
                    source = ApplyNativeBytecodeDecompilation(
                        source, obj, package, nativeIndex, packageBytes, out int nativeDecoded);
                    nativeFunctions += nativeDecoded;
                    if (nativeDecoded > 0)
                        Log($"Descompilación nativa verificada: {safeName}.uc; funciones={nativeDecoded}.");
                }

                SaveTextWithBackup(Path.Combine(output, safeName + ".uc"), source);
                ok++;
            }
            catch (Exception ex) { failed++; Log($"No se pudo exportar una clase: {ex.Message}"); }
        }
        Log($"Descompilación terminada. Clases exportadas: {ok}; errores: {failed}; funciones reparadas: {repairedFunctions}; llaves insertadas: {insertedClosures}; cierres sobrantes eliminados: {removedClosures}; funciones descompiladas/verificadas por Methos: {nativeFunctions}. Carpeta: {output}");
        if (ok == 0) throw new InvalidOperationException("No se pudo exportar ninguna clase. El paquete puede usar una variante de Unreal/Lineage que requiere ajustes de compatibilidad.");
    });

    private static string ApplyNativeBytecodeDecompilation(string source, UClass classObject,
        UnrealPackage metadataPackage, UnrealPackageIndex packageIndex, byte[] packageBytes,
        out int nativeDecoded)
    {
        nativeDecoded = 0;
        var sourceFunctions = UnrealScriptSourceIndex.ParseFunctions(source);
        var classFunctions = EnumerateClassFunctions(classObject).ToList();
        var replacements = new List<(int Start, int Length, string Body)>();

        foreach (var sourceFunction in sourceFunctions)
        {
            var matches = classFunctions.Where(function =>
                string.Equals(function.Name.ToString(), sourceFunction.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) continue;

            var function = matches[0];
            if (function.ExportTable == null) continue;
            int exportReference = (int)function;
            if (exportReference < 1 || exportReference > packageIndex.Exports.Count) continue;
            var export = packageIndex.Exports[exportReference - 1];
            long relativeLong = function.ScriptOffset;
            if (relativeLong < 4 || relativeLong > int.MaxValue || relativeLong > export.SerialSize - 4) continue;
            int relative = (int)relativeLong;
            int sizeOffset = checked(export.SerialOffset + relative - 4);
            if (sizeOffset < 0 || sizeOffset > packageBytes.Length - 4) continue;
            int virtualScriptSize = BinaryPrimitives.ReadInt32LittleEndian(packageBytes.AsSpan(sizeOffset, 4));
            if (virtualScriptSize < 0) continue;

            try
            {
                string nativeSource = UnrealScriptFunctionDecompiler.DecompileExport(
                    packageIndex, exportReference, relative, export.SerialSize - relative, out int scriptSize);
                byte[] originalBytecode = packageBytes.AsSpan(
                    checked(export.SerialOffset + relative), scriptSize).ToArray();
                byte[] rebuiltBytecode = UnrealScriptFunctionCompiler.CompileReturnOnlyBody(
                    sourceFunction.Body, name =>
                    {
                        for (int i = 0; i < packageIndex.Names.Count; i++)
                            if (string.Equals(packageIndex.Names[i], name, StringComparison.OrdinalIgnoreCase))
                                return i;
                        throw new InvalidDataException($"La constante Name '{name}' no existe en la tabla del paquete.");
                    });

                // Only replace the source text when the native compiler recreates the exact original bytecode.
                // This makes the native decompiler a verified round-trip, not a guess based on formatting.
                if (!rebuiltBytecode.AsSpan().SequenceEqual(originalBytecode)) continue;
                replacements.Add((sourceFunction.BodyOffset, sourceFunction.BodyLength,
                    Environment.NewLine + "        " + nativeSource + Environment.NewLine + "    "));
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or EndOfStreamException or ArgumentOutOfRangeException)
            {
                // Unsupported bytecode stays exactly as emitted by the compatibility decompiler.
            }
        }

        foreach (var replacement in replacements.OrderByDescending(item => item.Start))
            source = source.Remove(replacement.Start, replacement.Length)
                .Insert(replacement.Start, replacement.Body);
        nativeDecoded = replacements.Count;
        return source;
    }

    private bool TryRestoreExactSources(string packageName, byte[] packageBytes, string outputDirectory)
    {
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packageBytes));
        string cachedDirectory = Path.Combine(SourceCacheRoot, hash, packageName);
        if (Directory.Exists(cachedDirectory))
        {
            int cachedCount = GetSourceFiles(cachedDirectory).Length;
            if (cachedCount > 0)
            {
                RestoreSourceDirectory(cachedDirectory, outputDirectory);
                Log($"Fuentes exactas restauradas desde la caché de Methos: {cachedCount} clases; SHA-256={hash}.");
                return true;
            }
        }

        if (packageName.Equals("Interface", StringComparison.OrdinalIgnoreCase) &&
            hash.Equals(OriginalInterfaceSha256, StringComparison.OrdinalIgnoreCase))
        {
            string referenceDirectory = Path.Combine(UccRuntimeRoot, "Interlude", "Interface", "Classes");
            if (Directory.Exists(referenceDirectory))
            {
                int referenceCount = GetSourceFiles(referenceDirectory).Length;
                if (referenceCount >= 150)
                {
                    RestoreSourceDirectory(referenceDirectory, outputDirectory);
                    Log($"Fuentes originales verificadas restauradas: {referenceCount} clases. El paquete coincide byte por byte con la referencia Interlude.");
                    return true;
                }
            }
        }

        return false;
    }

    private static string[] GetSourceFiles(string directory)
    {
        if (!Directory.Exists(directory)) return Array.Empty<string>();
        return Directory.GetFiles(directory, "*.uc", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(directory, path)
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment.Equals("backups", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void RestoreSourceDirectory(string sourceDirectory, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string[] sourceFiles = GetSourceFiles(sourceDirectory);
        var expected = sourceFiles
            .Select(path => Path.GetRelativePath(sourceDirectory, path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string existing in GetSourceFiles(outputDirectory))
        {
            string relative = Path.GetRelativePath(outputDirectory, existing);
            if (expected.Contains(relative)) continue;
            BackupExistingFile(existing);
            File.Delete(existing);
        }

        foreach (string source in sourceFiles)
        {
            string relative = Path.GetRelativePath(sourceDirectory, source);
            string destination = Path.Combine(outputDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            CopyWithBackup(source, destination);
        }
    }

    private void SaveSourceSnapshot(string packageName, byte[] packageBytes, string sourceDirectory)
    {
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packageBytes));
        string destinationRoot = Path.Combine(SourceCacheRoot, hash, packageName);
        if (Directory.Exists(destinationRoot)) Directory.Delete(destinationRoot, recursive: true);
        Directory.CreateDirectory(destinationRoot);

        foreach (string source in GetSourceFiles(sourceDirectory))
        {
            string relative = Path.GetRelativePath(sourceDirectory, source);
            string destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
        Log($"Copia exacta de fuentes guardada en caché: {GetSourceFiles(sourceDirectory).Length} clases; SHA-256={hash}.");
    }

    private static void BackupExistingFile(string path)
    {
        if (!File.Exists(path)) return;
        string category = Path.GetExtension(path).Equals(".u", StringComparison.OrdinalIgnoreCase)
            ? "Packages" : "Sources";
        string parentName = new DirectoryInfo(Path.GetDirectoryName(path)!).Name;
        string backupRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Methos U Editor", "Backups", category, parentName);
        Directory.CreateDirectory(backupRoot);
        string backupName = Path.GetFileNameWithoutExtension(path) + "_" +
            DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + Path.GetExtension(path);
        File.Copy(path, Path.Combine(backupRoot, backupName), overwrite: true);
    }

    private Task CompileFolder(string packagePath) => Task.Run(() =>
    {
        var baseName = Path.GetFileNameWithoutExtension(packagePath);
        var sourceDirectory = Path.Combine(PartsDir, baseName);
        if (!Directory.Exists(sourceDirectory))
        {
            var candidate = Path.Combine(PartsDir, "Classes");
            if (Directory.Exists(candidate)) sourceDirectory = candidate;
            else throw new DirectoryNotFoundException($"No existe la carpeta de fuentes: {sourceDirectory}");
        }

        var ucFiles = GetSourceFiles(sourceDirectory);
        if (ucFiles.Length == 0)
            throw new InvalidOperationException($"No se encontraron archivos .uc en {sourceDirectory}");

        var duplicateNames = ucFiles.GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicateNames.Length > 0)
            throw new InvalidOperationException("Hay nombres de clase .uc duplicados: " + string.Join(", ", duplicateNames));

        if (baseName.Equals("Interface", StringComparison.OrdinalIgnoreCase) &&
            UccCompiler.IsAvailable(UccRuntimeRoot))
        {
            Log($"Compilador UCC Interlude detectado. Compilando {ucFiles.Length} fuentes .uc con el backend completo.");
            UccCompileResult uccResult = UccCompiler.CompileInterface(UccRuntimeRoot, sourceDirectory);
            string uccTemporaryOutput = Path.Combine(Path.GetTempPath(), "MethosU_UCC_" + Guid.NewGuid().ToString("N") + ".u");
            try
            {
                File.WriteAllBytes(uccTemporaryOutput, uccResult.PackageBytes);
                var verifiedUcc = UnrealPackageIndex.Open(uccTemporaryOutput);
                using (var validation = UnrealLoader.LoadPackage(uccTemporaryOutput, FileAccess.Read))
                {
                    validation.InitializePackage();
                    int classCount = validation.Objects.OfType<UClass>().Count(item => (int)item > 0);
                    if (classCount < 100)
                        throw new InvalidDataException($"UCC generó un Interface.u con solo {classCount} clases reconocibles.");
                    Log($"Validación UCC: {classCount} clases; names={verifiedUcc.Names.Count}; imports={verifiedUcc.Imports.Count}; exports={verifiedUcc.Exports.Count}.");
                }

                var destination = Path.Combine(CompleteDir, baseName + ".u");
                SaveSourceSnapshot(baseName, uccResult.PackageBytes, sourceDirectory);
                CopyWithBackup(uccTemporaryOutput, destination);
                Log($"Compilación UCC terminada: {ucFiles.Length} fuentes; {uccResult.PackageBytes.Length:N0} bytes; perfil={uccResult.ProfileDescription}; archivo={destination}");
            }
            finally
            {
                if (File.Exists(uccTemporaryOutput)) File.Delete(uccTemporaryOutput);
            }
            return;
        }

        Log($"Runtime UCC no disponible para {baseName}. Se utilizará el motor nativo Methos (compatibilidad limitada).");
        Log($"Motor nativo Methos: preparando {ucFiles.Length} fuentes .uc.");
        byte[] packageBytes = File.ReadAllBytes(packagePath);
        var nativePackage = UnrealPackageIndex.Parse(packageBytes);

        using var metadataPackage = UnrealLoader.LoadPackage(packagePath, FileAccess.Read);
        metadataPackage.InitializePackage();
        var classes = metadataPackage.Objects.OfType<UClass>()
            .Where(item => (int)item > 0)
            .ToDictionary(item => item.Name.ToString(), StringComparer.OrdinalIgnoreCase);

        int classesProcessed = 0, functionsCompiled = 0, repairedFunctions = 0, insertedClosures = 0;
        foreach (string sourcePath in ucFiles)
        {
            string className = Path.GetFileNameWithoutExtension(sourcePath);
            if (!classes.TryGetValue(className, out var classObject))
                throw new InvalidDataException($"La clase '{className}' no existe como UClass en el paquete base. Crear clases nuevas todavía no está implementado; no se modificó el paquete.");

            string sourceText = File.ReadAllText(sourcePath);
            var sourceRepair = UnrealScriptSourceIndex.RepairMissingFunctionClosures(sourceText);
            var sourceFunctions = UnrealScriptSourceIndex.ParseFunctions(sourceRepair.Source);
            if (sourceFunctions.Count == 0)
            {
                Log($"Sin cuerpos de función para compilar: {className}.uc (se conserva el bytecode original).");
                continue;
            }

            var sourceFunctionNames = sourceFunctions.Select(function => function.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ownedFunctions = EnumerateClassFunctions(classObject)
                .Where(function => sourceFunctionNames.Contains(function.Name.ToString())).ToList();
            var targets = new List<UnrealFunctionTarget>();
            foreach (var function in ownedFunctions)
            {
                if (function.ExportTable == null)
                    throw new InvalidDataException($"La función '{className}.{function.Name}' no tiene una entrada export válida.");
                int exportReference = (int)function;
                if (exportReference < 1 || exportReference > nativePackage.Exports.Count)
                    throw new InvalidDataException($"La referencia de export de '{className}.{function.Name}' no es válida.");
                var exportEntry = nativePackage.Exports[exportReference - 1];
                long relativeOffset = function.ScriptOffset;
                if (relativeOffset < 4 || relativeOffset > int.MaxValue ||
                    relativeOffset > exportEntry.SerialSize - 4)
                    throw new InvalidDataException($"La posición del bytecode de '{className}.{function.Name}' no es válida.");
                int sizeFieldOffset = checked(exportEntry.SerialOffset + (int)relativeOffset - 4);
                int originalVirtualScriptSize = BinaryPrimitives.ReadInt32LittleEndian(
                    packageBytes.AsSpan(sizeFieldOffset, 4));
                if (originalVirtualScriptSize < 0)
                    throw new InvalidDataException($"El tamaño virtual del bytecode de '{className}.{function.Name}' es negativo.");
                _ = UnrealScriptFunctionDecompiler.DecompileExport(
                    nativePackage, exportReference, checked((int)relativeOffset),
                    checked(exportEntry.SerialSize - (int)relativeOffset), out int originalScriptSize);
                targets.Add(new UnrealFunctionTarget(function.Name.ToString(), exportReference,
                    checked((int)relativeOffset), originalScriptSize, originalVirtualScriptSize));
            }

            var result = UnrealScriptCompilerPipeline.CompileClassSource(
                sourceRepair.Source, nativePackage, targets, repairMissingFunctionClosures: false);
            packageBytes = result.PackageBytes;
            nativePackage = UnrealPackageIndex.Parse(packageBytes);
            classesProcessed++;
            functionsCompiled += result.FunctionsCompiled;
            repairedFunctions += sourceRepair.Repairs.Count;
            insertedClosures += sourceRepair.Repairs.Sum(item => item.ClosuresInserted);
            Log($"Clase compilada con motor nativo: {className}; funciones={result.FunctionsCompiled}; reparaciones={sourceRepair.Repairs.Count}.");
            foreach (var repair in sourceRepair.Repairs)
                Log($"  Reparación auditada: {repair.FunctionName}, línea {repair.Line}, llaves insertadas={repair.ClosuresInserted}.");
        }

        if (classesProcessed == 0 || functionsCompiled == 0)
            throw new NotSupportedException("El motor nativo todavía no encontró funciones compatibles para compilar. No se reemplazó el .u. Actualmente admite un subconjunto de cuerpos return con literales; el soporte general de UnrealScript sigue en desarrollo.");

        string temporaryOutput = Path.Combine(Path.GetTempPath(), "MethosU_" + Guid.NewGuid().ToString("N") + ".u");
        try
        {
            File.WriteAllBytes(temporaryOutput, packageBytes);
            var verified = UnrealPackageIndex.Open(temporaryOutput);
            if (verified.Exports.Count != nativePackage.Exports.Count ||
                verified.Names.Count != nativePackage.Names.Count ||
                verified.Imports.Count != nativePackage.Imports.Count)
                throw new InvalidDataException("La verificación final detectó cambios inesperados en las tablas del paquete.");

            using (var validation = UnrealLoader.LoadPackage(temporaryOutput, FileAccess.Read))
            {
                validation.InitializePackage();
                int classCount = validation.Objects.OfType<UClass>().Count(item => (int)item > 0);
                if (classCount == 0)
                    throw new InvalidDataException("El paquete reconstruido no contiene clases reconocibles.");
                Log($"Verificación final: {classCount} clases; names={verified.Names.Count}, imports={verified.Imports.Count}, exports={verified.Exports.Count}.");
            }

            var destination = Path.Combine(CompleteDir, baseName + ".u");
            SaveSourceSnapshot(baseName, packageBytes, sourceDirectory);
            CopyWithBackup(temporaryOutput, destination);
            Log($"Compilación nativa terminada: clases={classesProcessed}; funciones={functionsCompiled}; funciones reparadas={repairedFunctions}; llaves insertadas={insertedClosures}. Archivo: {destination}");
        }
        finally
        {
            if (File.Exists(temporaryOutput)) File.Delete(temporaryOutput);
        }
    });

    private static IEnumerable<UFunction> EnumerateClassFunctions(UStruct owner)
    {
        owner.Load();
        foreach (var field in owner.EnumerateFields())
        {
            field.Load();
            if (field is UFunction nestedFunction)
            {
                yield return nestedFunction;
                continue;
            }
            if (field is UStruct nested)
            {
                foreach (var childFunction in EnumerateClassFunctions(nested))
                    yield return childFunction;
            }
        }
    }

    private static void SaveTextWithBackup(string destination, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination)) BackupExistingFile(destination);
        File.WriteAllText(destination, content, new UTF8Encoding(false));
    }

    private static void CopyWithBackup(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination)) BackupExistingFile(destination);
        File.Copy(source, destination, true);
    }

    private static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
    private void OpenFolder(string path) { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe", Quote(path)) { UseShellExecute = true }); }
    private void Log(string text)
    {
        if (log.InvokeRequired) { log.BeginInvoke(new Action<string>(Log), text); return; }
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
    }
    private void SetBusy(bool busy)
    {
        if (InvokeRequired) { BeginInvoke(new Action<bool>(SetBusy), busy); return; }
        decompile.Enabled = compile.Enabled = !busy;
        UseWaitCursor = busy;
    }
}
