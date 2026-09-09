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
| `0x14` | 4 | **Highest AutoNumber value assigned** = the id of the last row inserted (the *next* id is this **`+ increment`**, see `0x18`); `0` when the table has no AutoNumber column. On a freshly created custom counter it is **`Seed - Increment`** so the first insert yields the `Seed` (verified: `COUNTER(1000, 7)` → `0x14` = `993`, first id `1000`). Verified **directly against `@@IDENTITY`**, and disambiguated from row count with a delete-gap: after inserting 3 rows, deleting id `3`, and inserting again (which is assigned id `4`, *not* reused `3`), `0x14` = `4` = the last inserted id while the row **count** is `3`. (Also: Northwind Categories = `8`, non-autonumber/text-PK tables = `0`.) mdbtools labels this *"Next autonumber value"* — that's **off by one**; the stored value is the last assigned, and the next id is `+ increment`. It is a **plain signed int32 that wraps** — there is no "counter exhausted" state (see the wrap note below). **Write requirement:** a writer inserting into an AutoNumber table must advance this to the last id it writes (LibRed does so in `RowInserter`); leaving it stale makes Access reissue an existing id and reject the insert as a duplicate primary key — verified end-to-end. |
| `0x18` | 4 | **AutoNumber increment** — a **signed 32-bit int** (same width as `0x14`); the step added to `0x14` for each new id. Default `1` (a plain `COUNTER`); a custom `COUNTER(seed, increment)` / `AUTOINCREMENT(seed, increment)` / `INTEGER IDENTITY(seed, increment)` sets it. **Confirmed a full int32, not a byte + 3 unknown** (verified vs ACE): `COUNTER(1, 300)` → `2C 01 00 00` (spans 2 bytes, ids `1, 301, 601`); `COUNTER(5, 100000)` → `A0 86 01 00` (3 bytes); and decisively `COUNTER(100, -5)` → `FB FF FF FF` = `-5` in two's-complement (all 4 bytes) with a **descending** sequence `100, 95, 90`. It reads `1` on every table (autonumber or not) because that is the default increment — mdbtools/Jackcess mislabel it a 1-byte constant / "autonumber enable" flag, which only *looks* right because the default increment is 1 (LibRed's own finding). The seed itself is not stored separately — it is recovered as `0x14 + increment` (correct on a freshly-created, un-inserted table). A writer/reader must treat it as a signed int32; the insert bump of `0x14` moves in the increment's direction (max for +, min for −) so a descending counter doesn't reissue an id — except at the int32 wrap, where the generated id is the correct continuation despite comparing as backwards (see the wrap note below). |
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
> reverse-engineering) but differ for user tables. For **sizing the body**, which is this section's
> concern: `0x33` (real index count) sizes the index-data blocks **and** the `0x3F` pre-column block, while
> `0x2F` (logical count) sizes the logical-index info blocks and the index names. Why the two differ, why
> `0x33 ≤ 0x2F` always holds, and which of them the 32-index limit binds on are
> [page-02d §3.5](page-02d-constraints.md).

> **The AutoNumber counter wraps at the int32 boundary — there is no overflow error** (verified vs ACE
> OLE DB 16.0/12.0, `AceAutoNumberOverflowProbeTest`). `0x14` is an ordinary signed int32 and the next id is
> `0x14 + 0x18` computed **unchecked**, so an ascending counter runs
> `… 2147483646, 2147483647, -2147483648, -2147483647 …` and a descending one mirrors it
> (`-2147483648 → 2147483647`). ACE issues the wrapped id, writes it to `0x14`, and carries on — nothing in
> the header records that the counter has been round the ring. This also happens without ever reaching the
> boundary by counting: an explicit `INSERT` of `2147483647` into a plain `COUNTER` sets `0x14` to it (the
> KB 884185 last-inserted rule), and the very next auto id is `-2147483648`.
>
> The only failure is a wrapped id that is **already occupied** — an ordinary duplicate-key rejection. ACE
> still advances `0x14` past it (the id is burned even though the insert failed), so the following insert
> succeeds and the counter steps over the squatter.
>
> **Write requirement:** a writer must treat an id it *generated* as advancing the counter unconditionally,
> because the wrapped value compares as going backwards. Applying a monotone "only if greater/lesser" rule to
> it pins `0x14` at `int.MaxValue` forever, so every later insert reissues `int.MinValue` and the table is
> wedged — for ACE too, since the damage is the on-disk `0x14`. LibRed therefore applies its monotone guard
> (the deliberate KB 884185 immunity) **only to caller-supplied explicit ids**; see `RowInserter`.

