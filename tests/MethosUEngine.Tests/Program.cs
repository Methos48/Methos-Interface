using System.Buffers.Binary;
using MethosUEngine;

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

byte[] fixture = BuildFixture();
var parsed = UnrealPackageIndex.Parse(fixture);
Assert(parsed.Summary.Version == 126 && parsed.Summary.LicenseeVersion == 0, "Version profile mismatch.");
Assert(parsed.Names.SequenceEqual(new[] { "None", "TestClass" }), "Name table mismatch.");
Assert(parsed.Exports.Count == 1 && parsed.Exports[0].SerialOffset == 64, "Export table mismatch.");

byte[] rebuilt = parsed.CreateLosslessRoundTrip();
var reparsed = UnrealPackageIndex.Parse(rebuilt);
Assert(reparsed.Names.SequenceEqual(parsed.Names), "Names changed after round trip.");
Assert(reparsed.Exports.SequenceEqual(parsed.Exports), "Exports changed after round trip.");
Assert(rebuilt.AsSpan(64, 4).SequenceEqual(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }), "Opaque object payload moved or changed.");
Assert(rebuilt.Length > fixture.Length, "Round-trip tables were not appended.");

Console.WriteLine("PASS: UE2 package index parse and lossless table round-trip.");
