# Envelope classification — step 7

Date: 2026-11-11
Scope: exact uploaded client samples in Methos-XDAT; static evidence only.

## Exact sample fingerprints

The uploaded files remain:
- `interface.u`: 1,241,967 bytes; SHA-256 `89fffcb34b0161033f6d2d1b9c650625cd6144fa575ccf3b170b4c0d5f31902d`
- `interface.xdat`: 705,813 bytes; SHA-256 `7ea4e8830c24b3d803b32429040eb673da87e15c5f83126c4b5c6904825b16bb`

Both begin with bytes `FF ED FA CE 01 00`. This is not the UTF-16LE `Lineage2Ver` header previously identified in the native archive reader. It is also not the literal ASCII `ActiveAnticheatCrypt` marker used by the separately documented AAC container. Therefore neither known envelope should be assumed to explain these samples.

## Important cross-check from public Interlude research

An independently documented retail Interlude client reports:
- `Interface.xdat` is a plaintext serialized widget-layout stream.
- `Interface.u` and `NWindow.u` use `Lineage2Ver111` and are handled by the existing `l2encdec -p 111` path.

This is useful as a baseline only. Its executable and file fingerprints have not been shown to match the uploaded L2Sudamerica client. It does not prove the uploaded files are plaintext or version 111.

## DES codec status

The native `FL2DESCodec` implementation is confirmed in the uploaded `l2.exe`, but no caller/dataflow evidence yet ties it to either uploaded interface file. Do not implement it as their decoder until that link is demonstrated.

## Next required evidence

1. Trace the exact file-open/read path for `interface.u` and `interface.xdat` in this client.
2. Identify which module handles the six-byte prefix and whether it transforms bytes before the Unreal/XDAT parser receives them.
3. Compare the post-envelope bytes against known UE package and XDAT structure invariants.
4. Only then implement decode/encode and require a byte-identical no-edit round trip.

No original binary was modified. No decryption success is claimed.
