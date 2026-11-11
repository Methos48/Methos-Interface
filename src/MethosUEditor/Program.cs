using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
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
    private string AppRoot => AppContext.BaseDirectory;
    private string CompleteDir => Path.Combine(AppRoot, "Archivos U", "Comprimido");
    private string PartsDir => Path.Combine(AppRoot, "Archivos U", "Descomprimido");
    private string XdatCompleteDir => Path.Combine(AppRoot, "Archivos XDAT", "Comprimido");
    private string XdatPartsDir => Path.Combine(AppRoot, "Archivos XDAT", "Descomprimido");
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
    private readonly Button loadOriginal = new() { Text = "CARGAR ARCHIVO", Height = 48, Width = 150 };
    private readonly Button loadSources = new() { Text = "CARGAR ARCHIVOS DESCOMPILADOS", Height = 48, Width = 235 };
    private readonly Button decompile = new() { Text = "DESCOMPILAR", Height = 48, Width = 140 };
    private readonly Button compile = new() { Text = "COMPILAR", Height = 48, Width = 170 };
    private readonly Panel xdatHostPanel = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 247, 250) };
    private Panel? uViewPanel;
    private Process? xdatProcess;
    private IntPtr xdatWindow = IntPtr.Zero;
    private string XdatExePath => Path.Combine(AppRoot, "XDAT", "Methos XDAT.exe");

    private const int GwlStyle = -16;
    private const long WsChild = 0x40000000L;
    private const long WsPopup = unchecked((long)0x80000000);
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsBorder = 0x00800000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

    public MainForm()
    {
        Text = "Methos - Archivos U";
        Width = 980; Height = 700; MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 247, 250);
        Directory.CreateDirectory(CompleteDir);
        Directory.CreateDirectory(PartsDir);
        Directory.CreateDirectory(XdatCompleteDir);
        Directory.CreateDirectory(XdatPartsDir);
        BuildLayout();
        RefreshInputs();
        Log("Carpetas de trabajo junto al programa:");
        Log(CompleteDir);
        Log(PartsDir);
    }

    private void BuildLayout()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(18), BackColor = Color.FromArgb(32, 48, 68) };
        var title = new Label { Text = "METHOS", ForeColor = Color.White, Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true, Location = new Point(18, 12) };
        var subtitle = new Label { Text = "Herramientas de paquetes Lineage II Interlude", ForeColor = Color.Gainsboro, Font = new Font("Segoe UI", 9), AutoSize = true, Location = new Point(20, 52) };
        var navigation = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 310, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 14, 0, 0), BackColor = Color.Transparent };
        var openXdatView = new Button { Text = "ARCHIVOS XDAT", Width = 140, Height = 44, Font = new Font("Segoe UI", 9, FontStyle.Bold), BackColor = Color.White };
        var openUView = new Button { Text = "ARCHIVOS U", Width = 130, Height = 44, Font = new Font("Segoe UI", 9, FontStyle.Bold), BackColor = Color.LightSteelBlue };
        navigation.Controls.Add(openXdatView);
        navigation.Controls.Add(openUView);
        top.Controls.Add(title);
        top.Controls.Add(subtitle);
        top.Controls.Add(navigation);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(12), BackColor = Color.White };
        
        var openComplete = new Button { Text = "Abrir Comprimido", Width = 145, Height = 42 };
        var openParts = new Button { Text = "Abrir Descomprimido", Width = 155, Height = 42 };
        actions.Controls.AddRange(new Control[] { loadOriginal, decompile, loadSources, compile, openComplete, openParts });

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 430, Padding = new Padding(10) };
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        left.Controls.Add(files);
        left.Controls.Add(new Label { Text = "ARCHIVOS .U", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI", 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
        split.Panel1.Controls.Add(left);
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        right.Controls.Add(log);
        right.Controls.Add(new Label { Text = "REGISTRO DE ACTIVIDAD", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI", 10, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
        split.Panel2.Controls.Add(right);

        uViewPanel = new Panel { Dock = DockStyle.Fill };
        uViewPanel.Controls.Add(split);
        uViewPanel.Controls.Add(status);
        uViewPanel.Controls.Add(progress);
        uViewPanel.Controls.Add(actions);

        xdatHostPanel.Visible = false;
        xdatHostPanel.Resize += (_, _) => ResizeXdatWindow();
        xdatHostPanel.Controls.Add(new Label
        {
            Name = "xdatPlaceholder",
            Text = "METHOS XDAT\nPreparando el módulo XDAT...",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = Color.FromArgb(32, 48, 68)
        });

        Controls.Add(xdatHostPanel);
        Controls.Add(uViewPanel);
        Controls.Add(top);

        loadOriginal.Click += (_, _) => AddUPackages();
        loadSources.Click += (_, _) => ImportSourcesForSelectedPackage();
        openComplete.Click += (_, _) => OpenFolder(CompleteDir);
        openParts.Click += (_, _) => OpenFolder(PartsDir);
        decompile.Click += async (_, _) => await RunSelectedAsync(DecompileFile);
        compile.Click += async (_, _) => await RunSelectedAsync(CompileFolder);
        openUView.Click += async (_, _) =>
        {
            openUView.BackColor = Color.LightSteelBlue;
            openXdatView.BackColor = Color.White;
            await ShowUViewAsync();
        };
        openXdatView.Click += async (_, _) =>
        {
            openXdatView.BackColor = Color.LightSteelBlue;
            openUView.BackColor = Color.White;
            await ShowXdatViewAsync();
        };
        FormClosed += (_, _) => StopXdatProcess();
    }

    private Task ShowUViewAsync()
    {
        StopXdatProcess();
        if (xdatHostPanel is not null) xdatHostPanel.Visible = false;
        if (uViewPanel is not null) uViewPanel.Visible = true;
        Text = "Methos - Archivos U";
        return Task.CompletedTask;
    }

    private async Task ShowXdatViewAsync()
    {
        if (uViewPanel is not null) uViewPanel.Visible = false;
        xdatHostPanel.Visible = true;
        Text = "Methos - Archivos XDAT";
        if (xdatProcess is { HasExited: false } && xdatWindow != IntPtr.Zero)
        {
            ResizeXdatWindow();
            return;
        }

        if (!File.Exists(XdatExePath))
        {
            ShowXdatHostMessage("No se encontró el módulo XDAT.\nReinstala Methos para reparar sus componentes.");
            return;
        }

        try
        {
            StopXdatProcess();
            xdatProcess = Process.Start(new ProcessStartInfo
            {
                FileName = XdatExePath,
                WorkingDirectory = Path.GetDirectoryName(XdatExePath)!,
                UseShellExecute = false
            });
            if (xdatProcess is null) throw new InvalidOperationException("Windows no pudo iniciar el módulo XDAT.");

            IntPtr handle = IntPtr.Zero;
            for (int attempt = 0; attempt < 150; attempt++)
            {
                xdatProcess.Refresh();
                handle = xdatProcess.MainWindowHandle;
                if (handle != IntPtr.Zero) break;
                if (xdatProcess.HasExited) throw new InvalidOperationException("El módulo XDAT se cerró durante el inicio.");
                await Task.Delay(100);
            }
            if (handle == IntPtr.Zero)
                throw new TimeoutException("El módulo XDAT no creó una ventana visible en 15 segundos.");

            xdatWindow = handle;
            long style = GetWindowLongPtr(xdatWindow, GwlStyle).ToInt64();
            style &= ~(WsPopup | WsCaption | WsThickFrame | WsBorder | WsSysMenu | WsMinimizeBox | WsMaximizeBox);
            style |= WsChild | 0x10000000L;
            SetWindowLongPtr(xdatWindow, GwlStyle, new IntPtr(style));
            SetParent(xdatWindow, xdatHostPanel.Handle);
            ResizeXdatWindow();
            xdatHostPanel.Controls.Clear();
        }
        catch (Exception ex)
        {
            StopXdatProcess();
            ShowXdatHostMessage("No se pudo abrir el módulo XDAT.\n\n" + ex.Message);
            Log("Error al iniciar el módulo XDAT: " + ex);
        }
    }

    private void ShowXdatHostMessage(string message)
    {
        xdatHostPanel.Controls.Clear();
        xdatHostPanel.Controls.Add(new Label
        {
            Text = message,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = Color.FromArgb(32, 48, 68)
        });
    }

    private void ResizeXdatWindow()
    {
        if (xdatWindow == IntPtr.Zero || xdatHostPanel.IsDisposed) return;
        MoveWindow(xdatWindow, 0, 0, Math.Max(1, xdatHostPanel.ClientSize.Width),
            Math.Max(1, xdatHostPanel.ClientSize.Height), true);
    }

    private void StopXdatProcess()
    {
        xdatWindow = IntPtr.Zero;
        try
        {
            if (xdatProcess is { HasExited: false })
            {
                xdatProcess.CloseMainWindow();
                if (!xdatProcess.WaitForExit(1500)) xdatProcess.Kill(entireProcessTree: true);
            }
        }
        catch { }
        finally
        {
            xdatProcess?.Dispose();
            xdatProcess = null;
        }
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

    private void ImportSourcesForSelectedPackage()
    {
        if (files.SelectedIndices.Count != 1)
        {
            MessageBox.Show("Selecciona un único paquete .u antes de cargar sus fuentes.");
            return;
        }
        int index = files.SelectedIndices[0];
        if (index < 0 || index >= inputs.Count) return;
        string packageName = Path.GetFileNameWithoutExtension(inputs[index]);
        using var dialog = new FolderBrowserDialog { Description = "Selecciona la carpeta externa que contiene las fuentes modificadas (.uc)." };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try
        {
            string destination = Path.Combine(PartsDir, packageName);
            int count = WorkspaceImport.ReplaceSources(dialog.SelectedPath, destination);
            Log($"Fuentes importadas de forma segura: {count} archivos .uc para {packageName}. Se conservaron los metadatos .methos.");
            MessageBox.Show($"Se importaron {count} fuentes para {packageName}.", "Methos", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("Importación cancelada: " + ex.Message);
            MessageBox.Show(ex.Message, "No se reemplazaron las fuentes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!,
            "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var input = entry.Open())
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            if (File.Exists(destination)) BackupExistingFile(destination);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
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
        var failures = new List<string>();
        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                progress.Value = (int)((i * 100.0) / selected.Count);
                status.Text = $"{i + 1}/{selected.Count}: {Path.GetFileName(selected[i])}";
                try { await action(selected[i]); }
                catch (Exception ex)
                {
                    string name = Path.GetFileName(selected[i]);
                    string detail = ex.GetBaseException().Message;
                    const string sourceFailureMarker = "METHOS_SOURCE_FAILURES\n";
                    if (detail.StartsWith(sourceFailureMarker, StringComparison.Ordinal))
                    {
                        failures.AddRange(detail[sourceFailureMarker.Length..]
                            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
                    }
                    else
                    {
                        failures.Add($"{name}: {detail}");
                    }
                    Log($"ERROR EN {name}: {ex}");
                }
            }
            progress.Value = 100;
            status.Text = failures.Count == 0 ? "Proceso finalizado" : $"Finalizado con {failures.Count} archivo(s) con errores";
            if (failures.Count > 0)
            {
                string report = "PROCESO FALLIDO\n\nArchivos con errores:\n- " +
                    string.Join("\n- ", failures.Select(item =>
                    {
                        int separator = item.IndexOf(": ", StringComparison.Ordinal);
                        return separator < 0 ? item : item[..separator] + "\n  Motivo: " + item[(separator + 2)..];
                    }));
                Log(report);
                MessageBox.Show(report, "Informe de compilación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else MessageBox.Show($"Revisión terminada. Todos los {selected.Count} archivos seleccionados finalizaron sin errores.", "Methos U Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { SetBusy(false); }
    }

    private Task DecompileFile(string path) => Task.Run(() =>
    {
        var baseName = Path.GetFileNameWithoutExtension(path);
        var output = Path.Combine(PartsDir, baseName);
        Log($"Analizando {Path.GetFileName(path)}...");
        byte[] packageBytes = File.ReadAllBytes(path);
        var detection = Lineage2FileDetector.Detect(packageBytes);
        Log($"Detección U: estado={detection.Status}; familia={detection.Family}; versión={detection.Version?.ToString() ?? "(ninguna)"}.");
        if (detection.Encrypted == true)
        {
            string technical = $"INFORMACIÓN TÉCNICA\nArchivo: {Path.GetFileName(path)}\nTamaño: {packageBytes.Length:N0} bytes\nEstado: {detection.Status}\nVersión: {detection.Version}\nFamilia: {detection.Family}\nCabecera: {detection.Header ?? "(no identificada)"}\nPrimeros bytes (hex): {detection.FirstBytesHex}\nDiagnóstico: {detection.Detail}\n\nEl archivo original no fue modificado.";
            string message = detection.DecoderAvailable
                ? "Cifrado identificado, pero el motor de descifrado U todavía debe conectarse.\n\n" + technical
                : "Cifrado reconocido, pero esta variante todavía no está soportada.\n\n" + technical;
            MessageBox.Show(message, "Methos U Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Log("Proceso detenido de forma segura: el archivo cifrado no se modificó.");
            return;
        }
        if (detection.Status != "plain_candidate")
        {
            string technical = $"No se reconoce este cifrado.\n\nINFORMACIÓN TÉCNICA\nArchivo: {Path.GetFileName(path)}\nTamaño: {packageBytes.Length:N0} bytes\nEstado: {detection.Status}\nPrimeros bytes (hex): {detection.FirstBytesHex}\nDiagnóstico: {detection.Detail}\n\nEl archivo original no fue modificado.";
            MessageBox.Show(technical, "Methos U Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
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
        if (TryRestoreExactSources(baseName, packageBytes, output))
        {
            string restoredMetadata = PackageOriginMetadata.Save(
                path, packageBytes, output, "UnrealPackage",
                detection.Status.ToString(), detection.Family, detection.Version,
                detection.Header, detection.Detail);
            Log($"Perfil de origen guardado: {restoredMetadata}; SHA-256={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packageBytes))}.");
            return;
        }

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
                string nativeRegistryPath = Path.Combine(UccRuntimeRoot, "native-function-registry.json");
                try
                {
                    if (!File.Exists(nativeRegistryPath) && UccCompiler.IsAvailable(UccRuntimeRoot))
                    {
                        var nativeEntries = NativeFunctionRegistryBuilder.Build(UccRuntimeRoot, nativeRegistryPath);
                        Log($"Registro nativo Interlude creado: {nativeEntries.Count} funciones.");
                    }
                    source = UnrealScriptNativeFunctionNormalizer.Normalize(
                        source, nativeRegistryPath, out int normalizedNativeCalls, out int unresolvedNativeCalls);
                    if (normalizedNativeCalls > 0 || unresolvedNativeCalls > 0)
                        Log($"Funciones nativas normalizadas: {normalizedNativeCalls}; sin correspondencia: {unresolvedNativeCalls}.");
                }
                catch (Exception normalizeError)
                {
                    Log($"Aviso: no se pudieron normalizar las funciones nativas en esta clase: {normalizeError.Message}");
                }
                var objectName = obj.Name?.ToString() ?? $"Class_{ok}";
                var safeName = string.Concat(objectName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
                if (baseName.Equals("Interface", StringComparison.OrdinalIgnoreCase))
                {
                    string referenceSourcePath = Path.Combine(
                        UccRuntimeRoot, "Interlude", "Interface", "Classes", safeName + ".uc");
                    if (File.Exists(referenceSourcePath))
                    {
                        try
                        {
                            string referenceSource = File.ReadAllText(referenceSourcePath);
                            source = UnrealScriptFailedFunctionRepair.RestoreFailedFunctions(
                                source, referenceSource, out var restoredFunctions);
                            source = UnrealScriptFailedFunctionRepair.RestoreInvalidTrailingReturns(
                                source, referenceSource, out var repairedTrailingReturns);
                            if (repairedTrailingReturns.Count > 0)
                                Log($"Retornos vacíos de funciones no-void corregidos: {safeName}.uc; funciones={string.Join(", ", repairedTrailingReturns)}.");
                            if (restoredFunctions.Count > 0)
                                Log($"Funciones dañadas o excesivamente anidadas, recuperadas de la referencia: {safeName}.uc; funciones={restoredFunctions.Count}.");
                            source = UnrealScriptReferenceNormalizer.RestoreEnumArguments(
                                source, referenceSource, out int restoredEnumArguments);
                            if (restoredEnumArguments > 0)
                                Log($"Constantes enum restauradas desde la referencia Interlude: {safeName}.uc; argumentos={restoredEnumArguments}.");
                            source = UnrealScriptCallsiteArgumentRestorer.RestoreSymbolicArguments(
                                source, referenceSource, out int restoredCallsiteArguments);
                            if (restoredCallsiteArguments > 0)
                                Log($"Argumentos simbólicos recuperados desde llamadas originales: {safeName}.uc; argumentos={restoredCallsiteArguments}.");
                            source = UnrealScriptCallsiteArgumentRestorer.RestoreSymbolicAssignments(
                                source, referenceSource, out int restoredSymbolicAssignments);
                            if (restoredSymbolicAssignments > 0)
                                Log($"Asignaciones simbólicas recuperadas desde la referencia: {safeName}.uc; asignaciones={restoredSymbolicAssignments}.");
                        }
                        catch (Exception referenceError)
                        {
                            Log($"Aviso: no se pudieron restaurar constantes enum de {safeName}.uc: {referenceError.Message}");
                        }
                    }
                }

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
        string metadataPath = PackageOriginMetadata.Save(
            path, packageBytes, output, "UnrealPackage",
            detection.Status.ToString(), detection.Family, detection.Version,
            detection.Header, detection.Detail, checked((int)package.Summary.Version),
            checked((int)package.Summary.LicenseeVersion));
        Log($"Perfil de origen guardado: {metadataPath}; SHA-256={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packageBytes))}.");
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
        string parent = Path.GetDirectoryName(destinationRoot)!;
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, "." + packageName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        string previous = Path.Combine(parent, "." + packageName + "." + Guid.NewGuid().ToString("N") + ".old");
        try
        {
            Directory.CreateDirectory(staging);
            foreach (string source in GetSourceFiles(sourceDirectory))
            {
                string relative = Path.GetRelativePath(sourceDirectory, source);
                string destination = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: false);
            }

            if (Directory.Exists(destinationRoot))
                Directory.Move(destinationRoot, previous);
            try
            {
                Directory.Move(staging, destinationRoot);
            }
            catch
            {
                if (Directory.Exists(previous) && !Directory.Exists(destinationRoot))
                    Directory.Move(previous, destinationRoot);
                throw;
            }
            if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
            Log($"Copia exacta de fuentes guardada en caché: {GetSourceFiles(destinationRoot).Length} clases; SHA-256={hash}.");
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            if (Directory.Exists(previous) && Directory.Exists(destinationRoot))
                Directory.Delete(previous, recursive: true);
        }
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
            DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" +
            Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(path);
        string destination = Path.Combine(backupRoot, backupName);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(path, temporary, overwrite: false);
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                stream.Flush(flushToDisk: true);
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
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

        string originManifest = Path.Combine(sourceDirectory, ".methos", "origin.json");
        if (File.Exists(originManifest))
        {
            string immutableOriginal = Path.Combine(Path.GetDirectoryName(originManifest)!, "originals", Path.GetFileName(packagePath));
            if (!File.Exists(immutableOriginal))
                throw new FileNotFoundException("Falta la copia original inmutable necesaria para validar el perfil Methos.", immutableOriginal);
            using var origin = PackageOriginMetadata.TryLoadAndValidate(originManifest, immutableOriginal)
                ?? throw new InvalidDataException("No se pudo leer el perfil de origen Methos.");
            var encoding = origin.RootElement.GetProperty("encoding");
            int? recordedProtocol = encoding.TryGetProperty("protocol_version", out var protocolValue) &&
                protocolValue.ValueKind == System.Text.Json.JsonValueKind.Number
                ? protocolValue.GetInt32() : null;
            if (recordedProtocol.HasValue)
                throw new NotSupportedException($"El paquete original usaba el protocolo {recordedProtocol}. El perfil fue conservado, pero el codificador U para ese protocolo todavía no está integrado. No se generó un archivo con cifrado diferente.");
            Log("Perfil de origen verificado por SHA-256. No se detectó protocolo cifrado que requiera recodificación.");
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
            var preflightFailures = new List<string>();
            foreach (string sourcePath in ucFiles)
            {
                try
                {
                    string text = File.ReadAllText(sourcePath);
                    _ = UnrealScriptSourceIndex.RepairMissingFunctionClosures(text);
                    _ = UnrealScriptSourceIndex.ParseFunctions(text);
                }
                catch (Exception ex)
                {
                    preflightFailures.Add($"{Path.GetFileName(sourcePath)}: {ex.GetBaseException().Message}");
                }
            }
            if (preflightFailures.Count > 0)
                throw new InvalidDataException("METHOS_SOURCE_FAILURES\n" + string.Join("\n", preflightFailures));
            Log($"Revisión previa correcta: {ucFiles.Length} fuentes .uc. Iniciando UCC.");
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
        var sourceFailures = new List<string>();
        foreach (string sourcePath in ucFiles)
        {
            string className = Path.GetFileNameWithoutExtension(sourcePath);
            try
            {
            if (!classes.TryGetValue(className, out var classObject))
                throw new InvalidDataException($"La clase '{className}' no existe como UClass en el paquete base. Crear clases nuevas todavía no está implementado.");

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
            catch (Exception ex)
            {
                sourceFailures.Add($"{Path.GetFileName(sourcePath)}: {ex.GetBaseException().Message}");
                Log($"ARCHIVO CON ERROR: {Path.GetFileName(sourcePath)}: {ex}");
            }
        }

        if (sourceFailures.Count > 0)
            throw new InvalidDataException("METHOS_SOURCE_FAILURES\n" + string.Join("\n", sourceFailures));
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
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!,
            "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var writer = new StreamWriter(
                new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None),
                new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
            }
            using (var durableStream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                durableStream.Flush(flushToDisk: true);
            if (File.Exists(destination)) BackupExistingFile(destination);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void CopyWithBackup(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!,
            "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(source, temporary, overwrite: false);
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                stream.Flush(flushToDisk: true);
            if (File.Exists(destination)) BackupExistingFile(destination);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
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
