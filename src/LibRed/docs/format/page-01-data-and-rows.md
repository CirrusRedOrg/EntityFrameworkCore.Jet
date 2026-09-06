# Data pages (type 0x01) and the row record

> Part of the [LibRed Jet / ACE file-format reference](README.md). Cross-references use the original **§-numbers**; the [section map](README.md#section-map) says which file each lives in.

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
**Offsets are therefore non-increasing with slot index**, and the rows stored below a given row are exactly
the *later slots* — an ordering the delete path below depends on.

### Deleting a row reclaims its bytes

ACE closes the gap rather than leaving the row in place: the rows below it slide up, their slot offsets
follow, and the emptied slot becomes a **zero-length tombstone flagged deleted + overflow (`0xC000`) whose
offset is the row's former end**. Slot *indices* never move, which is what keeps index entries and row ids
valid. Measured on three 19-byte rows, deleting each position in turn — free space rises by 19 every time:

| deleted | directory after |
| --- | --- |
| first | `D000 0FED 0FDA` — tombstone at the page end (`0x1000`) |
| middle | `0FED CFED 0FDA` — at the row above's start |
| last | `0FED 0FDA CFDA` — nothing below to move |

Pointing the tombstone at the former end rather than at the page end is what preserves the non-increasing
order, which is what makes it zero-length: its offset equals the preceding slot's. LibRed used to only set
the deleted flag and leave the row where it was, so the space was never reclaimed — about 21 bytes per
delete, permanently. `DeletedRowSpaceAccessTests`.

The **slot directory** is not reclaimed by either engine: a tombstoned slot is never reused, so a page that
has seen thirteen rows carries thirteen slots whatever is live. Only the row bytes come back.

> **A record is capped at 4060 bytes**, counting everything in the row itself — the leading count, fixed
> data, variable data, the offset table and the null bitmap — but not the payload of a Memo/OLE column,
> which lives on LVAL pages behind a 12-byte descriptor. Past it ACE refuses the insert with *"Record is
> too large."*
>
> **The cap is ACE's, not the page's.** A page holds 4080 (4096 less the 14-byte header and a 2-byte slot),
> and the 20-byte reserve below that is measured, not explained. It is also not derived from the row's
> shape: three tables whose overhead differs by 23 bytes — 9, 12 and 20 text columns — all stop at the same
> 4060 (`RecordSizeAccessTests`).
>
> It is **the same 4060 under page-level and row-level locking** (`Jet OLEDB:Database Locking Mode` 0 and 1),
> which is what it must be — a limit that moved with the connection would make a file written by one client
> unreadable by another opening it differently. That independence is also what lets LibRed, which has no
> locking mode at all, enforce one constant for every caller.
>
> *Unverified lead, recorded because the evidence is suggestive rather than because it is established:* the
> 20 bytes may be space the engine always keeps for row-lock bookkeeping, whether or not the connection uses
> it. Two things point that way. Row-level locking arrived in Jet 4, the same generation as this reserve.
> And the **class** of error is wrong for corruption: a malformed row reads as "Unrecognized database
> format" or a decode failure, whereas this one reports a concurrent edit — so ACE parsed the row and then
> its multi-user layer objected, meaning something is *interpreting* those bytes rather than merely running
> past them. Nothing here tests it; the mode-independence above is equally consistent with the reserve being
> something else.
>
> **Writing into the 4061–4080 band is worse than writing past it.** It fits the page, so nothing fails at
> write time, and ACE then cannot materialise the row — it reports *"you and another user are attempting to
> change the same data at the same time"*, naming a concurrency problem that does not exist. Only above 4080
> does anything complain locally. A writer must therefore enforce 4060 rather than the page geometry;
> LibRed does so in `RowInserter` from `JetFormatBase.MaxRecordSize`.

> **Reader guardrails.** LibRed requires an exact format-sized type-`0x01` page before either a full
> scan or the O(1) index-seek slot path. The declared slot directory must fit before the heap; every
> masked row offset must lie between the directory end and page end and must not increase relative to
> the previous slot. Equal offsets remain valid because ALTER/relocation can deliberately leave a
> zero-length deleted+overflow tombstone. Violations are reported as `InvalidDataException` before a
> row span is constructed.

### Relocated rows

A live slot with `0x4000` set **begins with** a 4-byte little-endian forward pointer,
`(targetPage << 8) | targetRow`. The target is a nonempty inline row on a type-`0x01` page owned by
the same table. Its target slot has `0x8000` (deleted/hidden) set and `0x4000` clear: ordinary scans
skip the hidden physical row, while the original row id and its index entries continue to resolve
through the live source slot. A zero-length slot with both flags set is a tombstone, not a relocation
source. These shapes are verified by LibRed-created files opened by Access and Access-relocated files
read by LibRed.

**The slot is normally exactly 4 bytes wide, but not always.** When ACE relocates a row through
ordinary DML it trims the slot down to the pointer, and LibRed does the same. Measured across **317
relocations with no exception**, covering ACE on x64, the **ACE 2010 runtime on x86**, and LibRed's
own writer, under: growing and shrinking text, repeated re-relocation of the same rows, page
fragmentation by interleaved deletes and re-inserts, and an OLE column going from NULL to a value.

Longer slots exist in the wild all the same. In the Northwind ACCDB, `MSysAccessStorage` carries live
overflow slots of 45–63 bytes. Their content is the row **as it was before it moved**, with only the
leading 4 bytes replaced by the pointer:

- discount those 4 bytes and every field lands exactly where the row format puts it — the pointer
  covers the 2-byte column count plus the first 2 bytes of the first column, leaving the remaining
  6 bytes of that column at offset 4;
- the remnant's `Id` / `ParentId` / `Type` / `Name` equal those of the row it forwards to;
- the remnant's null bitmap differs from its target's in exactly one bit, the OLE column `Lv` —
  NULL in the remnant, set in the target — which is what grew the row and forced the move;
- every remnant is shorter than its target (55→89, 55→89, 63→87, 63→75, 45→57, 57→71, 53→65).

So the slot kept the previous row's width and was stamped with the pointer rather than trimmed.
**What writes them is not known**, and is deliberately not asserted here. No write path reproduces
the shape — not the OLE-column transition the bytes themselves record, and not an older engine
(ACE 2010 trims exactly as the current one does). What has *not* been exercised is Access's own
maintenance of its system tables, which is not reachable through SQL DML. Readers must therefore
take the pointer from the leading 4 bytes and ignore any remainder rather than requiring a width.

LibRed follows relocations through one shared resolver used by scans, index seeks, and raw-row
mutation helpers. It validates that the source begins with a 4-byte pointer, plus the in-file page
number, target row, page owner, and source/target flag shapes before exposing target bytes;
malformed pointers fail with `InvalidDataException`.

## 5. Row record format

```
[ colCount : 2 ]                                ← (max column id + 1), NOT the live column count
[ fixed-length column data ... ]
[ variable-length column data ... ]             ┐
[ variable-offset table : (numVarCols + 1) × 2 ]│ present ONLY when numVarCols > 0
[ numVarCols : 2 ]                              ┘
[ null bitmap : ceil(colCount / 8) bytes ]      ← the very end of the row
```

- **The variable section (offset table + `numVarCols` field) is OMITTED entirely when the table has no
  variable columns.** An all-fixed row is just `[colCount][fixed][nullBitmap]` — verified vs ACE
  (`AceModifyByteDiffProbe.Diff_libred_vs_ace_all_fixed_row_bytes`): `T(A,B,C LONG)` + row `(11,22,33)` is
  **15 bytes** `03 00 | 0B000000 16000000 21000000 | 07`, not 19. The fixed-region length is recovered from the
  schema (column offsets), so the row needs no var-data-start pointer. (A reader keyed on fixed offsets +
  null bitmap decodes both forms; a *writer* must omit the section to be byte-faithful.)

- **`colCount` is `max(column id) + 1`, not the live column count** — the two coincide only while ids are
  contiguous (a fresh table, or after ADD COLUMN, which keeps ids contiguous). They **diverge** once ids have a
  gap — a burned id from a type-change ALTER, or a DROP COLUMN gap — and then `colCount` (and therefore the null
  bitmap width) is driven by the **highest id**, leaving bit positions for the dead ids. Verified vs ACE
  (`AceModifyByteDiffProbe`): after `ALTER COLUMN B DOUBLE` burns B's id 1→3 in a 3-column table, the row's
  `colCount` field is **4** and the null bitmap is `0x0F` (the dead id 1's bit is set present). A writer that
  sizes these by the live count writes a bit ACE can't find for any id ≥ live count → ACE reads that column null.
- **Null bitmap** is indexed by **column id**; a **set bit = the value is present** (non-null).
- **Fixed** column value is at `rowStart + 2 + fixedOffset`, `length` bytes.
  - A **fixed-length text** column (`CHAR`/`NCHAR`, not `TEXT`/`VARCHAR`) fills its whole `length`: the value is
    **space-padded** (UTF-16LE `0x20 0x00`) to the fixed byte width, and reads back padded — verified vs ACE
    (`CHAR(50)` of "Eastern" stores + returns 50 chars, trailing spaces; `TEXT(50)` is variable and stores/
    returns the 7 chars). A fixed-length **binary** column is **zero-padded** to its width.
- **Variable** column value: with `varTableStart = rowEnd − nullBitmapSize − 2 − (numVarCols+1)×2`,
  variable column `j` spans `[offset(numVarCols − j), offset(numVarCols − j − 1))`, where
  `offset(k)` is the little-endian 16-bit value at `varTableStart + k×2`. (The table is stored
  end-first, i.e. ascending column-id order maps to descending table index.)
  - A variable **text**/**binary** value must **fit its column's declared width**. Where a fixed column pads or
    truncates, ACE **rejects** an over-long variable one — six characters into a `TEXT(5)`, six bytes into a
    `VARBINARY(5)`, both *"The field is too small to accept the amount of data you attempted to add"* (verified
    vs ACE, `ColumnLengthAccessTests`). The bound is the descriptor's `length`, in **bytes** for both, so
    `TEXT(5)` is 10. `Memo`/`OLE` are exempt — their inline form is a long-value descriptor whose size is
    unrelated to `length`.
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

> **Row-reader guardrails.** LibRed bounds the null bitmap and optional variable trailer before reading
> them. The offset table must fit the row, entry 0 (the variable-data end) must not pass the table start,
> and its end-first offsets must remain within the row and be non-increasing. LibRed permits unused bytes
> between entry 0 and the table itself; this preserves existing schema-evolved rows in the functional corpus,
> but the reason those rows retain the gap has not yet been verified against Access. A requested variable slot
> must exist. Fixed values must fit the derived fixed region, and fixed-width scalar codecs require the
> exact widths in §6. A column id beyond an older row's stored `colCount` is absent/null—this preserves
> ADD COLUMN behavior—and the variable trailer is considered present only when the stored row count
> covers a currently known variable column. This also preserves the verified all-fixed form that omits
> the trailer entirely. Structural violations are reported as `InvalidDataException`.

## Writing — inserting a row

Placing an encoded row record (§5) into a data page's heap:

> **Inserting a row** (verified — Access reads the result): place the record at
> `lowestRowOffset − recordLength` (just below the current lowest row, or `pageSize − recordLength`
> on an empty page), append its offset as a new slot at `0x0E + rowCount×2`, increment the row
> count (`0x0C`), and decrease free space (`0x02`) by `recordLength + 2` (record bytes plus the
> slot entry). The new row's **row id** is `(thisPage, oldRowCount)`. The fixed-region length of
> the encoded record must match the table's existing rows — read it off any existing row's
> variable-offset table (its last entry is the variable-data start = `2 + fixedRegionLength`).
> A row is found by **table scan** as soon as it is in the heap, but an **indexed lookup** (and
> Access's PK seek) misses it until it is also added to every index B-tree (§10.4).
