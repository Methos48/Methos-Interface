# Step 20 — Methos-native encryption engine: implementation decision

Date: 2026-11-12

## Product requirement

The user must be able to import a protected .u or .xdat file, edit it, compile it, and export a file compatible with the target Lineage II client. No manual renaming, CFF Explorer editing, external decryptor, or manual second encryption step should be required.

## Evidence already established

- The exact original interface.u and interface.xdat both begin FF ED FA CE 01 00 and are not directly recognized as Lineage2Ver, ActiveAnticheatCrypt, or Unreal package files.
- The supplied npkscrypt.dll is PE 1.5.2.2 (MD5 5908c3ee24b40f6f08f1500723b1a41d). Its executable payload is packed/high-entropy; a simple static entry-point disassembly is not meaningful.
- ogg.dll imports npkscrypt.dll!Akumu_ICQ_10050005.
- Published SmartDecrypt 1.5.2.2 procedure captures the client-read bytes for a file named SysString-e.dat. Its published sample incorrectly appends nNumberOfBytesToRead rather than the actual successful byte count, and it does not reconstruct non-sequential/overlapped reads safely.
- The published capture output may still contain ordinary Lineage II encryption (e.g. 413/111), so SmartCrypt and the inner L2 codec must be separate pipeline stages.
- No exact decryption or re-encryption has yet been demonstrated for these two original samples.

## Required architecture inside Methos

Create a shared library, e.g. methos_crypto/, used by both U-Editor and XDAT:
- detect.py: format/protection detection; unknown formats must fail closed.
- pipeline.py: ordered protection stages and metadata describing each stage.
- smartcrypt/: implementation of the SmartCrypt 1.5.2.2 adapter after algorithm/key recovery.
- lineage2/: standard L2 codecs selected by verified version (111/121/120/211/212/311/411-413 etc. only where implemented and tested).
- u_codec/: U package parse/build validation.
- xdat_codec/: XDAT parse/build validation.
- tests/: known-answer vectors, round-trip tests, corruption tests, and exact sample regression tests.

A file must carry a protection profile recording the exact layers and version detected on import. Export must apply inverse stages in reverse order. Never guess a key, version, or cipher. Never overwrite the imported original; export to a new path and verify the result.

## How to get the missing SmartCrypt algorithm

Primary route: analyze the exact client runtime and npkscrypt.dll to locate the file transform used by the 1.5.2.2 protection. The runtime is packed, so unpack/devirtualize or observe the loaded module in a controlled Windows test process. Trace the transform at the file-read boundary and identify:
- header fields and their semantics,
- cipher and mode,
- key derivation/key material and whether it is per-file or client/server-specific,
- block/stream state and file offsets,
- inverse encryption path and any integrity/authentication checks.

A corrected client-assisted capture is a temporary diagnostic adapter only. It can extract the exact plaintext stream the client consumes, but it does not meet the final standalone requirement by itself.

If the only available supported route is to call the client's own routines, Methos may package a controlled Windows helper/runtime adapter. That still must be automated behind the Methos UI and must be tested for licensing/redistribution and client compatibility. It must not modify the user's live game installation.

## Acceptance gates (must all pass)

1. Decrypt the user's exact interface.u and independently validate the resulting Unreal package structure.
2. Decrypt the user's exact interface.xdat and independently validate the resulting XDAT structure.
3. Re-encrypt each edited file and confirm the target client accepts it.
4. Round-trip test: decrypt -> encrypt without edits; compare behavior and document expected non-deterministic fields/headers.
5. Confirm U and XDAT are handled independently even if they share an outer protection.
6. Confirm malformed/truncated/wrong-key files fail safely and originals remain unchanged.

## Current status

Architecture decision recorded. SmartCrypt algorithm/key recovery is still the blocking research task. Do not label the project as having a working native SmartCrypt codec until gates 1-3 pass on the exact samples.
