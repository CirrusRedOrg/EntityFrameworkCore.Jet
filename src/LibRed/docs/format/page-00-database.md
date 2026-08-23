# Page 0 — database definition

> Part of the [LibRed Jet / ACE file-format reference](README.md). Cross-references use the original **§-numbers**; the [section map](README.md#section-map) says which file each lives in.

## 2. Page 0 — database definition

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Page type, `0x00` |
| `0x01` | 3 | Unknown (observed `01 00 00`, constant across Jet 4 and ACE; not decoded) |
| `0x04` | 15 | Format identifier ASCII: `Standard Jet DB` or `Standard ACE DB` |
| `0x13` | 1 | NUL terminator of the identifier string |
| `0x14` | 1 | Version byte (see below). mdbtools reads `jet_version` as a 4-byte word at `0x14`; the version is its low byte |
| `0x15` | 1 | Version **minor/update** byte: **`0x01` on ACE 14 / Access 2010 (version `0x03`)**, `0x00` on every other version tested (Jet 4, ACE 12/17). mdbtools says this is always zero — not universally true. Purpose beyond distinguishing the 2010 format unknown |
| `0x16` | 2 | Unknown (zero observed) |
| `0x18`–`0x98` | 128 | **Obfuscated header** — XOR'd with a fixed 128-byte mask (§2.1). Jet 3 masks 126 bytes. Fields below are offsets into it. |
| `0x18`, `0x1C` | 4+4 | Fixed constants `0x00000100`, `0x00000101` (not page pointers — out of range in small files) |
| `0x20`–`0x2C` | 4×4 | **System-catalog bootstrap pointers**: TDEF pages of `MSysObjects` / `MSysACEs` / `MSysQueries` / `MSysRelationships` = `2, 3, 4, 5`. `0x20` is the **catalog root** (how the engine finds `MSysObjects`). |
| `0x30`–`0x3B` | 12 | Reserved (zero) |
| `0x3C` | 2 | **ANSI code page** — LE (`0x04E4` = 1252, `0x04E2` = 1250) |
| `0x3E` | 4 | **Database (encryption) key** — 0 when there is no password |
| `0x42` | 40 | **Password** (Jet 4; Jet 3 = 20 bytes) — additionally masked by a creation-date-derived value, so an empty password does not read as zeroes |
| `0x6A` | 4 | Fixed constant `0x000011A6` — invariant across the entire Jet 4 lineage (every version/engine/collation/language tested); likely a validation sentinel/marker (cf. the `0x0659` TDEF record marker, §3.1), exact purpose unconfirmed |
| `0x6E` | 4 | **Default text collating sort order** — LCID (2, LE, `0x0409` = 1033 en-US) + **sort-order version** at `0x71` (0 = General Legacy, 1 = General) |
| `0x72` | 8 | **Database creation timestamp** — OLE automation `double` (days from 1899-12-30) |
| `0x98` | 4 | **Past the masked window** (cleartext). Fixed constant `0x00000654` (1620), undecoded |
| `0x9C` | 4 | ASCII **engine/format version string `"4.0"`** (NUL-terminated) — the Jet **4.0** version, present in both `.mdb` (Jet 4) and `.accdb` (ACE, which is Jet-4-based) |
| `0xA0`–`0x298` | … | Zero padding |
| `0x299` | 2 | **`EncryptionInfo` blob length** (LE) — Access's *"is this file encrypted?"* signal; `0` when unencrypted. Only meaningful for ACE `.accdb` (§2.5/§2.6) |
| `0x29B` | *len* | **`EncryptionInfo` descriptor** — present only when `0x299 ≠ 0`: a **binary** header for Office "Standard"/CryptoAPI (RC4/AES, §2.5) or an `04 00 04 00`-prefixed **XML** descriptor for Agile (§2.6). `len` bytes as given at `0x299` |
| `0x29B+len`–`0xDFF` | … | Zero padding |
| **`0xE00`–`0xFFF`** | **512** | **User commit-byte table** — 256 users × 2 bytes; per-user commit/lock status (see §2.2) |

Version byte → format:

| `0x14` | Version | Page size | Family |
| --- | --- | --- | --- |
| `0x00` | Jet 3 (Access 97) | 2048 | MDB |
| `0x01` | Jet 4 (Access 2000–2003) | 4096 | MDB |
| `0x02` | ACE 12 (Access 2007) | 4096 | ACCDB |
| `0x03` | ACE 14 (Access 2010) | 4096 | ACCDB |
| `0x04` | ACE 15 (Access 2013) — **reserved, never emitted** | 4096 | ACCDB |
| `0x05` | ACE 16 (Access 2016) — triggered by **Large Number** | 4096 | ACCDB |
| `0x06` | ACE 17 (Access 2019+) — triggered by **Date/Time Extended** | 4096 | ACCDB |

The identifier and version are one discriminator, not independent hints: `Standard Jet DB` uses version
`0x00`/`0x01`, `Jet System DB` uses `0x01`, and `Standard ACE DB` uses `0x02` or later. LibRed rejects
mismatched pairs before selecting offsets. It also deliberately rejects Jet 3 (`0x00`) until the distinct
2 KB page, TDEF, column-descriptor, and row layouts listed in [README §12](README.md#version-differences)
are implemented; recognizing the byte is not sufficient to read it safely.

The version byte is **"the minimum ACE engine whose format features this file uses,"** and Microsoft allocates one
byte per engine release whether or not that release adds a format-forcing feature. The only features that push
past `0x03` are the two new *data types*: **Large Number** (Int64) → `0x05`, and **Date/Time Extended** (datetime2)
→ `0x06`. Access **2010 through 2019 all default to `0x03`** unless a file actually uses one of those types.
**`0x04` (ACE 15 / Access 2013) is reserved but never stamped** — 2013 added no format-forcing data type, so its
files fall back to `0x03` (verified: a real `db2013` reads `0x03`; jackcess ships no 2013 fixture; Access 2013
defaults to the 2007-2016 format). LibRed maps `0x04` to the `0x03` (2010) layout rather than a clone class.
A genuinely **unknown** version byte on an `.accdb` that still carries the cleartext `"4.0"` engine string at
`0x9C` is read as the **latest known ACE** layout (currently ACE 17) — the format grows conservatively, so an
unrecognised byte is almost certainly a newer 4KB ACE variant; the `"4.0"` guard stops a genuinely different
future engine (e.g. a `"5.0"` string) from being mis-read as ACE.

**Catalog bootstrap.** Reading the database is a two-step hop from page 0: the pointer at `0x20` gives the
`MSysObjects` TDEF page (2), and `MSysObjects` then lists every other object (each table's row `Id` is *its*
TDEF page). LibRed reads `0x20` into `DatabaseDefinitionPage.CatalogRootPage` and hands it to `JetCatalog`
(falling back to page 2 only if it reads 0). Verified: the four pointer values equal the objects'
`MSysObjects.Id` and each names a real TDEF page.

> **Creation from scratch (implemented — `DatabaseCreator`).** A minimal bootable page 0 needs the mask, the
> code page / collation / creation date, and this pointer block aimed at the four core system tables
> (`MSysObjects`, `MSysACEs`, `MSysQueries`, `MSysRelationships`) — the minimum catalog a new file must
> contain. LibRed now synthesises all of this natively (no DAO/ADOX, no template copy) and the result opens
> **clean in the Access desktop GUI** (no permission popups, no auto-compact error). Two non-obvious facts made
> that work, both recorded below: the **creation date is bound to the on-disk security SIDs** (§2.3), and the
> file must **not** hand-create the `MSysAccessStorage` / `MSysNavPane*` tables — real DAO files omit them and
> Access adds them (with the nav-pane long SID) on first open (verified across ~135 pure-DAO files).

> **How the reference engine lays out a new file** (DAO-created ACE 12, 42 pages — `DaoPageLayoutProbeTest`).
> Per table the allocation order is **TDEF → usage-map page → one page per index root**, in table-creation
> order; both usage maps share one page (owned = row 0, free = row 1, inline), which is what every TDEF's
> `0x37`/`0x3B` pointers show. **Data pages are allocated lazily on first insert**, so they appear out of
> sequence and an empty table has none at all.
>
> | pages | contents |
> | --- | --- |
> | `0`, `1` | database definition; global free-pages map |
> | `2`–`5` | the four core TDEFs — fixed, because page 0's bootstrap pointers name them |
> | `6`, `9`, `11`, `13` | usage maps for MSysObjects / MSysACEs / MSysQueries / MSysRelationships |
> | `7`, `8`, `10`, `12`, `14`–`16` | their index roots (2 + 1 + 1 + 3), each a leaf page |
> | `17` | MSysObjects' first data page — the catalog rows |
> | `18`–`22` | MSysComplexColumns: TDEF, usage map, three index roots |
> | `23`–`40` | the nine `MSysComplexType_*` tables: TDEF, then usage map, in pairs |
> | `41` | MSysACEs' data page — the ACL rows |
>
> MSysQueries and MSysRelationships are empty in a fresh database and own no data page.
>
> **Column descriptors are stored sorted by name, while column ids follow creation order** — the two do not
> agree, and code that treats a column's position as its id is wrong wherever the TDEF is keyed by id (the
> long-value usage-map block is). E.g. `MSysComplexColumns.ComplexID` is the 2nd descriptor with id 4, and
> `MSysComplexType_Attachment.FileURL` is the last descriptor with id 0. Sorting is by name, not
> fixed-before-variable: `ColumnName` is variable-length and still sorts first.

### 2.1 The obfuscated header (`0x18`–`0x98`)

From `0x18` for **128 bytes** (Jet 4 / ACE; 126 for Jet 3), page 0 is obfuscated by XOR-ing the
plaintext with a **fixed byte mask** — a constant baked into the format, not a per-file salt. Past
the window the bytes are in the clear: a fixed constant `0x00000654` at `0x98`, the NUL-terminated
ASCII engine-version string **`"4.0"`** at `0x9C` (the Jet 4.0 version — identical in `.mdb` and
`.accdb`), then zero padding.

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
    recoverable, as mdbtools/Jackcess do. **`(int)creationDate` = the 8-byte creation-date double at
    `0x72` truncated to a 32-bit int, written little-endian and cycled over the 40-byte field** (matching
    jackcess `getPasswordMask`; the high 2 bytes are usually 0, so half the field is plaintext UTF-16).
    **Setting/removing this password is implemented** (`DatabaseEncryption.SetJetPassword` /
    `RemoveJetPassword`): write `UTF-16LE(password)` zero-padded to 40 bytes, XOR the date mask, then the
    base header mask — the exact inverse of the read. **Verified byte-identical to Access's own output**:
    `SetJetPassword` on a copy of `2002plain.mdb` reproduces Access-set `Test1`/`Test2`/`AAAA`/`z` files
    bit-for-bit in the `0x42` field (`LegacyJetPasswordTests.SetJetPassword_matches_access_output`). Those
    fixtures are not committed, so that case **skips with a reason** unless they are present — point
    `LIBRED_ENCTEST_DIR` at them to run it. The rest of `LegacyJetPasswordTests` builds its own Jet 4 header and
    covers the field transformation, limits, removal, and encoding independence on every platform. This is
    password-only obfuscation — the
    data pages stay plaintext (`0x3E` key = 0); it is a *different* feature from Jet RC4 page encryption
    (§2.4), which the "Encode/Encrypt" menu applies. The earlier "per-file SID mask = f(date,password)"
    theory was a misdiagnosis — the mask is simply `(int)creationDate`.
  - **ACE `.accdb`**: real encryption — this region is an encryption **verifier**, not recoverable
    plaintext (an actual password decodes to random-looking bytes under the Jet 4 scheme). Recovering
    it is a crypto attack, not format work.
- **Writing it:** `DatabaseCreator.CreateEmpty(path, version, collation)` (and
  `LibRedConnection.CreateDatabase(connectionString, collation)`) set this pair, defaulting to General-Legacy.
  The chosen collation goes into page 0 *and* into the system tables' column descriptors, and
  `JetDatabase.Collation` reads it back so every table created later inherits it — matching Access, which
  writes v1 descriptors on `MSys*` in a General database. LibRed is currently the only way to create a v1
  database programmatically: DAO writes v0 whatever the application setting says, and Access honours its
  "New database sort order" option only through its own UI.
- **Collation sort order (`0x6E`, 4 bytes)** → `DefaultCollationLcid` (LCID at `0x6E`) +
  `DefaultCollationVersion` (the byte at `0x71`, 0 = General Legacy, 1 = General). The version here
  **matches each column descriptor's `0x0E`** — the sort version lives both database-wide (page 0)
  and per column (see [page-02b-columns.md](page-02b-columns.md)).
- **Creation date (`0x72`, 8 bytes)** → `CreationDate` — an OLE `double`. Matches the earliest
  `MSysObjects.DateCreate`; on an *edited* database (e.g. Northwind) it is the **file's** creation
  instant and can differ from the first object's by minutes. **Unlike a normal Jet/ACE `DateTime`
  column (whole-second resolution), this header stamp carries sub-second precision** — verified across
  144 files, every value sits a whole number of milliseconds off a whole second (−284, −383, +78, +462 ms…),
  i.e. ~1 ms resolution, consistent with a Windows `SYSTEMTIME`. The column codec truncates to seconds;
  this field is written straight from the OS clock and keeps the milliseconds. See §2.3 — those low bits
  matter because the security SIDs are bound to them.

Regression tests: `DatabaseDefinitionPageTests.Decodes_creation_date_matching_catalog` and
`Decodes_code_page_and_default_collation`.

### 2.2 The user commit-byte table (`0xE00`–`0xFFF`)

The last 512 bytes of page 0 are a **per-user commit-byte table**: 256 possible users × 2 bytes. This is
the Jet 3.x locking structure (documented in Microsoft's Jet locking white paper, written for Jet 2.x/3.x)
relocated to the end of the larger 4 KB page:

- Jet 2.x: `0x700`–`0x800` (256 × 1 byte, end of the 2 KB page)
- Jet 3.x: `0x600`–`0x800` (256 × 2 bytes, end of the 2 KB page)
- **Jet 4 / ACE: `0xE00`–`0x1000`** (256 × 2 bytes, end of the 4 KB page) — same structure, same "end of
  header page" placement, scaled to the bigger page.

The first slot is the **exclusive-mode** commit state; the remaining 255 are shared-mode users. Each 2-byte
value is a commit/lock status Jet uses (with the matching user lock in the `.ldb`/`.laccdb`) to coordinate
concurrency — this table is only the per-user *overall status*; the `.ldb`/`.laccdb` holds the actual
page-level read/write registration. Observed: an idle/unused slot reads **`00 01`**; the head slots carry
per-file last-commit states (`01 01`, `05 01`, …). **`00 00` means "mid-write to disk"**, and `01 00` means
"accessed a corrupted page" — either one *without a matching user lock* makes Jet declare the database
suspect and demand a repair before it will open.

This region is **undocumented by mdbtools and Jackcess** — LibRed's own decode, cross-checked three ways:
the white paper's Jet 2.x/3.x structure, the raw bytes of real ACE files, and the Microsoft **LDBView**
utility (Jet 2/3 only), which shows `1` for every unregistered slot — matching the idle `00 01`.

> **Creation must seed this.** A freshly created file has no users, so every slot must be the neutral
> `00 01`, **not** zero — an all-zero table reads as "every user is mid-write," which Access rejects as
> corrupt. `DatabaseCreator.BuildDefinitionPage` fills `0xE00`–`0xFFF` with the repeating `00 01`.
> LibRed itself does not read the table.

### 2.3 Creation date ⇄ security-SID coupling (verified)

Access opens the **workgroup file** (`System.mdw`, in `%AppData%\Microsoft\Access`) *before* the database —
confirmed with Process Monitor — and authenticates the current user against it. `System.mdw` is itself a Jet 4
DB (identifier `"Jet System DB"`, version byte `0x01`) with **legacy Jet RC4 page encryption** (§2.4); LibRed
reads it directly. Its `MSysAccounts`/`MSysGroups` hold the **default-workgroup account SIDs**, which LibRed
now decodes to exactly the values Access shows (cross-checked against a VBA `Debug.Print` of the `SID` column):

| Account | kind | SID |
|---|---|---|
| `admin` | user | `03-01` |
| `Users` | group | `02-01` |
| `Engine` | — | `02-03` |
| `Creator` | — | `02-04` |
| `Admins` | group | `01-DB-87-93-20-81-4F-AB-38-…` (long, per-workgroup-unique) |

These short SIDs are the well-known Access defaults (identical on every stock install — which is why a captured
SID cluster opens cross-PC). Object ownership in a database uses the **"user" form** (byte0 `0x03`) of
`Engine`/`Creator`.

The 2-byte on-disk SIDs in `MSysACEs.SID` / `MSysObjects.Owner` are each a **workgroup account SID XOR'd with a
per-file 2-byte mask**. Verified against WideTable (mask `24-CC`): `Users 02-01 ^ 24-CC = 26-CD`,
`admin 03-01 ^ 24-CC = 27-CD` (read grantee), `Engine 03-03 ^ 24-CC = 27-CF` (system-object owner),
`Creator 03-04 ^ 24-CC = 27-C8` (inheritable container grant). The long `Admins` SID isn't emitted — Access
materialises it (as a 98-byte SID) on first open.

That mask is **bound to the exact millisecond-precise creation-date `double`** at `0x72`: a file with
self-consistent SIDs but a *different* creation date is rejected with *"Record(s) cannot be read; no read
permission on 'MSysObjects'/'MSysACEs'"* (Jet 3112). Grafting a real file's date **and** SIDs together opens
clean; either alone fails. There is **no closed-form `date → mask` function** — tested against all 144
reference files (word XOR/sum, CRC-16, MSVCRT `rand`, VBA LCG, multiplicative hashes: 0 hits) and same-second
files have unrelated masks; Access most likely draws both the mask and the sub-second creation bits from one
PRNG state, so they correlate but neither derives from the other. `DatabaseCreator` therefore **bakes one
verified `(SeedCreationDateBits, SidMask)` pair** (`0x40E68F1E8943D217` + `24-CC`, from WideTable) rather than
computing it — the from-scratch analogue of the account-SID constants. Limitations (deferred): every
LibRed-created file reports the same creation instant, and only the **default** workgroup is supported;
per-file-random dates and custom/secured workgroups both need the date↔mask coupling cracked (reading a
custom `System.mdw` itself now works — §2.4).

### 2.4 Legacy Jet 3/4 RC4 page encryption (verified)

The pre-ACE engine-level encryption (used by password-protected `.mdb` files and *always* by the `.mdw`
workgroup file, which is why its account/password data isn't readable in a hex editor). The 4-byte **database
key** at page-0 `0x3E` is the whole secret — there is **no password or key derivation** (unlike ACE Agile,
§2 above). Every page **except page 0** is RC4-encrypted with a per-page key of

```
key = LE32(pageNumber XOR databaseKey)
```

and the page bytes are the RC4 keystream XOR'd over the plaintext. This is the same per-page key mixing ACE
Agile uses (`LE32(pageNumber) XOR encodingKey`), just feeding RC4 directly instead of deriving an AES IV.
Verified against a real `System.mdw` (`databaseKey = 0xABBB315C`): with XOR (not ADD) page-number mixing,
every page decrypts to a valid page-type byte (page 1 → `01` data, pages 2/3 → `02` TDEF, index pages → `04`),
`MSysObjects`/`MSysACEs` parse, and `MSysAccounts` yields the account SIDs in §2.3. Implemented as
`LibRed.Crypto.JetLegacyEncryption`; `PageChannel` selects it for non-ACE (`!IsAccdb`) files with a nonzero
database key. Regression tests in `JetLegacyEncryptionTests` (published RC4 vector + independent-oracle
key-derivation check).

**Creating/removing the encoding (implemented).** `DatabaseEncryption.SetJetEncoding` picks a fresh random
`0x3E` key, writes it (header-masked), and RC4s every page `1..n` in place; `RemoveJetEncoding` decrypts and clears
the key (RC4 is symmetric, so this reuses the read codec). This is the "Encode Database" feature and is
**completely independent of the database password** (§2, the `0x42` field): legacy Jet4 stores the encoding key
plainly at `0x3E` — it is *not* password-derived (verified: the encoded fixtures `2002encoded`/`db-enc` have a
nonzero `0x3E` key and an *empty* password; MSISAM/Money's password-derived key is a different format). A file may
carry both — encoding scrambles the pages, the password gates opening — and the `0x42` field lives on page 0 which
is never page-encrypted, so the two don't interact. Verified: encode-then-decode of `2002plain.mdb` is
byte-identical, the encoded output re-opens through the codec, and the password field survives encoding unchanged.
Jet 3 (`version byte 0x14 == 0`, 2048-byte pages) is rejected — unsupported. Tests: `LegacyJetPasswordTests`.

**Combined encode + password — confirmed against a real Access file** (`2002encodedpw.mdb`, dbKey `0xaca0f84c`,
password `Test1`): LibRed opens Access's combined file and reads its table (our RC4 decode matches Access's encode);
the `0x42` password field is byte-identical to the password-only `2002plainpw.mdb`; and `RemoveJetEncoding` yields
valid re-openable plaintext. (`2002encodedpw` was made by copying `2002encoded` and adding a password, so it shares
that file's dbKey `0xaca0f84c` — this is *not* evidence of a derived key; the encoding key is treated as opaque per
file, read straight from `0x3E`.) Setting a password on an already-encoded file is identical to on a plain file,
since `0x42` is on the never-encrypted page 0. Access's "Encode Database" **also compacts** (`2002plain` 245760 →
`2002encoded` 237568; the later password add kept 237568), so a byte-for-byte reproduction from an un-compacted
source isn't achievable — our `SetJetEncoding` is a pure in-place RC4, a valid encoding without the compact.

### 2.5 Office "Standard"/CryptoAPI page encryption (verified)

> **Why RC4 (background, per Wayne Phillips / EverythingAccess — explains the behaviour we verified).** Access 2007
> encrypts at **page level but writes at record level** (record-level locking rewrites a single record, not the
> whole 4096-byte page), so it needs a **stream cipher** (encrypts byte-by-byte). Block ciphers (AES) can't do
> partial-page writes, and RC4 is the only stream cipher in the standard Windows CSPs — hence Access 2007 is
> RC4-only. **Access 2010's AES works only by forcibly disabling record-level locking**, and that path is Agile
> (XML descriptor), *not* this binary AES-"Standard" descriptor. The default is Base provider / **RC4-40 / SHA-1**;
> the Base provider caps at RC4-56, Enhanced providers reach RC4-128. A file keeps its own provider+algorithm
> regardless of the registry `DefaultEncryption` CSP, and opens wherever that CSP is installed (the Enhanced RSA/AES
> and Enhanced v1.0 CSPs are universal on modern Windows — "effectively the same algorithm, different package").

The pre-Agile `.accdb` encryption, carried by a **binary** `EncryptionInfo` header (version x.2, no XML) rather
than the Agile XML — covering **RC4-CryptoAPI** and an **AES "non-standard"** variant. `LibRed.Crypto.
OfficeStandardEncryption`; `PageChannel` selects it for an ACE file when no Agile descriptor is present.
Algorithm (matched to jackcess-encrypt and verified against real fixtures — db2007-oldenc = RC4-40 / `Test123`;
db-nonstandard = AES-256 / `password`):

The **hashing algorithm is parameterised by the header `AlgIDHash` field** (offset `h+12`), *not* fixed to SHA-1:
Access and third-party tools (e.g. EverythingAccess's "Encryption Manager for Access 2007") let the encryptor
pick it independently of the cipher. Verified values: `0x8003` MD5, `0x8004` SHA-1, `0x800c` SHA-256, `0x800d`
SHA-384, `0x800e` SHA-512 (MD2 `0x8001` / MD4 `0x8002` have no managed implementation → unsupported). The same
algorithm is used for `baseHash`, the per-block `H`, and the AES `0x36`/`0x5C` expansion. Let `hash = f(AlgIDHash)`:

- `baseHash = hash(salt ‖ UTF16LE(password))`.
- per-block key: `iterHash = iterate(baseHash, N)` where `N` = **0** for RC4-CryptoAPI and the AES non-standard
  variant, **50000** for ECMA-standard AES (`iterate` folds `hash(LE32(i) ‖ H)`); `H = hash(iterHash ‖ block)`.
- **key derivation (CryptDeriveKey): RC4 truncates; AES is one of two forms, tried both, verifier selects.**
  - RC4 always uses the plainly truncated hash: `key = H[:keyLen]`.
  - AES is *either* the truncated hash `H[:keyLen]` *or* the `0x36`/`0x5C` expansion
    `key = (hash(0x36pad⊕H) ‖ hash(0x5Cpad⊕H))[:keyLen]`. **The choice does NOT follow a clean keyLen-vs-hashLen
    rule** — verified counterexamples: AES-128 from MD5 (16==16) *expands*, yet AES-256 from SHA-256 (32==32)
    *truncates*; AES-256 from SHA-1/MD5 expands, from SHA-384/512 truncates. LibRed tries both and keeps whichever
    authenticates the verifier (a wrong derivation matching the verifier is cryptographically infeasible). The
    expansion pad buffer is a **fixed 64 bytes for every hash** (not HMAC — SHA-384/512 do *not* use a 128-byte block).
- **`KeySize` (header `h+16`) of `0` means "the algorithm default"** — the descriptor doesn't spell the length out.
  LibRed resolves it by trying the standard defaults (RC4 → 40 or 128 bits; AES → 128/192/256) and keeping
  whichever authenticates the verifier. **RC4 keys shorter than 128 bits are zero-padded to 16 bytes** by the base
  CryptoAPI provider (verified: RC4-40 and RC4-120 both pad to 16); this is also tried as a candidate.
- verifier block = `LE32(0)`; per-page block = `LE32(pageNumber) XOR databaseKey` (the `0x3E` key) — the same
  page mixing as Agile/legacy.
- cipher: RC4 (re-keyed per page; the verifier + verifier-hash decrypt as one continuous stream) or **AES-ECB**.

The applicable `(key length, RC4 pad, AES iteration count)` is decided by whichever authenticates the verifier.
Fixture-free known-answer tests (real salt + verifier vectors, synthetic page 0) live in
`OfficeStandardEncryptionTests`; `DatabaseEncryptionTests` exercise generated RC4 key/hash variants end-to-end,
and `OfficeStandardVariantReadTests` mutate generated descriptors to verify clean rejection of unsupported
ciphers and hashes. The broader **RC4 and AES-128/192/256 × MD5/SHA-1/SHA-256/SHA-384/SHA-512 ×
`KeySize=0`** sweep was verified against Access-tool re-encryptions of `db2007-oldenc` during format research.

> **LibRed reads more than Access opens.** Verified on EverythingAccess-re-encrypted `db2007-oldenc` variants:
> **AES-128/AES-256 with MD5 or SHA-512 hashing** authenticate and decode correctly in LibRed, but **Access refuses
> them** ("your encryption settings are not valid — reinstall Microsoft Access"). Those files *are* validly
> encrypted; Access's open path just whitelists cipher/hash combinations. Genuinely unsupported by both (LibRed
> throws a clean `NotSupportedException`, never mis-reads ciphertext as plaintext): **3DES-168** (`0x6603`),
> **3DES-112** (`0x6609`), **DES** (`0x6601`), **RC2** (`0x6602`), and **MD2 hashing** (`0x8001`, no managed impl).
> These are weak legacy algorithms — deliberately not implemented.
>
> **What a stock Access install opens (observed across all re-encrypted variants).** The `EncryptionEnhancer`
> add-in is required **only for the block ciphers** (AES/DES/3DES/RC2) — **RC4 always opens natively**:
> - **RC4, every key length (40–128) and every hash (MD2/MD4/MD5/SHA-1/SHA-256/384/512): opens with no add-in**
>   (verified — the user opened all created RC4 files in Access). The descriptor may name the Enhanced RSA/AES
>   *provider* (for >56-bit or SHA-2), but that CSP ships with Windows, so Access reads it fine.
> - **AES/DES/3DES/RC2 (binary "Standard" descriptor): require the withdrawn EncryptionEnhancer COM add-in** to open
>   in Access. These are **validly encrypted** — a plain Access install refusing them (returning "incorrect
>   password" / "settings not valid") is a **deliberate guard**, not a validity failure: the add-in made Access
>   reject block-cipher files unless the add-in is present, precisely so Access can't corrupt them by writing
>   partial pages under record-level locking (block ciphers need whole-page writes). The add-in forced page-level
>   locking on; **Access 2010 does the same thing natively** for block ciphers — and that path is Agile, not this
>   binary AES-Standard descriptor.
>
> So vs LibRed: **LibRed reads the AES-Standard files that need the add-in** (128/192/256 × MD5/SHA-1/256/384/512) —
> effectively a modern, cross-platform equivalent of that (WOW64-incompatible, withdrawn) add-in. And because LibRed
> always writes whole 4096-byte pages (`EncryptPage`/`WritePage` are page-at-a-time), it has none of the
> record-level-locking corruption risk, so it can safely **read and write** these files. The only files LibRed does
> **not** read that Access opens are
> **RC4 + MD2/MD4** (`AlgIDHash 0x8001/0x8002`): .NET has no MD2/MD4, so those throw `NotSupported`. Implementing
> the two hashes from scratch (~130 lines) would close the gap; **deferred** as 1980s hashes unlikely in any real
> database. (Modern Access AES is Agile, not this binary AES-Standard descriptor — a separate codec.)

An encrypted file (`databaseKey != 0`) whose descriptor no codec recognises now fails with a clear
`NotSupportedException` in `PageChannel.Open` instead of decoding ciphertext as plaintext.
Remaining unsupported: **Jet 3** (Access 97) encryption, which also needs Jet 3 format support (2048-byte pages).

**Descriptor placement + the encryption signal (verified).** For a binary-descriptor ACE file the
`EncryptionInfo` sits at a **fixed page-0 offset `0x29B`**, immediately preceded by a **2-byte blob length at
`0x299`**. That length is **Access's "is this file encrypted?" signal**: on open Access reads `len@0x299` and, if
nonzero, parses `len` bytes of `EncryptionInfo` at `0x29B`; if **zero it treats the file as unencrypted** — even
with a nonzero `0x3E` key and a valid descriptor present. Verified across `db-nonstandard`/`db2007-oldenc`/
`db2013` (each length equals its exact blob size: 224 / 190 / 1055) and by experiment: a file with the key +
descriptor but `len@0x299 = 0` makes Access read ciphertext as plaintext and offer to "recover"; writing the
length makes it prompt for the password and open. LibRed likewise treats the length as authoritative: binary or
XML content outside the declared frame is ignored, and a frame extending beyond page 0 is rejected as malformed.
The Agile XML descriptor uses the same `len@0x299` + blob-at-`0x29B` framing.

> **Creating encryption from scratch (implemented — Office Standard).** `LibRed.Crypto.DatabaseEncryption`
> (`SetPassword`/`RemovePassword`/`ChangePassword`, scheme via `AccessEncryption`) encrypts a plaintext `.accdb`
> with no Access/COM: generate a random `0x3E` key + salt + verifier, build the `EncryptionInfo` **byte-for-byte
> as Access writes it** (`EncryptionHeader` incl. `ProviderType` + the CSP-name string — AES `"Microsoft Enhanced
> RSA and AES Cryptographic Provider"`/RC4 `"Microsoft Base Cryptographic Provider v1.0"`), write the `0x299`
> length signal, and encrypt every page. **Verified: AES-256, RC4-40, and Agile files created this way open in
> the Access desktop GUI with the password.** For **Agile**, the same path emits the XML descriptor (version 4.4
> prefix `04 00 04 00 40 00 00 00` + UTF-8 XML) with a random data key wrapped via the 100000-spin SHA-512 KDF —
> Access's `.accdb` Agile has **no `<dataIntegrity>` element** (verified against `db2013` and a created file), so
> none is emitted. `ChangePassword` = decrypt + re-encrypt. Legacy Jet set-password and Jet 3 remain
> unimplemented.
>
> **RC4 key length + hash are caller-selectable** via `DatabaseEncryption.SetPasswordRc4(path, password, keyBits =
> 40, hash = StandardHash.Sha1)` / `ChangePasswordRc4` — `keyBits` 40–128 (multiple of 8), `hash` ∈ MD5/SHA-1/
> SHA-256/384/512 (`StandardHash` enum; MD2/MD4 excluded, no managed impl). The descriptor's `ProviderType`/CSP
> name follows the CryptoAPI split: Base provider for RC4 ≤56-bit with MD5/SHA-1, else Enhanced RSA/AES. RC4 always
> uses the truncated hash (no `0x36/0x5C` expansion) and only the 40-bit case zero-pads to 128. `keyBits == 40`,
> `Sha1` reproduces the byte-identical Access-2007-default RC4-40 descriptor. **Every RC4 combo produced this way
> opens natively in Access** — no add-in needed (the add-in is only for the block ciphers). Round-tripped in
> `DatabaseEncryptionTests` across the key-length × hash matrix.

> **Writing to an existing encrypted database (implemented).** `IPageCodec.EncryptPage` is the inverse of
> `DecryptPage`, so `PageChannel.WritePage` encrypts each page on the way to disk (page 0 stays clear) while the
> page cache holds plaintext — a symmetric mirror of the read path. RC4 is self-inverse; AES flips CBC/ECB
> decrypt→encrypt. Verified by round-tripping a row insert through all four schemes (legacy RC4, Standard RC4,
> Standard AES-ECB, Agile AES-CBC): reopening with the codec reads the new row back, and the file stays
> encrypted (opening without the password still fails). *Creating* a new encrypted database from scratch is also
> implemented — see the "Creating encryption from scratch" note above (§2.5) and §2.6 for Agile.

### 2.6 ACE Agile page encryption (verified)

Access 2010+ "Set Database Password" on an `.accdb` uses **Office Agile encryption** (MS-OFFCRYPTO §2.3.4): an
`EncryptionInfo` **XML** descriptor sits in the clear in page 0 (framed by `len@0x299` and the blob at `0x29B`), carrying the AES-256-CBC
+ SHA-512 parameters, salts, and the password verifier. `LibRed.Crypto.AgileEncryption` derives the data key from
the password (SHA-512 KDF, 100 000-spin), validates the verifier (wrong password → `UnauthorizedAccessException`),
then decrypts each data page. **Access's one deviation from stock Agile:** the per-page IV block key is
`LE32(pageNumber) XOR databaseKey` (the 4-byte key at `0x3E`), so `IV = SHA512(keyDataSalt ‖ blockKey)[:blockSize]`
and the page is `AES-256-CBC(dataKey, IV)`. Page 0 is never page-encrypted. Verified end-to-end against a
known-password fixture (decrypted pages match the unencrypted twin; `AgileEncryptionTests`).

LibRed supports the exact Access profile verified from real files and its own Access-openable writer:
AES-256-CBC, SHA-512, 16-byte salts/blocks, and `spinCount=100000`. These cleartext dimensions are validated
before allocation, KDF work, or AES setup; the encrypted verifier/key fields must be exactly 16/64/32 bytes.
The reader also cross-checks redundant `saltSize`/`hashSize` attributes and requires `keyData` and `encryptedKey`
to agree. Unsupported or malformed profiles throw `NotSupportedException`, while a structurally valid profile
with the wrong password throws `UnauthorizedAccessException`. XML outside the declared `0x299`/`0x29B`
descriptor frame is ignored. This is the scheme Access 2010+ writes by default; the pre-2010 binary-descriptor
schemes are §2.4/§2.5. Creating Agile encryption from scratch is implemented (§2.5's note).
