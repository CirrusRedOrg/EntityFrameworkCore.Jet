# Page 0 — database definition

> Part of the [LibRed Jet / ACE file-format reference](README.md). Cross-references use the original **§-numbers**; the [section map](README.md#section-map) says which file each lives in.

## 2. Page 0 — database definition

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Page type, `0x00` |
| `0x01` | 3 | Unknown (not decoded) |
| `0x04` | 15 | Format identifier ASCII: `Standard Jet DB` or `Standard ACE DB` |
| `0x13` | 1 | Unknown — string padding/terminator (not decoded) |
| `0x14` | 1 | Version byte (see below). mdbtools reads `jet_version` as a 4-byte word at `0x14`; the version is its low byte and `0x15`–`0x17` are zero |
| `0x15` | 3 | Unknown — upper bytes of the version word (zero observed; not decoded) |
| `0x18`–`0x98` | 128 | **Obfuscated header** — XOR'd with a fixed 128-byte mask (§2.1). Jet 3 masks 126 bytes. Fields below are offsets into it. |
| `0x18`, `0x1C` | 4+4 | Fixed constants `0x00000100`, `0x00000101` (not page pointers — out of range in small files) |
| `0x20`–`0x2C` | 4×4 | **System-catalog bootstrap pointers**: TDEF pages of `MSysObjects` / `MSysACEs` / `MSysQueries` / `MSysRelationships` = `2, 3, 4, 5`. `0x20` is the **catalog root** (how the engine finds `MSysObjects`). |
| `0x30`–`0x3B` | 12 | Reserved (zero) |
| `0x3C` | 2 | **ANSI code page** — LE (`0x04E4` = 1252, `0x04E2` = 1250) |
| `0x3E` | 4 | **Database (encryption) key** — 0 when there is no password |
| `0x42` | 40 | **Password** (Jet 4; Jet 3 = 20 bytes) — additionally masked by a creation-date-derived value, so an empty password does not read as zeroes |
| `0x6A` | 4 | Fixed constant `0x000011A6` (undecoded) |
| `0x6E` | 4 | **Default text collating sort order** — LCID (2, LE, `0x0409` = 1033 en-US) + **sort-order version** at `0x71` (0 = General Legacy, 1 = General) |
| `0x72` | 8 | **Database creation timestamp** — OLE automation `double` (days from 1899-12-30) |
| `~0x9C` | 4 | ASCII engine-version string `"4.0"` — **in the clear** (past the masked window) |
| `~0xA0`+ | … | Zero padding to end of page |

Version byte → format:

| `0x14` | Version | Page size | Family |
| --- | --- | --- | --- |
| `0x00` | Jet 3 (Access 97) | 2048 | MDB |
| `0x01` | Jet 4 (Access 2000–2003) | 4096 | MDB |
| `0x02` | ACE 12 (Access 2007) | 4096 | ACCDB |
| `0x03` | ACE 14 (Access 2010) | 4096 | ACCDB |
| `0x05` | ACE 16 (Access 2016) | 4096 | ACCDB |
| `0x06` | ACE 17 (Access 2019+) | 4096 | ACCDB |

**Catalog bootstrap.** Reading the database is a two-step hop from page 0: the pointer at `0x20` gives the
`MSysObjects` TDEF page (2), and `MSysObjects` then lists every other object (each table's row `Id` is *its*
TDEF page). LibRed reads `0x20` into `DatabaseDefinitionPage.CatalogRootPage` and hands it to `JetCatalog`
(falling back to page 2 only if it reads 0). Verified: the four pointer values equal the objects'
`MSysObjects.Id` and each names a real TDEF page.

> **Implication for creating a database from scratch.** A minimal bootable page 0 needs the mask, the
> code page / collation / creation date, and this pointer block aimed at the four core system tables
> (`MSysObjects`, `MSysACEs`, `MSysQueries`, `MSysRelationships`) — which are therefore the minimum
> catalog a new file must contain. (Native creation is still template-copy; this is the target layout.)

### 2.1 The obfuscated header (`0x18`–`0x98`)

From `0x18` for **128 bytes** (Jet 4 / ACE; 126 for Jet 3), page 0 is obfuscated by XOR-ing the
plaintext with a **fixed byte mask** — a constant baked into the format, not a per-file salt. Past
the window (`~0x9C`) the ASCII engine-version string `"4.0"` and zero padding appear in the clear.

**The mask.** LibRed uses the 128-byte mask below (`JetFormatBase.PageZeroHeaderMask`), de-obfuscating
the whole region once in `DatabaseDefinitionPage.Read`:

```
B5 6F 03 62 61 08 C2 55 EB A9 67 72 43 3F 00 9C   ; 0x18
7A 9F 90 FF 80 9A 31 C5 79 BA ED 30 BC DF CC 9D   ; 0x28
63 D9 E4 C3 7B 42 FB 8A BC 4E 86 FB EC 37 5D 44   ; 0x38  (7B 42 @0x3C = code page)
9C FA C6 5E 28 E6 13 B6 8A 60 54 94 7B 36 F5 72   ; 0x48
DF B1 77 F4 13 43 CF AF B1 33 34 61 79 5B 92 B5   ; 0x58
7C 2A 05 F1 7C 99 01 1B 98 FD 12 4F 4A 94 6C 3E   ; 0x68  (01 1B @0x6E = LCID; 12 4F.. @0x72 = date)
60 26 5F 95 F8 D0 89 24 85 67 C6 1F 27 44 D2 EE   ; 0x78
CF 65 ED FF 07 C7 46 A1 78 16 0C ED E9 2D 62 D4   ; 0x88
```

**Verification (why this is recorded despite being an external mask).** The mask is Jackcess's
`BASE_HEADER_MASK`, but it is **not adopted on faith** — it is confirmed against real files two ways:

1. **Reproduces bytes recovered from first principles.** Independently, by a known-plaintext attack —
   varying one Access setting and reading its plaintext from an unobfuscated in-file copy — LibRed
   recovered the mask at three fields: the code page (`mask[0x3C]=7B,42` — de-obfuscation yields the
   canonical Windows code pages `0x04E4`/`0x04E2`), the collation LCID (`mask[0x6E]=01,1B`, checked
   against each column descriptor's own locale at `0x0B`–`0x0C` over five distinct LCIDs), and the
   creation date (`mask[0x72]=12 4F 4A 94 6C 3E 60 26`, matching `MSysObjects.DateCreate` to the
   second). The Jackcess mask matches all twelve of those bytes exactly.
2. **Decodes every fixture sensibly.** Applied whole, it yields valid code pages (1252/1250), the
   expected LCIDs, correct creation dates, a zero database key (no-password files), and an empty
   password that unmasks to the creation-date-derived pattern (below).

**Decoded fields** (all little-endian; `DatabaseDefinitionPage` → `JetDatabase`):

- **Code page (`0x3C`, 2 bytes)** → `CodePage` (1252 / 1250).
- **Database key (`0x3E`, 4 bytes)** → `DatabaseKey`. **Zero ⇒ the data pages are unencrypted;
  nonzero ⇒ the file is ACE-encrypted** (Access 2007+ "Set Database Password" encrypts the whole
  database). Verified: an unprotected file reads `0`; a password-protected `.accdb` reads a nonzero
  key and its data pages are encrypted. **Page 0's header itself is never page-encrypted — only
  base-masked — so the creation date, code page, LCID, etc. stay readable even on an encrypted file**
  (they must, to bootstrap decryption).
- **Password (`0x42`, 40 bytes)** — *not* decoded to a value, and the two families differ:
  - **Jet 4 `.mdb`**: light access-control obfuscation only — the field is the password XOR the base
    mask XOR an **additional 4-byte mask = `(int)creationDate`** (repeated). An empty password
    therefore unmasks to that creation-date pattern, not zeroes (this is the per-file variation once
    mistaken for a signature — there is no ESE-style machine signature here). The plaintext is
    recoverable, as mdbtools/Jackcess do.
  - **ACE `.accdb`**: real encryption — this region is an encryption **verifier**, not recoverable
    plaintext (an actual password decodes to random-looking bytes under the Jet 4 scheme). Recovering
    it is a crypto attack, not format work.
- **Collation sort order (`0x6E`, 4 bytes)** → `DefaultCollationLcid` (LCID at `0x6E`) +
  `DefaultCollationVersion` (the byte at `0x71`, 0 = General Legacy, 1 = General). The version here
  **matches each column descriptor's `0x0E`** — the sort version lives both database-wide (page 0)
  and per column (see [page-02b-columns.md](page-02b-columns.md)).
- **Creation date (`0x72`, 8 bytes)** → `CreationDate` — an OLE `double`. Matches the earliest
  `MSysObjects.DateCreate`; on an *edited* database (e.g. Northwind) it is the **file's** creation
  instant and can differ from the first object's by minutes.

Regression tests: `DatabaseDefinitionPageTests.Decodes_creation_date_matching_catalog` and
`Decodes_code_page_and_default_collation`.

> **ACE page decryption (implemented — Office Agile).** A password-encrypted `.accdb` (nonzero
> `DatabaseKey`) uses **Office Agile encryption** (MS-OFFCRYPTO §2.3.4): an `EncryptionInfo` XML
> descriptor sits in the clear in page 0 (after the masked header), giving AES-256-CBC + SHA-512
> parameters, salts, and the password verifier. `LibRed.Crypto.AgileEncryption` derives the data key
> from the password (SHA-512 KDF, 100 000-spin), validates the verifier (wrong password →
> `UnauthorizedAccessException`), then decrypts each data page. **Access's one deviation from stock
> Agile:** the per-page IV block key is `LE32(pageNumber) XOR databaseKey` (the 4-byte key at `0x3E`),
> so `IV = SHA512(keyDataSalt ‖ blockKey)[:blockSize]`; the page is `AES-256-CBC(dataKey, IV)`. Page 0
> is never page-encrypted. Verified end-to-end against a known-password fixture (decrypted pages match
> the unencrypted twin; `AgileEncryptionTests`). Encrypted databases open **read-only**; *writing*
> encryption is not implemented, and neither is the legacy Jet 3/4 RC4 scheme.

