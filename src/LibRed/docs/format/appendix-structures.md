# Appendix — on-disk structures (quick reference)

Field-layout tables for every on-disk structure, with **no prose** — a fast lookup. Each
structure links to the file with the verified detail (edge cases, write rules).
All integers little-endian unless noted; offsets are hex, relative to the structure's start.

---

## Page header (first word = page type)

| Word at `0x00` (LE) | Page type |
| --- | --- |
| `0x0100` | Database definition (page 0 only) |
| `0x0101` | Data page (also LVAL long-value pages) |
| `0x0102` | Table definition (TDEF) |
| `0x0103` | Index B-tree node |
| `0x0104` | Index B-tree leaf |
| `0x0105` | Page-usage bitmap |
| `0x0106`, `0x0107` | Unknown — never seen; ACE reads them as row-bearing |
| `0x0108` | Released table definition — a dropped table's TDEF, otherwise unchanged |
| `0x0109` | Released data page — emptied by DELETE, or a packed LVAL page emptied of its values |

---

## Page 0 — database definition → [page-00](page-00-database.md)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 2 | Page type `0x0100` |
| `0x02` | 2 | Unknown (zero) |
| `0x04` | 15 | Format id ASCII: `Standard Jet DB` (`0x00`/`0x01`) / `Jet System DB` (`0x01`) / `Standard ACE DB` (`0x02`+) |
| `0x13` | 1 | NUL terminator of the id string |
| `0x14` | 1 | Version byte (`0x00` Jet3, `0x01` Jet4, `0x02` ACE12, `0x03` ACE14, `0x04` ACE15/2013 never emitted and refused by ACE, `0x05` ACE16, `0x06` ACE17) |
| `0x15` | 1 | Version minor byte (`0x01` on a file created as ACE14/Access 2010, else `0x00`; a version raise writes `0x00`) |
| `0x16` | 2 | Unknown (zero) |
| `0x18`–`0x98` | 128 | **Obfuscated header** — XOR'd with the RC4 keystream of the key `C7 DA 39 6B`; the `0x18`–`0x72` fields below are offsets into it (Jet3 masks 126) |
| `0x18` | 4 | Global free-pages map pointer `[row:1][page:3]` (`0x00000100` = page 1 row 0) |
| `0x1C` | 4 | Global released-pages map pointer `[row:1][page:3]` (`0x00000101` = page 1 row 1) |
| `0x20`–`0x2C` | 4×4 | Catalog bootstrap pointers — `MSysObjects`/`MSysACEs`/`MSysQueries`/`MSysRelationships` TDEF pages (`2`/`3`/`4`/`5`); `0x20` = catalog root |
| `0x30`–`0x3B` | 12 | Reserved (zero) |
| `0x3C` | 2 | ANSI code page (LE; `0x04E4` = 1252) |
| `0x3E` | 4 | Database/encryption key (`0` = not encrypted) |
| `0x42` | 40 | Password (Jet4; Jet3 = 20) — also XOR `(int)creationDate` |
| `0x6A` | 4 | Creating engine's build number (`0x000011A6` = 4518 on everything ACE writes; Jet 4 files carry their `msjet40.dll` build) |
| `0x6E` | 4 | Collation, a 32-bit LCID with the version in its top byte: LANGID (2, LE), sort id at `0x70`, sort-order version at `0x71` (`0` legacy table, `1` Access-2010) |
| `0x72` | 8 | Creation timestamp — OLE `double` (days from 1899-12-30) |
| `0x98` | 4 | Fixed constant `0x00000654` (past the masked window) |
| `0x9C` | 4 | Engine version string `"4.0"` (ASCII, NUL-term) |
| `0xA0`–`0x298` | … | Zero padding |
| `0x299` | 2 | `EncryptionInfo` blob length (LE) — nonzero ⇒ ACE encrypted, `0` ⇒ not |
| `0x29B` | *len* | `EncryptionInfo` descriptor — **binary** (Office Standard/CryptoAPI RC4·AES) or `04 00 04 00`-prefixed **XML** (Agile) |
| `0x29B+len`–`0xDFF` | … | Zero padding |
| `0xE00` | 512 | User commit-byte table (256 × 2 bytes; idle slot `00 01`) |

