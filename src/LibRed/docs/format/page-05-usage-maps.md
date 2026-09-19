# Usage maps and page allocation

> Part of the [LibRed Jet / ACE file-format reference](README.md). Cross-references use the original **§-numbers**; the [section map](README.md#section-map) says which file each lives in.

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

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Type `0x01` |
| `0x01` | 4 × 17 | Bitmap-page pointers, little-endian; `0` ⇒ that page range owns nothing |

The record is exactly **69 bytes** (`1 + 17 × 4`). Seventeen slots is not arbitrary: each bitmap page
covers `(4096 − 4) × 8 = 32,736` pages ≈ 134 MB, so 17 slots span ≈ 2.28 GB — just past Jet's 2 GB
file ceiling. A bitmap page's header is `[0]=0x05`, `[1]=0x01`, `[2..3]=0`, bitmap from offset 4.
Bitmap pages are allocated **lazily**, only when a bit in their range is first set, and are *not*
themselves marked as owned by the table.

LibRed treats this geometry as mandatory on read: a type-`0x01` record must be exactly 69 bytes,
each nonzero pointer must be within the physical file, and its target must carry the complete verified
`[05 01 00 00]` bitmap-page header. This validation happens before any bitmap is expanded into page
numbers, so appended pointer-shaped bytes or pointers to ordinary data pages cannot become ownership data.

> Verified against an ACE-built table large enough to need one: owned map record
> `01 017D0000 E17F0000 00…` — type `0x01`, slot 0 → page 32,001, slot 1 → page 32,737, remaining
> 15 slots zero, record length 69. Bitmap page 32,737's first bitmap byte is `0xFC`: pages 32,736 and
> 32,737 clear (32,737 *is* the bitmap page), 32,738–32,743 set.

> The usage map is authoritative: a brute-force owner-scan can over-count, because deleted/
> orphaned pages can retain a stale owner stamp that the map correctly omits.

> **LibRed write behaviour (multi-page growth on insert).** When an insert finds no owned data page
> with room, LibRed allocates a new page (via the global map, §9.1), initialises it as an empty data
> page owned by the table, sets its **owned** bit, and moves the **free** marker to it. Verified:
> Access reads a table LibRed spilled across many data pages and can still insert into it.
> - **Free-pages map = the current append tail only.** Access clears a page from the free-pages map
>   when an insert finds it too full and moves on, so after a sequential fill only the *last* page
>   stays marked free — verified: of equally-full ACE pages only the last is in the free map, because
>   each earlier one had a next-row attempt that didn't fit. LibRed matches this: on allocating a new page
>   it **clears the previous tail's free bit and sets the new page's**, leaving exactly the tail marked
>   free. (Non-sequential fills and deletes aren't specially handled — neither is supported yet.)
> - **Inline map, grown in place.** LibRed writes the inline map with `startPage = 0` and an initially
>   64-byte bitmap (pages 0–511). When an insert needs to mark a page **past** that window, LibRed grows
>   the bitmap **record in place** — still type `0x00`, same `startPage` — extending it in **32-bit (4-byte)
>   steps** and repacking the usage-map page's records (the `owned`/`free` maps and any index/column maps)
>   from the end backward. So the record length is exactly
>   `5 + roundUp(ceil((maxPage + 1 − startPage) / 8), 4)`.
>   Verified against ACE owned-map lengths on a 255-column table whose data pages start at 353:
>   8,000 data pages → **1053**, 12,000 → **1553**, 16,000 → **2053**, 30,000 → **3801**, 31,000 → **3925** — the
>   formula reproduces every one. The step is **4 bytes, not 32**: a table spanning to page 753 carries a
>   **96**-byte bitmap (record length **101**), which fits either because 96 is a multiple of both, but
>   32-byte rounding would give 1056 / 1568 / 3808 for the larger tables. Access opens a LibRed-grown table,
>   counts every row, and reads one living past page 512.
> - **Inline → reference conversion.** Access keeps growing the inline record until it no longer fits its
>   usage-map page, then rewrites the map as type `0x01`. Verified by owned-map record length against
>   page count: 8,000 pages → 1053, 12,000 → 1553, 16,000 → 2053, 30,000 → **3801, still type `0x00`**;
>   at 34,000 pages the map is type `0x01`. So the switch is driven purely by *record fits the page*, not
>   by a fixed page-count threshold. LibRed applies the same rule: grow inline while the repacked record
>   fits, otherwise convert — re-marking every previously-owned page into freshly allocated bitmap pages
>   (grouped by slot, one write per bitmap page) and shrinking the record to the fixed 69 bytes.
>   ACE reads a LibRed-written reference map and counts every row back.
> - **Movable window (free-pages maps).** A free-pages map's set bits stay clustered at the append tail, so
>   Access never grows it: it slides a fixed **64-byte bitmap (512 pages)** whose `startPage` is
>   `floor(page / 512) × 512`. Verified — a table whose tail page was 852 / 1227 / 1852 / 2852 had a free
>   record of length **69** with `startPage` 512 / 1024 / 1536 / 2560, one bit set, while the *owned* map of
>   the same table kept `startPage = 0` and grew. An owned map cannot slide: it must retain every page it has
>   ever taken. LibRed slides the window for free-pages maps (table and long-value column), and only for
>   them; if a bit already set would fall outside the new window it grows in place instead, so nothing is
>   silently forgotten.
> - **The conversion point is a page-budget calculation, not a constant.** The owned map converts as soon as
>   its next grown record would not fit the usage-map page alongside the page header (14 bytes), the row
>   directory (2 bytes/record) and the *other* records sharing that page. So the threshold moves with the
>   table's shape:
>
>   | Table | Records on the map page | Owned-record budget | Converts at |
>   | --- | --- | --- | --- |
>   | No primary key | owned + free | `4096 − 14 − 4 − 69` = **4009** | page **32,032** |
>   | Primary key | owned + free + the index's own map | `4096 − 14 − 6 − 69 − 69` = **3938** | page **31,456** while the index map stays 69 bytes |
>
>   The no-PK row is verified end-to-end: LibRed's table converts at exactly page 32,032 with a last inline
>   record of 4009 bytes; ACE's is still inline at page 31,354 (record 3925) and reference by 32,356,
>   bracketing the same value. **The primary-key row is not reconciled.** ACE carries the third record but has
>   already converted by page 31,409, earlier than 31,456 — consistent with the index's map growing past 69
>   bytes (below) and shrinking the budget, but the exact ACE conversion point with a key is not measured.
>
>   > Beware comparing thresholds across table shapes: a **primary-keyed** table and a **key-less** one
>   > convert at different page counts under the identical rule, because only the budget differs. A keyed
>   > table's index usage map also *grows* with the index's pages (its own B-tree), further shrinking the
>   > owned map's budget — LibRed matches this (`IndexWriter` marks each index page it allocates).

> **Owned-row recycle on an index rebuild (verified vs ACE, §3.8).** When ACE rebuilds an index (e.g. an
> `ALTER COLUMN` on an indexed column) it gives the index a **new** owned-pages usage-map row rather than
> editing the old one in place, in two steps whose leftover is observable on disk: **(1)** append a fresh row
> at the end of the usage-map data page and set the new root's bit — those bytes are then **abandoned** and
> stay behind, stale, in free space; **(2)** **re-lay** the live records with the *old* row's record
> **reclaimed**: its slot becomes a **0-length deleted + overflow tombstone** at the preceding record's
> offset, every later row **keeps its number** while its record slides up by the reclaimed width, and the
> fresh map takes the position freed at the end of the live region under the appended row number. The
> index-data block's usage-map row field (`0x22`, §3.5) is re-pointed to that number; **no other pointer
> changes**, because no other row's number does. LibRed reproduces this exactly (`RecycleOwnedMapRow`),
> abandoned bytes included, so the whole file matches ACE byte-for-byte. Measured on ACE's own page, records
> identified by content, with a long-value column's maps sitting below the index's:
>
> ```
> before  row2 @3889 pages=[353]   row3 @3820 pages=[]   row4 @3751 pages=[]     free=3727
> after   row2 @3958 TOMBSTONE     row3 @3889 pages=[]   row4 @3820 pages=[]     free=3725
>         row5 @3751 pages=[355]   ← the new map, under the appended row number
>         stale @3731 = 0x08       ← step (1)'s abandoned record, at 3682
> ```
>
> The long-value maps slid up a record width and kept rows 3 and 4; only the index's pointer moved, to row 5.
>
> > **Each half hides from a different measurement.** The abandoned copy lies *below* the lowest live
> > record, inside the region free space already covers, so slot offsets and free-space arithmetic both read
> > the page as though it were not there — yet dropping step (1) costs a byte against ACE. The re-lay is
> > invisible to offsets alone, because a moved record and a slid record occupy the same places; only
> > identifying records **by content** separates them. And the two are indistinguishable altogether when the
> > recycled row is the **last** one — the only kind an ACE-built schema produces, since a long-value column
> > declared in `CREATE TABLE` takes its map rows before the index's. So writing step (1)'s record into the
> > old row's slot passes every last-row shape, and then points a slot back **up** the page the moment a
> > Memo/OLE column is added *after* an index — which no reader can walk, a row's extent running to where the
> > previous slot begins. Only a whole-file byte diff against ACE catches both halves.


### 9.1 Global usage maps — free and released pages (page allocation)

Besides the per-table maps, the database has two **global** usage maps, found through page 0 rather than the
catalog ([page-00 §2](page-00-database.md)):

| Page 0 | Map | In every file ACE writes |
| --- | --- | --- |
| `0x18` | **free pages** — a set bit is a page available for allocation | page 1, row 0 |
| `0x1C` | **released pages** — a set bit is a page freed but not yet reusable | page 1, row 1 |

Both are ordinary usage-map records (inline or reference form, §9) on a data page whose owner field
(`0x04`) reads `0x00000001`, each starting as a 69-byte inline map with start page `0`. In the free map a
**set bit means the page is free / available**, the *opposite* of a per-table owned map — verified by
diffing before/after an ACE `CREATE TABLE`.

**ACE follows the pointers; the location is not fixed.** Allocation uses whichever record `0x18` names, row
included: pointed at page 1 row 1, ACE allocates from that map and leaves row 0 untouched. With both maps
copied to another data page and the pointers aimed there, ACE allocates, releases and reopens entirely on
that page and never reads or writes page 1; the holder page's owner field (`0x04`) is not checked. The two
pointers must name **different** records — naming the same one clears the free map when the released map
is emptied, and freed pages are lost.

**Both pointers are range-checked on every open, row byte excepted.** A page past the end of the file opens
and reads once, then records `01 00` ("accessed a corrupted page", [page-00 §2.2](page-00-database.md)) in
the opening user's commit slot at close, and every later open fails with *"Unrecognized database format"*.
A far larger page number is refused on the first open — from page 524,289 exactly, as measured on `0x1C`.
Only the page is checked on open — any row byte passes — but a `0x18` naming a record that is not a usage
map (page 0, a TDEF page, an ordinary data page) fails at the first allocation and damages the database.

**Freed pages are released at close, through the released-pages map.** Pages freed during a session are not
reusable on the same connection: later allocations in that session grow the file instead, and the free map on
disk does not change until the file closes. At close ACE moves them into the free map. This holds for the
long-value pages of a deleted row, the pages of a dropped index — by `DROP INDEX`, by `DROP CONSTRAINT` on a
foreign key, or by the index rebuild of an `ALTER COLUMN` — and every page of a dropped table. A free made in a
transaction that rolls back frees nothing; one that commits stays released even if a later transaction on the
connection rolls back.

The exception is the long value an `UPDATE` replaces: its pages are set in the free map at once, and later
statements on the same connection reuse them; the `UPDATE` that frees them does not.

Any page set in the released-pages map is likewise **never allocated**, and at close its pages are merged into
the free map and the map is left empty. Before that merge, the close sizes the map to cover every page released.
An inline record that already covers them stays as it is. Otherwise, in order of preference:

- **Lengthen it.** Keeping its start page, the record grows, never shrinks, to the shortest that covers the
  highest page released: 5 header bytes plus `roundUp(⌈(highest + 1 − start) / 8⌉, 4)` bitmap bytes — page 569
  gives a 77-byte record, page 728 a 97-byte one. The record grows only while its holder keeps **4 bytes free**,
  the same limit as the free map's growth below.
- **Move its window.** When that is too long, the start page becomes the lowest page released rounded down to a
  multiple of 8, and the record is sized the same way from there — pages 32,819–33,825 released into a 69-byte
  record starting at page 0 give start page 32,816 and a 133-byte record.
- **Convert it to reference form.** When even the moved window is too long:
  1. the inline record grows at its old start just far enough to cover the highest released page it can reach
     — at most 4,005 bytes beside a 69-byte free map, covering 32,000 pages — and the released pages it covers
     are set in it. Released pages running on past page 32,000 take it to the full 4,005 bytes; with released
     pages only in the first and third ranges it stopped at 3,609;
  2. a bitmap page is allocated for each 32,736-page range the released pages fall in, in range order, from
     the free map as it stands before the released pages are merged into it — the first page past the end of
     the file when nothing is free. A range holding no released page gets none, even between two that do;
  3. a 69-byte reference record naming them replaces the inline one. The records are repacked from the page
     end and the vacated bytes are not cleared, so the long record's bitmap stays on the page below the new
     one.

A map already in reference form gains a bitmap page, allocated the same way, for each range holding a released
page that it has none for — a released table-definition page among the free pages is taken like any other, its
type byte becoming `0x05`. The merge then clears every bitmap page, each keeping its `05 01 00 00` header.

So at rest the released-pages map has no bits set, though it may have grown, moved its start page or converted
to reference form. A non-empty one is *inferred* to be a release interrupted before close.

**Page allocation works through the free-pages map.** Access does **not** simply grow the file: it finds a
set bit (a free page), **clears it** (marking the page used), and reuses that page — only growing
the file when no free page remains. Verified: an ACE `CREATE TABLE` reuses free pages (for the TDEF,
usage map, etc.), and the only change to page 1 is one cleared bit per page taken.

> LibRed allocates **through** this map (`PageAllocator`), found as ACE finds it — through page 0's `0x18`
> pointer, row included — and never takes a page set in the released-pages map named at `0x1C`. A released
> page at the end of the file is materialized, so the file stays contiguous, but not handed out. It takes a
> free page, clears its bit, and reuses it — only growing the file when none is free — so its pages match
> Access's allocation. Free bits at the current file end are the pre-allocated growth region; LibRed materializes
> that next page contiguously before returning it, and rejects a bit that would skip beyond it. Repeated
> allocations can therefore consume an ACE-authored run of future bits without creating a sparse file.
> **Both map forms are handled.** For an inline (`0x00`) map it scans the record's
> bitmap directly; for a **reference (`0x01`)** map — as a very large pre-existing ACE file carries —
> it scans each slot's dedicated bitmap page (type `0x05`), where a **set bit is a free page** (the
> global map's sense), clears the bit on that bitmap page, and returns `slot × (pageSize−4)×8 + bit`.
> `Free` is the inverse (sets the bit). A page outside a pre-existing map's coverage cannot be recorded
> as free until that coverage exists.
>
> **Release at close.** The frees ACE holds go through `Release`, which keeps the page in a list on the handle
> — staged with the open transaction, kept on commit, dropped on rollback or on a rollback to a savepoint
> taken before it. Closing a writable `JetDatabase` returns those pages and any already set in the
> released-pages map to the free map, clears the released map and first sizes it as above, all in one
> transaction. A close that released nothing and wrote nothing writes
> nothing. Only an `UPDATE`'s replaced long value goes through `Free` at once.
>
> **Where LibRed differs from ACE.** An `UPDATE` frees the old long value before writing the new one, so the
> new value reuses those pages in the same statement. Held pages live in the handle, not in the released-pages
> map, so every handle releases its own at its own close, even while other handles are open.
>
> **Global-map growth.** The inline growth rule in §9 applies, but ACE leaves **4 bytes free in the
> holder page** before promoting the global map to reference form. With a 69-byte companion row,
> the final inline record is 4005 bytes, covering 32,000 pages. LibRed matches this transition and
> allocates each required bitmap page before the data page, marking the bitmap itself used.
> Existing file pages are marked used and the remaining new coverage free.
>
> **Allocator mutation guardrails.** Both page-0 pointers must name distinct, live, non-overflow rows on
> data pages inside the file, each an inline record with its complete header or a reference record of
> exactly 69 bytes with unique in-file bitmap pointers and the complete `[05 01 00 00]` header. A writable
> open checks this before anything else; a read-only open, which never allocates, does not. Page 0, the
> map holder pages, bitmap pages themselves, out-of-file free targets, and non-contiguous growth targets are
> rejected before a free bit is cleared or set.

**Releasing a table means walking every map it owns, not just the data-page one.** A Memo/OLE (or
calculated long-value) column holds its LVAL pages in a **per-column owned map**, whose (row, page) pointer
sits in the TDEF keyed by column id — those pages never appear in the table's own data-page owned map. A
`DROP TABLE` that frees only the data pages therefore strands the entire content of the table. Likewise each
index holds every page of its B-tree — root, intermediate and leaf — in its own owned map, whose pointer sits
in the index's data block (`0x22`); ACE frees them all.

> Measured. A table of 3,000-character memo values can occupy many pages while its data-page map names
> **one**: the row records, each holding a 12-byte descriptor, fit on a single page while the text lives on
> LVAL pages. Dropping it through ACE returns all of them to the global free map at close, and refilling the
> file reuses the space; freeing only the data pages and the TDEF returns almost nothing, and a refill grows the
> file instead — the engine reuses exactly what the global map offers it, and nothing else.

**A map's records are retired from their holder, and the holder goes back once nothing else lives on it.**
The records are rows on owner-zero data pages, and one holder can carry records for several columns or
tables. Dropping a table retires each of its map records in turn: it clears the freed pages' bits where it
clears them, tombstones the record's row, and — once no live row is left — frees the holder page itself.
Dropping a memo/OLE column retires that column's two records the same way — step 1 below, for that column
alone ([long-values](long-values.md#dropping-a-long-value-column)).

Each tombstone slides the records below it up the page, and the bytes they vacate are not cleared, so a moved
record leaves a copy of itself behind. The order is therefore visible on disk, and ACE's is fixed:

1. each long-value column's owned map, then its free map — bits cleared, except for a page still in the
   column's free map (its current append page), whose bit stays set in both records;
2. each index's owned map, in index order — bits cleared;
3. the table's own data owned map — bits cleared — then its free map — bits left set.

A map in reference form has its record retired in the same order; each of its bitmap pages has its bitmap
zeroed and is freed, its `05 01 00 00` header left in place.

> Measured against an ACE drop, page by page. ACE clears the bitmap bytes of the record as it frees each
> page (the zeroing is visible inside the space the row then gives up), tombstones each 69-byte map record
> with a `0xD000` slot, raises the page's free-space field by the bytes they occupied, and returns the page.
> Doing all three, in the order above, makes the holder **byte-identical** between the two engines —
> including for an indexed table whose second index's record sits below the long-value maps and slides
> twice; returning the page alone leaves it differing.
>
> The exclusivity test matters — releasing a holder that still carries another map's row would hand away a
> live page, which is corruption rather than a leak. It is also the case that *only* clearing the bits is
> not enough on a shared holder: the row has to go, or the dropped table's map records outlive it.

**The released definition page is marked.** Access sets the dropped table's TDEF page type to **`0x08`**
and changes nothing else on it; the other pages a drop frees (data, long-value, map holders, bitmap pages, and
a wide definition's continuation pages) keep their original type bytes. The marker, what survives on the page
and how close a LibRed drop lands to an ACE one are in [page-08](page-08-released-tdef.md).

#### A long-value page is released on its own terms

Deleting the last value that shared a packed long-value page releases the page and stamps it **`0x09`**,
clearing it from the column's owned and free maps here. That mechanism, and the page it leaves behind, are in
[page-09](page-09-released-long-value.md).
