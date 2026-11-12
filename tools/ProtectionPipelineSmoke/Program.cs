using System.Text;
using MethosUEngine;

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}

byte[] smartSample = Convert.FromHexString("FFEDFACE").Concat(new byte[0x400 - 4]).ToArray();
var smart = ProtectionPipelinePlanner.Inspect(smartSample, "interface.u");
Check(smart.Status == "recognized-key-required" && !smart.CanDecode && !smart.CanEncode,
    "SmartCrypt sample must be identified as a candidate, never falsely reported as decrypted.");

byte[] l2 = Encoding.Unicode.GetBytes("Lineage2Ver413");
var lineage = ProtectionPipelinePlanner.Inspect(l2, "systemmsg-e.dat");
Check(lineage.Status == "recognized-supported-encryption" && lineage.CanDecode && lineage.CanEncode,
    "Lineage2Ver413 must be identified as a supported codec.");

var codec413 = new Lineage2StandardCodec(413);
byte[] knownPayload413 = Encoding.UTF8.GetBytes("Methos protocol 413 round-trip verification.");
byte[] encrypted413 = codec413.Encode(knownPayload413, "systemmsg-e.dat");
byte[] decoded413 = codec413.Decode(encrypted413, "systemmsg-e.dat");
Check(decoded413.AsSpan().SequenceEqual(knownPayload413),
    "Lineage2Ver413 encode/decode round-trip failed.");

var unknown = ProtectionPipelinePlanner.Inspect(new byte[] { 0x12, 0x34, 0x56 }, "unknown.bin");
Check(unknown.Status == "unknown-or-unsupported" && !unknown.CanDecode,
    "Unknown files must fail closed.");

byte[] unreal = new byte[] { 0xC1, 0x83, 0x2A, 0x9E, 0, 0 };
var package = ProtectionPipelinePlanner.Inspect(unreal, "Interface.u");
Check(package.Status == "plain-format-candidate" && package.Layers[0].Kind == ProtectionLayerKind.UnrealPackage,
    "Unreal package signature was not recognized.");

Console.WriteLine("PASS: SmartCrypt candidate, Lineage II header, unknown fail-closed, and Unreal signature checks.");
