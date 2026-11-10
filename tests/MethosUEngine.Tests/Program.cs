using System.Buffers.Binary;
using System.IO.Compression;
using MethosUEngine;
using UELib;
using UELib.Core;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static IEnumerable<UFunction> EnumerateClassFunctions(UStruct owner)
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
            foreach (var childFunction in EnumerateClassFunctions(nested))
                yield return childFunction;
    }
}

static byte[] Compact(int value)
{
    var bytes = new List<byte>();
    bool negative = value < 0;
    uint magnitude = (uint)Math.Abs((long)value);
    byte first = (byte)(magnitude & 0x3f);
    if (negative) first |= 0x80;
    magnitude >>= 6;
    if (magnitude != 0) first |= 0x40;
    bytes.Add(first);
    while (magnitude != 0)
    {
        byte next = (byte)(magnitude & 0x7f);
        magnitude >>= 7;
        if (magnitude != 0) next |= 0x80;
        bytes.Add(next);
    }
    return bytes.ToArray();
}

static byte[] BuildFunctionFixture()
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write(0x9E2A83C1u);
    writer.Write((ushort)126);
    writer.Write((ushort)0);
    writer.Write(0u);
    writer.Write(2); writer.Write(0);
    writer.Write(1); writer.Write(0);
    writer.Write(0); writer.Write(0);
    writer.Write(new byte[16]);
    writer.Write(1);
    writer.Write(1); writer.Write(2);
    writer.Write(new byte[] { 0xAA, 0xBB });
    writer.Write(2);
    writer.Write(new byte[] { 0x3A, 0x47, 0xCC });
    int namesOffset = (int)stream.Position;
    foreach (string name in new[] { "None", "TestFunc" })
    {
        byte[] raw = System.Text.Encoding.Latin1.GetBytes(name + "\0");
        writer.Write(Compact(raw.Length));
        writer.Write(raw);
        writer.Write(0u);
    }
    int exportOffset = (int)stream.Position;
    writer.Write(Compact(0));
    writer.Write(Compact(0));
    writer.Write(0);
    writer.Write(Compact(1));
    writer.Write(0u);
    writer.Write(Compact(9));
    writer.Write(Compact(64));
    byte[] bytes = stream.ToArray();
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), 2);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), namesOffset);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20, 4), 1);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24, 4), exportOffset);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28, 4), 0);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32, 4), exportOffset);
    return bytes;
}

static byte[] BuildFixture()
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write(0x9E2A83C1u);
    writer.Write((ushort)126);
    writer.Write((ushort)0);
    writer.Write(0u);
    writer.Write(2); writer.Write(0); // name count/offset
    writer.Write(1); writer.Write(0); // export count/offset
    writer.Write(0); writer.Write(0); // import count/offset
    writer.Write(new byte[16]);       // GUID
    writer.Write(1);                  // generation count
    writer.Write(1); writer.Write(2); // exports, names in generation

    // Opaque export payload remains at its original absolute offset.
    writer.Write(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
    int namesOffset = (int)stream.Position;
    foreach (string name in new[] { "None", "TestClass" })
    {
        byte[] raw = System.Text.Encoding.Latin1.GetBytes(name + "\0");
        writer.Write(Compact(raw.Length));
        writer.Write(raw);
        writer.Write(0u);
    }
    int exportOffset = (int)stream.Position;
    writer.Write(Compact(0)); // class ref
    writer.Write(Compact(0)); // super ref
    writer.Write(0);          // outer ref
    writer.Write(Compact(1)); // name index
    writer.Write(0u);         // object flags
    writer.Write(Compact(4));// serial size
    writer.Write(Compact(64));// serial offset

    byte[] bytes = stream.ToArray();
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), 2);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), namesOffset);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20, 4), 1);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24, 4), exportOffset);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28, 4), 0);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32, 4), exportOffset);
    return bytes;
}

foreach (int sample in new[] { 0, 1, -1, 63, 64, -64, 8191, 8192, int.MaxValue, -int.MaxValue })
{
    byte[] encoded = Compact(sample);
    int cursor = 0;
    int decoded = UnrealPackageIndex.DecodeCompactIndex(encoded, ref cursor);
    Assert(decoded == sample && cursor == encoded.Length, $"Compact index mismatch for {sample}.");
}

