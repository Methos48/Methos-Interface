namespace MethosUEngine;

public sealed record UnrealFunctionTarget(string Name, int ExportReference,
    int ScriptOffsetRelative, int OriginalScriptSize);

public sealed record UnrealScriptCompileResult(byte[] PackageBytes, int FunctionsCompiled,
    IReadOnlyList<UnrealScriptSourceIndex.ClosureRepair> Repairs);

/// <summary>
/// Connects source indexing, literal-return bytecode generation, bytecode patching
/// and package rebuilding. It never writes the source package itself.
/// </summary>
public static class UnrealScriptCompilerPipeline
{
    public static UnrealScriptCompileResult CompileClassSource(string source,
        UnrealPackageIndex package, IReadOnlyCollection<UnrealFunctionTarget> targets,
        bool repairMissingFunctionClosures = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(targets);

        if (package.Summary.Version < 95 || package.Summary.Version >= 639)
            throw new NotSupportedException($"El generador de bytecode inicial no admite la versión {package.Summary.Version}/{package.Summary.LicenseeVersion}.");

        var targetMap = new Dictionary<string, UnrealFunctionTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            if (!targetMap.TryAdd(target.Name, target))
                throw new InvalidDataException($"Hay más de un destino de compilación para la función '{target.Name}'.");
        }

        var repairResult = repairMissingFunctionClosures
            ? UnrealScriptSourceIndex.RepairMissingFunctionClosures(source)
            : new UnrealScriptSourceIndex.ClosureRepairResult(source,
                Array.Empty<UnrealScriptSourceIndex.ClosureRepair>());
        var sourceFunctions = UnrealScriptSourceIndex.ParseFunctions(repairResult.Source);
        var builder = new UnrealPackageBuilder(package);
        var compiledBytecode = new List<(UnrealFunctionTarget Target, byte[] Bytecode)>();
        int compiled = 0;
        var usedTargets = new HashSet<int>();

        foreach (var function in sourceFunctions)
        {
            if (!targetMap.TryGetValue(function.Name, out var target))
                throw new InvalidDataException($"La función '{function.Name}' del código fuente no tiene un UFunction correspondiente en el paquete.");
            if (!usedTargets.Add(target.ExportReference))
                throw new InvalidDataException($"El export de '{function.Name}' se intentó compilar más de una vez.");

            byte[] bytecode = UnrealScriptFunctionCompiler.CompileReturnOnlyBody(
                function.Body, name => builder.AddName(name));
            UnrealScriptPatcher.ReplaceExportBytecode(package, builder,
                target.ExportReference, target.ScriptOffsetRelative,
                target.OriginalScriptSize, bytecode);
            compiledBytecode.Add((target, bytecode));
            compiled++;
        }

        if (compiled == 0)
            throw new InvalidDataException("No se encontraron cuerpos de función compilables en el archivo .uc.");

        byte[] rebuiltPackage = builder.Build();
        var rebuiltIndex = UnrealPackageIndex.Parse(rebuiltPackage);
        foreach (var item in compiledBytecode)
        {
            var export = rebuiltIndex.Exports[item.Target.ExportReference - 1];
            byte[] payload = rebuiltPackage.AsSpan(export.SerialOffset, export.SerialSize).ToArray();
            int sizeOffset = item.Target.ScriptOffsetRelative - 4;
            if (sizeOffset < 0 || sizeOffset > payload.Length - 4 ||
                System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(sizeOffset, 4)) != item.Bytecode.Length)
                throw new InvalidDataException($"La verificación posterior a la compilación falló para '{item.Target.Name}': tamaño de bytecode incorrecto.");
            if (item.Target.ScriptOffsetRelative > payload.Length - item.Bytecode.Length ||
                !payload.AsSpan(item.Target.ScriptOffsetRelative, item.Bytecode.Length).SequenceEqual(item.Bytecode))
                throw new InvalidDataException($"La verificación posterior a la compilación falló para '{item.Target.Name}': bytecode distinto al generado.");
        }
        return new UnrealScriptCompileResult(rebuiltPackage, compiled, repairResult.Repairs);
    }
}
