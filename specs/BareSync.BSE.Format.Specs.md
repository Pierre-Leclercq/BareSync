# BareSync — BSE File Format Specification

Status: implementation-aligned format specification for the native `.bse` encrypted archive format.

Source of truth:
- `src/BareSync/App/EncryptedFolderService.cs`

This document describes the current format used by BareSync for:
- per-file encrypted archives (`*.bse`), and
- the encrypted index archive (`.baresync.encindex.bse`).

## Iteration 11 changes (non-normative)

- Clarified compatibility notes for CLI `/EXTRACT` workflows that consume native `.bse` archives.
- Clarified native archive detection expectations used by extract entrypoints.
- Clarified extract-time integrity outcomes (password validation and CRC mismatch semantics).

---

## 1) Scope

The `.bse` format is a BareSync-native container that combines:

1. Compression (`Brotli`),
2. Encryption (`AES-CBC`),
3. Integrity check (`HMAC-SHA256`),
4. A small binary header.

The same container format is used for both file payloads and encrypted index payloads.

---

## 2) Artifact naming conventions

- Data archives: one archive per source file, extension `.bse`.
- Encrypted index archive: fixed filename `.baresync.encindex.bse`.

Path obfuscation for data archives:
- BareSync hashes each source path segment with SHA-256.
- Each segment becomes a 64-hex-character name (uppercase hex from `Convert.ToHexString`).
- The resulting obfuscated relative path receives `.bse`.

---

## 3) Binary container layout

All integer primitive fields are encoded with .NET `BinaryWriter` defaults (little-endian for `Int32`).

### 3.1 Header layout

| Field | Size | Type | Description |
|---|---:|---|---|
| `Magic` | 4 | ASCII bytes | Fixed value: `BSE2` |
| `Version` | 1 | byte | Fixed value: `1` |
| `KdfIterations` | 4 | Int32 LE | PBKDF2 iteration count |
| `SaltLength` | 1 | byte | Salt length in bytes |
| `IvLength` | 1 | byte | IV length in bytes |
| `Salt` | `SaltLength` | bytes | PBKDF2 salt |
| `IV` | `IvLength` | bytes | AES-CBC IV |
| `StoredMac` | 32 | bytes | HMAC-SHA256 tag |
| `Ciphertext` | variable | bytes | Encrypted compressed payload |

### 3.2 Current implementation constants

- `Magic = "BSE2"`
- `Version = 1`
- `DefaultKdfIterations = 210_000`
- `SaltLength = 16`
- `IvLength = 16`
- `StoredMac` length = 32 bytes

Validation at read time:
- `Version` must match exactly.
- `KdfIterations >= 10_000`.
- `1 <= SaltLength <= 64`.
- `1 <= IvLength <= 32`.

---

## 4) Cryptographic and compression pipeline

Write pipeline (plaintext -> archive):

1. Derive keys with PBKDF2-HMAC-SHA256 from `(password, salt, iterations)`.
2. Split 64-byte derived material:
   - first 32 bytes: AES key,
   - next 32 bytes: HMAC key.
3. Compress plaintext using `BrotliStream(CompressionLevel.Fastest)`.
4. Encrypt compressed bytes using `AES-CBC` + `PKCS7` padding.
5. Compute `HMAC-SHA256` over the ciphertext stream.
6. Write resulting 32-byte HMAC into `StoredMac` header slot.

Read pipeline (archive -> plaintext):

1. Parse and validate header.
2. Derive AES/HMAC keys from header salt + iterations and the provided password.
3. Recompute HMAC over ciphertext and compare to `StoredMac` using fixed-time comparison.
4. If valid, decrypt ciphertext with AES-CBC.
5. Decompress decrypted stream with Brotli.

---

## 5) KDF details

- Function: `Rfc2898DeriveBytes.Pbkdf2(..., HashAlgorithmName.SHA256, 64)`
- Password input: UTF-16 .NET string API input to PBKDF2.
- Salt: random per archive.
- Iteration count: stored in header (`KdfIterations`).

---

## 6) Integrity model

Integrity check:
- Algorithm: `HMACSHA256`.
- Covered bytes: ciphertext only (from `Ciphertext` start to end-of-file).
- Comparison: `CryptographicOperations.FixedTimeEquals`.

Error behavior:
- Invalid MAC yields: wrong password or corrupted archive semantics.
- Corrupt compressed payload after successful crypto stage yields decompression error.

---

## 7) Encrypted index payload (`.baresync.encindex.bse`)

After decryption/decompression, the index payload is JSON-serialized as:

- `EncryptedIndexPayload`
  - `Entries: IReadOnlyList<EncryptedIndexEntry>`

`EncryptedIndexEntry` fields:
- `Id: long`
- `ObfuscatedName: string`
- `OriginalRelativePath: string`
- `SizeBytes: long`
- `LastWriteTimeUtc: DateTime`
- `Crc64Hex: string`
- `EntryKind: IndexEntryKind` (file/directory semantics)

The index is required during restore to map obfuscated archive names back to original relative paths.

---

## 8) Restore integrity guarantees

During restore, BareSync:

1. Verifies archive HMAC before decryption.
2. Decrypts/decompresses into a temporary target file.
3. Recomputes CRC64 of the restored file.
4. Compares restored CRC64 against `EncryptedIndexEntry.Crc64Hex`.
5. Moves temp file atomically to final restored path on success.

This gives a second integrity checkpoint at file-content level, in addition to archive-level HMAC.

---

## 9) Compatibility and versioning

- Format identity is controlled by (`Magic`, `Version`).
- Current reader only accepts:
  - `Magic == "BSE2"`
  - `Version == 1`

Any future incompatible format should use a new version (and/or magic) and a dedicated reader branch.

### 9.1 Native archive detection in extract flows

For extract entrypoints (including CLI `/EXTRACT`), a candidate `.bse` file is considered **native/exploitable** when:

- the container header can be parsed,
- `Magic == "BSE2"`,
- `Version == 1`,
- and mandatory header constraints are satisfied (iteration/salt/iv bounds).

Notes:
- File extension alone is insufficient for native detection.
- Extract flows should fail with an actionable message when no native archive is found in the requested source scope.

### 9.2 Password validation outcomes

At extract-time, password validation uses the container integrity check:

- success => HMAC verification passed and payload can be decrypted/decompressed,
- failure => wrong password and/or corrupted archive (same observable category for safety).

Error messaging for end users should remain actionable without exposing cryptographic internals.

### 9.3 CRC integrity at single-archive extraction

When an expected CRC64 is available (for example from encrypted index metadata), extract-time behavior is:

1. decrypt/decompress archive payload,
2. recompute CRC64 on restored content,
3. compare with expected CRC64,
4. on mismatch, fail extraction and do not keep a final destination file.

This CRC guard complements archive-level HMAC and protects against content-level inconsistencies after restoration.

---

## 10) Security notes and current limitations

1. The design uses Encrypt-then-MAC semantics over ciphertext (good practice for CBC-based schemes).
2. Header fields are parsed before decryption and are not included verbatim in the MAC input.
3. The IV is stored in header and not explicitly MAC-bound as header data.
4. The format is internal to BareSync and not declared as a stable external interoperability standard.

If cryptographic hardening is planned, a future version may move to an AEAD construction (for example AES-GCM) with fully authenticated metadata.
