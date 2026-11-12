# Envelope classification — step 7

Date: 2026-11-11
Scope: exact uploaded client samples in Methos-XDAT; static evidence only.

## Exact sample fingerprints

The uploaded files remain:
- `interface.u`: 1,241,967 bytes; SHA-256 `89fffcb34b0161033f6d2d1b9c650625cd6144fa575ccf3b170b4c0d5f31902d`
- `interface.xdat`: 705,813 bytes; SHA-256 `7ea4e8830c24b3d803b32429040eb673da87e15c5f83126c4b5c6904825b16bb`

Both begin with bytes `FF ED FA CE 01 00`. This is not the UTF-16LE `Lineage2Ver` header previously identified in the native archive reader. It is also not the literal ASCII `ActiveAnticheatCrypt` marker used by the separately documented AAC container. Therefore neither known envelope should be assumed to explain these samples.

## Important cross-check from public Interlude research

An independently documented retail Interlude client reports:
- `Interface.xdat` is a plaintext serialized widget-layout stream.
- `Interface.u` and `NWindow.u` use `Lineage2Ver111` and are handled by the existing `l2encdec -p 111` path.

This is useful as a baseline only. Its executable and file fingerprints have not been shown to match the uploaded L2Sudamerica client. It does not prove the uploaded files are plaintext or version 111.

## DES codec status

The native `FL2DESCodec` implementation is confirmed in the uploaded `l2.exe`, but no caller/dataflow evidence yet ties it to either uploaded interface file. Do not implement it as their decoder until that link is demonstrated.

## Next required evidence

1. Trace the exact file-open/read path for `interface.u` and `interface.xdat` in this client.
2. Identify which module handles the six-byte prefix and whether it transforms bytes before the Unreal/XDAT parser receives them.
3. Compare the post-envelope bytes against known UE package and XDAT structure invariants.
4. Only then implement decode/encode and require a byte-identical no-edit round trip.

No original binary was modified. No decryption success is claimed.


## Step 8 — external LameCrypt cross-check

Date: 2026-11-11

A source-level cross-check was performed against the open-source acmi/L2crypt implementation (L2Crypt.java and lame/LameCrypt.java).

Confirmed behavior in that implementation:
- LameCrypt variants are still wrapped in a 28-byte UTF-16LE Lineage2Ver### header.
- Recognized LameCrypt version numbers include 811/821 (XOR family), 820 (120 family), 911/912 (Blowfish family), and 611–614 (RSA family).
- The LameCrypt layer XORs subsequent bytes with a repeating fixed error-message string, then delegates to the corresponding standard Lineage2 codec.

Consequence for our exact samples:
- Their first six bytes are FF ED FA CE 01 00, not the beginning of the required UTF-16LE Lineage2Ver header.
- Therefore these samples do not match the documented acmi/L2crypt LameCrypt envelope as-is. This rules out that specific public implementation as the direct explanation; it does not rule out a different/private/custom fork or an additional outer wrapper.

SmartCrypt remains a candidate because its vendor describes a custom-key protection product supporting .u and .xdat, but there is no sample-level proof connecting SmartCrypt to these files. No SmartCrypt key, algorithm, or decryption path has been identified.

Sources:
- https://github.com/acmi/L2crypt/blob/master/src/main/java/acmi/l2/clientmod/crypt/L2Crypt.java
- https://github.com/acmi/L2crypt/blob/master/src/main/java/acmi/l2/clientmod/crypt/lame/LameCrypt.java
- https://smart-guard.eu/en/smartcrypt

## Updated status

Known public LameCrypt format: checked and incompatible with the observed leading bytes.
SmartCrypt/private custom protector: still unconfirmed.
Exact producer/tool and cipher for the uploaded samples: not identified.


## Step 9 — SmartCrypt decryptor workflow cross-check

Date: 2026-11-11

New external evidence materially strengthens SmartCrypt as the leading candidate:

