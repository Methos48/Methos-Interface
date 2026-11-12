# Step 25 — Headless file-read experiment

Date: 2026-11-12

## Exact experiment

A disposable x86 worker was built and run without starting `l2.exe`. It loaded the exact `npkscrypt.dll` from the user's `system.zip`, and in a separate process loaded the exact `ogg.dll` (which imports the npkscrypt success stub). The worker opened a copy of the supplied `interface.u` renamed to `SysString-e.dat` and read it through Win32 `CreateFileW/ReadFile`.

Input:
- Size: 1,241,967 bytes
- SHA-256: `89fffcb34b0161033f6d2d1b9c650625cd6144fa575ccf3b170b4c0d5f31902d`
- Header: `FF ED FA CE 01 00 BC 61 45 50 78 DE A3 D5 9C 12`

Results:
- `npkscrypt.dll` loaded successfully in the worker.
- `ogg.dll` loaded successfully in a separate worker; its import dependency loaded.
- Both ordinary Win32 reads returned exactly 1,241,967 bytes with the same SHA-256 as the input.
- Therefore, loading npkscrypt/ogg by themselves does not trigger the SmartCrypt file transformation in this minimal worker. This is a negative result, not a decryption success.
- The additional Core/engine stack could not yet be validated: `Core.dll` failed to load with Win32 error 126 in this environment. Its import table includes `vorbisfile.dll`; all client DLL dependencies must be resolved before interpreting that failure.

## Consequence

The public SmartCrypt 1.5.2.2 method is not a callable exported cipher routine. The published exports both point to a trivial success stub. The documented extraction path is a capture hook placed before npkscrypt in the `ogg.dll` import order; it observes the client file-read buffer after SmartCrypt processing. The captured output may still contain a second Lineage II encryption layer (e.g. 413/111).

The next required experiment is to build a controlled x86 capture shim that installs CreateFileW/ReadFile hooks before loading the real client DLL chain, then compare captured bytes against the original. Only after this yields a stable intermediate output can the SmartCrypt transform be reverse-engineered or wrapped in a deterministic codec. Do not register a SmartCrypt codec yet.

## Validation

This step validates only:
- the exact DLLs can be loaded independently in a headless process;
- direct file reads remain unchanged without the documented capture/load order;
- the original input remains untouched.

It does not validate SmartCrypt decryption, the L2 second layer, or either file's round-trip.
