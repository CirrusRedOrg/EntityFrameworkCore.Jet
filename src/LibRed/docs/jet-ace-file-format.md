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
| `0x14` | 4 | **Highest AutoNumber value assigned** = the id of the last row inserted (the *next* id is this `+ 1`); `0` when the table has no AutoNumber column. Verified **directly against `@@IDENTITY`**, and disambiguated from row count with a delete-gap: after inserting 3 rows, deleting id `3`, and inserting again (which is assigned id `4`, *not* reused `3`), `0x14` = `4` = the last inserted id while the row **count** is `3`. (Also: Northwind Categories = `8`, non-autonumber/text-PK tables = `0`.) mdbtools labels this *"Next autonumber value"* — that's **off by one**; the stored value is the last assigned, and the next id is `+ 1`. **Write requirement:** a writer inserting into an AutoNumber table must advance this to the max id it writes (LibRed does so in `RowInserter`); leaving it stale makes Access reissue an existing id and reject the insert as a duplicate primary key — verified end-to-end. |
| `0x18` | 1 | Constant `0x01` — set on **every** table, autonumber or not (verified across autonumber, non-autonumber, and text-PK tables). mdbtools calls it a 1-byte "autonumber enable" flag, but it is not conditional on having an AutoNumber column, so it reads as a plain constant |
| `0x19` | 3 | Unknown (zero observed) |
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

### 3.2 Multi-page TDEFs

If a table has enough columns, the definition spans pages chained by the `0x04` pointer.
Reassemble before parsing: take the **first page whole**, then append each continuation
page's bytes **from offset 8** (continuation pages have an 8-byte header). Column offsets are
absolute from the first page, so parsing is otherwise unchanged.

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
> LibRed currently *derives* the variable-table index (`0x07`) by ranking variable columns by
> column id rather than reading it; the stored value matches that ranking in every file tested.

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
| `0x2A` | 4 | Unknown / reserved (zero observed). mdbtools places a 1-byte index-flags field at `+0x2A`, but ACE's effective flags are at `0x2E` and this is zero in every file checked |
| `0x2E` | 2 | Flags: `0x01` unique, `0x08` required, `0x80` always-set (Access 2000+) |
| `0x30` | 4 | Unknown / reserved (zero observed) — trailing bytes of the 52-byte block |

### 3.6 Index-info block (28 bytes) — one per *logical* index

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Marker (`0x00000659` = 1625, or 0) |
| `0x04` | 4 | **Logical index number** (`index_num`) — a unique id per logical index, `0 … logicalCount-1` |
| `0x08` | 4 | **Real index-data block ordinal** (`index_num2`) this logical index uses, `0 … realIndexCount-1` |
| `0x0C` | 1 | Foreign-key index type |
| `0x0D` | 4 | Foreign-key index number |
| `0x11` | 4 | Foreign-key table page (non-zero ⇒ a relationship index) |
| `0x15` | 1 | Update action |
| `0x16` | 1 | Delete action |
| `0x17` | 1 | Index type (`1` = primary, `2` = foreign) |
| `0x18` | 4 | Unknown / reserved (zero observed) — trailing bytes of the 28-byte block |

The index **name** read at the same ordinal applies to this logical index. To name the
physical (data-block) index, prefer a real index's name over a foreign-key relationship's
(distinguished by `0x11` ≠ 0), and take `IsPrimaryKey` from the type byte `0x17`.

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
  records — and the usage-map page's own owner field is `0`.

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
  must therefore *not* advance the fixed offset for a Boolean column.)
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
- otherwise **multi-page** — the payload is chained across LVAL pages; each chunk's row begins
  with a 4-byte pointer (row + 3-byte page) to the next chunk, followed by chunk data.

LVAL pages are data pages (type `0x01`) whose owner field (`0x04`) is the ASCII marker `LVAL`.