bool rejectedMalformed = false;
try
{
    int cursor = 0;
    UnrealPackageIndex.DecodeCompactIndex(new byte[] { 0x40, 0x80, 0x80, 0x80, 0x80 }, ref cursor);
}
catch (InvalidDataException) { rejectedMalformed = true; }
Assert(rejectedMalformed, "Malformed five-byte compact index was accepted.");

byte[] fixture = BuildFixture();
var parsed = UnrealPackageIndex.Parse(fixture);
byte[] functionFixture = BuildFunctionFixture();
var functionPackage = UnrealPackageIndex.Parse(functionFixture);
var compileResult = UnrealScriptCompilerPipeline.CompileClassSource(
    "class TestFunc extends Object; function int TestFunc() { return 5;",
    functionPackage,
    new[] { new UnrealFunctionTarget("TestFunc", 1, 6, 2) });
Assert(compileResult.FunctionsCompiled == 1, "Native compiler did not compile the function.");
Assert(compileResult.Repairs.Count == 1 && compileResult.Repairs[0].ClosuresInserted == 1,
    "Native compiler did not report its source-closure repair.");
var compiledPackage = UnrealPackageIndex.Parse(compileResult.PackageBytes);
var compiledExport = compiledPackage.Exports[0];
byte[] compiledPayload = compileResult.PackageBytes.AsSpan(
    compiledExport.SerialOffset, compiledExport.SerialSize).ToArray();
Assert(compiledExport.SerialSize == 14, "Compiled function export size is incorrect.");
Assert(BinaryPrimitives.ReadInt32LittleEndian(compiledPayload.AsSpan(2, 4)) == 7,
    "Compiled function bytecode size was not updated.");
Assert(compiledPayload.AsSpan(6, 7).SequenceEqual(
    new byte[] { 0x04, 0x1D, 5, 0, 0, 0, 0x47 }),
    "Native compiler emitted incorrect return bytecode.");
Assert(compiledPayload[13] == 0xCC, "Compiled function suffix data was not preserved.");
Assert(UnrealScriptFunctionDecompiler.DecompileExport(compiledPackage, 1, 6, 7) == "return 5;",
    "Native bytecode decompiler did not recover the compiled function source.");