---

## Data page — type `0x0101` → [page-01](page-01-data-and-rows.md)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 2 | Page type `0x0101` |
| `0x02` | 2 | Free-space count — bytes still free on the page |
| `0x04` | 4 | Owning TDEF page — or ASCII `LVAL` (`0x4C41564C`) for long-value pages |
| `0x08` | 4 | Jet4-only; zero except on the **first page of a long-value chain**, where it is the chain stamp matching the pointing descriptor's `0x08` |
| `0x0C` | 2 | Row count |
| `0x0E` | 2×N | Row-slot directory |

**Row-slot entry (2 bytes):** offset = `slot & 0x1FFF`; `0x8000` deleted; `0x4000` overflow/lookup.

**Inline row record → [page-01](page-01-data-and-rows.md):**
```
[colCount:2 = TDEF 0x29 high-water] [fixed data] [var data] [varOffsetTable:(numVar+1)×2] [numVar:2] [nullBitmap:ceil(colCount/8)]
```
Variable section (`varOffsetTable`+`numVar`) omitted only when the table has never had a variable column (TDEF `0x2B` = 0); `numVar` is that high-water. Null bitmap keyed by column id (set = present); a dead id's bit is set in a row the ALTER COLUMN re-lay rewrote and clear in one inserted afterwards. Booleans carry no data (the bit *is* the value). With the variable section omitted, `fixed data` is padded to a **minimum of 2 bytes** (a floor, not an alignment — an odd 3-byte region stays 3), making 5 the shortest record; ACE misreads anything shorter.

---

## TDEF header — type `0x0102` → [page-02a](page-02a-tdef.md)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 2 | Page type `0x0102` |
| `0x02` | 2 | Free-space count — bytes still free on the page |
| `0x04` | 4 | Next TDEF page (0 = single page) |
| `0x08` | 4 | TDEF length (total logical bytes) |
| `0x0C` | 4 | Constant marker `0x00000659` |
| `0x10` | 4 | Row count |
| `0x14` | 4 | AutoNumber high-water = last assigned id (assignment rules: [page-02a](page-02a-tdef.md) §3.1) |
| `0x18` | 4 | AutoNumber increment (signed int32; default 1) |
| `0x1C` | 4 | Complex-type AutoNumber high-water |
| `0x20` | 8 | Unknown / reserved (zero) |
| `0x28` | 1 | Table type: `0x4E` 'N' user / `0x53` 'S' system |
| `0x29` | 2 | Maximum column count (id high-water; lifetime cap 255) |
| `0x2B` | 2 | Variable-length column count (high-water) |
| `0x2D` | 2 | Live column count |
| `0x2F` | 4 | Logical index count |
| `0x33` | 4 | Real index count (index-data blocks) |
| `0x37` | 4 | Owned-pages usage-map pointer (1-byte row + 3-byte page) |
| `0x3B` | 4 | Free-pages usage-map pointer |
| `0x3F` | — | Start of index-statistics blocks |

**Continuation-page header (8 bytes, multi-page TDEF):** `[0x02][0x01][free:2][nextPage:4]`.

**Body order** (after header): index stats (`0x33`×12) · column descriptors (`0x2D`×25) · column names · index-data blocks (`0x33`×52) · index-info blocks (`0x2F`×28) · index names · per-long-value-column usage maps (×10) + `0xFFFF` terminator.

---

