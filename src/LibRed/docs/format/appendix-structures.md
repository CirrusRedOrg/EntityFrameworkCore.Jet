# Appendix — on-disk structures (quick reference)

Field-layout tables for every on-disk structure, with **no prose** — a fast lookup. Each
structure links to the file with the verified detail (edge cases, write rules, provenance).
All integers little-endian unless noted; offsets are hex, relative to the structure's start.

---

## Page header (first byte = page type)

| Byte `0x00` | Page type |
| --- | --- |
| `0x00` | Database definition (page 0 only) |
| `0x01` | Data page (also LVAL long-value pages) |
| `0x02` | Table definition (TDEF) |
| `0x03` | Index B-tree node |
| `0x04` | Index B-tree leaf |
| `0x05` | Page-usage bitmap |

---

## Page 0 — database definition → [page-00](page-00-database.md)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Page type `0x00` |
| `0x01` | 3 | Unknown (not decoded) |
| `0x04` | 15 | Format id ASCII: `Standard Jet DB` / `Standard ACE DB` |
| `0x13` | 1 | Unknown — string padding/terminator (not decoded) |
| `0x14` | 1 | Version byte (`0x00` Jet3, `0x01` Jet4, `0x02` ACE12, `0x03` ACE14, `0x05` ACE16, `0x06` ACE17) |
| `0x15` | 3 | Unknown — upper bytes of the version word (zero) |
| `0x18`+ | … | Obfuscated (code page, collation, creation date, password) — not decoded |

---

## Data page — type `0x01` → [page-01](page-01-data-and-rows.md)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Page type `0x01` |
| `0x01` | 1 | Flags (`0x01`) |
| `0x02` | 2 | Free space |
| `0x04` | 4 | Owning TDEF page — or ASCII `LVAL` (`0x4C41564C`) for long-value pages |
| `0x08` | 4 | Jet4-only, zero observed |
| `0x0C` | 2 | Row count |
| `0x0E` | 2×N | Row-slot directory |

**Row-slot entry (2 bytes):** offset = `slot & 0x1FFF`; `0x8000` deleted; `0x4000` overflow/lookup.

**Inline row record → [page-01](page-01-data-and-rows.md):**
```
[colCount:2 = maxColumnId+1] [fixed data] [var data] [varOffsetTable:(numVar+1)×2] [numVar:2] [nullBitmap:ceil(colCount/8)]
```
Variable section (`varOffsetTable`+`numVar`) omitted when the table has no variable columns. Null bitmap keyed by column id (set = present); dead ids' bits set. Booleans carry no data (the bit *is* the value).

---

## TDEF header — type `0x02` → [page-02a](page-02a-tdef.md)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Page type `0x02` |
| `0x01` | 1 | Flags (`0x01`) |
| `0x02` | 2 | Free space |
| `0x04` | 4 | Next TDEF page (0 = single page) |
| `0x08` | 4 | TDEF length (total logical bytes) |
| `0x0C` | 4 | Constant marker `0x00000659` |
| `0x10` | 4 | Row count |
| `0x14` | 4 | AutoNumber high-water = last assigned id (next = `+ 0x18`); seed `= 0x14 + increment` |
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
| `0x07` | 2 | Variable-table index (0 for fixed) |
| `0x09` | 2 | Column number (= id, until an `ALTER COLUMN` burns a new id at `0x05`) |
| `0x0B` | 1 | Precision (Decimal) — else locale low byte `0x09` |
| `0x0C` | 1 | Scale (Decimal) — else locale high byte `0x04` |
| `0x0D` | 2 | Sort-order version (0 = General legacy) |
| `0x0F` | 1 | Flags: `0x01` fixed, `0x02` updatable, `0x04` auto-number, `0x40` auto-number GUID, `0x80` hyperlink |
| `0x10` | 1 | Extended flags: `0x01` compressed-Unicode capable, `0xC0` calculated |
| `0x11` | 4 | Unknown (zero) |
| `0x15` | 2 | Fixed-data offset within the row's fixed region |
| `0x17` | 2 | Length (bytes) |

Nullability is **not** in the descriptor — it's the `Required` property in `LvProp` (see [system-catalog](system-catalog.md)).

---

## Index statistics block — 12 bytes, one per real index → [page-02d](page-02d-constraints.md)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Total entry count — compact-time only (`0` in a live-edited file) |
| `0x04` | 4 | Unique entry count — maintained live, cumulative, never decremented |
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
| `0x2E` | 2 | Flags: `0x01` unique, `0x02` ignore-nulls, `0x08` required, `0x80` always-set |
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
| `0x00` | 3 | Length (24-bit) |
| `0x03` | 1 | Flags: `0x80` inline, `0x40` single LVAL page, `0x00` multi-page chain |
| `0x04` | 1 | Row |
| `0x05` | 3 | Page |
| `0x08` | 4 | Reserved |

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
**Bitmap page (type `0x05`):** header `[0x05][0x01][0][0]`, bitmap from offset 4.
Global free-pages map: **page 1, row 0**, inline — set bit = **free** (opposite of a table map).

