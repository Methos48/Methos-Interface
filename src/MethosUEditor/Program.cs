using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UELib;
using UELib.Core;

namespace MethosUEditor;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0].Equals("--self-test", StringComparison.OrdinalIgnoreCase))
        {
            var output = args.Length >= 3 ? Path.GetFullPath(args[2]) : Path.Combine(Path.GetTempPath(), "MethosU_SelfTest");
            Directory.CreateDirectory(output);
            var report = new StringBuilder();
            try
            {
                using var package = UnrealLoader.LoadPackage(args[1], FileAccess.Read);
                package.InitializePackage();
                var classes = package.Objects.OfType<UClass>().Where(o => (int)o > 0).ToList();
                int exported = 0, failed = 0;
                long sourceCharacters = 0;
                foreach (var scriptClass in classes)
                {
                    try
                    {
                        var source = scriptClass.Decompile();
                        var name = scriptClass.Name?.ToString() ?? $"Class_{exported}";
                        var safeName = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
                        File.WriteAllText(Path.Combine(output, safeName + ".uc"), source, new UTF8Encoding(false));
                        sourceCharacters += source.Length;
                        exported++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        report.AppendLine(ex.ToString());
                    }
                }
                report.AppendLine($"Classes={classes.Count}; Exported={exported}; Failed={failed}; Characters={sourceCharacters}");
                File.WriteAllText(Path.Combine(output, "selftest.log"), report.ToString(), Encoding.UTF8);
                Environment.ExitCode = exported >= 100 && sourceCharacters >= 500_000 ? 0 : 1;
            }
            catch (Exception ex)
            {
                report.AppendLine(ex.ToString());
                File.WriteAllText(Path.Combine(output, "selftest.log"), report.ToString(), Encoding.UTF8);
                Environment.ExitCode = 1;
            }
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private readonly string docsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Methos U Editor");
    private string CompleteDir => Path.Combine(docsRoot, "u completo");
    private string PartsDir => Path.Combine(docsRoot, "u por partes");
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
            int count = 0;
            var zipBase = Path.GetFileNameWithoutExtension(d.FileName);
            var targetName = zipBase.Contains("interface", StringComparison.OrdinalIgnoreCase) ? "Interface" : zipBase;
            var targetDir = Path.Combine(PartsDir, targetName);
            Directory.CreateDirectory(targetDir);
            foreach (var entry in archive.Entries.Where(e => e.Name.EndsWith(".uc", StringComparison.OrdinalIgnoreCase)))
            {
                var name = Path.GetFileName(entry.Name);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var dst = Path.Combine(targetDir, name);
                using var input = entry.Open(); using var output = File.Create(dst);
                input.CopyTo(output); count++;
            }
            Log($"ZIP importado: {count} archivos .uc.");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Error al importar ZIP", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void AddFolder()
    {
        using var d = new FolderBrowserDialog { Description = "Selecciona una carpeta con archivos .u o clases .uc" };
        if (d.ShowDialog() != DialogResult.OK) return;
        var uFiles = Directory.GetFiles(d.SelectedPath, "*.u", SearchOption.AllDirectories);
        if (uFiles.Length > 0)
        {
            foreach (var src in uFiles)
            {
                var dst = Path.Combine(CompleteDir, Path.GetFileName(src));
                if (!SamePath(src, dst)) CopyWithBackup(src, dst);
            }
            RefreshInputs();
        }
        else
        {
            var ucFiles = Directory.GetFiles(d.SelectedPath, "*.uc", SearchOption.AllDirectories);
            if (ucFiles.Length == 0) { MessageBox.Show("No se encontraron archivos .u ni .uc."); return; }
            var folderName = new DirectoryInfo(d.SelectedPath).Name;
            var targetName = folderName.Contains("interface", StringComparison.OrdinalIgnoreCase) ? "Interface" : folderName;
            var target = Path.Combine(PartsDir, targetName);
            Directory.CreateDirectory(target);
            foreach (var src in ucFiles)
            {
                var dst = Path.Combine(target, Path.GetFileName(src));
                if (!SamePath(src, dst)) CopyWithBackup(src, dst);
            }
            Log($"Importadas {ucFiles.Length} clases .uc.");
        }
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
        using var package = UnrealLoader.LoadPackage(path, FileAccess.Read);
        Log($"Paquete detectado: versión {package.Summary.Version}, licencia {package.Summary.LicenseeVersion}.");
        package.InitializePackage();
        var classes = package.Objects.OfType<UClass>().Where(o => (int)o > 0).ToList();
        int ok = 0, failed = 0;
        foreach (var obj in classes)
        {
            try
            {
                var source = obj.Decompile();
                var objectName = obj.Name?.ToString() ?? $"Class_{ok}";
                var safeName = string.Concat(objectName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
                SaveTextWithBackup(Path.Combine(output, safeName + ".uc"), source);
                ok++;
            }
            catch (Exception ex) { failed++; Log($"No se pudo exportar una clase: {ex.Message}"); }
        }
        Log($"Descompilación terminada. Clases exportadas: {ok}; errores: {failed}. Carpeta: {output}");
        if (ok == 0) throw new InvalidOperationException("No se pudo exportar ninguna clase. El paquete puede usar una variante de Unreal/Lineage que requiere ajustes de compatibilidad.");
    });

    private Task CompileFolder(string packagePath) => Task.Run(() =>
    {
        var baseName = Path.GetFileNameWithoutExtension(packagePath);
        var source = Path.Combine(PartsDir, baseName);
        if (!Directory.Exists(source))
        {
            var candidate = Path.Combine(PartsDir, "Classes");
            if (Directory.Exists(candidate)) source = candidate;
            else throw new DirectoryNotFoundException($"No existe la carpeta de fuentes: {source}");
        }
        var ucFiles = Directory.GetFiles(source, "*.uc", SearchOption.AllDirectories);
        if (ucFiles.Length == 0) throw new InvalidOperationException($"No se encontraron archivos .uc en {source}");
        var ucc = FindTool("UCC.exe");
        if (ucc == null)
        {
            ucc = (string?)Invoke(new Func<string?>(() =>
            {
                using var d = new OpenFileDialog { Filter = "Compilador Unreal (UCC.exe)|UCC.exe", Title = "Selecciona UCC.exe del compilador Lineage II" };
                return d.ShowDialog() == DialogResult.OK ? d.FileName : null;
            }));
            if (string.IsNullOrWhiteSpace(ucc)) throw new FileNotFoundException("Para compilar se necesita UCC.exe, incluido en un entorno de compilación compatible con Lineage II.");
        }
        var systemDir = Path.GetDirectoryName(ucc)!;
        var root = Directory.GetParent(systemDir)?.FullName ?? throw new InvalidOperationException("No se pudo identificar la carpeta raíz del compilador.");
        var temp = Path.Combine(Path.GetTempPath(), "MethosU_" + Guid.NewGuid().ToString("N"));
        var tempSystem = Path.Combine(temp, Path.GetFileName(systemDir));
        CopyDirectory(systemDir, tempSystem);
        var classDir = Path.Combine(temp, baseName, "Classes");
        Directory.CreateDirectory(classDir);
        foreach (var file in ucFiles) File.Copy(file, Path.Combine(classDir, Path.GetFileName(file)), true);
        ConfigureCompilerIni(tempSystem, baseName);
        Log($"Compilando {ucFiles.Length} clases con {ucc}...");
        var tempUcc = Path.Combine(tempSystem, Path.GetFileName(ucc));
        var ini = FindCompilerIni(tempSystem);
        var args = "make" + (ini == null ? "" : " -ini=" + Quote(Path.GetFileName(ini)));
        var psi = new ProcessStartInfo(tempUcc, args) { WorkingDirectory = tempSystem, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        using var process = new Process { StartInfo = psi };
        var outputText = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (outputText) outputText.AppendLine(e.Data); Log(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (outputText) outputText.AppendLine(e.Data); Log(e.Data); } };
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit();
        var result = Path.Combine(tempSystem, baseName + ".u");
        if (!File.Exists(result))
        {
            result = Directory.GetFiles(tempSystem, "*.u", SearchOption.TopDirectoryOnly).FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(baseName, StringComparison.OrdinalIgnoreCase)) ?? "";
        }
        if (process.ExitCode != 0 || string.IsNullOrEmpty(result) || !File.Exists(result))
            throw new InvalidOperationException("UCC no generó el paquete. Revisa los errores del registro. Comprueba que el compilador sea de la misma crónica y que tenga Core, Engine, Fire, Editor y NWindow.");
        var destination = Path.Combine(CompleteDir, baseName + ".u");
        CopyWithBackup(result, destination);
        Log($"Compilación terminada: {destination}");
        try { Directory.Delete(temp, true); } catch { }
    });

    private static string? FindTool(string name)
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[] { Path.Combine(baseDir, "herramientas"), Path.Combine(baseDir, "tools"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Methos U Editor", "herramientas") };
        foreach (var dir in candidates.Where(Directory.Exists))
        {
            var found = Directory.GetFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault();
            if (found != null) return found;
        }
        return null;
    }

    private static string? FindCompilerIni(string systemDir)
    {
        var preferred = new[] { "UT2003.ini", "L2.ini", "Default.ini", "Unreal.ini" };
        foreach (var name in preferred)
        {
            var path = Path.Combine(systemDir, name);
            if (File.Exists(path)) return path;
        }
        return Directory.GetFiles(systemDir, "*.ini", SearchOption.TopDirectoryOnly).FirstOrDefault();
    }

    private static void ConfigureCompilerIni(string systemDir, string packageName)
    {
        var ini = FindCompilerIni(systemDir);
        if (ini == null) throw new FileNotFoundException("No se encontró el archivo .ini del compilador.");
        var text = File.ReadAllText(ini, Encoding.Default).Replace("\r\n", "\n");
        var lines = text.Split('\n').ToList();

        const string editorSection = "[Editor.EditorEngine]";
        int editorStart = lines.FindIndex(x => x.Trim().Equals(editorSection, StringComparison.OrdinalIgnoreCase));
        if (editorStart < 0)
        {
            lines.Add(editorSection);
            editorStart = lines.Count - 1;
        }
        int editorEnd = lines.FindIndex(editorStart + 1, x => x.TrimStart().StartsWith("["));
        if (editorEnd < 0) editorEnd = lines.Count;
        var editorSettings = lines.Skip(editorStart + 1).Take(editorEnd - editorStart - 1)
            .Where(x => !x.TrimStart().StartsWith("EditPackages=", StringComparison.OrdinalIgnoreCase)).ToList();
        lines.RemoveRange(editorStart + 1, editorEnd - editorStart - 1);
        var packages = new[] { "Core", "Engine", "Fire", "Editor", "NWindow", packageName };
        lines.InsertRange(editorStart + 1, editorSettings.Concat(packages.Select(x => "EditPackages=" + x)));

        const string coreSection = "[Core.System]";
        int coreStart = lines.FindIndex(x => x.Trim().Equals(coreSection, StringComparison.OrdinalIgnoreCase));
        if (coreStart < 0)
        {
            lines.Add(coreSection);
            coreStart = lines.Count - 1;
        }
        int coreEnd = lines.FindIndex(coreStart + 1, x => x.TrimStart().StartsWith("["));
        if (coreEnd < 0) coreEnd = lines.Count;
        var sourcePath = "Paths=.." + Path.DirectorySeparatorChar + packageName + Path.DirectorySeparatorChar + "Classes" + Path.DirectorySeparatorChar + "*.uc";
        if (!lines.Skip(coreStart + 1).Take(coreEnd - coreStart - 1).Any(x => x.Trim().Equals(sourcePath, StringComparison.OrdinalIgnoreCase)))
            lines.Insert(coreEnd, sourcePath);

        File.WriteAllText(ini, string.Join(Environment.NewLine, lines), Encoding.Default);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.TopDirectoryOnly))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static void SaveTextWithBackup(string destination, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            var backup = Path.Combine(Path.GetDirectoryName(destination)!, "backups");
            Directory.CreateDirectory(backup);
            var backupName = Path.GetFileNameWithoutExtension(destination) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + Path.GetExtension(destination);
            File.Copy(destination, Path.Combine(backup, backupName), true);
        }
        File.WriteAllText(destination, content, new UTF8Encoding(false));
    }

    private static void CopyWithBackup(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            var backup = Path.Combine(Path.GetDirectoryName(destination)!, "backups");
            Directory.CreateDirectory(backup);
            File.Copy(destination, Path.Combine(backup, Path.GetFileNameWithoutExtension(destination) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + Path.GetExtension(destination)), true);
        }
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