## Column descriptor — 25 bytes → [page-02b](page-02b-columns.md)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Data type (see codes below) |
| `0x01` | 2 | Marker `0x0659` |
| `0x03` | 2 | Unknown (zero) |
| `0x05` | 2 | Column id |
| `0x07` | 2 | Variable-table index — on a **fixed** column the running count of preceding variable columns, dropped ones included (so the `0x2B` high-water for an added column), **NOT `0`**; on a variable column its own slot index, which follows the `0x2B` high-water ([page-02b §3.4](page-02b-columns.md)) |
| `0x09` | 2 | Ordinal position (DAO `Field.OrdinalPosition`), ties allowed; the engine presents columns in descriptor order, which DAO keeps in step by moving the descriptor when it sets this. A second copy of the id `0x05` at creation on a **user** table, but **`0`** on the tables the engine writes for itself; unchanged by `DROP COLUMN` and by an `ALTER COLUMN` that burns a new id at `0x05`, ranked by `ADD COLUMN` ([page-02b §3.4](page-02b-columns.md)) |
| `0x0B` | 1 | Precision (Decimal) — else collation LANGID low byte (`0x09` en-US) |
| `0x0C` | 1 | Scale (Decimal) — else collation LANGID high byte (`0x04` en-US) |
| `0x0D` | 1 | Collation sort id — the LCID's high word (`0x01` = an alternate sort order, e.g. Hungarian Technical) |
| `0x0E` | 1 | Sort-order version (`0` legacy table, `1` Access-2010) |
| `0x0F` | 1 | Flags: `0x01` fixed, `0x02` updatable, `0x04` auto-number, `0x10` system-catalog column, `0x20` security-identifier column, `0x40` auto-number GUID, `0x80` hyperlink |
| `0x10` | 1 | Extended flags: `0x01` compressed-Unicode capable, `0x10` attachment value column, `0xC0` calculated; `0x04`/`0x08` on a complex flat table's columns |
| `0x11` | 4 | Unknown (zero) |
| `0x15` | 2 | Fixed-data offset within the row's fixed region |
| `0x17` | 2 | Length (bytes) |

Nullability is **not** in the descriptor — it's the `Required` property in `LvProp` (see [system-catalog](system-catalog.md)).

---

## Calculated-column value envelope — variable slot, *n* + 23 bytes → [page-02b §3.4a](page-02b-columns.md)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | VBA error number, little-endian — `0` for a value or Null; an error envelope is 38 bytes |
| `0x04` | 12 | Reserved (zero in every row observed) |
| `0x10` | 4 | Payload length, little-endian |
| `0x14` | *n* | Payload — the value in its ordinary encoding |
| `0x14`+*n* | 3 | Padding (zero in every row observed) |

The descriptor's type at `0x00` is a **promoted storage type**; the payload length says the real one
(Int16+1 = Boolean, Int32+1 = Byte, Int32+2 = Int16, Double+4 = Single). Length `0` means Null, and the
null-bitmap bit is set regardless — including for a calculated Boolean storing False. A calculated Memo is
declared Text with length `0`, owns a long-value map entry, and reaches the envelope through a long-value
descriptor.

---

## Index statistics block — 12 bytes, one per real index → [page-02d](page-02d-constraints.md)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Total entry count — set when the index is built or the file compacted; lowered by a delete and by an update that moves the entry, never raised by an insert |
| `0x04` | 4 | Unique entry count — raised by an insert bringing a new key; lowered by a delete of a key's last holder, and held to the total by an update |
| `0x08` | 4 | Reserved (zero) |

