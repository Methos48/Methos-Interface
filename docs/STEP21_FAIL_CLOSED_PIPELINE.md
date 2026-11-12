# Step 21 — Fail-closed protection pipeline planner

Date: 2026-11-12

## Implemented in this step

- Added a reusable C# planner for the protection pipeline.
- It distinguishes a candidate match for the exact FF ED FA CE 01 00 prefix seen in the supplied U/XDAT samples from a proven SmartCrypt decoder.
- It recognizes the standard UTF-16LE `Lineage2Ver###` header as a version identification only.
- It distinguishes Unreal package signatures from the XDAT extension and does not claim that a generic Unreal signature proves XDAT validity.
- Unknown or unsupported files fail closed. The planner never guesses a key, cipher, or version.
- Decode/encode availability remains false until a real codec passes known-answer, round-trip, and target-client tests.

## Important limitation

This is the orchestration/detection foundation, not a decryption implementation. It does not yet decrypt SmartCrypt 1.5.2.2, standard Lineage II versions, U packages, or XDAT payloads. In particular, the supplied SmartCrypt samples still require recovery of the actual transform and inverse transform. The public ReadFile capture approach is only diagnostic and is not treated as a standalone codec.

## Research evidence

The public SmartCrypt 1.5.2.2 example hooks CreateFileW/ReadFile for `SysString-e.dat`, but appends the requested byte count rather than the actual successful byte count and can therefore produce invalid captures. It also documents that the captured output may retain ordinary Lineage II encryption. Source: https://maxcheaters.com/topic/249577-smart-crypt-decryptor-1522/

## Next integration gates

1. Recover the SmartCrypt transform from the exact supplied runtime or implement a controlled Windows runtime adapter.
2. Validate the extracted bytes as a U package and as XDAT independently.
3. Implement the inverse encryption path.
4. Add known-answer and round-trip tests for every codec before enabling it in the UI.
5. Run exact-sample tests and target-client acceptance tests on Windows.
