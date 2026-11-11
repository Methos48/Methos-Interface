# FL2DESCodec implementation evidence — step 6

Date: 2026-11-11  
Input: exact uploaded `l2.exe`. Static disassembly only.

## Confirmed concrete DES implementation

The static initializer at VA `0x10954AF0` constructs an object at `0x10990AF0` by calling the constructor at `0x109094D0`. That constructor installs the `FL2DESCodec` vftable (`0x10958EE0`) and populates internal lookup/permutation tables.

The first table begins with the sequence:

```
58, 50, 42, 34, 26, 18, 10, 2,
60, 52, 44, 36, 28, 20, 12, 4, ...
```

This matches the standard DES Initial Permutation table. The same constructor populates further permutation and substitution tables; the later arrays include the characteristic DES S-box values. This is direct implementation evidence, not merely the class name.

The `FL2DESCodec` vftable dispatches to local code at:
- `0x10906530`: processes input in 8-byte blocks and calls a per-block transform.
- `0x10906580`: archive/buffer-oriented processing that also works in 8-byte blocks.

The per-block call path leads through local thunk `0x10901041` to code at `0x109062B0`. This establishes a native 64-bit-block DES-like transform associated with `FL2DESCodec`.

## Global codec object

The static initializer constructs the object at `0x10990AF0`; another initialization path assigns that address to the imported global `GL2DESCodec`. This connects the concrete `FL2DESCodec` instance to the version-311 dispatch found in the archive code.

## Still unresolved

- The exact key schedule/key material used for the actual file path.
- Whether the mode is ECB, CBC, or a custom DES wrapper; 8-byte block processing alone does not settle the mode.
- Exact padding, byte order, and whether all 311/411–413 versions share the same DES parameters.
- The matching read-side inverse and proof that both uploaded `.u` and `.xdat` files use this codec.

Do not yet implement a DES decryptor using a guessed key or mode. The next target is the caller that supplies key material and the archive methods that feed blocks into this object.