Assert(UnrealScriptFunctionDecompiler.DecompileReturnOnly(
    UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return;")) == "return;",
    "Void source/bytecode round trip failed.");
Assert(UnrealScriptFunctionDecompiler.DecompileReturnOnly(
    UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return 1.5f;")) == "return 1.5f;",
    "Float source/bytecode round trip failed.");
Assert(UnrealScriptFunctionDecompiler.DecompileReturnOnly(
    UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return \"界\";")) == "return \"界\";",
    "Unicode source/bytecode round trip failed.");
Assert(UnrealScriptFunctionDecompiler.DecompileReturnOnly(
    UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return 'Core';", _ => 7), _ => "Core") == "return 'Core';",
    "Name source/bytecode round trip failed.");
Assert(functionFixture.AsSpan(64, 9).SequenceEqual(
    new byte[] { 0xAA, 0xBB, 2, 0, 0, 0, 0x3A, 0x47, 0xCC }),
    "Native compilation modified the original package bytes.");
foreach (ushort profile in new ushort[] { 111, 121, 123, 126 })
{
    byte[] profileFixture = fixture.ToArray();
    BinaryPrimitives.WriteUInt16LittleEndian(profileFixture.AsSpan(4, 2), profile);
    Assert(UnrealPackageIndex.Parse(profileFixture).Summary.Version == profile,
        $"Classic UE package profile {profile} was not accepted.");
}
Assert(parsed.Summary.Version == 126 && parsed.Summary.LicenseeVersion == 0, "Version profile mismatch.");
Assert(parsed.Summary.ProfileDescription.Contains("UE2 v126/0", StringComparison.Ordinal),
    "Unknown v126/0 profile was incorrectly classified as Lineage II.");
Assert(parsed.Names.SequenceEqual(new[] { "None", "TestClass" }), "Name table mismatch.");
Assert(parsed.Exports.Count == 1 && parsed.Exports[0].SerialOffset == 64, "Export table mismatch.");

byte[] rebuilt = parsed.CreateLosslessRoundTrip();
var reparsed = UnrealPackageIndex.Parse(rebuilt);
Assert(reparsed.Names.SequenceEqual(parsed.Names), "Names changed after round trip.");
Assert(reparsed.Exports.SequenceEqual(parsed.Exports), "Exports changed after round trip.");
Assert(rebuilt.AsSpan(64, 4).SequenceEqual(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }), "Opaque object payload moved or changed.");
Assert(rebuilt.Length > fixture.Length, "Round-trip tables were not appended.");

Assert(UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return;")
    .SequenceEqual(new byte[] { 0x3A, 0x47 }), "Void return bytecode is incorrect.");
Assert(UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return 0;")
    .SequenceEqual(new byte[] { 0x04, 0x25, 0x47 }), "Zero return bytecode is incorrect.");
Assert(UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return 1;")
    .SequenceEqual(new byte[] { 0x04, 0x26, 0x47 }), "One return bytecode is incorrect.");
Assert(UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return 42;")
    .SequenceEqual(new byte[] { 0x04, 0x1D, 42, 0, 0, 0, 0x47 }), "Integer return bytecode is incorrect.");
Assert(UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return -5;")
    .SequenceEqual(new byte[] { 0x04, 0x1D, 0xFB, 0xFF, 0xFF, 0xFF, 0x47 }), "Negative integer return bytecode is incorrect.");
Assert(UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return true;")
    .SequenceEqual(new byte[] { 0x04, 0x27, 0x47 }), "Boolean return bytecode is incorrect.");
Assert(UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return 1.5f;")
    .SequenceEqual(new byte[] { 0x04, 0x1E, 0x00, 0x00, 0xC0, 0x3F, 0x47 }), "Float return bytecode is incorrect.");
Assert(UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return \"界\";")
    .SequenceEqual(new byte[] { 0x04, 0x34, 0x4C, 0x75, 0, 0, 0x47 }), "Unicode string return bytecode is incorrect.");
byte[] pathLiteralCode = UnrealScriptFunctionCompiler.CompileReturnOnlyBody("""return "..\L2text\";""");
byte[] quoteLiteralCode = UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return \"\"\";");
Assert(UnrealScriptFunctionDecompiler.DecompileReturnOnly(quoteLiteralCode) == "return \"\"\";",
    "Triple-quote string literal was not preserved.");
Assert(UnrealScriptFunctionDecompiler.DecompileReturnOnly(pathLiteralCode) == """return "..\L2text\";""",
    "A trailing path backslash was not preserved in string bytecode.");
Assert(UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return \"ok // text\"; // trailing comment")
    .SequenceEqual(new byte[] { 0x04, 0x1F, (byte)'o', (byte)'k', (byte)' ', (byte)'/', (byte)'/', (byte)' ', (byte)'t', (byte)'e', (byte)'x', (byte)'t', 0, 0x47 }),
    "String return bytecode or comment handling is incorrect.");
Assert(UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return 'Core';", _ => 7)
    .SequenceEqual(new byte[] { 0x04, 0x21, 7, 0, 0, 0, 0x47 }), "Name return bytecode is incorrect.");

string brokenSource = """
class Broken extends Object;
function First()
{
    if (true)
    {
        return;
function Second()
{
    return;
}
""";
var repairResult = UnrealScriptSourceIndex.RepairMissingFunctionClosures(brokenSource);
Assert(repairResult.Repairs.Count == 1 && repairResult.Repairs[0].ClosuresInserted == 2,
    "Missing function closures were not reconstructed at the next function boundary.");
Assert(UnrealScriptSourceIndex.ParseFunctions(repairResult.Source).Count == 2,
    "Repaired source did not produce two complete function bodies.");

string brokenBeforeDefaults = """
class BrokenDefaults extends Object;
function First()
{
    if (true)
    {
        return;
defaultproperties
{
    TestValue=1
}
""";
var defaultsRepair = UnrealScriptSourceIndex.RepairMissingFunctionClosures(brokenBeforeDefaults);
Assert(defaultsRepair.Repairs.Count == 1 &&
    defaultsRepair.Source.IndexOf("defaultproperties", StringComparison.Ordinal) >
    defaultsRepair.Source.IndexOf("}", defaultsRepair.Source.IndexOf("return;", StringComparison.Ordinal), StringComparison.Ordinal),
    "Missing function braces were not inserted before the defaultproperties section.");