> **Writing (inline only).** LibRed writes a memo/OLE value as an **inline** long value: the 12-byte
> descriptor with length + the `0x80` flag (bytes `0x04`–`0x0B` zero), immediately followed by the
> payload (memo = UTF-16LE text, OLE = raw bytes) — the exact shape the reader resolves. Values too
> large to fit inline (chained LVAL pages) are not written yet.
>
> **LibRed writes the §3.3.2 entry + empty usage maps for every memo/OLE column** — byte-faithful with
> ACE, whose usage-map page lays the records out as: row 0 table-owned, row 1 table-free, then two
> rows (owned/free) **per long-value column**, then one row per index (verified against Northwind
> Categories). For a fresh table all these maps are empty; inline values allocate no LVAL pages so they
> stay empty, and LVAL support will simply set bits in the per-column maps.
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
> - **Inline map only, fixed window.** LibRed writes/updates the inline map with `startPage = 0` and a
>   64-byte bitmap (pages 0–511). A data page numbered outside that window needs a wider start or a
>   **reference-type** map (neither implemented); LibRed throws rather than writing past the record.

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
> **Remaining create-table gaps (Access still rejects):** an ACE `CREATE TABLE` *also* (1) adds two
> rows to **`MSysACEs`** (the new object's permission entries) and updates its `ObjectId` index, and
> (2) bumps a counter in page 0's obfuscated region at `~0xE02` (meaning not yet decoded). Those two
> remain between "Access resolves the table" (§11) and "Access opens it".

---

## 10. Index B-tree pages — types `0x03` (node) and `0x04` (leaf)

### 10.1 Header

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Page type (`0x03` node / `0x04` leaf) |
| `0x01` | 1 | Flags (observed constant `0x01` — verified) |
| `0x02` | 2 | Free space |
| `0x04` | 4 | Owning table TDEF page |
| `0x08` | 4 | Previous leaf page (`0` if none) — observed `0` on single-leaf samples, semantics unverified |
| `0x0C` | 4 | Next leaf page (`0` if none) — observed `0` on single-leaf samples, semantics unverified |
| `0x10` | 4 | Unknown / reserved (zero observed). mdbtools' header puts `tail_page` at `0x10`, but in ACE the child-tail is 4 bytes later at `0x14` (verified: a node page's tail pointer reads correctly at `0x14`, zero at `0x10`). This is a **Jet4-inserted field** — corroborated by mdbtools' version-labelled bitmask offset (see `0x1B` below): the Jet3→Jet4 bitmask shift of +5 is accounted for *exactly* by this 4-byte field plus the 1-byte field at `0x1A` |
| `0x14` | 4 | **Child-tail** page (node pages: the rightmost child, referenced by no entry). For Jet4/ACE this offset is **definitive — byte-for-byte verified** (the tail pointer reads correctly here and drives correct multi-level traversal; the hypothesis on `0x10` above concerns only *why* mdbtools lists `0x10`, not whether `0x14` is correct) |
| `0x18` | 2 | Compressed-byte count (shared key prefix length, §10.3) |
| `0x1A` | 1 | Unknown (observed `0x01`) — a Jet4-inserted byte (the +1 of the +5 bitmask shift; see `0x1B`) |
| `0x1B` | … | Entry-position bitmask. mdbtools **version-labels** this: bitmask at `0x16` (Jet3) / **`0x1B` (Jet4)** — confirming our offset. The +5 Jet3→Jet4 shift equals the inserted 4-byte (`0x10`) + 1-byte (`0x1A`) fields |
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

> **Compression is optional.** A `compressedByteCount` of 0 (every entry stored in full) is a
> valid page that Access reads without complaint — verified by inserting into a leaf: LibRed
> rewrites the whole leaf uncompressed, sets `0x18` to 0, and Access still seeks it. A
> minimal/correct writer need not reproduce Access's prefix compression.

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
  of non-ignorable characters before it)` and `<code>` is `0x80` for apostrophe / `0x82` for
  hyphen — verified against ACE (e.g. `ANNE-MARIE` → `… 80 17 06 82 …`, the hyphen at position 4).

  **Descending** text keys are the **bitwise inverse of the ascending key, with a `0x00`
  appended** — verified against ACE (e.g. ascending `A` = `7F 4A 01 00` → descending
  `80 B5 FE FF 00`). The inverted start flag is `~0x7F = 0x80`, matching the descending flag of
  the fixed-type keys.

  *Not yet handled:* non-ASCII characters.
- **Binary / GUID:** collation encoding not implemented (read-only as before).

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
  an OLE long-value blob (~110 bytes, "MR2"-prefixed) holding the object's **extended
  properties** — including column-level properties such as *Required* (see §3.4). LibRed leaves
  `LvProp` null (long values are not writable yet).

  > With those fields set, Access **enumerates** a LibRed-created table (it appears in the
  > schema/Tables rowset) — verified via OLE DB. Maintaining MSysObjects' indexes (the composite
  > `ParentId+Name` and `Id` indexes) then lets Access **resolve the table by name** and attempt
  > to open it. Opening it then requires the table's own structures to be byte-valid to Access
  > (see §3.7).

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
(`HACKING.md`, and its `table.c` / `data.c` / `index.c`) and Jackcess (`TableImpl`, `ColumnImpl`,
`IndexData`, `IndexCodes`) — consulted upstream, not vendored here. The LibRed test suite (`test/LibRed.Core.Tests/`) pins these
structures, including whole-database golden dumps. Write-side structures (row insertion,
order-preserving key encoding, leaf-entry layout) are additionally cross-checked against
Access's own engine via OLE DB: insert the same row through LibRed and through Access, then
confirm the row sets match and that Access seeks the LibRed-written index entry.
