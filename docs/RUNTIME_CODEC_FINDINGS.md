# Native Lineage II runtime codec findings

Analysis date: 2026-11-11. Static inspection only; no executable or DLL was run or modified.

The supplied L2 Sudamérica runtime files are all 32-bit x86 PE32:
- `l2.exe`: 491,540 bytes; SHA-256 `5967D5F6221F091D5B557D27F9A40BFB31BA2BA965B57D9B42F94F9B4FEB5707`
- `Core.dll`: 1,372,180 bytes; SHA-256 `D83449B1CDF0AC717A98BE9289CAAB03CB507324A696EF449E31369E28416639`
- `engine.dll`: 30,741,504 bytes; SHA-256 `FE4135724D552FB8B0117FB6C9A661252CA86F1822E275632A571BD661F1F98E`

## Confirmed symbols

`l2.exe` imports `Core.dll` and `Engine.dll`, and imports from Core:
- `FCodec::Decode` and `FCodec::Encode`
- `FArchive::GetCurrentEncryptVersion`
- `GL2DESCodec`

`engine.dll` also imports `FCodec::Decode`, `FCodec::Encode`, and `FArchive::GetCurrentEncryptVersion` from Core.dll. It imports Kernel32 file-I/O functions including `ReadFile`, `CreateFileA`, and `CreateFileW`.

Core.dll exports include:
- `FBlowFish::BlowfishDecrypt` / `BlowfishEncrypt`
- `FBlowFish::Blowfish_decipher` / `Blowfish_encipher`
- multiple `FCodec::Decode` / `FCodec::Encode` overloads
- `GL2CurrentTxtCodec`, `GL2DESCodec`, `GL2UseKeyCrypt`
- `FArchive::GetCurrentEncryptVersion`

The l2.exe RTTI/string data contains `FL2RSACodec`, `FL2DESCodec`, `FL2FileDecryptArchive`, and `FL2FileEncryptArchive`.

## Interpretation and limits

This confirms native Lineage II archive/codec implementations exist in the supplied runtime. It does not yet establish which codec or archive class is selected for a particular `.u` or `.xdat` file, nor the exact order relative to the previously identified SmartCrypt/NProtect outer wrapper.

Do not treat a SmartCrypt capture as a fully decoded file until the output validates as a real Unreal package or XDAT structure. No algorithm has yet been fully reverse-engineered and no successful decode using these native routines has yet been validated.

## Next steps

Map references/call sites for the archive classes and codec selectors, then trace each target format independently. Build a staged pipeline that preserves the original input and validates each intermediate output.
