# Step 24 — Runtime load and unpacked-memory snapshot

Date: 2026-11-12

## Successful headless runtime experiment

A disposable 32-bit Windows process loaded the exact `npkscrypt.dll` extracted from the supplied `system.zip`. No `l2.exe` process was started, no game window was opened, and the exported function was not called.

The observer recorded:

- `LoadLibraryW`: success
- Module base in that run: `0x72E00000`
- `GetProcAddress("Akumu_ICQ_10050005")`: `base + 0x28C0`
- `GetProcAddress("Email_me_at_akumu_dot_ru")`: same address
- `SizeOfImage`: `0x16B000`
- Snapshot: 1,486,848 bytes read from the mapped module.

The process exited normally. The complete mapped image was saved as a workflow artifact for analysis.

## Important new evidence

The PE's first section has virtual size about `0x3B8B7` but raw size zero. In the on-disk DLL it has no section payload; after loading, the mapped section contains executable code. This demonstrates that DLL initialization populates a memory-only code section. The static file alone therefore does not expose all code needed for analysis.

The loaded export at RVA `0x28C0` begins with `mov eax,1; ret`. It is not itself the recovered cipher routine. The large high-entropy section remains largely unchanged after loading, so the unpacked code section is only one part of the protection implementation.

## What this proves and what it does not

Proved:
- A background x86 process can load the exact DLL without launching the game.
- DLL initialization populates a memory-only section.
- The mapped module can be captured for offline reverse engineering.
- The imported export is a tiny success-returning stub, not an identified decryptor.

Not proved:
- That `Akumu_ICQ_10050005` decrypts files.
- The calling convention or arguments for a decryption routine.
- The SmartCrypt transform, key derivation, or inverse transform.
- Successful decryption of either supplied `.u` or `.xdat`.

## Next investigation

Analyze the captured memory-only code section and trace its calls/data into the high-entropy region. Correlate this with `Core.dll`, `engine.dll`, and the file-read path. The eventual Methos runtime should load only the minimum verified dependencies in a disposable worker process, and must not register a codec until exact-sample plaintext validation and round-trip/client acceptance succeed.
