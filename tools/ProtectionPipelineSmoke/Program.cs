using System.Text;
using MethosUEngine;

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}

byte[] smartSample = Convert.FromHexString("FFEDFACE0100") .Concat(new byte[32]).ToArray();
var smart = ProtectionPipelinePlanner.Inspect(smartSample, "interface.u");
Check(smart.Status == "recognized-candidate" && !smart.CanDecode && !smart.CanEncode,
    "SmartCrypt sample must be identified as a candidate, never falsely reported as decrypted.");

byte[] l2 = Encoding.Unicode.GetBytes("Lineage2Ver413");
var lineage = ProtectionPipelinePlanner.Inspect(l2, "systemmsg-e.dat");
Check(lineage.Status == "recognized-encryption" && !lineage.CanDecode && !lineage.CanEncode,
    "Lineage2Ver413 must be identified without claiming a codec is available.");

var unknown = ProtectionPipelinePlanner.Inspect(new byte[] { 0x12, 0x34, 0x56 }, "unknown.bin");
Check(unknown.Status == "unknown-or-unsupported" && !unknown.CanDecode,
    "Unknown files must fail closed.");

byte[] unreal = new byte[] { 0xC1, 0x83, 0x2A, 0x9E, 0, 0 };
var package = ProtectionPipelinePlanner.Inspect(unreal, "Interface.u");
Check(package.Status == "plain-format-candidate" && package.Layers[0].Kind == ProtectionLayerKind.UnrealPackage,
    "Unreal package signature was not recognized.");

Console.WriteLine("PASS: SmartCrypt candidate, Lineage II header, unknown fail-closed, and Unreal signature checks.");
