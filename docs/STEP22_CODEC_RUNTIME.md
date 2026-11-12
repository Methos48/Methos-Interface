# Step 22 — Codec runtime and safe pipeline executor

Date: 2026-11-12

The U-Editor engine now includes the same `IProtectionCodec` registry and `ProtectionCodecRuntime` used by Methos-XDAT. Every layer must have an explicitly registered verified codec; unknown or missing layers stop processing. Decode applies layers in detection order and encode reverses that order. Output writing refuses to overwrite the imported original or an existing destination and uses a temporary file plus atomic move.

This is orchestration infrastructure, not a claim that SmartCrypt or every Lineage II cipher is implemented. The supplied `FF ED FA CE 01 00` sample still requires recovery of its actual transform and inverse. The runtime will refuse to process it until a verified codec is registered.

The XDAT repository's Windows smoke workflow is extended with synthetic multi-layer round-trip tests and a negative test proving that the real SmartCrypt candidate is rejected without a codec.
