# Jet / ACE file format — LibRed reference

This is LibRed's own specification of the Microsoft Jet 4 / ACE (Access `.mdb` / `.accdb`)
on-disk format. **Every offset and structure here has been verified byte-for-byte against
real database files** (the Northwind ACE-2007 sample, a generated 200-column wide table,
and a generated ~150 MB large table) and cross-checked against mdbtools (its `HACKING.md`)
and Jackcess. Several structures are additionally verified from the **write** side: LibRed
produces them and Access's own OLE DB engine reads the result back (a LibRed-inserted row is
found by an Access indexed primary-key seek; encoded index keys match Access's stored bytes).
Where something is assumed or unverified, it says so explicitly.

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
| `0x01` | 3 | Unknown (not decoded) |
| `0x04` | 15 | Format identifier ASCII: `Standard Jet DB` or `Standard ACE DB` |
| `0x13` | 1 | Unknown — string padding/terminator (not decoded) |
| `0x14` | 1 | Version byte (see below). mdbtools reads `jet_version` as a 4-byte word at `0x14`; the version is its low byte and `0x15`–`0x17` are zero |
| `0x15` | 3 | Unknown — upper bytes of the version word (zero observed; not decoded) |
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
| `0x14` | 4 | **Highest AutoNumber value assigned** = the id of the last row inserted (the *next* id is this **`+ increment`**, see `0x18`); `0` when the table has no AutoNumber column. On a freshly created custom counter it is **`Seed - Increment`** so the first insert yields the `Seed` (verified: `COUNTER(1000, 7)` → `0x14` = `993`, first id `1000`). Verified **directly against `@@IDENTITY`**, and disambiguated from row count with a delete-gap: after inserting 3 rows, deleting id `3`, and inserting again (which is assigned id `4`, *not* reused `3`), `0x14` = `4` = the last inserted id while the row **count** is `3`. (Also: Northwind Categories = `8`, non-autonumber/text-PK tables = `0`.) mdbtools labels this *"Next autonumber value"* — that's **off by one**; the stored value is the last assigned, and the next id is `+ increment`. **Write requirement:** a writer inserting into an AutoNumber table must advance this to the max id it writes (LibRed does so in `RowInserter`); leaving it stale makes Access reissue an existing id and reject the insert as a duplicate primary key — verified end-to-end. |
| `0x18` | 4 | **AutoNumber increment** — a **signed 32-bit int** (same width as `0x14`); the step added to `0x14` for each new id. Default `1` (a plain `COUNTER`); a custom `COUNTER(seed, increment)` / `AUTOINCREMENT(seed, increment)` / `INTEGER IDENTITY(seed, increment)` sets it. **Confirmed a full int32, not a byte + 3 unknown** (verified vs ACE): `COUNTER(1, 300)` → `2C 01 00 00` (spans 2 bytes, ids `1, 301, 601`); `COUNTER(5, 100000)` → `A0 86 01 00` (3 bytes); and decisively `COUNTER(100, -5)` → `FB FF FF FF` = `-5` in two's-complement (all 4 bytes) with a **descending** sequence `100, 95, 90`. It reads `1` on every table (autonumber or not) because that is the default increment — mdbtools/Jackcess mislabel it a 1-byte constant / "autonumber enable" flag, which only *looks* right because the default increment is 1 (LibRed's own finding). The seed itself is not stored separately — it is recovered as `0x14 + increment` (correct on a freshly-created, un-inserted table). A writer/reader must treat it as a signed int32; the insert bump of `0x14` moves in the increment's direction (max for +, min for −) so a descending counter doesn't reissue an id. |
| `0x1C` | 4 | Complex-type AutoNumber (mdbtools `ct_autonum`) — the high-water value for a *complex* column (multi-value / attachment). `0` in every table observed; LibRed has no complex-column fixture to confirm a non-zero value (OLE DB DDL can't create such a column) |
| `0x20` | 8 | Unknown / reserved (zero observed) |
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
> not isolated: `0x659` recurs as a fixed marker at the start of each repeating TDEF record —
> column descriptor `+1` (2 bytes, §3.4), index-info block `+0` (4 bytes, §3.6), and this header
> slot (4 bytes) — while `0x783` (1923) marks each index-data block `+0` (§3.5). They are constant within and
> across files; mdbtools describes them as "usually 1625 / 1923 *or 0*", so they appear to be
> reserved record markers/tags. LibRed's *reader* ignores them, but they are part of the format
> and a *writer* must emit them — Access validates them when opening a table (see §3.7). `0x0C` is
> therefore **not** the code page — `1625` is not a valid code page, and the code page is a
> database-wide value on page 0, not per-table.
>
> **`0x659` is a fixed constant, not a per-TDEF "definition id".** mdbtools labels the `0x0C` word
> (and the column-descriptor `+0x01` / index-info `+0x00` markers) *"Matches definition block
> unknown field"*, which could suggest a per-table id that these locations cross-reference.
> Verified otherwise: `0x0C` reads `1625` on **all 33 tables** of Northwind — user, system,
> complex-type, and hidden data tables — and on freshly ACE-created tables, and the header value
> equals the first column-descriptor marker in every one. So the "match" is simply that a shared
> constant appears in each spot, not a table-scoped identifier. (The mdbtools "*or 0*" variant was
> not observed in any ACE table; it may be a Jet 3 or degenerate-record case.)

> ⚠️ `0x2F` vs `0x33`: these are equal for MSysObjects (which hid the distinction during
> reverse-engineering) but differ for user tables. `0x33` (real index count) sizes the
> index-data blocks **and** the `0x3F` pre-column block; `0x2F` (logical count) is the number
> of logical-index info blocks and index names. A relationship adds a *logical* index that
> shares a real index's data, so logical ≥ real.

> **`ADD` / `DROP COLUMN` are metadata-only edits — the three column counts behave differently
> (all probed vs ACE).** ACE never renumbers surviving columns or rewrites existing rows; a dropped
> column's bytes become dead space, and an added column reads NULL on old rows (via the null bitmap).
> - **`0x2D` column count** — the **live** count. `DROP COLUMN` decrements it; `ADD COLUMN` increments it.
> - **`0x29` maximum column count** — a **high-water** = the *next* column id to assign. `ADD COLUMN`
>   takes the current value as the new column's id, then increments `0x29`; `DROP COLUMN` **leaves it**
>   (dropped ids are **never reused**, so ids develop gaps, e.g. dropping id 1 leaves `0,2,3` and the next
>   add is `4` — verified by dropping the highest column and observing the next id still continues past it).
> - **`0x2B` variable-length column count** — also a **high-water**. `ADD COLUMN` of a variable column
>   increments it (the new column's variable index = the old value); `DROP COLUMN` of a variable column
>   **leaves it unchanged**, so survivors keep their stored variable index (§3.4) and existing rows keep the
>   same number of variable slots. (A fixed column doesn't touch `0x2B`.)
>
> An added **fixed** column's fixed offset is the current end of the fixed region (`max(offset+length)`);
> an added **variable** column appends. A dropped column's descriptor + name are removed from the column
> region; a dropped **memo/OLE** column's §3.3.2 entry is removed, an added one's is appended (its two page
> maps go on the table's usage-map page, or a dedicated page if that's full — the same rule as create).

### 3.2 Multi-page TDEFs

If a table has enough columns (or indexes), the definition spans pages chained by the `0x04` pointer.
Reassemble before parsing: take the **first page whole**, then append each continuation
page's bytes **from offset 8** (continuation pages have an 8-byte header). Column offsets are
absolute from the first page, so parsing is otherwise unchanged.

> **Writing a multi-page TDEF (verified vs ACE).** The 8-byte continuation header is
> `[0x02][0x01][free space: 2][next page: 4]` (page type, flags, then the same `0x02` free-space and
> `0x04` next-page fields as page 1). The **first page is filled completely** (free space `0`) and its
> `0x04` points to the first continuation; each continuation carries `PageSize − 8` bytes of definition
> data (from offset 8), the **last** one leaving the usual 8-byte trailing reserve — so its free space is
> `PageSize − 8 − dataLen − 8`. The definition-length field (`0x08`, on the first page) is the **total**
> length across all pages. LibRed writes this in `TableCreator.WriteDefinition`, used when `CREATE INDEX`
> grows a definition past one page (confirmed: a 30-column, 30-index table spills to one continuation
> page, `defLen 4115`, exactly as ACE writes it, and Access reads all 30 indexes).

### 3.3 Body layout (in order, after the header)

```
0x3F : index statistics      RealIndexCount(0x33) × 12 bytes   (per-index, §3.3.1)
       column descriptors    ColumnCount(0x2D)    × 25 bytes
       column names          ColumnCount          × (2-byte length + UTF-16LE)
       index-data blocks     RealIndexCount(0x33) × 52 bytes
       index-info blocks     LogicalIndexCount(0x2F) × 28 bytes
       index names           LogicalIndexCount    × (2-byte length + UTF-16LE)
       column usage maps     (per long-value column) × 10 bytes, then 0xFFFF  (§3.3.2)
```

### 3.3.2 Column usage-map list (trailing the index names)

After the index names comes a list of per-**long-value-column** (memo/OLE) usage-map pointers,
terminated by a `col_num` of `0xFFFF`. Iterate reading 10-byte records *until* `col_num == 0xFFFF`:

> **LVAL-only, despite mdbtools calling it "Variable Column Tracking".** Only **Memo (`0x0C`) and
> OLE (`0x0B`)** columns appear here — *not* plain **Text (`0x0A`)**, even though Text is
> variable-length — because only memo/OLE have their own long-value (LVAL) page chains that need
> usage maps; Text is stored inline in the row. Verified by correlating each entry with its column
> type: Categories → `{col2 Memo, col3 OLE}`, Employees → `{col14 OLE, col15 Memo}`, Suppliers →
> `{col11 Memo}`, and — the clincher — **Customers, with 11 Text columns and no memo/OLE, has an
> empty list**. So mdbtools' name is imprecise; the list is keyed to long-value columns.

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 2 | `col_num` — the column's index; `0xFFFF` terminates the list |
| `0x02` | 4 | `used_pages` pointer (1-byte row + 3-byte page) to the column's owned-pages usage map |
| `0x06` | 4 | `free_pages` pointer (1-byte row + 3-byte page) to its free-pages usage map |

The **definition length** (`0x08`) points just *past* the terminating `0xFFFF`, so the whole
list (terminator included) counts toward the definition, not free space.

> **The terminating `0xFFFF` is mandatory on write — even for a table with no long-value
> columns** (where the list is empty and the `0xFFFF` is the only bytes here). Omitting it makes
> Access reject the whole table with *"Unrecognized database format"* even though every other byte
> of the TDEF is valid — verified by byte-diffing an ACE-created single-index table against a LibRed
> one whose only difference was the missing terminator. LibRed's reader doesn't consume this list
> (it stops after the named indexes; long values are located via the in-row LVAL pointer, not these
> maps), but the terminator **must be written**. A table with memo/OLE columns must additionally
> allocate the usage-map records and emit a real `{col_num, used, free}` entry per long-value column
> — verified against Northwind's Categories (cols 2/3) and Employees (cols 14/15).

### 3.3.1 Index statistics block (12 bytes, one per real index)

The block at `0x3F`, in the same order as the index-data blocks (§3.5), holds per-index
statistics:

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Total entry count — **compact-time only** (see note): the row count in a *saved/compacted* file, but `0` in a live-edited one |
| `0x04` | 4 | **Unique entry count** — distinct entries ever added; maintained live on every insert (see note) |
| `0x08` | 4 | Reserved (zero observed) |

**These two fields are maintained very differently — verified with an ACE
insert/delete/insert sequence and against saved Northwind tables:**

- **Total entry count (`+0`) is *not* maintained on insert.** Access leaves it `0` through live
  inserts and only writes the row count on **compact/repair**. Saved Northwind tables read
  `total == rowCount` (Categories 8, Orders 830, Order Details 2155) precisely because they were
  compacted; a freshly SQL-inserted table reads `total == 0` while `rowCount` climbs. A writer
  should therefore **leave `+0` at `0`** on insert (LibRed does), not set it to the row count —
  doing so would falsely mark the file as compacted.
- **Unique entry count (`+4`) *is* maintained live and is cumulative** — Access increments it per
  insert and **never decrements** it. Verified: after 3 inserts it is `3`; after deleting a row it
  stays `3` (not decremented); after one more insert it is `4`. It equals the current
  distinct-value count only with no deletions. A **unique** index gains one distinct key per row,
  so a writer increments `+4` by one per insert per unique index (LibRed does this in
  `RowInserter`). A **non-unique** index should advance `+4` only when the inserted key is
  genuinely new — not yet handled (LibRed creates only unique indexes; see the
  `TODO(non-unique-index-stats)` marker). LibRed exposes `+4` as `IndexDef.UniqueEntryCount`.

### 3.4 Column descriptor (25 bytes)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Data type (see §6) |
| `0x01` | 2 | Record marker `0x0659` (see §3.1 note); ignored |
| `0x03` | 2 | Unknown (zero observed) |
| `0x05` | 2 | Column id |
| `0x07` | 2 | Variable-length table index — this column's position among the variable columns (0 for fixed columns) |
| `0x09` | 2 | Column number (equals the column id `0x05` in every file observed) |
| `0x0B` | 1 | Numeric **precision** (Decimal/Numeric columns); otherwise the low byte of the locale id, `0x09` |
| `0x0C` | 1 | Numeric **scale** (Decimal/Numeric columns); otherwise the high byte of the locale id, `0x04` |
| `0x0D` | 2 | Text sort-order **version** (mdbtools `misc_ext`; Jackcess's sort-order version) — the high half of a 4-byte sort-order descriptor whose low half is the locale at `0x0B` (`0x0409` = General, §10.4). `0` for **every** column (all types) in ACE 2007 — the General collation is version 0; see note |
| `0x0F` | 1 | Flags (see below) |
| `0x10` | 1 | Extended flags: `0x01` compressed-Unicode capable, `0xC0` calculated column |
| `0x11` | 4 | Unknown (zero observed) |
| `0x15` | 2 | Fixed-data offset within the row's fixed region |
| `0x17` | 2 | Length (bytes) |

**Flags (`0x0F`):** `0x01` fixed-length, `0x02` updatable, `0x04` auto-number,
`0x40` auto-number GUID, `0x80` hyperlink (on a Memo column).

> **Nullability is *not* in the descriptor.** The column's *Required* (NOT NULL) property is
> **not** encoded anywhere in the 25-byte descriptor — verified against Northwind: a nullable
> column (`Orders.ShippedDate`) and a non-null column of the same type (`Orders.OrderDate`) have
> **byte-identical** descriptors; the flag byte `0x0F` only ever distinguishes fixed-length
> (`0x01`), the always-set updatable bit (`0x02`), and auto-number (`0x04`), while the extended
> flags (`0x10`) and reserved bytes (`0x03`, `0x0D`) are zero for every column. The *Required*
> property instead lives in the table's **column-properties blob** (Jet's per-object extended
> properties, a.k.a. `LvProp`), which LibRed does **not** parse yet — *(location assumed from
> Jet's documented property storage, not yet verified against a file; needs a fixture with
> explicit Required columns to confirm)*. Consequently LibRed currently reports every column as
> nullable.

> `0x0B`–`0x0C` is a union keyed by type: for a Decimal/Numeric column (type `0x10`) it holds
> the **precision** (`0x0B`) and **scale** (`0x0C`) — verified with a `DECIMAL(12,3)` column,
> which reads precision = 12, scale = 3; for every other type it reads the constant `0x0409`
> (the en-US LCID / text collation).
>
> **Sort-order version (`0x0D`) — confirmed.** For non-Numeric columns, `0x0B`–`0x0E` form a 4-byte
> **sort-order descriptor**: locale `0x0409` (1033) at `0x0B`, **version** at `0x0D`. mdbtools states
> it explicitly (note after its index-record section): the encoding is the **"General" sort order in
> Access 2000–2007 = (1033, version 0)**; as of Access 2010 that is renamed **"General legacy"** and
> the new default **"General" = (1033, version 1)** is a *different* key encoding. So `0x0D` selects
> which collation the text keys use.
>
> Observed `0` on **every** column of ACE-2007 Northwind, *and* on a text column freshly created by
> the installed ACE 16 engine — because the file is ACE-2007 format (version byte `2`), the engine
> writes the version-0 "General legacy" order regardless of its own age. LibRed's `JetTextCollation`
> (§10.4) implements exactly this **version-0** order and its writer leaves `0x0D` at 0 — correct for
> ACE-2007 files. A **version-1** file (Access 2010+, version byte ≥ `3`, created with the default
> General order) would carry `0x0D = 1` and different index-key bytes that LibRed does not yet handle
> (no version-1 fixture available here to verify against).
>
> LibRed **reads** the variable-table index from the descriptor (`0x07`) rather than deriving it by
> ranking column ids. For an untouched table the two agree, but they **diverge after a `DROP COLUMN`**:
> ACE's drop is a metadata-only TDEF edit that does **not** renumber the surviving columns or rewrite
> existing rows, so a survivor keeps its original variable index even though ranking would shift it down
> into the gap. Deriving would then decode the wrong variable slot (verified: after dropping a middle
> Text column, the next Text column read the dropped column's value); reading `0x07` decodes correctly.

Variable-length columns carry a *variable index* — their position in the row's variable-offset table
(§5), stored in the descriptor at `0x07`. For an untouched table this equals their rank among variable
columns ordered by ascending column id, but a `DROP COLUMN` can leave a gap (see the note above).

### 3.5 Index-data block (52 bytes)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Marker (`0x00000783` = 1923, or 0) |
| `0x04` | 30 | **Exactly 10** column slots × (2-byte column id + 1-byte flags); column id `0xFFFF` = unused; flag `0x01` = ascending. This fixed array (no count field, no continuation) is why a Jet/ACE index — and therefore any `PRIMARY KEY`, `UNIQUE`, or `FOREIGN KEY` built on one — is **limited to 10 columns**. |
| `0x22` | 1 | Usage-map row |
| `0x23` | 3 | Usage-map page |
| `0x26` | 4 | **B-tree root page** |
| `0x2A` | 4 | Unknown / reserved (zero observed). mdbtools places a 1-byte index-flags field at `+0x2A`, but ACE's effective flags are at `0x2E` and this is zero in every file checked |
| `0x2E` | 2 | Flags: `0x01` unique, `0x02` ignore-nulls (`WITH IGNORE NULL` — null-keyed rows excluded from the index), `0x08` required (`WITH DISALLOW NULL` / part of a primary key), `0x80` always-set (Access 2000+). Verified vs ACE: a plain index is `0x0080`, `IGNORE NULL` `0x0082`, `DISALLOW NULL` `0x0088`, a PK `0x0089`. |
| `0x30` | 4 | Unknown / reserved (zero observed) — trailing bytes of the 52-byte block |

> **Unique (`0x01`) enforcement treats NULLs as distinct (verified vs ACE).** A `UNIQUE` index (that is
> **not** `WITH IGNORE NULL`) rejects a duplicate **non-null** key but permits **multiple NULL** keys — two
> rows may both be null in the indexed column(s). So uniqueness is enforced only over the non-null keys; a
> row with a null in any indexed column is exempt (matching SQL's "nulls are distinct"). LibRed enforces
> this on insert/update (`IndexWriter.KeyExists`, skipping null-keyed rows). A `WITH IGNORE NULL` index
> (`0x02`) additionally leaves null-keyed rows out of the B-tree entirely; a PK (`0x08` required) forbids
> nulls, so the question doesn't arise.

A table has **at most 32 index-data blocks** (the `0x33` count, §3.1) — the Jet/ACE "32 indexes per
table" limit, counting the indexes that back primary keys, unique constraints and the child side of
relationships. (Incoming relationships add *logical* index-info blocks, §3.6, which reuse an existing
data block and so do not count toward this.)

### 3.6 Index-info block (28 bytes) — one per *logical* index

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Marker (`0x00000659` = 1625, or 0) |
| `0x04` | 4 | **Logical index number** (`index_num`) — a unique id per logical index, `0 … logicalCount-1` |
| `0x08` | 4 | **Real index-data block ordinal** (`index_num2`) this logical index uses, `0 … realIndexCount-1` |
| `0x0C` | 1 | Foreign-key index type: `0x00` = none, `0x01` = **incoming** (this table is the parent/referenced end), `0x02` = **outgoing** (this table is the child/referencing end), `0x03` = **outgoing, `FOREIGN KEY NO INDEX`** (verified vs ACE: identical to `0x02` — same data block and same parent incoming `0x01` block — only this type byte differs) |
| `0x0D` | 4 | Foreign-key index number: the `index_num` (`0x04`) of the **matching logical block on the other table**; `0xFFFFFFFF` when not a relationship |
| `0x11` | 4 | Foreign-key table page (the *other* table's TDEF page; non-zero ⇒ a relationship index) |
| `0x15` | 1 | Update action: `0x04` plain index; on a relationship `0x00` = no cascade, `0x01` = cascade update |
| `0x16` | 1 | Delete action: `0x04` plain index; on a relationship `0x00` = no cascade, `0x01` = cascade delete |
| `0x17` | 1 | Index type: `0x00` = plain secondary, `0x01` = primary, `0x02` = foreign/relationship |
| `0x18` | 4 | Unknown / reserved (zero observed) — trailing bytes of the 28-byte block |

The index **name** read at the same ordinal applies to this logical index. To name the
physical (data-block) index, prefer a real index's name over a foreign-key relationship's
(distinguished by `0x11` ≠ 0), and take `IsPrimaryKey` from the type byte `0x17`.

> **Writing a relationship's logical blocks — verified by having ACE create a minimal `P1(Id PK)` /
> `C1(Id PK, Pid FK→P1.Id)` pair and diffing.** The **child** (referencing) table gives its FK-column
> index a *single* logical block that **is** the relationship: `index_num2` → the FK-column data
> block, `0x0C = 0x02` (outgoing), `0x11` = parent page, `0x17 = 0x02`, name = the constraint name.
> The **parent** (referenced) table gains an **extra** logical block beyond its data blocks:
> `index_num2` → its referenced-key (PK) data block, `0x0C = 0x01` (incoming), `0x11` = child page,
> `0x17 = 0x02`, name = an auto-generated hidden `.r?` name. The two ends cross-reference: each block's
> `0x0D` holds the other block's `index_num` (`0x04`). Logical blocks are stored **sorted by name**;
> `index_num` is assigned in creation order (a table's own indexes first, then relationships as added —
> so a parent's Nth incoming relationship gets `index_num` = its logical count before the insert).
> Cascade `ON UPDATE`/`ON DELETE` set `0x15`/`0x16` to `0x01` on **both** ends' blocks.
>
> **Self-reference** (a table whose FK targets itself): both ends live in the **one** TDEF, each with
> `0x11` = the table's own page — an outgoing `0x02` block (`index_num2` → the FK-column index) and an
> incoming `0x01` block (`index_num2` → the referenced-key index), cross-referenced by `index_num`. The
> incoming block is numbered after the data-block logical indexes (`index_num` = data-block count).
> Verified byte-for-byte against an ACE-created self-reference.

> **`index_num` (`0x04`) vs `index_num2` (`0x08`) — verified against Northwind.** `0x04` is the
> logical index's own unique number; `0x08` is the ordinal of the **real index-data block** (§3.5)
> it maps to. They differ because **several logical indexes share one real block**: a relationship
> (`0x11` ≠ 0) reuses the real index on *this table's* side of the foreign key rather than owning its
> own. Confirmed on Orders (7 real / 13 logical): real block 3 = `OrderID` `PK_Orders` is referenced
> both by its own PRIMARY logical block (`index_num2 = 3`) and by an **incoming** relationship
> (`index_num2 = 3`, `fkTablePage` = Order Details), while the **outgoing** FK relationships map to
> the child-column real indexes (CustomerID/EmployeeID/ShipVia). So `index_num2` points to the real
> index on this table's side — the child FK column for an outgoing FK, the referenced key (PK) for an
> incoming one — which is exactly mdbtools' "index into index cols list". LibRed's reader keys off
> `0x08` (its `DataNumber`) and does not use `0x04`.

### 3.7 Writing a TDEF Access accepts (in progress)

Every field documented above is part of the format and must be written — **including the constants
and markers the reader ignores** (`0x01` flags, the `0x0659`/`0x0783` markers, the en-US locale
`0x0409`, the `0x80`/`0x08` index-flag bits, …). The reader being lenient about a field does **not**
make it optional on write; Access validates them when it opens the table. With every documented
field populated, a LibRed-written TDEF matches an ACE-created one **byte-for-byte** (verified by
diffing; only page numbers and the auto-generated index name differ).

Only a few fields are *not* fixed constants and so warrant a write note:

- **Definition length** (`0x08`) — the byte offset just past the last structure written.
- **Free space** (`0x02`) — `page size − definition length − 8` (Access reserves an 8-byte
  continuation header).
- **Index-info update/delete actions** (`+0x15/+0x16`, §3.6) — `0x04` on a plain primary key (no
  relationship); the FK index number (`+0x0D`) is `0xFFFFFFFF` when there is no foreign key.
- **Usage maps** — Access keeps *both* an owned-pages map (`0x37`) and a free-pages map (`0x3B`);
  an indexed table adds a third usage-map record (the index's own pages, §3.5 `+0x22`) covering the
  index root. A **fresh table has no data page** (Access allocates the first lazily on the first
  insert), so all of these maps start **empty** — `[0x00][startPage = 0][all-zero bitmap]` inline
  records — and the usage-map page's own owner field is `0`. LibRed's `IndexWriter` navigates the
  B-tree structurally (root child-pointers + leaf next-pointers) and **never updates the per-index
  usage map**, so index pages allocated by a split are not reflected in it — Access reads the index
  regardless (verified). Consequently a new index only needs its usage-map **record to exist** (an
  empty inline map); when an index is **added to a populated table**, LibRed *appends* that one record
  to the existing usage-map page (preserving the data/other-index records) rather than rewriting it,
  then **back-fills** the index B-tree by scanning every existing row (`AddEntry` per row). Verified
  vs ACE: a primary key added after data enforces uniqueness and seeks correctly, incl. a 2000-row
  back-fill that splits the tree into multiple levels.

> **Access now opens and round-trips a LibRed-created table** (empty `COUNT`, `INSERT`, read-back —
> verified through the ACE OLE DB provider). Getting there required *all* of the following together;
> each was independently necessary (removing any one reproduces "Unrecognized database format"):
>
> 1. **TDEF byte-validity** — every constant/marker written (§3.1), and the **trailing `0xFFFF`
>    index-name terminator** included in the definition length (§3.3). This terminator was the last
>    blocker found: with it omitted the TDEF was otherwise byte-identical to ACE's yet still rejected.
> 2. **Global page allocation** — pages must be taken from the database's **global free-pages map**
>    (§9.1), not by blindly growing the file, so Access accounts for them. LibRed allocates by
>    clearing a free bit there.
> 3. **Lazy data page** — match Access's model of a fresh table with *no* data page and empty usage
>    maps (above). The first insert (LibRed's or Access's) allocates the data page on demand and sets
>    its bit in both the table's owned- and free-pages maps.
> 4. **Catalog rows** — a complete MSysObjects row with its indexes maintained (§11) and the
>    MSysACEs permission rows, so Access resolves the table by name before it opens it.

---

## 4. Data page — type `0x01`

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Page type `0x01` |
| `0x01` | 1 | Flags (observed constant `0x01`; the same byte appears on TDEF and index pages — verified) |
| `0x02` | 2 | Free space |
| `0x04` | 4 | Owning table's TDEF page — **or** the ASCII marker `LVAL` (`0x4C41564C`) for long-value pages |
| `0x08` | 4 | Jet4-only; purpose unknown — **zero** on every page observed (data, usage-map, LVAL). Jet3 has the row count here instead (which is why Jet4's row count sits 4 bytes later). LibRed writes zero. |
| `0x0C` | 2 | Row count on this page |
| `0x0E` | 2×N | Row slot directory: one 2-byte entry per row |

Row slot entry: lower 13 bits (`& 0x1FFF`) = the row's byte offset in the page; `0x8000` =
deleted, `0x4000` = overflow/lookup pointer (not an inline row). Rows are packed from the end
of the page backward, so a slot runs from its offset up to where the previous slot's row began.

> **Inserting a row** (verified — Access reads the result): place the record at
> `lowestRowOffset − recordLength` (just below the current lowest row, or `pageSize − recordLength`
> on an empty page), append its offset as a new slot at `0x0E + rowCount×2`, increment the row
> count (`0x0C`), and decrease free space (`0x02`) by `recordLength + 2` (record bytes plus the
> slot entry). The new row's **row id** is `(thisPage, oldRowCount)`. The fixed-region length of
> the encoded record must match the table's existing rows — read it off any existing row's
> variable-offset table (its last entry is the variable-data start = `2 + fixedRegionLength`).
> A row is found by **table scan** as soon as it is in the heap, but an **indexed lookup** (and
> Access's PK seek) misses it until it is also added to every index B-tree (§10.4).

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
  columns are never null, and they occupy **no fixed-region bytes**: their descriptor's fixed
  offset is 0 and the fixed offsets of other columns skip over them. (Verified: Northwind's
  `Products.Discontinued` is `fixed@0` even though it follows several fixed columns. A writer
  must therefore *not* advance the fixed offset for a Boolean column.) On **write**, the value
  reaches the encoder as a bool *or* a number (a `bit` column is commonly inserted as `1`/`-1`/`0`
  or defaulted from `"0"`), so LibRed coerces it with Access truthiness — **any non-zero number (or
  bool true) sets the bit**, `0`/false clears it. Verified: Access reads LibRed-written bits back
  correctly and a bare-boolean predicate returns the right rows.
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
| `0x10` | FixedPoint (Numeric/Decimal) | 17 bytes: sign byte (`0x80` = negative) + 128-bit magnitude (four 32-bit little-endian words, low word last); value = magnitude / 10^scale. Precision/scale from the column descriptor (§3.4) |
| `0x12` | Complex (multi-value / attachment) | descriptor parsed; contents not materialized (out of scope for SQL/EF) |
| `0x13` | Int64 — **BIGINT** (ACE 16) | 8-byte little-endian signed integer. Stored as a *variable*-length column |
| `0x14` | DateTimeExtended — **DATETIME2** (ACE 16) | fixed 42-byte ASCII `<day>:<time>:<precision>` (see below) |

**ACE 16 types.** Office 2016 (the `Microsoft.ACE.OLEDB.16.0` engine, which creates version-byte
`0x06` databases) added `BIGINT` and `DATETIME2`. `DATETIME2` is a fixed 42-byte ASCII string of
three colon-separated fields: the .NET **day number**, the count of **100-ns ticks within the
day**, and the fractional **precision** (e.g. `7`). The first two are zero-padded to 19 digits so
that byte order equals chronological order (an order-preserving inline encoding). The value is
`new DateTime(day * TicksPerDay + time)`; e.g. `…693593:…0:7` is the 1899-12-30 epoch and
`…737590:…495300000000:7` is 2020-06-15 13:45:30. Sub-second precision (to 100 ns) is preserved.

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
- `0x00` **multi-page** — the payload is chained across LVAL pages; each chunk's row begins
  with a 4-byte pointer (`[row:1][page:3]`) to the next chunk (zero on the last), followed by chunk
  data. Each chunk row is **`MAX_LONG_VALUE_ROW_SIZE` = 4076 bytes** (Jet4; Jet3 = 2032) — a 4-byte
  pointer + up to 4072 data bytes — except the last, which is shorter. Verified against ACE's own
  chained OLE (Northwind Employee photos: 4076, 4076, 2606-byte chunk rows).

LVAL pages are data pages (type `0x01`) whose owner field (`0x04`) is the ASCII marker `LVAL`.

> **Writing.** LibRed inlines a memo/OLE value only up to **64 bytes** (Jackcess
> `MAX_INLINE_LONG_VALUE_SIZE`, same for Jet3/Jet4): the 12-byte descriptor with length + the `0x80`
> flag (bytes `0x04`–`0x0B` zero) then the payload (memo = UTF-16LE, OLE = raw bytes). A value **larger
> than 64 bytes** is written to its own **single LVAL page** (`0x40` descriptor, `LongValueWriter`) —
> `RowInserter` materialises it before encoding. This matters for Access, not just LibRed: Access
> tolerates an inline value its reader resolves, but **rejects an over-64-byte value inlined** (e.g. it
> opens the database yet fails to *run* a view whose subquery `Expression` was inlined; on an LVAL page
> it runs — verified against the derived-table view, §11). A value **larger than one LVAL row** (4076
> bytes) is written as a **chain** (`0x00` descriptor): the payload is split into 4072-byte data chunks,
> each on its own page with a 4-byte next-pointer, matching ACE byte-for-byte (verified: LibRed and
> Access both read back memo values from 65 bytes to 100 KB — single-page and multi-page).
>
> **LibRed writes the §3.3.2 entry + empty usage maps for every memo/OLE column** — byte-faithful with
> ACE, whose usage-map page lays the records out as: row 0 table-owned, row 1 table-free, then one row
> **per index**, then two rows (owned/free) **per long-value column** (verified against Northwind
> Categories and against an ACE-created 80-memo-column table). **Multi-page distribution (wide tables):**
> a usage-map page holds ~57 of the 69-byte inline records, so a table with many memo/OLE columns can't
> fit all its used/free maps on one page. Access fills the primary page (data + indexes + as many *whole*
> columns as fit — 27 columns alongside a single index), then gives **each remaining long-value column its
> own dedicated usage-map page** with owned = row 0, free = row 1. LibRed reproduces this exactly (verified:
> an 80-memo table lands 27 columns on the primary page at rows 3–56, then one page each for the rest;
> ACE opens it and round-trips an 8000-char value written to an overflow column). Each column's §3.3.2
> `used_pages`/`free_pages` pointers, and the index blocks' `+0x22` pointers, carry the resolved (row, page).
> For a fresh table all these maps are empty. When LibRed writes a value to an LVAL page
> (§8), it now **sets that page's bit in the column's owned-pages *and* free-pages maps** — both §3.3.2
> pointers are parsed from the TDEF (`TableDefinitionPage.LongValueOwnedMaps` / `LongValueFreeMaps`, keyed
> by column id) and the inline bitmap bit is set. **Pages are packed like Access:** a value up to one row
> is appended to the first **free-map** page with room (many small values share a page as separate rows);
> only when none has room is a fresh page allocated (owned + free). A page is dropped from the free map
> once it can't hold the smallest long value (65-byte payload + its 2-byte slot). This reproduces Access's
> layout — MSysQueries.Expression **owns** {42, 282} but **frees** only {282}, the current append target;
> and 20 medium memos land on ~2 pages (full one owned-only, current one owned+free), not 20. The same
> packing is used for the MSysObjects **LvProp** property blob (via `RowInserter.StorePackedLongValue`) —
> but always to a page, never inline (Access reads object properties only from a page), so two tables'
> DEFAULT/CHECK blobs share one LvProp page. A chained value uses dedicated pages. A page outside the inline
> map's window would need a reference-type map — not exercised here.
>
> The entry is only strictly *required* once a value spills to LVAL pages — an entry-less table still
> round-trips inline values through both LibRed and Access, but Access fails *"Not a valid bookmark"*
> writing a 6000-char value into one (nowhere to record the LVAL page). LibRed writes it regardless, so
> its memo tables match ACE's structure and are already LVAL-ready.

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

> **LibRed write behaviour (multi-page growth on insert).** When an insert finds no owned data page
> with room, LibRed allocates a new page (via the global map, §9.1), initialises it as an empty data
> page owned by the table, sets its **owned** bit, and moves the **free** marker to it. Verified:
> 200 rows spill across ~20 data pages and Access reads the whole table and can still insert.
> - **Free-pages map = the current append tail only.** Access clears a page from the free-pages map
>   when an insert finds it too full and moves on, so after a sequential fill only the *last* page
>   stays marked free — verified: six equally-full ACE pages (all 252 bytes free) had only the last in
>   the free map, because the earlier five each had a next-row attempt that didn't fit. LibRed matches
>   this: on allocating a new page it **clears the previous tail's free bit and sets the new page's**,
>   leaving exactly the tail marked free. (Non-sequential fills and deletes aren't specially handled —
>   neither is supported yet.)
> - **Inline map, grown in place.** LibRed writes the inline map with `startPage = 0` and an initially
>   64-byte bitmap (pages 0–511). When an insert needs to mark a page **past** that window, LibRed grows
>   the bitmap **record in place** — still type `0x00`, same `startPage` — extending it in **256-bit
>   (32-byte) chunks** and repacking the usage-map page's records (the `owned`/`free` maps and any index/
>   column maps) from the end backward. This matches Access, which does **not** switch to a reference-type
>   map at this scale: verified that a table spanning to page 753 carries a **96-byte** bitmap (768 bits,
>   record length **101**, grown from 64), and that Access opens a LibRed-grown table, counts every row,
>   and reads one living past page 512. A **reference-type** map (for a table so large the grown record no
>   longer fits on its usage-map page, ~128 MB+) is still not implemented — LibRed throws then.

### 9.1 Global free-pages map — page 1 (page allocation)

Besides the per-table maps, the database has a **global free-pages map** at **page 1, row 0** (a
data page; its row 0 is an inline usage map, start page `0`). Here a **set bit means the page is
free / available**, the *opposite* of a per-table owned map — verified against Northwind (161 free
pages among 353) and by diffing before/after an ACE `CREATE TABLE`.

**Page allocation works through this map.** Access does **not** simply grow the file: it finds a
set bit (a free page), **clears it** (marking the page used), and reuses that page — only growing
the file when no free page remains. Verified: creating a table in Northwind reused four free pages
(for the TDEF, usage map, etc.) and grew the file by a single page; the only change to page 1 was
one cleared bit per page taken.

> LibRed allocates **through** this map (`PageAllocator`): it takes a free page, clears its bit,
> and reuses it — only growing the file when none is free — so its pages now match Access's
> allocation. Free bits beyond the current file end are the pre-allocated growth region; taking one
> grows the file. The reference-type global map (very large databases) is not handled yet.
>
> **Create-table side effects.** An ACE `CREATE TABLE` *also* (1) adds two rows to **`MSysACEs`**
> (the new object's permission entries) and updates its `ObjectId` index, and (2) bumps a counter in
> page 0's obfuscated region at `~0xE02` (not yet decoded). **(1) is now done** —
> `TableCreator.AddPermissionRows` writes both permission rows (§11), and ACE opens LibRed-created
> tables without repair (`CreateTableAccessTests`). **(2) appears not to be required:** LibRed does not
> touch the page-0 counter, yet ACE opens/queries the created tables — so it's either unused for
> table open or benign when stale. (Views likewise get their two `MSysACEs` rows now — §11.)

---

## 10. Index B-tree pages — types `0x03` (node) and `0x04` (leaf)

### 10.1 Header

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Page type (`0x03` node / `0x04` leaf) |
| `0x01` | 1 | Flags (observed constant `0x01` — verified) |
| `0x02` | 2 | Free space |
| `0x04` | 4 | Owning table TDEF page |
| `0x08` | 4 | **The 4-byte field Jet4 inserted** right after the owner — purpose unknown, **`0` observed** on every ACE- and LibRed-written index page (Jackcess has no constant for it either). Inserting it here is what pushes prev/next/tail/compress down by 4 vs Jet3 (see the Jet3→Jet4 note under `0x1B`). |
| `0x0C` | 4 | **Previous leaf page** (`0` on the first/leftmost leaf), little-endian. **Verified against ACE:** on an ACE-built split index the higher-key leaf's `0x0C` points back at the lower-key leaf. (An earlier draft mis-placed prev/next at `0x08`/`0x0C` — wrong by 4 bytes; the real insertion is at `0x08`.) |
| `0x10` | 4 | **Next leaf page** (`0` on the last/rightmost leaf), little-endian. **Verified against ACE — and load-bearing:** Access's full-table `COUNT(*)`/scan descends to the leftmost leaf and walks this forward chain. If it is wrong (e.g. `next` written at `0x0C`), Access stops after the first leaf and **silently sees only those rows** — a data-loss/corruption hazard, since it then treats the rest of the table's space as free. LibRed maintains `0x0C`/`0x10` across splits (§10.5). (This is **Jet3's `0x0C` next-pointer shifted +4**; the child-tail that mdbtools lists at `0x10` is the *Jet3* tail position — in Jet4 it too shifted to `0x14`.) |
| `0x14` | 4 | **Child-tail** page (node pages: the rightmost child, referenced by no entry). For Jet4/ACE this offset is **definitive — byte-for-byte verified** (the tail pointer reads correctly here and drives correct multi-level traversal). This is **Jet3's `0x10` tail shifted +4** by the `0x08` insertion, which is exactly why mdbtools (Jet3) documents the tail at `0x10`. |
| `0x18` | 2 | Compressed-byte count (shared key prefix length, §10.3). Jet3's `0x14`, shifted +4. |
| `0x1A` | 1 | The **1-byte field Jet4 inserted** just before the bitmask. ACE writes `0` on leaves and `1` on the root of a two-level split index, consistent with a **B-tree level/height** — but **only `0` and `1` have been observed** (no 3-level tree was built against ACE, so `2`+ is a guess). **Required only for leaves (verified):** writing `0x01` on a *leaf* makes ACE fail to open the whole database (`"could not find the object 'Databases'"`). **Node value is cosmetic (verified):** an isolation test — correct leaf-chain offsets but node `0x1A=0` *and* nodes prefix-compressed — still gave ACE the right `COUNT`/`SUM` at 700 and 1500 rows, so Access reads a node's tail child regardless. **Jackcess likewise has no offset constant for `0x1A`** (it tells leaf from node by the page-type byte at `0x00`). LibRed still writes the height to match ACE byte-for-byte, but the only hard requirements are the leaf-chain offsets and a *leaf's* `0x1A=0`. |
| `0x1B` | … | Entry-position bitmask. mdbtools **version-labels** this: bitmask at `0x16` (Jet3) / **`0x1B` (Jet4)** — confirming our offset. The `+5` Jet3→Jet4 shift is **fully decomposed**: a **4-byte field inserted at `0x08`** (right after the owner) plus the **1-byte B-tree level at `0x1A`** = `+5`. Everything between — prev/next leaf, child-tail, compressed count — is Jet3's field shifted by 4, with the level accounting for the final `+1`. No unexplained bytes remain in this header. **Corroborated by Jackcess**, whose `JetFormat` constants give (Jet3 → Jet4): prev `8`→`12`, next `12`→`16`, child-tail `16`→`20`, compressed-count `20`→`24`, entry-mask `22`→`27` — i.e. `0x08`/`0x0C`/`0x10`/`0x14`/`0x16` each `+4`, and the mask an extra `+1`. Two independent implementations now agree on the shift; the `0x08` insertion is the only thing that produces it. (The *positions* are ACE-verified and Jackcess-corroborated; a real Jet3 index page would still be the final confirmation that these are the exact bytes Jet3 lacked — notably Jackcess has no constant for the `0x08` field or `0x1A` either.) |
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

> **Compression is optional on leaves.** A `compressedByteCount` of 0 (every entry stored in full)
> is a valid *leaf* that Access reads without complaint — verified by rewriting a leaf uncompressed
> and re-seeking it. **On node (`0x03`) pages, ACE writes them uncompressed (`0x18 = 0`)**, and
> LibRed matches that. *Verified cosmetic:* compressing a node does **not** break Access — an isolation test
> (correct leaf-chain offsets, but nodes compressed and `0x1A=0`) still gave the right `COUNT`/`SUM`.
> The earlier "compression breaks tail descent" idea was a misattribution to the leaf-chain bug (§10.1).
> LibRed writes nodes uncompressed only to stay byte-faithful with ACE, not because it's required.

### 10.4 Key encoding (order-preserving)

Each key column is encoded so that raw byte comparison equals value comparison. LibRed both
**decodes** these keys and **encodes** them (`IndexKeyEncoder`, the inverse), so it can insert
into an index. The encoder is verified **byte-for-byte against Access**: re-encoding the value
decoded from Access's own stored key reproduces the exact bytes, and after a LibRed insert
Access satisfies an indexed primary-key seek over the entry LibRed wrote.

> **Text keys are the version-0 "General legacy" collation only.** The text weights below are the
> Access 2000–2007 **General** sort order = (locale 1033, **version 0**), selected by the column
> descriptor's sort-order version (`0x0D`, §3.4). Access 2010+ introduced a *new* default **General**
> order = (1033, **version 1**) with different key bytes (the old one was renamed "General legacy").
> LibRed implements version 0 only; a version-1 column/index (a database created by Access 2010+ with
> the default order, `0x0D = 1`) would need a separate weight table for both decode and encode.
> Before writing/seeking a text index key, a version-aware implementation should check `0x0D` and
> refuse (or switch tables) on version 1 rather than emit version-0 bytes. Not yet handled — no
> version-1 fixture available to reverse-engineer against.

Non-boolean columns are prefixed by a **flag byte**:

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
- **Text:** Jet's "General" collation. The key is the start flag, then one or two
  **primary-weight** bytes per character, then a `01 00` terminator. Weights are **case-folded**
  (lowercase weighs the same as uppercase), **trailing spaces are dropped**, and an internal
  space weighs `0x07`. Most characters weigh one byte; `^ _ \` { | } ~` weigh two (sharing the
  `0x2B` page). The weight table is a fixed lookup (A=`4A`, B=`4C`, C=`4D`, …, digits step by two
  from `0x36`), **verified byte-for-byte against the ACE engine** over printable ASCII and
  implemented by `JetTextCollation` — so LibRed can now *write* ASCII text index keys (e.g. a
  string primary key). Decoding remains lossy (case is discarded — that is why a text primary key
  treats `'A'` and `'a'` as duplicates).

  **Apostrophe and hyphen are "ignorable"** (so `O'Brien` sorts next to `OBrien`): they add **no
  primary weight**, but each appends an inline record to a trailing section. After the primary's
  `0x01` end marker, if any ignorable char is present the key adds `01 01 01` once, then per
  ignorable char four bytes `80 <pos> 06 <code>`, then the final `00`. `<pos> = 0x07 + 4 × (count
  of **primary weight bytes** emitted before it)` and `<code>` is `0x80` for apostrophe / `0x82` for
  hyphen — verified against ACE (e.g. `ANNE-MARIE` → `… 80 17 06 82 …`, the hyphen at position 4;
  `Aß-B` → `7F 4A 6B 6B 4C 01 01 01 01 80 13 06 82 00`, hyphen at position **3** because ß expands to
  two primary bytes `SS`).

  **A few letters expand to multiple base letters** (each expanded letter weighs its normal primary,
  no accent): `ß`→`SS`, `Þ`/`þ`→`TH`, `Æ`→`AE` — verified against ACE (`ß` = `7F 6B 6B 01 00`, same as
  `SS`). Because the ignorable-position count is by primary byte, an expansion counts as its expanded
  length (above).

  **Accented Latin-1 letters** sort with their **base letter's primary weight** and record the
  accent in a **secondary section**. Each character has a secondary weight (default `0x02`); an
  accented letter carries the weight of its diacritic instead (verified against ACE, and the weight
  depends only on the accent, not the base letter): **acute `0x0E`, grave `0x0F`, circumflex `0x12`,
  diaeresis/umlaut `0x13`, tilde `0x19`, ring `0x1A`, cedilla `0x1C`**; plus atomic `Ø`→base `O`+`0x21`,
  `Ð`→base `D`+`0x68`, and the ligature `Æ`→primaries `A E` (no accent). The section is emitted only
  when some character is accented: after the primary's `0x01` end marker it lists the secondary weight
  of **every byte from the first up to and including the last accented one**, e.g. `México D.F.` →
  `7F 60 51 75 59 4D 64 07 4F 1C 53 1C 01 02 0E 00` (é = primary `0x51` = E, secondary `0x0E`), and
  `Montréal` (é at position 5) → `… 01 02 02 02 02 02 0E 00`. LibRed decomposes via Unicode NFD (base
  letter + combining mark) plus the small atomic table above; `JetTextCollation` reproduces these keys
  **byte-for-byte vs ACE** (México/Montréal/München/São Paulo/Résumé and single accents).

  **Descending** text keys are the **bitwise inverse of the ascending key, with a `0x00`
  appended** — verified against ACE (e.g. ascending `A` = `7F 4A 01 00` → descending
  `80 B5 FE FF 00`). The inverted start flag is `~0x7F = 0x80`, matching the descending flag of
  the fixed-type keys.

  *Not yet handled:* characters outside ASCII + the accented Latin-1 set above (and a key mixing an
  accent with an ignorable apostrophe/hyphen is untested).
- **GUID:** the start flag `0x7F`, then the 16 GUID bytes in **canonical string order** (i.e.
  `guid.ToString("N")` bytes — **not** the mixed-endian `.ToByteArray()` storage layout), split into two
  8-byte halves by a constant `0x09` marker, and terminated by `0x08` — a fixed **19-byte** key. Data
  bytes equal to `0x08`/`0x09` need no escaping (every field is at a fixed offset). Verified byte-for-byte
  against ACE (zeros, all-`FF`, sequential, and random GUIDs); ACE also opens a LibRed-written GUID-PK
  table and seeks a row by its key. Encoded/decoded by `IndexKeyEncoder`/`IndexKeyDecoder`. Example:
  `01020304-0506-0708-090a-0b0c0d0e0f10` → `7F 0102030405060708 09 090A0B0C0D0E0F10 08`.
  **Descending** inverts every byte of the ascending key **except the `0x09` field marker** (kept constant
  so the structure stays parseable — and it doesn't affect ordering since it's equal in every key): the
  start flag becomes `0x80` and the `0x08` terminator becomes `0xF7`, but the middle `0x09` is unchanged.
  Verified against ACE (`CREATE INDEX … (K DESC)`), e.g. `00000000-…-0` → `80 FFFFFFFFFFFFFFFF 09
  FFFFFFFFFFFFFFFF F7`. No trailing `0x00` (unlike descending text keys).
- **Binary (general):** the start flag (`0x7F` asc / `0x80` desc), then the raw bytes in **8-byte
  chunks**. Each chunk is 8 bytes — real bytes left-aligned, **zero-padded on the right** — followed by
  a **control byte**: `0x09` when a further chunk follows (a full 8-byte chunk with more data to come),
  otherwise the **real-byte count of this final chunk** (`0x01…0x08`; `0x08` for a full final chunk,
  `0x00` for empty data). The count `≤ 8 < 0x09`, so control values never collide. This is exactly the
  GUID chunking generalised to any length: a 16-byte value is two chunks (`… 09 … 08`), and the old
  fixed 4-byte MSysQueries.Order case is the single-chunk form `7F <4B> 00000000 04`. The trailing
  length-terminator makes shorter values sort before longer ones that share a prefix (correct binary
  prefix order). **Descending** inverts every byte **except the `0x09` continuation markers** (mirrors
  GUID): flag → `0x80`, data bytes and the terminator inverted, markers unchanged. Verified byte-for-byte
  against ACE's `EverythingIsBytes` fixture (3/4/5/8/16-byte keys, single- and multi-chunk) by
  re-encoding each stored key's row value; descending has no ACE fixture and is extrapolated from the
  verified GUID descending (ordering-tested for internal consistency). `IndexKeyEncoder.EncodeBinaryChunked`.

### 10.5 Insertion and splitting

To insert a key, descend from the index root (§3.5 offset `0x26`) following node separators — a
separator is the **maximum key of its child subtree**, stored as a full leaf key (column key ++
4-byte row pointer), so descend into the first child whose separator `≥` the new full key, else the
child-tail (`0x14`). Slot the new entry into the target leaf in key order and rewrite the page.

When a page would overflow, **split** it (LibRed's `IndexWriter`). Verified by inserting 1500 keys —
past one leaf — and reading every one back in order through a now multi-level tree, **and against ACE**:
Access opens the file and an indexed point seek (`WHERE Id = 1234`), an indexed range (`Id BETWEEN 300
AND 309`), a full `COUNT(*)`, a non-indexed scan (`T LIKE 'r%'`) and `SUM(Id)` all return the correct
result — i.e. every row is reachable both by the tree and by the leaf-chain scan Access uses.

The split mechanics:

- **Leaf split:** partition the sorted entries in half; the lower half stays on the original page,
  the upper half goes to a newly allocated page. The doubly-linked leaf chain is maintained — the
  new right page's *prev* (`0x0C`) points at the left, its *next* (`0x10`) inherits the left's old
  next, the left's *next* becomes the right, and the old next leaf's *prev* is repointed to the
  right. **Getting these offsets right is essential** — Access's scan walks the `0x10` next-chain
  from the leftmost leaf, so a mis-placed pointer makes it lose every row past the first leaf. The
  promoted separator is the **left half's maximum full key**, which stays in the leaf (a copy is
  promoted, B+tree-style). Leaves are prefix-compressed; LibRed also writes node pages uncompressed
  with `0x1A` set to their height above the leaves to match ACE byte-for-byte, though (unlike the
  leaf-chain offsets) neither is *verified* to be required — see §10.1 `0x1A` and §10.3.
- **Node split:** partition on a **middle entry** whose key is *promoted* (removed from the node);
  its child becomes the left node's child-tail, and the old tail stays the right node's tail.
- **Propagation:** the promoted separator `[key → left page]` is inserted into the parent, whose
  pointer to the just-split page is repointed to the new right page; if the parent overflows it
  splits in turn, up to the root.
- **Root growth:** when the root itself splits, a new root node is allocated holding one entry
  `[promoted → old root]` with the new page as its child-tail, and the index-data block's root
  pointer (§3.5 `0x26`) is repointed to it. (The single-leaf → two-leaves case hits this on the
  first overflow, changing the root page's type from leaf `0x04` to node `0x03`.)

> Newly allocated split pages are taken from the global free-page map (§ page 1). Registering them
> in the *index's own* owned-pages usage map (§3.5) is **not yet done** — tolerated so far, but a
> point to revisit when validating large indexes against Access.

---

## 11. System catalog

- **MSysObjects** (TDEF at page **2**) lists every object. Columns include `Id`, `Name`,
  `Type`, `Flags`, `ParentId`. For a **table** object (`Type == 1`), **`Id` is the table's TDEF
  page number**. An object is excluded from the **user-table** list (as Access's own schema view
  does — it hides system *and* hidden objects) if `Flags & 0x80000002` (system: `0x80000000` +
  `0x00000002`) **or** `Flags & 0x00000008` (**hidden** — observed on nav-pane tables and on
  EFCore.Jet's `#Dual` helper) is set, **or** its name begins with `MSys` / `~` / `#`. Bootstrap:
  build a TableDef for MSysObjects from page 2 and read its rows like any table.

  > **Why the hidden bit / `#` prefix matter.** Missing them makes a hidden helper such as
  > EFCore.Jet's `#Dual` (`Flags = 0x08`) count as a *user* table, so a "has any user tables?" check
  > wrongly reports a schema-less database as populated — which makes EF Core's `EnsureCreated` skip
  > creating the model's tables. Real user tables carry `Flags = 0x00000000`, so excluding the
  > system/hidden bits never drops a genuine table.

  **Writing a table object** (verified against Northwind rows). A complete user-table row sets:
  `Id` = TDEF page; `ParentId` = `0x0F000001` (the database's "Tables" container, constant);
  `Type` = `1`; `Name`; `Flags` = `0`; `Owner` = a 2-byte binary SID (`0x69 0x0C` for a
  workgroup-less database, constant across tables); and `DateCreate` / `DateUpdate`. The other
  columns (`Connect`, `Database`, `ForeignName`, `Lv*`, `RmtInfo*`) are null **except `LvProp`**,
  an OLE long-value blob ("MR2"-prefixed) holding the object's **extended properties** — including
  column-level properties such as *Required* (see §3.4) and *DefaultValue*.

  > **Permission rows (`MSysACEs`) — one per object, verified against Northwind.** Every new object needs
  > `MSysACEs` rows or Access warns about permissions when opening it (a **table** still opens; a **query**
  > opens but pops a permissions warning). The table has exactly **four columns** (verified vs Northwind):
  > `ObjectId` (Int32, the object's id), `SID` (Binary, a security id), `ACM` (Int32, an access mask), and
  > `FInheritable` (Boolean). Each row sets `ObjectId` = the object id, `SID` = a 2-byte binary security id,
  > `ACM` = an access mask, `FInheritable` = false, and the object's `ObjectId` index must be maintained so
  > Access's security check finds them. Access writes **two** rows per object, and the mask
  > **differs by object type**:
  > - **Table:** owner (`0x690C`) and admin/users (`0x680C`) both get full access `ACM = 0xFFEFF` (1048319).
  > - **Query/view:** owner (`0x690C`) gets `ACM = 0xF00FE` (983294, a query-specific mask), admin/users
  >   (`0x680C`) gets full `0xFFEFF`.
  >
  > LibRed writes both rows for tables (`TableCreator.AddPermissionRows`) and for queries/views
  > (`ViewCreator.AddPermissionRows`). (System-table `MSysACEs` rows in an existing file carry restricted
  > masks like `0x60000`/`0x14` and a long per-database owner SID; those are the pre-existing catalog's, not
  > what a writer emits for a new user object.)

  > **Property blob (`LvProp`) format — verified byte-for-byte against ACE.** A 4-byte signature
  > (`MR2\0` on ACE, `KKD\0` on older MDB) then blocks, each `[int length][short type][body]` with the
  > length covering the whole block. Type `0x80` is the **property-name pool** (`[short len][UTF-16
  > name]` repeated, indexed 0,1,…). Other blocks are a **per-owner value map** (owner = a column name,
  > or `""` for the table): `[short ownerRecLen][short 0][short nameLen][owner name]` then property
  > entries `[short entryLen][byte flag=1][byte dataType][short nameIndex][short valueLen][value]`. The
  > `dataType` is an ordinary **`JetDataType` code** (the same byte used by column descriptors and
  > MSysQueries): **`0x0C`** (Memo) for a text value stored as **UTF-16**, **`0x01`** (Boolean) for a single
  > **0/1 byte**, and — on the `MSysDb` object's UI/nav settings only — `0x0A` (Text), `0x02`/`0x03`/`0x04`
  > (Byte/Int16/Int32). The value-block **type** is `0x01` for a column-owned map and `0x00` for the
  > table-owned map (empty owner name). A `DefaultValue` (column property) is the expression's **source
  > text** (e.g. `42`, `'hi'`); table-level `CHECK` constraints are a single **table** property named
  > `CheckConstraints` whose value is a `name\0expression\0` list, terminated by an extra `\0` (verified
  > byte-for-byte vs ACE for `CONSTRAINT CK_BD CHECK ([BirthDate] < NOW())`).
  >
  > **`Required` (NOT NULL)** is a per-column **boolean** property (`dataType 0x01`, one `0x01` byte); a
  > **nullable** column simply has **no** `Required` property, and an AutoNumber column is left without one
  > too (verified vs ACE). Within a column's map ACE orders `DefaultValue` **before** `Required`; the
  > name-pool order follows first appearance across all properties. Example (`Req int NOT NULL, …, Def int
  > DEFAULT 7 NOT NULL`): name pool `["Required","DefaultValue"]`, then `Req`'s `Required`, then `Def`'s
  > `DefaultValue`=`7` and `Required` — reproduced byte-for-byte by `PropertyBlob.Write`.
  >
  > LibRed **writes** `DefaultValue`, `Required` and `CheckConstraints` properties (`PropertyBlob.Write`) and
  > **reads** them back (`ColumnDef.DefaultValue`, `ColumnDef.IsNullable`, `TableDef.CheckConstraints`),
  > applying the default when an insert omits the column and **rejecting** an insert that leaves a required
  > column null ("You must enter a value in the '<table>.<column>' field.", matching Access). Access
  > **applies the default**, **enforces Required**, and **enforces the CHECK** on its own inserts —
  > including on a LibRed-created table (verified: ACE rejects an insert omitting a LibRed `NOT NULL` column). `LvProp` is stored
  > on a **single LVAL page** (`LongValueWriter`, descriptor flag `0x40`) — the form Access's property
  > loader requires. **Verified:** Access opens the file and **applies the default** on its own insert
  > that omits the column. (An *inline* value, flag `0x80`, is written and read fine by LibRed but is
  > **not** recognised by Access's property loader — established by dumping the raw descriptors; nothing
  > else differs, only `MSysObjects`+`MSysACEs` are touched.)
  >
  > **"Random" AutoNumber (New Values = Random) is a `DefaultValue` = `GenUniqueID()`.** An AutoNumber column
  > whose *New Values* property is **Random** (rather than Increment) is stored as an ordinary AutoNumber column
  > (descriptor flag `0x04`, TDEF `0x14`/`0x18` at their plain-counter defaults `0`/`1` and **ignored**) plus a
  > **column `DefaultValue` extended-property** holding the built-in expression **`GenUniqueID()`** — the
  > function that returns a random Long. There is **no** dedicated flag or "New Values" property; the
  > Increment-vs-Random distinction lives entirely in this default expression. Verified against a modern
  > Office-365-authored file (`Table1(ID AutoNumber, New Values=Random)`): the ID descriptor and TDEF header are
  > **byte-identical** to an increment counter, and LibRed already surfaces it (`ColumnDef.DefaultValue` =
  > `"GenUniqueID()"`) via the ordinary DefaultValue read path — no special handling needed to detect it. A
  > Random AutoNumber **can** be created in pure SQL (not UI/DAO-only): `CREATE TABLE T (Id COUNTER DEFAULT
  > GenUniqueID(), ...)` — also `AUTOINCREMENT`/`COUNTER PRIMARY KEY` forms — is accepted by ACE and yields
  > genuinely random signed-Long IDs on insert (verified: `-1637443712, 1680187777, 83315118`), reading back
  > byte-identical to the UI-authored column. `GenUniqueID()` **is a real ACE default-expression**, not a marker:
  > `SELECT GenUniqueID()`
  > errors ("Undefined function"), yet an **unquoted** `col long DEFAULT GenUniqueID()` **is** accepted (on
  > numeric columns — rejected on text: "Cannot place this validation expression on this field") and generates a
  > **random signed Long per row** (verified: `117617513`, `904519542`, `-1470084161`). Quoting it —
  > `DEFAULT 'GenUniqueID()'` — makes it a plain literal string stored verbatim. So a Random AutoNumber is
  > effectively an AutoNumber column carrying the unquoted `GenUniqueID()` default; LibRed reads the default text
  > but does not evaluate the generator, so it would assign via the sequential counter, not random (a divergence
  > only relevant if such a table is written back and inserted through LibRed).

- **LVAL (long-value) page** — a data page (type `0x01`) whose owner field (`0x04`) is the ASCII marker
  `"LVAL"` instead of a TDEF page number. A single-page long value stores the whole payload as row 0; the
  in-row reference descriptor is `[length:3][flags:1][row:1][page:3][4 reserved]` with flag `0x40` = single
  page (`0x80` = inline, payload follows the descriptor; `0x00` = chained across pages). LibRed writes the
  single-page form (`LongValueWriter`); chained pages for payloads larger than one page are not written yet.

  > With those fields set, Access **enumerates** a LibRed-created table (it appears in the
  > schema/Tables rowset) — verified via OLE DB. Maintaining MSysObjects' indexes (the composite
  > `ParentId+Name` and `Id` indexes) then lets Access **resolve the table by name** and attempt
  > to open it. Opening it then requires the table's own structures to be byte-valid to Access
  > (see §3.7).

- **Views / queries** are `MSysObjects` rows of **Type 5** with a **negative synthetic `Id`** (queries
  increment from `0x80000000`), `ParentId 0x0F000001`, `Flags 0x10000000`, `LvProp` null.

  > **MSysQueries columns (8, verified vs Northwind).** The table has exactly: `ObjectId` (Int32, the
  > query object's `Id`), `Attribute` (Byte, the row kind — see below), `Flag` (Int16, attribute-specific),
  > `Name1` and `Name2` (Text, attribute-specific names), `Expression` (Memo, attribute-specific text —
  > SQL fragments), `Order` (Binary, a 4-byte big-endian per-attribute sequence counter), and `LvExtra`
  > (Int32) — a long-value/overflow field that is **null in every Northwind query row** and that LibRed
  > leaves null (not needed for the queries it writes). Only index = composite PK `(ObjectId, Attribute,
  > Order)`.

  The query itself is stored in **MSysQueries**, decomposed into rows keyed by `ObjectId`, each with an `Attribute`
  byte (Jackcess "query rows", verified vs ACE for the "simple SELECT" a view may contain): `0x00` =
  query type (`Flag 1` = SELECT), `0x02` = a **declared parameter** (`Name1`=parameter name, `Flag`=Jet
  type code — same codes as on-disk column types, e.g. `8`=DateTime; one row per parameter, `Order`
  1-based), `0x03` = flags (`Flag 2` = DISTINCT; **`Flag 0x10` = TOP**, with `Name1` = the count as text,
  e.g. `Name1=10`), `0x05` = FROM source, `0x06` =
  output column (`Expression`=verbatim text; **`Name1`=the column's output alias** when it has one, e.g.
  `Expression=Customers.CompanyName`, `Name1=CustomerName`; a computed column stores its whole verbatim
  expression, `Expression=(FirstName + ' ' + LastName)`, `Name1=Salesperson`), `0x07` = join
  (`Expression`=condition, `Flag`=kind, and **`Name1`/`Name2`=the two tables named in the condition** —
  `Customers.CustomerID = Orders.CustomerID` → `Name1=Customers`, `Name2=Orders`), `0x08` = WHERE
  (`Expression`), `0x09` = a **GROUP BY** column (`Expression`; one row per group column, in order —
  their presence makes it a "totals" query, and the aggregate output columns are ordinary `0x06` rows,
  e.g. `Expression=Sum(...)`), `0x0B` = an **ORDER BY** key (`Expression`=the sort column, `Name1`=`"d"`
  for **descending**, absent for ascending; one row per key, `Order` 1-based — verified against Northwind's
  "Ten Most Expensive Products", `SELECT TOP 10 … ORDER BY Products.UnitPrice DESC`), `0xFF` = end. A **FROM source** (`0x05`) is either a **named table**
  (`Name1`=table, `Name2`=alias) or a **derived table / subquery** (`Expression`=the verbatim inner
  subquery SQL — outer parens and `AS alias` stripped, whitespace preserved — `Name2`=alias, **no `Name1`**;
  verified against Northwind's "Customer and Suppliers by City"). **Nested / parenthesised joins are stored
  flat** — one `0x05` per base table and one `0x07` per join condition, no grouping — so Access re-derives
  the join tree from the conditions (verified against "Invoices": 6 tables, 5 flat joins). `Order` is a 4-byte **big-endian**
  per-attribute counter (stored in the Binary `Order` column). MSysQueries' only index is the composite PK
  `(ObjectId Int32, Attribute Byte, Order Binary)`; its Binary key encodes as `0x7F` + the raw bytes +
  `00 00 00 00` + a length byte.

  > **Row order matters.** Access writes the rows in the order **type, end, parameters (`0x02`), distinct/top,
  > tables (`0x05`), columns (`0x06`), joins (`0x07`), where (`0x08`), group-by (`0x09`), order-by (`0x0B`)** — *tables before columns* (verified across five
  > Northwind views). Access tolerates the wrong order for a **named** table, but a **derived** table
  > defines an alias the column expressions reference, so its `0x05` row must precede the `0x06` rows or
  > Access opens the database yet **fails to run the view**.
  >
  > **Long `Expression` lives on an LVAL page.** `Expression` is a Memo, so a subquery longer than the
  > 64-byte inline limit is written to an LVAL page (§8) — required for Access to *run* the view (an
  > inlined long value opens but won't execute). Verified: a LibRed derived-table UNION view returns the
  > same rows in Access as the equivalent Northwind view.
  >
  > **CREATE PROCEDURE** is stored identically to a view (Type-5 `MSysObjects` row + `MSysQueries` rows) —
  > a stored query is a stored query — with one `0x02` parameter row per declared parameter. The Access
  > syntax accepts the parameter list either bare or **parenthesised**, and a parameter may be written
  > `@name`; Access stores the **bare** name (the `@` is stripped — `@Beginning_Date` → `Name1=Beginning_Date`)
  > while the body keeps the `@` reference verbatim: `CREATE PROCEDURE name (p1 datatype, p2 datatype) AS
  > select` or `CREATE PROCEDURE name p1 datatype AS select`. Verified: a LibRed-written parameterized query
  > runs in Access and honours supplied parameter values. **Read-back:** LibRed reconstructs a parameterized query with a leading `PARAMETERS
  > name Type, …;` clause (the `0x02` rows) and lowers body references to a declared name into engine
  > parameters, so LibRed's own engine executes the stored procedure when values are supplied.
  >
  > **Action-query procedure bodies** (a CREATE PROCEDURE body that is not a SELECT) are stored with a
  > different MSysObjects `Flags` and an `Attribute=0x01` row (verified vs ACE):
  > - **Data-definition** (CREATE TABLE / DROP TABLE): MSysObjects `Flags=0x10000060`; one `0x01` row with
  >   `Flag 7` and `Expression` = the **whole DDL statement** verbatim (ACE prepends a single space).
  > - **Append** (INSERT): MSysObjects `Flags=0x10000040`; a `0x01` row with `Flag 3` and `Name1` = the
  >   target table, then one `0x06` column row per appended column — `Name2` = target column, `Expression`
  >   = the value; `Flag 0x8000` marks an INSERT … **VALUES** append (an INSERT … **SELECT** instead uses
  >   `Flag 0` on the `0x06` rows plus the usual `0x05` table / `0x08` where rows).
  >
  > (A plain view/SELECT query uses `Flags=0x10000000` and no `0x01` row.) LibRed writes CREATE TABLE and
  > INSERT … VALUES bodies; INSERT … SELECT and UPDATE/DELETE are not written yet. **Read-back:** LibRed
  > reconstructs a stored action query from these rows (DDL → the verbatim SQL; INSERT … VALUES → a rebuilt
  > `INSERT INTO t (cols) VALUES (…)`) and executes it by name; kinds it can't run (INSERT … SELECT, etc.)
  > read back with an "unsupported" reason and throw when executed.

- **MSysRelationships** defines foreign keys (one row per relationship column): `szRelationship`
  (name), `szObject` (child/referencing table), `szColumn` (child column), `szReferencedObject`
  (parent table), `szReferencedColumn`, `icolumn` (0-based column order within the key),
  `ccolumn` (total column count of the key, repeated on every row), `grbit` (flags: `0x02`
  don't-enforce, `0x100` cascade-update, `0x1000` cascade-delete). Verified against Northwind: an
  enforced, no-cascade single-column FK stores `ccolumn = 1`, `icolumn = 0`, `grbit = 0`; the
  cascade nav-pane relationships store `grbit = 0x1100` (update+delete cascade).

  > **Writing a relationship.** Access records a relationship purely in `MSysRelationships` (there is
  > **no** `MSysObjects` row for it) **plus** a non-unique index on the child table's FK column(s) —
  > enforcement requires the child FK to be indexed and the parent key to be uniquely indexed (the
  > parent PK). LibRed writes the `MSysRelationships` rows, creates that child-side index, **and** the
  > byte-faithful relationship logical-index linkage in *both* tables' TDEFs (§3.6: outgoing block on
  > the child, incoming block on the parent, cross-referenced by `index_num`) at `CREATE TABLE` time.
  > Verified: a LibRed-created relationship is byte-identical to an ACE-created one (bar index *names*),
  > Access opens the file without repair, and `GetOleDbSchemaTable(Foreign_Keys)` enumerates it.
  >
  > **`ALTER TABLE … ADD CONSTRAINT … FOREIGN KEY`** writes the *same* linkage, but **surgically** onto the
  > two existing (empty) TDEFs: it inserts the child's backing index + outgoing block into the child TDEF
  > (the shared index-insert path, name-sorted) and appends the incoming block to the parent TDEF, then the
  > `MSysRelationships` rows — no format difference from the inline case (and the child index is
  > back-filled if the table already has rows). Verified: Access reads and **enforces** a LibRed-`ALTER`-added
  > FK (RI rejects an orphan child row). A **self-reference** (child = parent, e.g. Employees.ReportsTo →
  > EmployeeID) hosts both ends in the one TDEF: the outgoing block links to an incoming block numbered one
  > past it (`Fk_number = outgoing index_num + 1`), and the incoming block's `index_num2` = the table's own
  > referenced-key (PK) data block — verified read+enforced vs ACE. `FOREIGN KEY NO INDEX` via `ALTER` is
  > not written yet.

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
(`HACKING.md`, and its `table.c` / `data.c` / `index.c`) and Jackcess (`TableImpl`, `ColumnImpl`,
`IndexData`, `IndexCodes`) — consulted upstream, not vendored here. The LibRed test suite (`test/LibRed.Core.Tests/`) pins these
structures, including whole-database golden dumps. Write-side structures (row insertion,
order-preserving key encoding, leaf-entry layout) are additionally cross-checked against
Access's own engine via OLE DB: insert the same row through LibRed and through Access, then
confirm the row sets match and that Access seeks the LibRed-written index entry.