- A Lineage II client-development discussion contains a developer's direct inspection of a pair of Interlude `interface.u` and `interface.xdat` samples and an explicit identification of those samples as SmartCrypt-protected. This is independent evidence that SmartCrypt is used on precisely these two file types in Interlude clients; it is not proof that our L2Sudamerica samples are the same protection build.
- A separate Smart Decryptor 1.0.1 resource documents a reproducible runtime extraction workflow: build/load a 32-bit `SmartDecrypt.dll`, add its `load` import to `ogg.dll`, ensure the SmartDecrypt import is ordered before `npkscrypt`, rename the target file to `sysstring-e.dat`, then launch the client's `l2.exe`. The output is `decrypted_file.txt`.
- The documented workflow explicitly warns that the resulting output may still carry ordinary Lineage II encryption (for example 413/111), which must be decoded as a separate second layer.
- A 2024 Smart Crypt Decryptor discussion describes the same `ReadFile`/`CreateFileW` hook approach and import-order requirement.

Relevance to this client:
- Our supplied system archive contains `npkscrypt.dll` and `ogg.dll`; this matches the module pair required by the published SmartDecrypt workflow.
- The samples' nonstandard six-byte prefix and the presence of this module pair are consistent with an outer SmartCrypt layer, but do not alone prove the exact protector or reveal its key.
- The documented workflow is an extraction technique, not yet a confirmed Methos implementation. It relies on executing the target client and modifying a test copy of `ogg.dll`; no original files should be touched.

Next verification required:
1. Obtain and inspect the published SmartDecrypt source and establish exactly what bytes it captures and writes.
2. Compare its hook semantics with the exact `ogg.dll` import table and `npkscrypt.dll` from this client.
3. Reproduce the extraction only in an isolated disposable copy of the client, preserving hashes of all originals.
4. Inspect the extracted output for a second standard Lineage2 header; decode that layer independently for U and XDAT.
5. Validate the resulting U package and XDAT structure, then test a no-edit round trip before enabling editing.

Sources:
- https://maxcheaters.com/topic/245142-decrypt-interfaceuxdat-interlude/
- https://mmo-dev.info/resources/smart-decryptor.3096/
- https://maxcheaters.com/topic/249577-smart-crypt-decryptor-1522/

Status update:
SmartCrypt is now the leading, evidence-supported hypothesis for this client's protection stack, not yet a proven identification for the exact uploaded bytes. The exact SmartCrypt cipher/key and successful decode of either sample remain unverified.


## Step 10 — SmartDecrypt source semantics and capture limitations

Date: 2026-11-11

The publicly posted SmartDecrypt 1.5.2.2 source was inspected at the source-snippet level.

Important finding:
- SmartDecrypt is a runtime capture shim, not a standalone implementation of the SmartCrypt cipher.
- It identifies the target file by intercepting `CreateFileW` and matching the name `SysString-e.dat`.
- It hooks `ReadFile`, calls the original API, then writes the read buffer to `decrypted_file.txt`.
- It forces the target through the game client's own loaded protection stack by arranging for SmartDecrypt's `load` import in `ogg.dll` to precede `npkscrypt`.
- Therefore the published method extracts the byte stream visible at that runtime boundary; it does not disclose the SmartCrypt key schedule, cipher, or key derivation.

Source-quality issues that Methos must not copy:
- The sample writes `nNumberOfBytesToRead` rather than the actual `*lpNumberOfBytesRead`, so a short read can append uninitialized/stale bytes.
- It does not correctly handle overlapped/asynchronous reads.
- It does not validate API success before writing.
- Its handle map and file-open checks are minimal.
- The sample assumes sequential reads and a single output stream.

Engineering consequence:
- This source is useful as a reference for a controlled extraction experiment, but it is not suitable to ship unchanged in Methos.
- A robust Methos capture harness would need to record successful byte counts, handle synchronous and overlapped completion correctly, preserve read order/offsets, and verify output length and hashes.
- Most importantly, the capture point must be proven to contain the post-SmartCrypt stream. Import order alone is not proof of the exact hook chain or returned buffer semantics.

