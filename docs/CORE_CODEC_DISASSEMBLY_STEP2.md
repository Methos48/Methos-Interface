# Static reverse-engineering step 2 — Core.dll codec entry points

Date: 2026-11-11  
Input: exact Core.dll uploaded to Methos-XDAT. Static disassembly only; no binary execution or modification.

## Important correction: FCodec base methods are not the decoder

The Core.dll export table maps the following FCodec base virtual methods to short jump thunks:
- `Decode(FArchive&, FArchive&)` at RVA 0x480E -> 0x101087E0
- `Decode(FArchive&, BYTE**, INT&)` at RVA 0x2509 -> 0x101087F0

Both targets are tiny default implementations that set EAX to zero and return. The corresponding base Encode methods are also jump-thunk entries. Therefore, the presence of these exported base methods does not establish a usable file decoder. The actual implementation must be in a derived codec class / vtable implementation or another archive layer.

## Blowfish implementation evidence

Core.dll exports:
- `FBlowFish::BlowfishDecrypt` (RVA 0x2482) -> code at VA 0x10150D30
- `FBlowFish::BlowfishEncrypt` (RVA 0x2F90) -> code at VA 0x10150D70

The disassembled wrappers iterate over the input in 8-byte blocks and call lower-level routines. This is consistent with the named Blowfish class and its 64-bit block processing. It establishes that the client includes a real Blowfish implementation, but does not prove that this is the codec selected for interface.u or interface.xdat.

## What remains unknown

- Which concrete codec object is assigned to `GL2DESCodec` or the current codec pointer.
- How `GL2UseKeyCrypt` changes the archive path.
- Whether `FL2FileDecryptArchive` handles .u, .xdat, both, or another file class.
- Whether the SmartCrypt/NProtect wrapper is applied before the native Lineage II archive codec.

## Next reverse-engineering target

Trace concrete codec constructors/vtables and the archive open/read path in this exact client build. Identify call sites for the .u and .xdat readers independently. Until that mapping is established, do not implement a guessed cipher or report a successful decode.
