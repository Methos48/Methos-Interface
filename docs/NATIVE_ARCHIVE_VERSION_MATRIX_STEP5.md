# Native archive version dispatch matrix — step 5

Date: 2026-11-11  
Input: exact uploaded `l2.exe`; static x86 disassembly.

## Version values found in archive dispatch

Several methods in the `FL2FileDecryptArchive` implementation (code around VA `0x1090B8A0` through `0x1090BB2D`) apply the same version gates to the version field at object offset `+0x3C`.

The branches explicitly distinguish:
- `0` — unversioned/default path
- `111` (0x6F)
- `121` (0x79)
- `211` (0xD3)
- `212` (0xD4)
- `311` (0x137)
- `411` (0x19B)
- `412` (0x19C)
- `413` (0x19D)

The 411–413 family is recognized by subtracting 411 and accepting offsets 0 through 2. The 211–212 family is recognized as the inclusive range 211–212. Version 311 has an explicit equality branch. Values outside the recognized sets generally return without taking those codec/archive dispatch paths.

No explicit 414 (0x19E) comparison was found in the inspected archive dispatch routines. This is a finding about these exact routines in this client build, not proof that no unrelated subsystem ever handles 414.

## Meaning

The earlier 18-version generic detector in Methos is a broad signature/version candidate list; it must not be confused with the supported native version matrix of this particular client. This client build visibly contains archive dispatch logic for the values listed above. Further work must map each version to the actual transformation and verify whether the same logic applies to both `.u` and `.xdat`.

## Next

- Map each accepted version to the exact virtual method and concrete codec implementation.
- Trace the `GL2DESCodec` object's actual constructor and dispatch target.
- Locate the read-side inverse of the write-side routine and run controlled round-trip tests only after the transformations are fully understood.