Current evidence:
- SmartCrypt remains the leading candidate for the outer protection layer.
- The available public decryptor workflow demonstrates a practical route to ask the original client to expose file contents, but does not identify the proprietary algorithm.
- No extracted post-SmartCrypt stream has yet been produced from our exact L2Sudamerica samples. No decoder is declared complete.

Source:
- https://maxcheaters.com/topic/249577-smart-crypt-decryptor-1522/


## Step 11 — early SmartCrypt implementation and post-decryption obfuscation

Date: 2026-11-11

A second, independent public SmartCrypt guide (March 2021) provides an older extraction method distinct from the 2024 ReadFile capture shim.

What the older method actually does:
- Injects a small DLL into the game process by adding its import to `l2.exe`.
- Waits until `WinDrv.dll` is loaded.
- Resolves exported functions from `Core.dll`: `appLoadFileToArray`, `appSaveArrayToFile`, and the global `GFileManager`.
- Calls the client's own file manager to load `..\\System\\Interface.u` and save `..\\System\\Interface.decrypted.u`.
- The guide labels this specifically as a decryptor for early SmartCrypt revisions and says it can be adapted to other files by changing the source path.

This gives us an important distinction:
- The 2021 method is a client-side file-manager extraction route for earlier SmartCrypt builds.
- The 2024 SmartDecrypt method captures the ReadFile stream after arranging the `ogg.dll` import order relative to `npkscrypt`.
- These are different hook/capture points and may expose different layers. We must not assume their outputs are interchangeable.

Critical finding about the U file:
- In the same Interlude discussion where a developer confirms the submitted `interface.u` and `interface.xdat` are SmartCrypt-protected, another experienced participant reports that the U file is additionally obfuscated after decryption.
- Therefore successful removal of the SmartCrypt envelope may still leave a second, independent U-specific obfuscation that prevents normal package extraction. Methos must classify this as a separate stage, not misdiagnose it as failed SmartCrypt decryption.
- That report does not establish whether the user's L2Sudamerica U has the same obfuscation.

Limits and next validation:
1. Compare the exact exports/signatures used by the 2021 method with the supplied `Core.dll`; do not assume the decorated names match across client builds.
2. Verify whether the loaded file manager applies SmartCrypt transparently in this client's revision.
3. Establish whether the 2024 `ogg.dll`/SmartDecrypt method intercepts the same layer or a later one.
4. Capture U and XDAT independently; inspect both outputs for valid format markers and structural consistency.
5. If U remains obfuscated, identify that transform separately after SmartCrypt has been proven removed.
6. Require unchanged round-trip and client-load tests before implementing write support.

Sources:
- https://maxcheaters.com/topic/239616-smart-crypt-decryptor/
- https://maxcheaters.com/topic/245142-decrypt-interfaceuxdat-interlude/
- https://mmo-dev.info/resources/smart-decryptor.3096/

Status update:
The SmartCrypt identification is strongly supported as a protection family for Interlude interface U/XDAT files. The 2021 and 2024 extraction mechanisms are now understood at a high level. The exact L2Sudamerica protection revision, output layer, and any additional U obfuscation remain to be tested against the uploaded binaries.


## Step 12 — exact six-byte prefix search

Date: 2026-11-11

A targeted public search for the exact byte sequence `FF ED FA CE 01 00`, and for its four-byte prefix in Lineage II / SmartCrypt contexts, did not locate a published format specification or a matching implementation.

One superficial resemblance was noted:
- The sequence begins `FF ED FA CE`, which differs by one byte from the classic big-endian 32-bit Mach-O magic `FE ED FA CE`.
- This is not enough to identify the file as Mach-O or to infer that SmartCrypt uses a Mach-O marker. The samples are Lineage II client assets and the leading byte may be a custom marker, a transformed value, or coincidental.
- No decoder should branch on this resemblance without further structural or runtime evidence.

