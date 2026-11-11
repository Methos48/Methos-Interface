# Native Lineage2Ver header and key derivation — step 4

Date: 2026-11-11  
Input: exact uploaded `l2.exe` (PE32 x86, ImageBase 0x10900000). Static analysis only.

## 1. Native archive header signature

The decrypt-archive constructor at VA `0x1090B620` reads the initial header as 16-bit values through the archive serialization interface. It compares the first 11 UTF-16 characters against the embedded constant:

```
Lineage2Ver
```

The string is stored UTF-16LE and followed by a null terminator. The code then reads the version text and converts it with the imported `appAtoi`. This directly explains why a previously supplied plaintext sample beginning with `Lineage2Ver111` is consistent with this client's native archive layer.

The adjacent embedded version strings include:
- `111`
- `121`
- `211`
- `311`

These are confirmed strings in the client's header/version data. The precise support/acceptance logic for every value still needs to be traced through all branches.

## 2. Version 111 and 121 key handling

After parsing the version:
- Version 111 sets the archive's byte key to `0xAC`.
- Version 121 calls a helper at VA `0x10906720` using an additional constructor argument.

The helper treats the input as a UTF-16 path, walks backward from its end, stops at a slash or backslash, converts ASCII uppercase letters to lowercase, and adds the remaining UTF-16 character values. The caller stores the low byte of the returned sum as the key byte. This is a filename-derived byte key, based on the basename rather than the directory path.

This finding is based on the actual helper instructions and caller, not on a guessed format convention.

## 3. Version 311

A separate archive processing path checks version `0x137` (311) and dispatches through the global `GL2DESCodec` object before writing the resulting buffer. The 111 and 121 paths use the byte-key XOR handling described in step 3. The full 311 codec implementation and the matching read-side inverse remain under investigation.

## 4. Scope and remaining uncertainty

The native header/key behavior is now substantially clearer, but it does not yet prove that the outer encrypted `.u` and `.xdat` files supplied by the user can be decoded directly with this layer. The current samples begin with a different outer prefix, so a wrapper/protection layer may precede this native `Lineage2Ver` archive format.

Still unknown:
- Whether the exact `.u` and `.xdat` samples both pass through this same archive constructor.
- The exact read-side transform for each version and whether 211 follows a distinct path.
- The concrete implementation and parameters behind the 311 `GL2DESCodec` dispatch.
- Whether SmartCrypt capture is a required first stage for these exact files.

Do not implement a decoder or claim a successful round trip until the read/write paths are paired and tested against the uploaded samples.