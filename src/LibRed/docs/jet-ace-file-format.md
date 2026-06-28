# Jet / ACE file format — LibRed reference

This is LibRed's own specification of the Microsoft Jet 4 / ACE (Access `.mdb` / `.accdb`)
on-disk format. **Every offset and structure here has been verified byte-for-byte against
real database files** (the Northwind ACE-2007 sample, a generated 200-column wide table,
and a generated ~150 MB large table) and cross-checked against mdbtools (`src/libmdb/`)
and Jackcess. Where something is assumed or unverified, it says so explicitly.

Unless noted, everything below describes **Jet 4 and ACE (12/14/16/17)**, which share one
structural layout. **Jet 3** (Access 97) differs in many of these and is *not yet
implemented* — see [Version differences](#version-differences).

Implemented by `src/LibRed/LibRed.Core/`. The canonical offsets live in
`Formats/JetFormatBase.cs`.

---

## 1. Conventions

- **Endianness:** little-endian for all integers, **except** index-page key/pointer values,
  which are big-endian (noted in §10).
- **Page size:** 4096 bytes (Jet 4 / ACE). Jet 3 is 2048.
- **Pages** are numbered from 0; a page's byte offset in the file is `pageNumber * pageSize`.
- **Page type** is the first byte of every page:

  | Byte | Page type | LibRed |
  | --- | --- | --- |
  | `0x00` | Database definition (page 0 only) | `DatabaseDefinitionPage` |
  | `0x01` | Data page (also long-value/LVAL pages) | `DataPage` |
  | `0x02` | Table definition (TDEF) | `TableDefinitionPage` |
  | `0x03` | Index B-tree node (intermediate) | `IndexCursor` |
  | `0x04` | Index B-tree leaf | `IndexCursor` |
  | `0x05` | Page-usage bitmap | `UsageMap` |

---

## 2. Page 0 — database definition

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Page type, `0x00` |
| `0x04` | 15 | Format identifier ASCII: `Standard Jet DB` or `Standard ACE DB` |
| `0x14` | 1 | Version byte (see below) |
| `0x18`+ | … | Obfuscated/encrypted (code page, collation, creation date, password) — **not decoded** |

Version byte → format:

| `0x14` | Version | Page size | Family |
| --- | --- | --- | --- |
| `0x00` | Jet 3 (Access 97) | 2048 | MDB |
| `0x01` | Jet 4 (Access 2000–2003) | 4096 | MDB |
| `0x02` | ACE 12 (Access 2007) | 4096 | ACCDB |
| `0x03` | ACE 14 (Access 2010) | 4096 | ACCDB |
| `0x05` | ACE 16 (Access 2016) | 4096 | ACCDB |
| `0x06` | ACE 17 (Access 2019+) | 4096 | ACCDB |

Everything from `0x18` onward on page 0 is obfuscated; LibRed reads only the identifier and
version. (Page-level encryption for password-protected files is not implemented.)

---

## 3. Table definition (TDEF) page — type `0x02`

### 3.1 Header

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Page type `0x02` |
| `0x01` | 1 | Flags (observed `0x01`) |
| `0x02` | 2 | Free space remaining in this page |
| `0x04` | 4 | Next TDEF page (0 if the definition fits one page) |
| `0x08` | 4 | TDEF length (total logical bytes) |
| `0x0C` | 4 | Unknown — a constant `0x00000659` (1625) observed in every file |
| `0x10` | 4 | Row count |
| `0x14` | 4 | Next auto-number value (e.g. next AutoNumber id) |
| `0x18` | 4 | Unknown — observed constant `0x01` (possibly the ACE complex-type auto-number) |
| `0x1C` | 12 | Unknown / reserved (zero observed) |
| `0x28` | 1 | Table type: `0x4E` 'N' user, `0x53` 'S' system |
| `0x29` | 2 | Maximum column count |
| `0x2B` | 2 | Variable-length column count |
| `0x2D` | 2 | Column count |
| `0x2F` | 4 | **Logical** index count (a.k.a. index slots) |
| `0x33` | 4 | **Real** index count (number of index-data blocks) |
| `0x37` | 4 | Owned-pages usage-map pointer: 1-byte row + 3-byte page |
| `0x3B` | 4 | Free-space-pages usage-map pointer |
| `0x3F` | — | Start of the real-index block (precedes column descriptors) |

> **The `0x659` / `0x783` record markers.** `0x0C` holds `0x659` (1625) in every file. This is
> not isolated: `0x659` recurs as a fixed 4-byte field at the start of each repeating TDEF
> record — column descriptor `+1` (§3.4), index-info block `+0` (§3.6), and this header slot —
> while `0x783` (1923) marks each index-data block `+0` (§3.5). They are constant within and
> across files; mdbtools describes them as "usually 1625 / 1923 *or 0*", so they appear to be
> reserved record markers/tags the engine does not depend on (LibRed ignores them). `0x0C` is
> therefore **not** the code page — `1625` is not a valid code page, and the code page is a
> database-wide value on page 0, not per-table.

> ⚠️ `0x2F` vs `0x33`: these are equal for MSysObjects (which hid the distinction during
> reverse-engineering) but differ for user tables. `0x33` (real index count) sizes the
> index-data blocks **and** the `0x3F` pre-column block; `0x2F` (logical count) is the number
> of logical-index info blocks and index names. A relationship adds a *logical* index that
> shares a real index's data, so logical ≥ real.

### 3.2 Multi-page TDEFs

If a table has enough columns, the definition spans pages chained by the `0x04` pointer.
Reassemble before parsing: take the **first page whole**, then append each continuation
page's bytes **from offset 8** (continuation pages have an 8-byte header). Column offsets are
absolute from the first page, so parsing is otherwise unchanged.

### 3.3 Body layout (in order, after the header)

```
0x3F : real-index block      RealIndexCount(0x33) × 12 bytes   (skipped to find columns)
       column descriptors    ColumnCount(0x2D)    × 25 bytes
       column names          ColumnCount          × (2-byte length + UTF-16LE)
       index-data blocks     RealIndexCount(0x33) × 52 bytes
       index-info blocks     LogicalIndexCount(0x2F) × 28 bytes
       index names           LogicalIndexCount    × (2-byte length + UTF-16LE)
```

### 3.4 Column descriptor (25 bytes)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Data type (see §6) |
| `0x01` | 4 | Record marker constant `0x659` (see §3.1 note); ignored |
| `0x05` | 2 | Column id (a.k.a. column number) |
| `0x0F` | 1 | Flags: `0x01` fixed-length, `0x04` auto-number |
| `0x15` | 2 | Fixed-data offset within the row's fixed region |
| `0x17` | 2 | Length (bytes) |

Bytes `0x07`–`0x0E` and `0x10`–`0x14` carry additional per-column flags/metadata LibRed does
not currently use.

Variable-length columns are assigned a *variable index* = their rank among variable columns
ordered by ascending column id (used by the row's variable-offset table, §5).

### 3.5 Index-data block (52 bytes)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Marker (`0x00000783` = 1923, or 0) |
| `0x04` | 30 | 10 column slots × (2-byte column id + 1-byte flags); column id `0xFFFF` = unused; flag `0x01` = ascending |
| `0x22` | 1 | Usage-map row |
| `0x23` | 3 | Usage-map page |
| `0x26` | 4 | **B-tree root page** |
| `0x2E` | 2 | Flags: `0x01` unique, `0x08` required, `0x80` always-set (Access 2000+) |

### 3.6 Index-info block (28 bytes) — one per *logical* index

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Marker (`0x00000659` = 1625, or 0) |
| `0x04` | 4 | Logical index number |
| `0x08` | 4 | Index-data block number this logical index uses |
| `0x0C` | 1 | Foreign-key index type |
| `0x0D` | 4 | Foreign-key index number |
| `0x11` | 4 | Foreign-key table page (non-zero ⇒ a relationship index) |
| `0x15` | 1 | Update action |
| `0x16` | 1 | Delete action |
| `0x17` | 1 | Index type (`1` = primary) |

The index **name** read at the same ordinal applies to this logical index. To name the
physical (data-block) index, prefer a real index's name over a foreign-key relationship's
(distinguished by `0x11` ≠ 0), and take `IsPrimaryKey` from the type byte `0x17`.

---

## 4. Data page — type `0x01`

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Page type `0x01` |
| `0x02` | 2 | Free space |
| `0x04` | 4 | Owning table's TDEF page — **or** the ASCII marker `LVAL` (`0x4C41564C`) for long-value pages |
| `0x0C` | 2 | Row count on this page |
| `0x0E` | 2×N | Row slot directory: one 2-byte entry per row |

Row slot entry: lower 13 bits (`& 0x1FFF`) = the row's byte offset in the page; `0x8000` =
deleted, `0x4000` = overflow/lookup pointer (not an inline row). Rows are packed from the end
of the page backward, so a slot runs from its offset up to where the previous slot's row began.

---

## 5. Row record format

```
[ colCount : 2 ]
[ fixed-length column data ... ]
[ variable-length column data ... ]
[ variable-offset table : (numVarCols + 1) × 2, stored end-first ]
[ numVarCols : 2 ]
[ null bitmap : ceil(colCount / 8) bytes ]      ← the very end of the row
```

- **Null bitmap** is indexed by **column id**; a **set bit = the value is present** (non-null).
- **Fixed** column value is at `rowStart + 2 + fixedOffset`, `length` bytes.
- **Variable** column value: with `varTableStart = rowEnd − nullBitmapSize − 2 − (numVarCols+1)×2`,
  variable column `j` spans `[offset(numVarCols − j), offset(numVarCols − j − 1))`, where
  `offset(k)` is the little-endian 16-bit value at `varTableStart + k×2`. (The table is stored
  end-first, i.e. ascending column-id order maps to descending table index.)
- **Booleans** carry **no data** — the value *is* the null-bitmap bit (set = true). Boolean
  columns are never null.
- Variable offsets are **always 2 bytes** in Jet 4 / ACE, at any row size. There is **no
  jump table** (that is a Jet 3 construct for its 1-byte offsets).

---

## 6. Data types

| Code | Type | Storage / decode |
| --- | --- | --- |
| `0x01` | Boolean | A bit in the null bitmap (no data) |
| `0x02` | Byte | 1 byte |
| `0x03` | Int16 | 2 bytes LE |
| `0x04` | Int32 | 4 bytes LE |
| `0x05` | Currency | int64 LE, scaled: value / 10000 |
| `0x06` | Single | 4-byte IEEE |
| `0x07` | Double | 8-byte IEEE |
| `0x08` | DateTime | 8-byte IEEE double, OLE-automation epoch (1899-12-30) |
| `0x09` | Binary | raw bytes |
| `0x0A` | Text | UTF-16LE, or compressed Unicode (§7); inline ≤ 255 chars |
| `0x0B` | OLE | long value (§8) |
| `0x0C` | Memo | long value (§8); text once resolved |
| `0x0F` | GUID | 16 raw bytes |
| `0x10` | FixedPoint (Numeric/Decimal) | not yet decoded |
| `0x12` | Complex (multi-value / attachment) | descriptor parsed; contents not materialized (out of scope for SQL/EF) |

---

## 7. Compressed Unicode

A text value that begins with the 2-byte marker `FF FE` is **compressed**: the following bytes
are one per character (ASCII range), not UTF-16. Otherwise the value is UTF-16LE. Applies to
both `Text` and resolved `Memo`.

> Not yet handled: the full format can toggle between 1-byte and 2-byte runs mid-string for
> mixed scripts. LibRed handles the common all-compressed case.

---

## 8. Long values (Memo / OLE)

The in-row value for a Memo/OLE column is a **12-byte descriptor**, not the data:

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 3 | Length (24-bit) |
| `0x03` | 1 | Flags |
| `0x04` | 1 | Row |
| `0x05` | 3 | Page |
| `0x08` | 4 | reserved |

Flags:
- `0x80` **inline** — the payload follows the descriptor in the row.
- `0x40` **single LVAL page** — the row at (page, row) *is* the whole payload.
- otherwise **multi-page** — the payload is chained across LVAL pages; each chunk's row begins
  with a 4-byte pointer (row + 3-byte page) to the next chunk, followed by chunk data.

LVAL pages are data pages (type `0x01`) whose owner field (`0x04`) is the ASCII marker `LVAL`.

---

## 9. Usage maps

Each table has an *owned-pages* usage map, referenced from TDEF `0x37` (1-byte row + 3-byte
page) pointing at a row on a data page. The first byte of that row is the map type.

**Inline map (type `0x00`):**

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Type `0x00` |
| `0x01` | 4 | Start page |
| `0x05` | … | Bitmap; bit `i` ⇒ page `startPage + i` is owned |

**Reference map (type `0x01`, for very large tables):** the row is a list of 4-byte pointers
to dedicated **bitmap pages** (type `0x05`). Pointer `k` (zero ⇒ none) points at a bitmap page
covering the page range starting at `k × (pageSize − 4) × 8`; on a bitmap page the bitmap data
begins at **offset 4**.

> The usage map is authoritative: a brute-force owner-scan can over-count, because deleted/
> orphaned pages can retain a stale owner stamp that the map correctly omits.

---

## 10. Index B-tree pages — types `0x03` (node) and `0x04` (leaf)

### 10.1 Header

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Page type (`0x03` node / `0x04` leaf) |
| `0x04` | 4 | Owning table TDEF page |
| `0x14` | 4 | **Child-tail** page (node pages: the rightmost child, referenced by no entry) |
| `0x18` | 2 | Compressed-byte count (shared key prefix length, §10.3) |
| `0x1B` | … | Entry-position bitmask |
| `0x1E0` | — | Start of entry data |

### 10.2 Entries

The entry bitmask (`0x1B` up to `0x1E0`) is a bitmap whose set bits, read in order, give the
**end offsets** of successive entries within the entry-data region. Entry `n` spans
`[prevEnd, end_n)` relative to `0x1E0` (first entry starts at 0).

Each entry ends with a **4-byte big-endian** trailing pointer:
- **Leaf:** `pointer` is a row id — page = `pointer >> 8`, row = `pointer & 0xFF`.
- **Node:** the trailing 4 bytes are the **child page**; recurse into it. After all entries,
  also recurse into the header's child-tail page (`0x14`).

### 10.3 Prefix compression

Entries on a page share a leading key prefix of `compressedByteCount` (`0x18`) bytes. The
**first** entry is stored in full; its first `compressedByteCount` bytes are the shared prefix,
which every subsequent entry omits. Reconstruct: `fullKey = prefix ++ storedKey`. (The trailing
pointer is never compressed, so reading row pointers needs none of this.)

### 10.4 Key encoding (order-preserving)

Each key column is encoded so that raw byte comparison equals value comparison. Non-boolean
columns are prefixed by a **flag byte**:

| | Ascending | Descending |
| --- | --- | --- |
| value present | `0x7F` | `0x80` |
| null | `0x00` | `0xFF` |

Then the value, transformed:

- **Integers** (Byte/Int16/Int32) and **Currency** (int64): big-endian, with the **sign bit of
  the first byte flipped**. Descending additionally inverts all bytes. (Decode reverses this.)
- **Single / Double / DateTime** (IEEE): if non-negative, flip the first bit; if negative,
  invert all bytes (ascending). Decode: first byte's top bit set ⇒ was positive (un-flip);
  else ⇒ was negative (invert all). DateTime is the resulting double via the OLE epoch.
- **Boolean:** no flag byte — a single constant: ascending `0x00` = true, `0xFF` = false
  (true sorts first).
- **Text / Binary / GUID:** Jet's collation encoding, which is **lossy** (case/diacritics
  folded) and **not reversible**. LibRed extracts row pointers from such indexes and decodes
  the leading reversible columns, but cannot recover text key *values*. (A future keyed *seek*
  needs the *encoder* + collation tables, not a decoder.)

---

## 11. System catalog

- **MSysObjects** (TDEF at page **2**) lists every object. Columns include `Id`, `Name`,
  `Type`, `Flags`, `ParentId`. For a **table** object (`Type == 1`), **`Id` is the table's TDEF
  page number**. An object is a system object if `Flags & 0x80000002` is set, or its name
  begins with `MSys`/`~`. Bootstrap: build a TableDef for MSysObjects from page 2 and read its
  rows like any table.
- **MSysRelationships** defines foreign keys (one row per relationship column): `szRelationship`
  (name), `szObject` (child/referencing table), `szColumn` (child column), `szReferencedObject`
  (parent table), `szReferencedColumn`, `icolumn` (order), `grbit` (flags: `0x02` don't-enforce,
  `0x100` cascade-update, `0x1000` cascade-delete).

---

## 12. Version differences

**Jet 3 (Access 97) is the odd one out** and is *not yet implemented*. Known differences from
the Jet 4 / ACE layout documented above:

- 2048-byte pages.
- 18-byte column descriptors (vs 25).
- 1-byte ASCII name lengths (vs 2-byte UTF-16).
- 1-byte row column-count (vs 2-byte), and 1-byte variable-offset entries **with a jump table**
  for rows > 256 bytes.
- Different data-page and TDEF header offsets.

Jet 4 and all later ACE versions (12/14/16/17) share the structural layout above; differences
between *those* are additive at the type/feature level (new data types, encryption schemes),
not the page offsets. In LibRed this is reflected by `JetFormatBase` virtual members with
Jet 4/ACE defaults; a future `Jet3Format` overrides the ones that differ.

---

## Provenance

Verified against: `Northwind.accdb` (ACE 2007), a generated 200-column ACCDB (multi-page TDEF),
and a generated ~150 MB ACCDB (reference usage map). Cross-referenced with mdbtools
`src/libmdb/` (`table.c`, `data.c`, `index.c`) and Jackcess (`TableImpl`, `ColumnImpl`,
`IndexData`, `IndexCodes`). The LibRed test suite (`test/LibRed.Core.Tests/`) pins these
structures, including whole-database golden dumps.
