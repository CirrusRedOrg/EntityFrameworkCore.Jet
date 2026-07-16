# Table-definition (TDEF) page — type 0x02

> Part of the [LibRed Jet / ACE file-format reference](README.md). Cross-references use the original **§-numbers**; the [section map](README.md#section-map) says which file each lives in.

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
| `0x1C` | 4 | Complex-type AutoNumber (mdbtools `ct_autonum`) — the high-water value for a *complex* column (multi-value / attachment). `0` in every table observed; LibRed has no complex-column fixture to confirm a non-zero value (OLE DB DDL can't create such a column). **Read into `TableDef.ComplexAutoNumber` and written through `TdefBuilder` (0 for a table with no complex column) so it round-trips via the model, not only the raw surgery path** (`ComplexAutoNumberRoundTripTests`) |
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
>   Because it never decrements, `0x29` is a hard **lifetime cap of 255**: once 255 ids have been handed out,
>   `ADD COLUMN` fails even if the *live* count (`0x2D`) is lower — only a **compact** (which renumbers) frees
>   the id space. ACE-verified: create 255 columns, drop 10, `ADD COLUMN` → *"Too many fields defined."*
>   LibRed enforces this on `0x29` (not the live count) rather than write a 256th id ACE can't represent.
>   **Access burns an id per *modify* too, but keeps the column's position (both verified vs ACE via OLE DB
>   `ALTER TABLE … ALTER COLUMN`).** Changing a column's type is internally *a new column*: the column keeps its
>   **ordinal position** in the field list but gets a **fresh id** from the `0x29` high-water. Probed: a
>   4-column table `A,B,C,D` (ids 0,1,2,3); `ALTER COLUMN B …` → B stays at index 1 with **id 4**; a later
>   `ALTER COLUMN C …` → C stays at index 2 with **id 5**; every *other* column keeps its id. So after modifies,
>   descriptor **position ≠ id** (ids non-contiguous, in a fixed physical order).
>   - **Null bitmap is keyed by column *id*** (not position) — verified: a row ACE wrote into that modified
>     table (`A` null, `B` id 4 non-null) is read back correctly by LibRed's id-keyed decoder, i.e. `B`'s
>     present-bit sits at bit *4*, not bit 1.
>   - **LibRed today:** `RewriteColumn` preserves each untouched column's **original descriptor bytes**
>     verbatim (the `RawDescriptor` passthrough — fields LibRed doesn't model survive the rewrite) and keeps
>     column order, but it does **not** yet burn the target's id: it rebuilds with **contiguous** ids (the
>     target keeps its position/id). Reason: LibRed's row **encoder** keys the null bitmap by id sized to the
>     *live* column count, so a burned id that exceeds the live count writes a present-bit ACE can't find
>     (verified: a LibRed-written burned-id table read back null in ACE). Burning the id faithfully needs the
>     encoder's bitmap sizing reconciled with ACE's first. Until then LibRed stays more permissive on the 255
>     cap (never spuriously "Too many fields"), which is a deliberate, ACE-readable divergence — not a
>     correctness gap.
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
       column names          ColumnCount          × (2-byte length + UTF-16LE)   (naming limits below)
       index-data blocks     RealIndexCount(0x33) × 52 bytes
       index-info blocks     LogicalIndexCount(0x2F) × 28 bytes
       index names           LogicalIndexCount    × (2-byte length + UTF-16LE)
       column usage maps     (per long-value column) × 10 bytes, then 0xFFFF  (§3.3.2)
```

> **Object-name limits (verified vs ACE OLE DB 2026-07-12).** The 2-byte length prefix could physically hold a
> 65535-byte name, but ACE enforces **64 characters** for table/column/index names — a longer name makes ACE
> reject the *entire file* (65+ char column → "Unrecognized database format"; 65+ char table → "Unspecified
> error"), not just the object. ACE's *storage/read* path tolerates every special character (quotes, `#`, `%`,
> `&`, spaces, tab, unicode all round-trip), but `. ! ` `` ` `` `[ ]` make the name **unreferenceable in SQL**
> (both bracket- and backtick-quoted `SELECT` fail) — matching Access's documented forbidden set. LibRed enforces
> both (max 64 + forbidden chars) on caller-supplied names via `JetName.Validate`, since writing the format
> directly bypasses ACE's DDL parser. Internal hidden names (the `.rN` incoming-relationship index names, §3.6)
> legitimately start with `.` and are **not** validated.


### 3.7 Writing a TDEF Access accepts (verified)

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
  an indexed table adds a usage-map record **per index** (the index's own pages, §3.5 `+0x22`). Each
  index's map covers **every page of that index's B-tree** — root, internal nodes and leaves — not just
  the root. The root's bit is set at **CREATE**, before any row exists (verified: a freshly created
  empty index has exactly its root bit set); thereafter every page a split allocates is added, so the
  union of a table's index maps equals exactly the set of index pages present (verified against ACE:
  union == owned index pages, byte-for-byte, incl. a 4000-row load that splits both trees several
  levels). LibRed reproduces this: `IndexWriter.AllocateIndexPage` marks each page it allocates during a
  split, and `TableCreator` marks the root at creation (both `CreateTable` and `CREATE INDEX`).
  Note this map is **advisory for LibRed's own reads** — `IndexWriter` navigates the B-tree structurally
  (root child-pointers + leaf next-pointers), never by the map — but Access's maintenance relies on it,
  and it feeds the owned-map page-budget calculation (a growing index map shrinks the owned map's room;
  see §9). A **fresh table still has no data page** (Access allocates the first lazily on the first
  insert), so the *data* owned/free maps start empty. When an index is **added to a populated table**,
  LibRed *appends* the new index's record to the existing usage-map page (preserving every other record —
  including the other indexes' root bits) rather than rewriting it, then **back-fills** the B-tree by
  scanning every existing row (`AddEntry` per row). Verified vs ACE: a primary key added after data
  enforces uniqueness and seeks correctly, incl. a 2000-row back-fill that splits the tree.

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