string sampleSource = """
class Sample extends Object;

native function int NativeCall(int A);

function int Add(int A, int B)
{
    // } must not close the function
    local string S;
    S = "}";
    S = "..\L2text\";
    if (A > 0) { return A + B; }
    /* } nested-looking comment */
    return 0;
}

state Idle
{
    event BeginState()
    {
        return;
    }
}
""";
string tripleQuoteSource = "class Sample; function QuoteTest() { Call(\"\"\" ); }";
Assert(UnrealScriptSourceIndex.ParseFunctions(tripleQuoteSource).Count == 1,
    "Triple-quote literal confused the source scanner.");

var indexedFunctions = UnrealScriptSourceIndex.ParseFunctions(sampleSource);
Assert(indexedFunctions.Count == 2, "Function source indexer found an incorrect number of bodies.");
Assert(indexedFunctions[0].Body.Contains("L2text") && indexedFunctions[0].Body.Contains("\\"),
    "A path string ending in a backslash was not scanned as a closed string literal.");
Assert(indexedFunctions[0].Name == "Add" && indexedFunctions[0].Body.Contains("return A + B"),
    "Function source indexer failed to extract the Add body.");
Assert(indexedFunctions[1].Name == "BeginState" && indexedFunctions[1].Body.Contains("return;"),
    "Function source indexer failed to extract a state event body.");

byte[] originalFunctionPayload = { 0xAA, 0xBB, 3, 0, 0, 0, 0x10, 0x20, 0x30, 0xCC, 0xDD };
byte[] newFunctionCode = { 0x99, 0x88 };
byte[] patchedFunctionPayload = UnrealScriptPatcher.ReplaceBytecode(
    originalFunctionPayload, 126, 6, 3, newFunctionCode);
Assert(patchedFunctionPayload.AsSpan(0, 2).SequenceEqual(new byte[] { 0xAA, 0xBB }),
    "Function prefix changed during bytecode replacement.");
Assert(BinaryPrimitives.ReadInt32LittleEndian(patchedFunctionPayload.AsSpan(2, 4)) == 2,
    "Function bytecode size was not updated.");
Assert(patchedFunctionPayload.AsSpan(6, 2).SequenceEqual(newFunctionCode),
    "Function bytecode was not replaced.");
Assert(patchedFunctionPayload.AsSpan(8).SequenceEqual(new byte[] { 0xCC, 0xDD }),
    "Function suffix changed during bytecode replacement.");

var replacementBuilder = new UnrealPackageBuilder(parsed);
byte[] replacementPayload = { 0xCA, 0xFE, 0xBA, 0xBE, 0x01, 0x02 };
replacementBuilder.ReplaceExportPayload(1, replacementPayload);
byte[] replacedBytes = replacementBuilder.Build();
var replacedPackage = UnrealPackageIndex.Parse(replacedBytes);
Assert(replacedPackage.Exports[0].SerialSize == replacementPayload.Length, "Replacement export size is incorrect.");
Assert(replacedBytes.AsSpan(replacedPackage.Exports[0].SerialOffset, replacementPayload.Length)
    .SequenceEqual(replacementPayload), "Replacement export payload is incorrect.");
Assert(replacedBytes.AsSpan(64, 4).SequenceEqual(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }),
    "Original payload bytes were not preserved in the append-only replacement.");

var builder = new UnrealPackageBuilder(parsed);
int newObjectName = builder.AddName("界面");
int coreName = builder.AddName("Core");
int className = builder.AddName("Class");
int objectName = builder.AddName("Object");
int importedClass = builder.AddImport(coreName, className, 0, objectName);
int newExportRef = builder.AddExport(importedClass, 0, 0, newObjectName, 0x00000001,
    new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 });
