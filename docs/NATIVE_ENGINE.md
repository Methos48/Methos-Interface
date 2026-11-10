# Methos U Editor — Native engine notes

## Current architecture

The application is being moved toward an in-process UnrealScript compiler and package writer. UCC is not the target architecture.

The independent engine project is `src/MethosUEngine`:

- `UnrealPackageIndex`: reads classic UE1/UE2 summaries and name/import/export tables, validates compact indices and object ranges, and can rebuild the index tables without moving original object payloads.
- `UnrealPackageBuilder`: append-only package writer. It can add names/imports/exports and replace an existing export payload while keeping the original payload bytes in place.
- `UnrealScriptPatcher`: replaces a UStruct bytecode segment. For versions before 639, it verifies the 32-bit bytecode-size field immediately before the script offset.
- `UnrealScriptSourceIndex`: locates function/event bodies while ignoring braces in comments and literals.
- `UnrealScriptFunctionCompiler`: initial bytecode generator for a deliberately small subset: a single return statement with a literal expression.
- `UnrealScriptCompilerPipeline`: connects the source indexer, initial code generator, bytecode patcher and package writer.

## Important format constraints

- UELib reports `UStruct.ScriptOffset` relative to the serialized object buffer, not the absolute package file. Add the export's `SerialOffset` only when an absolute file offset is needed.
- For package versions below 639, `DataScriptSize` equals `ByteScriptSize`; the script starts immediately after the 32-bit `ByteScriptSize` field.
- The initial bytecode subset targets the classic UE2 token map (e.g. Return `0x04`, IntConst `0x1D`, ReturnNothing `0x3A`, EndOfScript `0x47`). Do not assume this token map for other engine branches without checking their token map.
- The package parser accepts classic package versions 68–194. Acceptance by the generic parser does not prove that a file belongs to Lineage II.

## Lineage II profile caution

Public L2Editor documentation reports native packages at versions 111 and 121, and packages saved by that editor at version 123/licensee 0, including testing across Interlude and other chronicles: https://iturdikulov.com/lineage_2_tools

The supplied `Interface.u` was previously identified as version 126/licensee 0. That is accepted as a generic UE2 package profile, but it is not classified as a standard Lineage II profile without further evidence. The app reports this distinction rather than silently assuming the game profile.

## Verification

The Windows workflow runs `tests/MethosUEngine.Tests` against:

1. Synthetic package fixtures for compact-index boundaries, table round-trips, Unicode names, append-only exports, export replacement, bytecode patching and the initial return compiler.
2. The repository's real `Interface.u`.
3. The repository's `Carpeta interface.u.zip` source archive, scanning all `.uc` files.

The native compiler is **not complete yet**. General expressions, variable/function resolution, control flow, property serialization, full class metadata generation and full package compatibility still need implementation and validation. Unsupported source must fail safely; it must never produce a supposedly successful but incomplete package.