## Index-data block — 52 bytes, one per real index → [page-02d](page-02d-constraints.md)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Marker `0x00000783` |
| `0x04` | 30 | 10 column slots × (2-byte id + 1-byte flags); id `0xFFFF` = unused, flag `0x01` = ascending |
| `0x22` | 1 | Usage-map row |
| `0x23` | 3 | Usage-map page |
| `0x26` | 4 | B-tree root page |
| `0x2A` | 4 | Unknown / reserved (zero) |
| `0x2E` | 2 | Flags: `0x01` unique (on a foreign key's child block: the relationship is one-to-one), `0x02` ignore-nulls, `0x08` required, `0x80` always-set, `0x0200` complex column (always `0x0289` on one) |
| `0x30` | 4 | Unknown / reserved (zero) — trailing bytes of the 52-byte block |

## Index-info block — 28 bytes, one per logical index → [page-02d](page-02d-constraints.md)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Marker `0x00000659` |
| `0x04` | 4 | Logical index number (`index_num`) |
| `0x08` | 4 | Real index-data block ordinal (`index_num2`) |
| `0x0C` | 1 | FK type: `0x00` none, `0x01` incoming, `0x02` outgoing, `0x03` outgoing NO INDEX |
| `0x0D` | 4 | Matching logical block's `index_num` on the other table (`0xFFFFFFFF` = none) |
| `0x11` | 4 | FK table page (other table's TDEF; non-zero ⇒ relationship) |
| `0x15` | 1 | Update action: `0x04` plain, `0x00`/`0x01` no-cascade/cascade |
| `0x16` | 1 | Delete action (same encoding) |
| `0x17` | 1 | Index type: `0x00` secondary, `0x01` primary, `0x02` foreign |
| `0x18` | 4 | Unknown / reserved (zero) — trailing bytes of the 28-byte block |

---

## Long-value in-row descriptor — 12 bytes → [long-values](long-values.md)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Little-endian word: length in bits 0–29; flags in bits 30–31 (`0x80000000` inline, `0x40000000` single LVAL page, `0x00000000` chain) |
| `0x04` | 1 | Row |
| `0x05` | 3 | Page |
| `0x08` | 4 | **Chain stamp**, chained form only (zero on inline and single-page): must equal the first chain page's header `0x08`, or ACE refuses the record. ACE stamps `GetTickCount()`; the value is arbitrary, the agreement is not. LibRed stamps both and verifies them on read |

**Per-long-value-column usage-map list entry (10 bytes; list ends at `col_num == 0xFFFF`):**

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 2 | `col_num` (`0xFFFF` terminates) |
| `0x02` | 4 | `used_pages` pointer (row + page) |
| `0x06` | 4 | `free_pages` pointer (row + page) |

---

## Usage maps → [page-05](page-05-usage-maps.md)

**Inline (type `0x00`):** `[0x00][startPage:4][bitmap…]` — bit `i` ⇒ page `startPage+i` owned.
**Reference (type `0x01`, 69 bytes):** `[0x01][17 × 4-byte bitmap-page pointers]`.
**Bitmap page (page type `0x0105`):** header `[05 01][00 00]` — the type word, then two zero bytes — bitmap from offset 4.
Global maps, located by page 0: free pages at `0x18` (page 1 row 0 as ACE writes it) — set bit = **free**
(opposite of a table map); released pages at `0x1C` (page 1 row 1) — set bit = freed, not reusable until close.

---

## Index B-tree page header — types `0x0103` / `0x0104` → [page-03-04](page-03-04-index-btree.md)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 2 | Page type `0x0103` node / `0x0104` leaf |
| `0x02` | 2 | Free-space count — bytes still free on the page |
| `0x04` | 4 | Owning TDEF page |
| `0x08` | 4 | Jet4-inserted field (zero); shifts the following fields +4 vs Jet3 |
| `0x0C` | 4 | Previous leaf page (0 = leftmost); a split node's left sibling |
| `0x10` | 4 | Next leaf page (0 = rightmost) — load-bearing for Access's scan; a split node's right sibling |
| `0x14` | 4 | Child-tail page (node: rightmost child) |
| `0x18` | 2 | Compressed-byte count (shared key-prefix length) |
| `0x1A` | 1 | B-tree level/height (leaf must be `0`) |
| `0x1B` | … | Entry-position bitmask (set bits = entry end-offsets) |
| `0x1E0` | — | Start of entry data |

**Entry trailing pointer (4-byte big-endian):** leaf → row id (`page = ptr>>8`, `row = ptr&0xFF`); node → child page.

**Key flag byte:** present `0x7F` asc / `0x80` desc; null `0x00` asc / `0xFF` desc. (Boolean: the flag, then `0x00` true / `0xFF` false; descending inverts both.)

---

## Data-type codes → [data-types](data-types.md)

| Code | Type | | Code | Type |
| --- | --- | --- | --- | --- |
| `0x01` | Boolean | | `0x0A` | Text |
| `0x02` | Byte | | `0x0B` | OLE (long value) |
| `0x03` | Int16 | | `0x0C` | Memo (long value) |
| `0x04` | Int32 | | `0x0F` | GUID (16 bytes) |
| `0x05` | Currency (int64/10000) | | `0x10` | FixedPoint (Numeric/Decimal, 17 bytes) |
| `0x06` | Single | | `0x12` | Complex (multi-value/attachment) |
| `0x07` | Double | | `0x13` | Int64 / BIGINT (ACE 16 / 2016, variable) |
| `0x08` | DateTime (double, 1899-12-30) | | `0x14` | DateTimeExtended / DATETIME2 (ACE 17 / 2019+, 42-byte ASCII) |
| `0x09` | Binary | | | |

---

## Catalog tables → [system-catalog](system-catalog.md)

- **MSysObjects** (TDEF page 2): `Id`, `Name`, `Type` (1 table, 2 database, 3 container, 5 query/view, 6 linked table, 8 relationship; negative for Access documents — see system-catalog §11 *Object kinds*), `Flags`, `ParentId` (the container; `Name` is unique within it), `Owner`, `DateCreate`/`DateUpdate`, `LvProp` (property blob).
- **MSysACEs** (4 cols): `ObjectId`, `SID`, `ACM`, `FInheritable` — two rows per object.
- **MSysQueries** (8 cols): `ObjectId`, `Attribute`, `Flag`, `Name1`, `Name2`, `Expression`, `Order`, `LvExtra`; PK `(ObjectId, Attribute, Order)`.
- **MSysRelationships**: `szRelationship`, `szObject`, `szColumn`, `szReferencedObject`, `szReferencedColumn`, `icolumn`, `ccolumn`, `grbit` (`0x01` one-to-one, `0x02` don't-enforce, `0x100` cascade-update, `0x1000` cascade-delete, `0x2000` delete-set-null, `0x1000000`/`0x2000000` join type: all records from the parent / from the child). The cascades ACE applies come from the index-info blocks' `0x15`/`0x16`, not from these bits.
- **LvProp blob:** `MR2\0` signature, then `[int len][short type][body]` blocks; type `0x80` = name pool; value-block type `0x00` = table, `0x01` = column (`0x02` = index, unverified); per-owner entries are `[short entryLen][byte flags][byte dataType][short nameIndex][short valueLen][value]`. The flag byte is a bit field — `0x01` marks a definition-protected/schema property, and `0x80` also occurs in Access-written files — preserved whole per entry. `dataType` is any `JetDataType` code on any owner (text as `0x0A` or `0x0C`), and `valueLen`, not the type, gives the value's size (a Boolean or Int16 can be 4 bytes). Values include `DefaultValue`, `Required`, and `CheckConstraints`.

---

## Limits

Three distinct kinds: **structural** — the byte layout can't represent more, so
guard in the serializer; **engine constant** — a fixed-size buffer in ACE's reader (the format holds more),
so guard with a validator; **query-engine** — ACE's SQL-engine limits that LibRed deliberately exceeds.

| Limit | Value | Kind |
| --- | --- | --- |
| Object / table / field name | 64 chars | engine constant (ACE `WCHAR[64]`-style; >64 corrupts the file — [page-02a](page-02a-tdef.md) §3.3); no `. ! ` `` ` `` `[ ]`, no leading space, no control character |
| Fields per table | 255 | engine constant — the `0x29` column-id high-water, ids never reused ([page-02a](page-02a-tdef.md) §3.1) |
| Indexes per table | 32 | engine constant — binds on the **logical** count `0x2F`, not the real count `0x33` ([page-02d](page-02d-constraints.md) §3.5) |
| Fields per index / PK | 10 | structural (the 52-byte index-data block's fixed 10-slot column array — [page-02d](page-02d-constraints.md) §3.5) |
| Short Text length | 255 chars | validator |
| Record (excl. Long Text/OLE) | **4060 bytes** | ACE-enforced, not page space ([page-01](page-01-data-and-rows.md) §5) |
| Database file size | 2 GiB | ACE-enforced file extent; page numbering and reference-map coverage extend beyond this limit |

`LvProp` property **values** (DefaultValue, CheckConstraints) are variable-length and length-tolerant — no
fixed-buffer overrun like the name pool, so no storage cap to guard (the Access "255-char property" and
"2048-char validation rule" caps are DAO/UI limits, not the file format). ACE's query-engine limits (tables
per query 32, joins 16, `AND`s in WHERE 99, nested queries 50, SQL length ~64k) are the capabilities LibRed
exists to beat and are deliberately **not** guarded.

### Ceilings one structure imposes on another

The limits above are all stated where they bind. These are not: a field in **one** structure fixes a ceiling
that a writer of a **different** structure has to respect, and nothing in the second structure's layout says
so. Crossing one is silent — the write succeeds, the read succeeds, and the wrong row comes back — so the
table records how each ceiling is actually held, not merely that it exists.

| Narrow field | Ceiling it imposes | How it is held |
| --- | --- | --- |
| Index leaf entry addresses a row as `page << 8 \| row` — 1 byte of slot | **255 rows per data page**: the pointer allows 256 but **ACE writes at most 255**, not for space (a filled page keeps ~2,297 of 4,096 bytes free) and it drops the page from the free-pages map on reaching it | **Enforced at ACE's 255.** `FindPageWithRoom` refuses a page at `RowPointer.MaxRowsPerPage`, covering both the insert path and `WriteHiddenRow` (a relocation target is named by the same pointer). Overfilling costs more than indexed reads — ACE parses the full 16-bit count but caps at 256 slots, so it silently cannot see the rest of the page's rows |
| Long-value descriptor names its row in 1 byte (`d[4]`) | **256 rows per LVAL page** | **Enforced** in `TryAppend`. Unreachable in practice — a payload ≤ 64 bytes inlines, and the free-map drop at `MinLvalRow` (258 bytes free) caps a page at 104 rows even for the smallest thing that can arrive (a 33-character memo compressed to 35 bytes; compression is applied *after* the inline test, so the floor is below the 65 bytes the inline limit suggests) |
| Page numbers are 3 bytes in the TDEF usage-map pointer, the long-value descriptor (`d[5..7]`) and an LVAL chunk's next-pointer | **page < 2²⁴** (16,777,216) | **Safe with 32× headroom**, because `PageChannel.WritePage` enforces the 2 GiB file limit at 524,288 pages. The 24-bit fields are never the binding constraint |
| Usage-map pointer names its record row in 1 byte | **256 records per usage-map page** | **Safe by a louder guard.** A record is 69 bytes, so `AppendEmptyUsageMapRow`'s space check admits 57 and refuses the 58th — 4.5× tighter than the byte — and it throws rather than truncating |
| Reference usage map holds 17 bitmap-page slots | **~2.28 GB of page coverage** | **Enforced** — `NotSupportedException` on both the set-bit and inline→reference conversion paths. Just past the 2 GiB file limit, by design |
| Index page entry mask spans `0x1B`–`0x1E0` (453 bytes = 3,624 bits) | one bit per byte of entry data, which starts at `0x1E0` | **Exact fit, not slack**: entry data tops out at `4096 − 0x1E0` = 3,616 bytes, so the highest bit lands in the mask's last byte. `EntryDataOffset` is evidently chosen for this |
| Row slot offset is 13 bits (`0x1FFF` = 8,191) | offsets within a 4 KB page | Safe by 2×; the mask exists for the two flag bits above it |

The rule: **a ceiling is only safe if something refuses to cross it, or if a tighter guard fires first and
says so.** "The arithmetic doesn't reach it" is not a guard.
