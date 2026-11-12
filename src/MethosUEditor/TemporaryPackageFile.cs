namespace MethosUEditor;

internal sealed class TemporaryPackageFile : IDisposable
{
    public string Path { get; }

    public TemporaryPackageFile(ReadOnlySpan<byte> content, string extension = ".u")
    {
        if (string.IsNullOrWhiteSpace(extension) || extension[0] != '.')
            throw new ArgumentException("A temporary package extension must begin with a dot.", nameof(extension));
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "MethosPackage_" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllBytes(Path, content.ToArray());
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
        catch
        {
            // Best-effort cleanup only; the validated output has already been written elsewhere.
        }
    }
}
