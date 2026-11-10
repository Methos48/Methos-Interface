using System.Buffers.Binary;
using System.IO.Compression;
using MethosUEngine;
using UELib;
using UELib.Core;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
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
byte[] localVariableCode = UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return LocalValue;",
    variableResolver: _ => new UnrealScriptVariableReference(0x00, 12));
Assert(UnrealScriptFunctionDecompiler.DecompileReturnOnly(localVariableCode,
    objectResolver: reference => reference == 12 ? "LocalValue" : throw new InvalidDataException()) == "return LocalValue;",
    "Local variable source/bytecode round trip failed.");
byte[] instanceVariableCode = UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return Health;",
    variableResolver: _ => new UnrealScriptVariableReference(0x01, 27));
Assert(UnrealScriptFunctionDecompiler.DecompileReturnOnly(instanceVariableCode,
    objectResolver: reference => reference == 27 ? "Health" : throw new InvalidDataException()) == "return Health;",
    "Instance variable source/bytecode round trip failed.");
byte[] defaultVariableCode = UnrealScriptFunctionCompiler.CompileReturnOnlyBody("return default.Health;",
    variableResolver: _ => new UnrealScriptVariableReference(0x02, 27));
Assert(UnrealScriptFunctionDecompiler.DecompileReturnOnly(defaultVariableCode,
    objectResolver: reference => reference == 27 ? "Health" : throw new InvalidDataException()) == "return default.Health;",
    "Default variable source/bytecode round trip failed.");
byte[] assignmentCode = UnrealScriptFunctionCompiler.CompileReturnOnlyBody(
    "Health = LocalValue; return Health;",
    variableResolver: name => name.Equals("LocalValue", StringComparison.OrdinalIgnoreCase)
        ? new UnrealScriptVariableReference(0x00, 12)
        : new UnrealScriptVariableReference(0x01, 27));
string assignmentSource = UnrealScriptFunctionDecompiler.DecompileReturnOnly(assignmentCode,
    objectResolver: reference => reference switch { 12 => "LocalValue", 27 => "Health", _ => throw new InvalidDataException() });
Assert(assignmentSource.Replace("\\r\\n", "\\n") == "Health = LocalValue;\\nreturn Health;",
    "Variable assignment and return source/bytecode round trip failed.");
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
byte[] pathLiteralCode = UnrealScriptFunctionCompiler.CompileReturnOnlyBody("""return "..\\L2text\\";""");
byte[] quoteLiteralCode = UnrealScriptFunctionCompiler.CompileReturnOnlyBody("""return "\"";""");
Assert(UnrealScriptFunctionDecompiler.DecompileReturnOnly(quoteLiteralCode) == """return "\"";""",
    "Triple-quote string literal was not preserved.");
Assert(UnrealScriptFunctionDecompiler.DecompileReturnOnly(pathLiteralCode) == """return "..\\L2text\\";""",
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

string sampleSource = """
class Sample extends Object;

native function int NativeCall(int A);

function int Add(int A, int B)
{
    // } must not close the function
    local string S;
    S = "}";
    S = "..\\L2text\\";
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
string tripleQuoteSource = """class Sample; function QuoteTest() { Call("\\""); }""";
Assert(UnrealScriptSourceIndex.ParseFunctions(tripleQuoteSource).Count == 1,
    "Triple-quote literal confused the source scanner.");

var indexedFunctions = UnrealScriptSourceIndex.ParseFunctions(sampleSource);
Assert(indexedFunctions.Count == 2, "Function source indexer found an incorrect number of bodies.");
Assert(indexedFunctions[0].Body.Contains("L2text") && indexedFunctions[0].Body.Contains("\\\\"),
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

    using (var uelibPackage = UnrealLoader.LoadPackage(packagePath, FileAccess.Read))
    {
        uelibPackage.InitializePackage();
        var realClasses = uelibPackage.Objects.OfType<UClass>().Where(item => (int)item > 0).ToArray();
        int decompiledClasses = 0, realFunctions = 0, sourceRepairs = 0, repairedBraces = 0;
        int functionsWithBytecode = 0, validFunctionOffsets = 0, invalidFunctionOffsets = 0;
        var decompileErrors = new List<string>();
        foreach (var realClass in realClasses)
        {
            foreach (var function in realClass.EnumerateFields<UFunction>())
            {
                if (function.ScriptSize <= 0) continue;
                functionsWithBytecode++;
                int exportReference = (int)function;
                if (exportReference < 1 || exportReference > realPackage.Exports.Count)
                {
                    invalidFunctionOffsets++;
                    continue;
                }
                var export = realPackage.Exports[exportReference - 1];
                long relativeOffset = function.ScriptOffset - export.SerialOffset;
                if (relativeOffset >= 4 && relativeOffset <= export.SerialSize - function.ScriptSize)
                    validFunctionOffsets++;
                else
                    invalidFunctionOffsets++;
            }
            try
            {
                string decompiled = realClass.Decompile();
                var repaired = UnrealScriptSourceIndex.RepairMissingFunctionClosures(decompiled);
                realFunctions += UnrealScriptSourceIndex.ParseFunctions(repaired.Source).Count;
                sourceRepairs += repaired.Repairs.Count;
                repairedBraces += repaired.Repairs.Sum(item => item.ClosuresInserted);
                decompiledClasses++;
            }
            catch (Exception ex)
            {
                decompileErrors.Add($"{realClass.Name}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        Assert(realClasses.Length > 0, "UELib found no classes in the real package.");
        Assert(decompileErrors.Count == 0, "Real package decompilation errors: " + string.Join(" | ", decompileErrors.Take(8)));
        Assert(decompiledClasses == realClasses.Length, "Not all real package classes were decompiled.");
        Assert(functionsWithBytecode > 0 && validFunctionOffsets == functionsWithBytecode && invalidFunctionOffsets == 0,
            $"Function bytecode offsets do not map safely into package exports: total={functionsWithBytecode}, valid={validFunctionOffsets}, invalid={invalidFunctionOffsets}.");
        Console.WriteLine($"PASS: real UELib differential decompilation; classes={decompiledClasses}, source functions={realFunctions}, repaired functions={sourceRepairs}, inserted braces={repairedBraces}, bytecode functions={functionsWithBytecode}, verified offsets={validFunctionOffsets}.");
    }
}

if (args.Length > 1)
{
    string sourceZipPath = args[1];
    using var archive = ZipFile.OpenRead(sourceZipPath);
    var sourceFiles = archive.Entries
        .Where(entry => entry.FullName.EndsWith(".uc", StringComparison.OrdinalIgnoreCase))
        .ToArray();
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
