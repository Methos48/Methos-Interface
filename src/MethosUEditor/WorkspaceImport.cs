using System;
using System.IO;
using System.Linq;

namespace MethosUEditor;

internal static class WorkspaceImport
{
    public static int ReplaceSources(string selectedFolder, string targetFolder)
    {
        string source = Path.GetFullPath(selectedFolder);
        string target = Path.GetFullPath(targetFolder);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("La carpeta seleccionada no existe.");
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Selecciona una carpeta externa, distinta de la carpeta de trabajo de Methos.");

        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(source, f).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part.Equals(".methos", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        int sourceCount = files.Count(f => Path.GetExtension(f).Equals(".uc", StringComparison.OrdinalIgnoreCase));
        if (sourceCount == 0)
            throw new InvalidDataException("La carpeta no contiene fuentes .uc. No se reemplazó el contenido actual.");

        string parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        string staging = target + ".import-" + Guid.NewGuid().ToString("N");
        string previous = target + ".previous-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(staging);
            if (Directory.Exists(target))
            {
                string metadata = Path.Combine(target, ".methos");
                if (Directory.Exists(metadata))
                    CopyTree(metadata, Path.Combine(staging, ".methos"));
            }
            foreach (string file in files)
            {
                string relative = Path.GetRelativePath(source, file);
                string destination = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: false);
            }

            if (Directory.Exists(target)) Directory.Move(target, previous);
            try { Directory.Move(staging, target); }
            catch
            {
                if (Directory.Exists(previous) && !Directory.Exists(target))
                    Directory.Move(previous, target);
                throw;
            }
            if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
            return sourceCount;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            if (Directory.Exists(previous) && Directory.Exists(target))
                Directory.Delete(previous, recursive: true);
        }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