---

## Index B-tree page header — types `0x03` / `0x04` → [page-03-04](page-03-04-index-btree.md)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Page type `0x03` node / `0x04` leaf |
| `0x01` | 1 | Flags (`0x01`) |
| `0x02` | 2 | Free space |
| `0x04` | 4 | Owning TDEF page |
| `0x08` | 4 | Jet4-inserted field (zero); shifts the following fields +4 vs Jet3 |
| `0x0C` | 4 | Previous leaf page (0 = leftmost) |
| `0x10` | 4 | Next leaf page (0 = rightmost) — load-bearing for Access's scan |
| `0x14` | 4 | Child-tail page (node: rightmost child) |
| `0x18` | 2 | Compressed-byte count (shared key-prefix length) |
| `0x1A` | 1 | B-tree level/height (leaf must be `0`) |
| `0x1B` | … | Entry-position bitmask (set bits = entry end-offsets) |
| `0x1E0` | — | Start of entry data |

**Entry trailing pointer (4-byte big-endian):** leaf → row id (`page = ptr>>8`, `row = ptr&0xFF`); node → child page.

**Key flag byte:** present `0x7F` asc / `0x80` desc; null `0x00` asc / `0xFF` desc. (Boolean: no flag; `0x00` true, `0xFF` false.)

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
| `0x07` | Double | | `0x13` | Int64 / BIGINT (ACE 16, variable) |
| `0x08` | DateTime (double, 1899-12-30) | | `0x14` | DateTimeExtended / DATETIME2 (ACE 16, 42-byte ASCII) |
| `0x09` | Binary | | | |

---

## Catalog tables → [system-catalog](system-catalog.md)

- **MSysObjects** (TDEF page 2): `Id`, `Name`, `Type` (1 table, 5 query/view), `Flags`, `ParentId`, `Owner`, `DateCreate`/`DateUpdate`, `LvProp` (property blob).
- **MSysACEs** (4 cols): `ObjectId`, `SID`, `ACM`, `FInheritable` — two rows per object.
- **MSysQueries** (8 cols): `ObjectId`, `Attribute`, `Flag`, `Name1`, `Name2`, `Expression`, `Order`, `LvExtra`; PK `(ObjectId, Attribute, Order)`.
- **MSysRelationships**: `szRelationship`, `szObject`, `szColumn`, `szReferencedObject`, `szReferencedColumn`, `icolumn`, `ccolumn`, `grbit` (`0x02` don't-enforce, `0x100` cascade-update, `0x1000` cascade-delete).
- **LvProp blob:** `MR2\0` signature, then `[int len][short type][body]` blocks; type `0x80` = name pool; per-owner value maps carry `DefaultValue`, `Required`, `CheckConstraints`.

---

## Limits

Three distinct kinds (the useful mental model): **structural** — the byte layout can't represent more, so
guard in the serializer; **engine constant** — a fixed-size buffer in ACE's reader (the format holds more),
so guard with a validator; **query-engine** — ACE's SQL-engine limits that LibRed deliberately exceeds.

| Limit | Value | Kind |
| --- | --- | --- |
| Object / table / field name | 64 chars | engine constant (ACE `WCHAR[64]`-style; >64 corrupts the file — [page-02a](page-02a-tdef.md) §3.3) |
| Fields per table | 255 | structural (the `0x29` column-id high-water; never reused — [page-02b](page-02b-columns.md)) |
| Indexes per table | 32 | structural (`0x33` real-index count) |
| Fields per index / PK | 10 | structural (the 52-byte index-data block's fixed 10-slot column array — [page-02d](page-02d-constraints.md) §3.5) |
| Short Text length | 255 chars | validator |
| Record (excl. Long Text/OLE) | ~4000 bytes | structural (page space) |
| DB / table size | 2 GB | structural (page numbering; the reference usage map's 17 slots span just past it — [page-05](page-05-usage-maps.md)) |

`LvProp` property **values** (DefaultValue, CheckConstraints) are variable-length and length-tolerant — no
fixed-buffer overrun like the name pool, so no storage cap to guard (the Access "255-char property" and
"2048-char validation rule" caps are DAO/UI limits, not the file format). ACE's query-engine limits (tables
per query 32, joins 16, `AND`s in WHERE 99, nested queries 50, SQL length ~64k) are the capabilities LibRed
exists to beat and are deliberately **not** guarded.