> **Reader/writer count guardrails.** LibRed validates the documented 255-column and 32-real-index
> limits before allocating count-sized structures, rejects negative logical/real index counts, and checks
> each fixed-size count-derived region against the fully assembled definition before parsing it. The writer
> likewise rejects column counts/ids outside `0..254`, duplicate ids, byte lengths that do not fit the 16-bit
> descriptor field, and fixed-data layouts whose offsets would exceed that field. These checks enforce existing
> geometry; they do not change the layout or the high-water semantics below.

> **Variable-region guardrails.** The declared definition length is also the parsing boundary for
> direct/synthetic buffers, not only continuation-chain reads. Column and index names must have a nonzero,
> even UTF-16LE byte length of at most 128 bytes (64 UTF-16 code units), decode without replacement, and fit
> before that boundary. Column type codes and ids are validated, ids must be unique in `0..254`, and stored
> variable-table indexes must fit the `0x2B` high-water. The final long-value-map list must contain complete,
> unique 10-byte entries for existing Memo/OLE columns, end with `0xFFFF`, and consume the definition exactly.
> LibRed reports violations as `InvalidDataException` before constructing catalog/index metadata.

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
>   **`ALTER COLUMN` consumes an id from `0x29` too**, keeping the column's ordinal position — so after a
>   modify, descriptor **position ≠ id**. That is a property of the ALTER mechanism rather than of this
>   field, and is documented with its measurements in
>   [page-02b §3.8](page-02b-columns.md#38-in-place-column-typelength-change-alter-column--verified-byte-for-byte).
> - **`0x2B` variable-length column count** — also a **high-water**. `ADD COLUMN` of a variable column
>   increments it (the new column's variable index = the old value); `DROP COLUMN` of a variable column
>   **leaves it unchanged**, so survivors keep their stored variable index (§3.4) and existing rows keep the
>   same number of variable slots. (A fixed column doesn't touch `0x2B`.)
>
> An added **fixed** column's fixed offset is the current end of the fixed region (`max(offset+length)`);
> an added **variable** column appends. A dropped column's descriptor + name are removed from the column
> region; a dropped **memo/OLE** column's §3.3.2 entry is removed, an added one's is appended (its two page
> maps go on the table's usage-map page, or a dedicated page if that's full — the same rule as create).

#### Which writers must honour each of these

Every rule above is a property of the **format**, so it binds every path that writes, not just the one it
was first measured on. That is not obvious from the prose: the rules are stated once, here, while the code
that must obey them is spread across three row writers and five definition mutators — and each new path
tends to re-derive the rule from the *live* columns, which is the one reading that is always wrong.

The table is the enforcement surface. A blank cell means the path cannot reach that invariant, not that it
is exempt.

| invariant | `RowEncoder`<br>(INSERT/UPDATE) | `BuildRelaidRecord`<br>(in-place ALTER) | `AddColumn` | `DropColumn` | `AlterColumn` retype |
|---|---|---|---|---|---|
| `colCount` + null-bitmap width = `max(live column id) + 1` | ✅ | ✅ `newMaxId` | | | |
| variable slots per row = `0x2B` | ✅ via `TableDef.VariableColumnCount` | ✅ appends onto a full-width row | | | |
| variable index from `0x2B`, hole abandoned | | | ✅ | ✅ leaves `0x2B` | ✅ |
| fixed offset = live `max(offset+length)`, hole **reused** | ✅ | ✅ `newFixedLen` from the old row | ✅ | ✅ no renumber | ⚠️ **NOT the live max** — measure the fixed-region end from an existing row's var-data start, because a prior retype's dead slot makes the descriptors under-count it ([§3.8](page-02b-columns.md)) |
| fixed region never shrinks below existing rows | ✅ `InferFixedDataLength` takes `max(pinned, derived)` | ✅ derived from the old row | | | |
| column id from the `0x29` high-water, 255 lifetime cap | | | ✅ | ✅ leaves `0x29` | ✅ burns an id |

**Audited, and two of the cells were wrong when the table was first drawn up** — the two row-writer cells
for the variable-slot count. `RowEncoder` packed the chunks densely and `BuildRelaidRecord` appended onto
the short row that produced, so a `DROP COLUMN` of a variable column silently moved every later column
down one slot, and dropping the *last* variable column then retyping another made ACE reject the file
outright. Both are fixed; the row is `VariableColumnHighWaterAccessTests`.

The remaining cells were checked the same way rather than by reading — ACE performing the identical DDL,
compared field by field, and ACE reading rows written on both sides of the drop. Two are worth recording
because the obvious guess is wrong:

- **A row's `colCount` is `max(live id) + 1`, not this page's `0x29` high-water** — they differ once the
  highest-id column is dropped. The row-side consequence is [page-01 §5](page-01-data-and-rows.md).
- **The fixed half follows the OPPOSITE rule to the variable half.** A dropped variable column's index is
  abandoned and the next added column goes *above* it; a dropped fixed column's offset is **reused** by the
  next fixed column added. `F(K, P, Q LONG, T TEXT)`, drop `Q`, add `R LONG` → ACE puts `R` at offset 8,
  where `Q` was. Nothing about one half predicts the other, which is exactly why deriving the variable
  section from the live columns looked reasonable.


### 3.2 Multi-page TDEFs

If a table has enough columns (or indexes), the definition spans pages chained by the `0x04` pointer.
Reassemble before parsing: take the **first page whole**, then append each continuation
page's bytes **from offset 8** (continuation pages have an 8-byte header). Column offsets are
absolute from the first page, so parsing is otherwise unchanged.

LibRed uses one shared reader for catalog parsing, index-root updates, and DDL surgery. It treats the
`0x08` definition length as authoritative: the length determines the exact number of continuation
pages and the exact number of bytes copied from the final page. Every page number must be in-file,
the chain must be acyclic, continuation headers must be `[02 01]`, and the chain must be neither
shorter nor longer than the declared length. LibRed additionally applies a **1 MiB per-definition
safety budget** before allocation. That budget is an implementation hardening limit—not a newly
claimed Jet/ACE field limit—and is deliberately far above the maximum observed/constructible from
the documented 255-column, 32-index, and 64-character-name limits.

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
0x3F : index statistics      IndexCount(0x33) × 12 bytes       (per-index, §3.3.1)
       column descriptors    ColumnCount(0x2D)    × 25 bytes
       column names          ColumnCount          × (2-byte length + UTF-16LE)   (naming limits below)
       index-data blocks     IndexCount(0x33) × 52 bytes
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
- **Writer sizing/preflight** — LibRed computes that definition length from the encoded descriptors,
  UTF-16 column/index names, index blocks, and complete long-value usage-map list before allocating or
  writing. Names obey ACE's verified 64-character limit; every referenced index column and Memo/OLE
  column id must exist; long-value map rows/pages must fit their 1-byte/3-byte fields; duplicate LVAL
  entries and definitions beyond the shared 1 MiB validated budget are rejected before serialization.
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
  including the other indexes' root bits) rather than rewriting it — unless that page is full, in which
  case the map goes on a page of its own, as ACE's does (see the multi-page distribution rule in
  [long-values.md](long-values.md)) — then **back-fills** the B-tree by
  scanning every existing row (`AddEntry` per row). Verified vs ACE: a primary key added after data
  enforces uniqueness and seeks correctly, incl. a 2000-row back-fill that splits the tree.

> **Access now opens and round-trips a LibRed-created table** (empty `COUNT`, `INSERT`, read-back —
> verified through the ACE OLE DB provider). Getting there required *all* of the following together;
> each was independently necessary (removing any one reproduces "Unrecognized database format"):
>
> 1. **TDEF byte-validity** — every constant/marker written (§3.1), and the trailing `0xFFFF` that
>    terminates the **long-value usage-map list** (§3.3.2 — *not* the index names, which precede it)
>    included in the definition length. It is mandatory even on a table with no long-value columns at all;
>    [long-values.md](long-values.md) owns the rule and the byte-diff that found it.
> 2. **Global page allocation** — pages must be taken from the database's **global free-pages map**
>    (§9.1), not by blindly growing the file, so Access accounts for them. LibRed allocates by
>    clearing a free bit there.
> 3. **Lazy data page** — match Access's model of a fresh table with *no* data page and empty usage
>    maps (above). The first insert (LibRed's or Access's) allocates the data page on demand and sets
>    its bit in both the table's owned- and free-pages maps.
> 4. **Catalog rows** — a complete MSysObjects row with its indexes maintained (§11) and the
>    MSysACEs permission rows, so Access resolves the table by name before it opens it.
