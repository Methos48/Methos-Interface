# l2.exe archive RTTI and version-path findings — step 3

Date: 2026-11-11  
Input: exact uploaded `l2.exe` (PE32 x86, ImageBase 0x10900000). Static analysis only; the executable was not run.

## 1. RTTI hierarchy confirmed

MSVC x86 RTTI records identify these concrete classes and their direct bases:

| Concrete class | Direct base |
|---|---|
| `FL2RSACodec` | `FCodec` |
| `FL2DESCodec` | `FCodec` |
| `FL2FileDecryptArchive` | `FArchive` |
| `FL2FileEncryptArchive` | `FArchive` |

This is stronger than merely finding class-name strings: valid x86 Complete Object Locators point to TypeDescriptors and ClassHierarchyDescriptors whose base arrays contain the stated base classes.

## 2. Archive virtual dispatch tables

The two archive classes have separate vftables. Their tables contain many entries that dispatch through imported `FArchive` methods in `Core.dll`, plus local overrides/thunks in `l2.exe`. The codec tables likewise connect to the imported `FCodec` virtual interface. This confirms that the archive layer and codec layer are separate polymorphic objects.

The import table exposes `GL2DESCodec`, `FCodec::Decode` overloads, `FCodec::Encode` overloads, and `FArchive::GetCurrentEncryptVersion`. The mere presence of these imports does not prove which implementation succeeds for every file format.

## 3. Concrete version-dependent archive behavior

A routine associated with the file-archive path at VA `0x1090C070` reads a version field at object offset `+0x38` and branches on the values:

- `0x6F` (111)
- `0x79` (121)
- `0x137` (311)

For version `0x137`, the code loads the global `GL2DESCodec` object and dispatches through its virtual table before writing the processed buffer with Win32 `WriteFile`. For versions `0x6F` and `0x79`, it instead applies a byte-wise XOR loop using a key byte stored at object offset `+0x44C`. Other versions take a different path without that branch's preprocessing.

A constructor associated with `FL2FileDecryptArchive` is present at VA `0x1090B620`. It initializes the decrypt-archive vtable, obtains/opens its underlying archive object, parses a decimal version value with the imported `appAtoi`, and has explicit branches for versions 111 and 121. The 111 branch sets the byte key to `0xAC`; the 121 branch obtains a key byte through a helper using an additional constructor argument. This is evidence of a versioned header/key path, not yet a complete specification of the file format.

## 4. What this establishes — and what it does not

Established:
- The client has native archive wrappers distinct from the codec objects.
- At least one archive path contains explicit handling for versions 111, 121, and 311.
- Version 311 reaches `GL2DESCodec` virtual dispatch.
- Versions 111 and 121 have byte-XOR/key handling in the archive path.
- The decrypt-archive constructor parses a version value and initializes version-specific state.

Not established:
- Whether the exact uploaded `.u` and `.xdat` files use this archive path, SmartCrypt, both layers, or a different path.
- The full header layout, key derivation for version 121, complete version 311 codec implementation, or exact inverse transformation.
- A successful decode/re-encode round trip for either uploaded sample.

## 5. Next targets

1. Trace callers that construct `FL2FileDecryptArchive` and `FL2FileEncryptArchive`, including the file-name/extension decision.
2. Disassemble the helper used to parse/decrypt the archive header and recover the full header structure.
3. Trace the `GL2DESCodec` object construction and the concrete function reached for version 311.
4. Identify the corresponding read/decrypt routine and validate it against the actual uploaded `.u` and `.xdat` files.

No decoder should be implemented from the version checks alone.