byte[] expanded = builder.Build();
var expandedPackage = UnrealPackageIndex.Parse(expanded);
Assert(newExportRef == 2, "New export reference is incorrect.");
Assert(expandedPackage.Summary.NameCount == 6, "New names were not appended.");
Assert(expandedPackage.Summary.ImportCount == 1, "New import was not appended.");
Assert(expandedPackage.Summary.ExportCount == 2, "New export was not appended.");
Assert(expandedPackage.Names[2] == "界面", "UTF-16 name was not preserved.");
var added = expandedPackage.Exports[1];
Assert(added.ClassRef == -1 && added.SerialSize == 5, "New export metadata is incorrect.");
Assert(expanded.AsSpan(added.SerialOffset, added.SerialSize).SequenceEqual(
    new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 }), "New export payload is incorrect.");
Assert(expanded.AsSpan(64, 4).SequenceEqual(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }),
    "Existing object payload moved or changed.");

Console.WriteLine("PASS: UE2 package parsing, lossless round-trip, and append-only builder.");

if (args.Length > 0)
{
    string packagePath = args[0];
    var realPackage = UnrealPackageIndex.Open(packagePath);
    byte[] realRoundTripBytes = realPackage.CreateLosslessRoundTrip();
    var realRoundTrip = UnrealPackageIndex.Parse(realRoundTripBytes);
    Assert(realRoundTrip.Names.SequenceEqual(realPackage.Names), "Real package name table changed during round trip.");
    Assert(realRoundTrip.Imports.SequenceEqual(realPackage.Imports), "Real package import table changed during round trip.");
    Assert(realRoundTrip.Exports.SequenceEqual(realPackage.Exports), "Real package export table changed during round trip.");
    Console.WriteLine($"PASS: real package {packagePath}; {realPackage.Summary.ProfileDescription}; names={realPackage.Summary.NameCount}, imports={realPackage.Summary.ImportCount}, exports={realPackage.Summary.ExportCount}.");

    using var metadataPackage = UnrealLoader.LoadPackage(packagePath, FileAccess.Read);
    metadataPackage.InitializePackage();
    var packageClasses = metadataPackage.Objects.OfType<UClass>()
        .Where(item => (int)item > 0)
        .ToDictionary(item => item.Name.ToString(), StringComparer.OrdinalIgnoreCase);
    var packageFunctions = packageClasses.Values.SelectMany(EnumerateClassFunctions).ToArray();
    byte[] packageFileBytes = File.ReadAllBytes(packagePath);
    int checkedFunctionOffsets = 0, invalidFunctionOffsets = 0;
    foreach (var function in packageFunctions)
    {
        if (function.ExportTable == null) { invalidFunctionOffsets++; continue; }
        long relative = function.ScriptOffset;
        int exportRef = (int)function;
        if (exportRef < 1 || exportRef > realPackage.Exports.Count ||
            relative < 4 || relative > int.MaxValue)
        { invalidFunctionOffsets++; continue; }
        var exportEntry = realPackage.Exports[exportRef - 1];
        if (relative > exportEntry.SerialSize - 4) { invalidFunctionOffsets++; continue; }
        int sizeField = checked(exportEntry.SerialOffset + (int)relative - 4);
        int virtualSize = BinaryPrimitives.ReadInt32LittleEndian(packageFileBytes.AsSpan(sizeField, 4));
        int actualSize = function.ScriptSize;
        if (actualSize <= 0 || virtualSize < actualSize ||
            relative > exportEntry.SerialSize - actualSize)
        { invalidFunctionOffsets++; continue; }
        checkedFunctionOffsets++;
    }
    if (checkedFunctionOffsets == 0)
        Console.WriteLine($"UFUNCTION OFFSET DIAGNOSTIC: objects={packageFunctions.Length}; invalid={invalidFunctionOffsets}; samples=" +
            string.Join(" | ", packageFunctions.Take(8).Select(function =>
                $"{function.Name}: outer={function.Outer?.Name}, script={function.ScriptOffset}, export={function.ExportTable?.SerialOffset}, index={(int)function}")));
    Assert(checkedFunctionOffsets > 0,
        $"No UFunction bytecode offsets validated (objects={packageFunctions.Length}, invalid={invalidFunctionOffsets}).");
    Console.WriteLine($"UELib metadata bridge: classes={packageClasses.Count}, functions={packageFunctions.Length}, bytecode offsets validated={checkedFunctionOffsets}, functions without a usable serialized bytecode span={invalidFunctionOffsets} (native/declaration-only entries are expected).");

    int nativePackageRoundTrips = 0, nativePackageUnsupported = 0;
    bool realPackageFunctionPatchPassed = false;
    foreach (var function in packageFunctions)
    {
        if (function.ExportTable == null) continue;
        int exportReference = (int)function;
        if (exportReference < 1 || exportReference > realPackage.Exports.Count) continue;
        var export = realPackage.Exports[exportReference - 1];
        long relativeLong = function.ScriptOffset;
        if (relativeLong < 4 || relativeLong > int.MaxValue || relativeLong > export.SerialSize - 4) continue;
        int relative = (int)relativeLong;
        int sizeOffset = export.SerialOffset + relative - 4;
        int virtualSize = BinaryPrimitives.ReadInt32LittleEndian(packageFileBytes.AsSpan(sizeOffset, 4));
        int scriptSize = function.ScriptSize;
        if (scriptSize <= 0 || virtualSize < scriptSize || relative > export.SerialSize - scriptSize) continue;
        try
        {
            byte[] originalCode = packageFileBytes.AsSpan(export.SerialOffset + relative, scriptSize).ToArray();
            string nativeSource = UnrealScriptFunctionDecompiler.DecompileExport(
                realPackage, exportReference, relative, scriptSize);
            int ResolvePackageName(string name)
            {
                for (int index = 0; index < realPackage.Names.Count; index++)
                    if (string.Equals(realPackage.Names[index], name, StringComparison.OrdinalIgnoreCase))
                        return index;
                throw new InvalidDataException($"Name not found: {name}");
            }
            byte[] rebuiltCode = UnrealScriptFunctionCompiler.CompileReturnOnlyBody(nativeSource, ResolvePackageName);
            if (!rebuiltCode.AsSpan().SequenceEqual(originalCode)) continue;
            nativePackageRoundTrips++;
            if (!realPackageFunctionPatchPassed)
            {
                string syntheticClass = $"class MethosNativeProbe; function {function.Name}() {{ {nativeSource} }}";
                var patched = UnrealScriptCompilerPipeline.CompileClassSource(
                    syntheticClass, realPackage,
                    new[] { new UnrealFunctionTarget(function.Name.ToString(), exportReference, relative, scriptSize, virtualSize) },
                    repairMissingFunctionClosures: false);
                var patchedIndex = UnrealPackageIndex.Parse(patched.PackageBytes);
                Assert(patchedIndex.Exports.Count == realPackage.Exports.Count &&
                    patchedIndex.Imports.Count == realPackage.Imports.Count,
                    "Native compiler could not patch a real Interface.u function without changing package tables.");
                realPackageFunctionPatchPassed = true;
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or EndOfStreamException or ArgumentOutOfRangeException)
        {
            nativePackageUnsupported++;
        }
    }
    Console.WriteLine($"NATIVE PACKAGE BYTECODE: exact return-only round trips={nativePackageRoundTrips}; unsupported bytecode={nativePackageUnsupported}.");
    Console.WriteLine($"REAL PACKAGE BYTECODE PATCH: {(realPackageFunctionPatchPassed ? "PASS" : "no return-only function found")}.");

}

if (args.Length > 1)
{
    string sourceZipPath = args[1];
    using var archive = ZipFile.OpenRead(sourceZipPath);
    var sourceFiles = archive.Entries
        .Where(entry => entry.FullName.EndsWith(".uc", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    using var sourceMetadata = UnrealLoader.LoadPackage(args[0], FileAccess.Read);
    sourceMetadata.InitializePackage();
    var sourceClasses = sourceMetadata.Objects.OfType<UClass>()
        .Where(item => (int)item > 0)
        .ToDictionary(item => item.Name.ToString(), StringComparer.OrdinalIgnoreCase);
    var sourcePackageFunctions = sourceClasses.Values.SelectMany(EnumerateClassFunctions).ToArray();
    var sourcePackageIndex = UnrealPackageIndex.Open(args[0]);
    byte[] sourcePackageBytes = File.ReadAllBytes(args[0]);
    int sourceClassMatches = 0, missingClassFiles = 0, missingFunctionMappings = 0, duplicateFunctionTargets = 0;
    int verifiedNativeRoundTrips = 0, unsupportedNativeFunctions = 0;
    bool realPackageFunctionPatchPassed = false;
    var missingClassExamples = new List<string>();
    var missingFunctionExamples = new List<string>();
    var duplicateFunctionExamples = new List<string>();
    var nativeUnsupportedExamples = new List<string>();
    var nativeMismatchExamples = new List<string>();
    foreach (var entry in sourceFiles)
    {
        string sourceClassName = Path.GetFileNameWithoutExtension(entry.Name);
        if (!sourceClasses.TryGetValue(sourceClassName, out var classObject))
        {
            missingClassFiles++;
            missingClassExamples.Add(sourceClassName);
            continue;
        }
        sourceClassMatches++;
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true);
        string sourceText = reader.ReadToEnd();
        var functionsInSource = UnrealScriptSourceIndex.ParseFunctions(
            UnrealScriptSourceIndex.RepairMissingFunctionClosures(sourceText).Source);
        var ownedFunctions = new List<UFunction>();
        var ownedNames = new List<string>();
        foreach (var function in sourcePackageFunctions)
        {
            for (UELib.Core.UObject? outer = function.Outer; outer != null; outer = outer.Outer)
            {
                if (ReferenceEquals(outer, classObject))
                {
                    ownedNames.Add(function.Name.ToString());
                    ownedFunctions.Add(function);
                    break;
                }
            }
        }
        var duplicateGroups = ownedNames.GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).ToArray();
        duplicateFunctionTargets += duplicateGroups.Length;
        duplicateFunctionExamples.AddRange(duplicateGroups.Select(group => sourceClassName + "." + group.Key));
        var targetNames = ownedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unmatchedFunctions = functionsInSource.Where(function => !targetNames.Contains(function.Name)).ToArray();
        missingFunctionMappings += unmatchedFunctions.Length;
        missingFunctionExamples.AddRange(unmatchedFunctions.Take(5).Select(function => sourceClassName + "." + function.Name));

        foreach (var sourceFunction in functionsInSource)
        {
            var matches = ownedFunctions.Where(function =>
                string.Equals(function.Name.ToString(), sourceFunction.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1 || matches[0].ExportTable == null) continue;
            var function = matches[0];
            int exportReference = (int)function;
            if (exportReference < 1 || exportReference > sourcePackageIndex.Exports.Count) continue;
            var export = sourcePackageIndex.Exports[exportReference - 1];
            long relativeLong = function.ScriptOffset;
            if (relativeLong < 4 || relativeLong > int.MaxValue || relativeLong > export.SerialSize - 4) continue;
            int relative = (int)relativeLong;
            int sizeOffset = export.SerialOffset + relative - 4;
            int virtualSize = BinaryPrimitives.ReadInt32LittleEndian(sourcePackageBytes.AsSpan(sizeOffset, 4));
            int scriptSize = function.ScriptSize;
            if (scriptSize <= 0 || virtualSize < scriptSize || relative > export.SerialSize - scriptSize) continue;
            try
            {
                byte[] originalCode = sourcePackageBytes.AsSpan(export.SerialOffset + relative, scriptSize).ToArray();
                string nativeSource = UnrealScriptFunctionDecompiler.DecompileExport(
                    sourcePackageIndex, exportReference, relative, scriptSize);
                int ResolveName(string name)
                {
                    for (int nameIndex = 0; nameIndex < sourcePackageIndex.Names.Count; nameIndex++)
                        if (string.Equals(sourcePackageIndex.Names[nameIndex], name, StringComparison.OrdinalIgnoreCase))
                            return nameIndex;
                    throw new InvalidDataException($"Name not found: {name}");
                }
                byte[] sourceCode = UnrealScriptFunctionCompiler.CompileReturnOnlyBody(sourceFunction.Body, ResolveName);
                byte[] nativeCode = UnrealScriptFunctionCompiler.CompileReturnOnlyBody(nativeSource, ResolveName);
                if (sourceCode.AsSpan().SequenceEqual(originalCode) &&
                    nativeCode.AsSpan().SequenceEqual(originalCode))
                {
                    verifiedNativeRoundTrips++;
                    if (!realPackageFunctionPatchPassed)
                    {
                        string minimalClassSource = $"class {sourceClassName}; function {sourceFunction.Name}() {{ {sourceFunction.Body} }}";
                        var nativeCompile = UnrealScriptCompilerPipeline.CompileClassSource(
                            minimalClassSource, sourcePackageIndex,
                            new[] { new UnrealFunctionTarget(sourceFunction.Name, exportReference, relative, scriptSize, virtualSize) },
                            repairMissingFunctionClosures: false);
                        var rebuiltPackage = UnrealPackageIndex.Parse(nativeCompile.PackageBytes);
                        Assert(rebuiltPackage.Exports.Count == sourcePackageIndex.Exports.Count &&
                            rebuiltPackage.Imports.Count == sourcePackageIndex.Imports.Count,
                            "Native compilation changed the package tables unexpectedly.");
                        realPackageFunctionPatchPassed = true;
                    }
                }
                else if (nativeMismatchExamples.Count < 20)
                    nativeMismatchExamples.Add($"{sourceClassName}.{sourceFunction.Name}: source=[{sourceFunction.Body.Trim()}], native=[{nativeSource.Trim()}], bytes={originalCode.Length}");
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or EndOfStreamException or ArgumentOutOfRangeException)
            {
                unsupportedNativeFunctions++;
                if (nativeUnsupportedExamples.Count < 20)
                    nativeUnsupportedExamples.Add($"{sourceClassName}.{sourceFunction.Name}: {ex.Message}");
            }
        }
    }
    Console.WriteLine($"SOURCE MAP: matched classes={sourceClassMatches}/{sourceFiles.Length}; missing class files={missingClassFiles}; source functions without package target={missingFunctionMappings}; duplicate target names={duplicateFunctionTargets}.");
    Console.WriteLine($"NATIVE BYTECODE ROUNDTRIP: exact source+decompiler matches={verifiedNativeRoundTrips}; unsupported or non-literal functions={unsupportedNativeFunctions}.");
    Console.WriteLine($"REAL PACKAGE FUNCTION PATCH: {(realPackageFunctionPatchPassed ? "PASS" : "no compatible function found in supplied sources")}.");
    Console.WriteLine("MISSING CLASS FILES: " + string.Join(", ", missingClassExamples.Take(30)));
    Console.WriteLine("MISSING FUNCTION TARGETS: " + string.Join(", ", missingFunctionExamples.Take(30)));
    Console.WriteLine("DUPLICATE FUNCTION TARGETS: " + string.Join(", ", duplicateFunctionExamples.Take(30)));
    Console.WriteLine("NATIVE UNSUPPORTED SAMPLES: " + string.Join(" | ", nativeUnsupportedExamples));
    Console.WriteLine("NATIVE BYTECODE MISMATCH SAMPLES: " + string.Join(" | ", nativeMismatchExamples));
    int totalIndexedFunctions = 0;
    int repairedFunctions = 0;
    int insertedClosures = 0;
    var sourceErrors = new List<string>();
    foreach (var entry in sourceFiles)
    {
        try
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true);
            string sourceText = reader.ReadToEnd();
            var repairedSource = UnrealScriptSourceIndex.RepairMissingFunctionClosures(sourceText);
            totalIndexedFunctions += UnrealScriptSourceIndex.ParseFunctions(repairedSource.Source).Count;
            repairedFunctions += repairedSource.Repairs.Count;
            insertedClosures += repairedSource.Repairs.Sum(repair => repair.ClosuresInserted);
        }
        catch (Exception ex)
        {
            sourceErrors.Add($"{entry.FullName}: {ex.Message}");
        }
    }
    Assert(sourceFiles.Length > 0, "No .uc source files were found in the source archive.");
    Assert(sourceErrors.Count == 0, "Source indexing errors: " + string.Join(" | ", sourceErrors.Take(10)));
    Console.WriteLine($"PASS: scanned {sourceFiles.Length} .uc files; located {totalIndexedFunctions} function bodies; repaired {repairedFunctions} incomplete functions with {insertedClosures} closing braces.");
}