The prefix therefore remains an unresolved outer-envelope signature. The stronger evidence for SmartCrypt comes from the presence of the matching client protection modules and published Interlude U/XDAT cases, not from this four-byte resemblance.

Sources consulted:
- https://maxcheaters.com/topic/245142-decrypt-interfaceuxdat-interlude/
- https://maxcheaters.com/topic/239616-smart-crypt-decryptor/
- https://mmo-dev.info/resources/smart-decryptor.3096/

Status:
Exact prefix not identified. SmartCrypt protection family strongly supported, exact client revision and envelope transformation still unproven.


## Step 13 — exact DLL fingerprint correlation with a public client protection manifest

Date: 2026-11-11

A public Lineage II client-protection manifest was found containing MD5 fingerprints for Core.dll, npkscrypt.dll, interface.u, and interface.xdat.

Exact cross-check against the binaries already supplied to this investigation:
- The manifest's Core.dll MD5 is `B519540B5F7F5B9153263ECB492E88CC`, exactly matching the supplied Core.dll analyzed earlier.
- The manifest's npkscrypt.dll MD5 is `5908C3EE24B40F6F08F1500723B1A41D`, exactly matching the supplied npkscrypt.dll analyzed earlier.
- The same manifest enumerates interface.u and interface.xdat as protected client files and records multiple accepted MD5 values for each.

This is a much stronger linkage than simply observing that the client contains files with familiar names: two exact binary fingerprints from our supplied client match a public protection manifest that also lists both interface assets under client integrity protection.

Interpretation:
- Strongly corroborates that the supplied client belongs to the same protection-module family/configuration represented by that manifest.
- Supports SmartCrypt/SmartGuard as the leading explanation for the custom protection layer around the interface assets.
- Does not by itself prove that the manifest's listed interface.u/xdat hashes equal our two samples; our sample hashes recorded in this project are SHA-256, and the manifest lists MD5 values. Do not claim exact sample identity without computing and comparing MD5 for the local samples.
- Does not disclose the SmartCrypt cipher or custom key.

Next exact verification:
1. Compute MD5 for our existing interface.u and interface.xdat samples and compare against every manifest hash.
2. If a match exists, establish exact client/sample lineage.
3. Reproduce the supported extraction method on a disposable client copy and verify the output independently.
4. Determine whether U has a second obfuscation layer after SmartCrypt, and test XDAT separately.

Source:
- https://maxcheaters.com/profile/235534-eusouomestredosmagos/

Status update:
The exact Core.dll and npkscrypt.dll fingerprint match is now confirmed from public evidence. SmartCrypt-family protection is strongly corroborated for this client environment. Exact interface sample hash match, the proprietary transform/key, and successful extraction remain outstanding.


## Step 14 — early SmartCrypt bypass source independently corroborated (2016)

Date: 2026-11-12

A 2016 public SmartCrypt discussion independently documents the same Core.dll file-manager technique later published as the 2021 Smart Crypt Decryptor guide.

The 2016 report explicitly states:
- SmartCrypt-protected client files are transparently decrypted when loaded through the engine's `appLoadFileToArray` API.
- The resulting in-memory `TArray<BYTE>` is described as free of the SmartCrypt encryption layer.
- The array can be written using `appSaveArrayToFile`.
- The example targets Interlude and exports the decorated symbols:
  - `?appLoadFileToArray@@YAHAAV?$TArray@E@@PBGPAVFFileManager@@@Z`
  - `?appSaveArrayToFile@@YAHABV?$TArray@E@@PBGPAVFFileManager@@@Z`
  - `?GFileManager@@3PAVFFileManager@@A`
- The example attaches a DLL to the client executable and waits until `WinDrv.dll` is loaded before calling the file-manager functions.

This is a direct explanation of how the old SmartCrypt layer can be removed without knowing its private cipher/key: use the client's own file loading path and save the post-protection byte array.

