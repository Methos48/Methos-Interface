using System.Text.Json;
using UELib;
using UELib.Core;

namespace MethosUEngine;

public sealed record NativeFunctionDescriptor(
    string Package,
    string Owner,
    string Name,
    int NativeToken,
    int Precedence,
    bool IsOperator,
    string ReturnType,
    string Flags);

public static class NativeFunctionRegistryBuilder
{
    public static IReadOnlyList<NativeFunctionDescriptor> Build(string runtimeRoot, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var registry = new List<NativeFunctionDescriptor>();
        foreach (string packageName in new[] { "Core", "Engine", "NWindow" })
        {
            string packagePath = Path.Combine(runtimeRoot, "Interlude", "System", packageName + ".u");
            if (!File.Exists(packagePath)) continue;

            using var package = UnrealLoader.LoadPackage(packagePath, FileAccess.Read);
            package.InitializePackage();
            var classes = package.Objects.OfType<UClass>().Where(item => (int)item > 0).ToArray();
            foreach (var function in classes.SelectMany(EnumerateClassFunctions))
            {
                if (function.NativeToken == 0) continue;
                registry.Add(new NativeFunctionDescriptor(
                    packageName,
                    function.Outer?.Name.ToString() ?? "",
                    function.Name.ToString(),
                    function.NativeToken,
                    function.OperPrecedence,
                    function.IsOperator(),
                    function.ReturnProperty?.Class?.Name.ToString() ?? "",
                    function.FunctionFlags.ToString()));
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(registry,
            new JsonSerializerOptions { WriteIndented = true }));
        return registry;
    }

    private static IEnumerable<UFunction> EnumerateClassFunctions(UStruct owner)
    {
        owner.Load();
        foreach (var field in owner.EnumerateFields())
        {
            field.Load();
            if (field is UFunction function)
            {
                yield return function;
                continue;
            }

            if (field is UStruct nested)
                foreach (var child in EnumerateClassFunctions(nested))
                    yield return child;
        }
    }
}
