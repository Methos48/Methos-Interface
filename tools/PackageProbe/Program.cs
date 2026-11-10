using UELib;
using System.Text;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: PackageProbe <package.u> [output-directory]");
    return 2;
}

var input = Path.GetFullPath(args[0]);
var output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(Environment.CurrentDirectory, "probe-export");
Directory.CreateDirectory(output);

try
{
    using var package = UnrealLoader.LoadPackage(input, FileAccess.Read);
    Console.WriteLine($"PackageVersion={package.Summary.Version}");
    Console.WriteLine($"LicenseeVersion={package.Summary.LicenseeVersion}");
    package.InitializePackage(UnrealPackage.InitFlags.Construct | UnrealPackage.InitFlags.RegisterClasses);

    var classes = package.Objects.Where(o => string.Equals(o.Class?.Name, "Class", StringComparison.OrdinalIgnoreCase)).ToList();
    Console.WriteLine($"ObjectCount={package.Objects.Count}");
    Console.WriteLine($"ClassCount={classes.Count}");

    int exported = 0, failed = 0;
    foreach (var obj in classes)
    {
        try
        {
            obj.BeginDeserializing();
            var source = obj.Decompile();
            var safeName = string.Concat(obj.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            File.WriteAllText(Path.Combine(output, safeName + ".uc"), source, new UTF8Encoding(false));
            exported++;
        }
        catch (Exception ex)
        {
            failed++;
            Console.WriteLine($"EXPORT_ERROR {obj.Name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    Console.WriteLine($"Exported={exported}");
    Console.WriteLine($"Failed={failed}");
    return failed == 0 && exported > 0 ? 0 : 3;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.ToString());
    return 4;
}