Important limitation:
- A December 2016 reply reports that the method had stopped working for that user's setup. Thus this is version/build dependent and cannot be assumed to work against the supplied L2Sudamerica client.
- The method is specifically documented for early SmartCrypt revisions. The later 2021 guide calls it an early-revision decryptor.
- It proves the extraction concept, not the exact algorithm or private key.
- The published sample has minimal error handling and should not be copied into Methos as production code.

Implications for our exact Core.dll:
- The earlier static analysis of this supplied Core.dll established the native archive classes and codec machinery, but the report did not yet verify the three decorated file-manager exports above.
- Their exact presence/signatures in our Core.dll are now the highest-priority static check.
- If present, the next controlled test is to invoke the file manager against copies of interface.u and interface.xdat and inspect the resulting buffers; if absent or behavior differs, move to the SmartDecrypt ReadFile-capture route.

Sources:
- https://maxcheaters.com/topic/207068-smart-guards-smart-crypt-is-a-scam/
- https://maxcheaters.com/topic/239616-smart-crypt-decryptor/

Status:
We now have two independent historical sources describing the same SmartCrypt transparent-decryption mechanism through Core.dll. Exact export availability and successful extraction in this L2Sudamerica build remain unverified.


## Step 15 — SmartDecrypt 1.5.2.2 version match and capture correctness audit

Date: 2026-11-12

The published SmartDecrypt 1.5.2.2 procedure is now compared against the exact protection-module version already identified in this client:
- The supplied `npkscrypt.dll` was previously fingerprinted as version 1.5.2.2.
- The published procedure explicitly labels itself Smart Crypt Decryptor 1.5.2.2.
- It hooks `CreateFileW` and `ReadFile`, tracks the handle opened for `SysString-e.dat`, and writes the bytes read by the client to `decrypted_file.txt`.
- Its instructions say to temporarily present the target file under the name `SysString-e.dat`, launch the client, and collect the captured output. The post describes the captured output as still using the ordinary Lineage II archive encryption (examples 413/111), so this is an intermediate extraction stage, not necessarily a final editor-ready file.

This is the closest documented version match found so far for the supplied protection DLL. It makes the ReadFile-capture route the leading controlled experiment for this exact client, ahead of assuming the older Core.dll export method works.

Correctness problems in the published sample that must be fixed before relying on its output:
1. It writes `nNumberOfBytesToRead` instead of the actual completed byte count `*lpNumberOfBytesRead`. This can append uninitialized/stale buffer bytes on short reads.
2. It does not check the `ReadFile` return value or whether `lpNumberOfBytesRead` is valid.
3. It does not correctly handle overlapped/asynchronous reads, where completion may occur later.
4. It concatenates reads in callback order without recording file offsets. If the client seeks or reads out of order, the dump may not reconstruct the original stream.
5. It uses a simple counter to choose truncate/append behavior and does not robustly reset per file handle/open instance.

Therefore the public DLL is useful as proof of the capture point and version target, but its raw output must not be trusted as a verified decrypt until byte counts, offsets, completion semantics, and output structure are checked.

Controlled test plan (not yet executed):
- Work only on a disposable copy of the client and preserve the original files.
- Test `interface.u` and `interface.xdat` separately, each renamed to `SysString-e.dat` only in the disposable test copy.
- Capture successful reads using the actual transferred byte count; preserve offsets and handle partial/overlapped completion correctly.
- Verify the dump length and structure, then identify whether the output begins with a normal Lineage2 archive header such as `Lineage2Ver` and determine its version.
- Feed the resulting U and XDAT through their respective standard decoders only after the outer capture is validated.
- For U, separately test for any additional obfuscation reported in historical Interlude cases.

Status:
The exact SmartCrypt module version aligns with the published SmartDecrypt 1.5.2.2 method. This is a strong actionable match, but no output from the user's exact U/XDAT samples has yet been captured or validated. Do not label either file decrypted until the controlled test passes.
