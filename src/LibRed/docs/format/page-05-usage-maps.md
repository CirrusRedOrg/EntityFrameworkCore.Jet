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
to dedicated **bitmap pages** (page type `0x0105`). Pointer `k` (zero ⇒ none) points at a bitmap page
covering the page range starting at `k × (pageSize − 4) × 8`; on a bitmap page the bitmap data
begins at **offset 4**.

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Type `0x01` |
| `0x01` | 4 × 17 | Bitmap-page pointers, little-endian; `0` ⇒ that page range owns nothing |

The record is exactly **69 bytes** (`1 + 17 × 4`). Seventeen slots is not arbitrary: each bitmap page
covers `(4096 − 4) × 8 = 32,736` pages ≈ 134 MB, so 17 slots span ≈ 2.28 GB — just past Jet's 2 GB
file ceiling. A bitmap page's header is the page type `0x0105` (bytes `05 01`) then `[2..3]=0`, bitmap from
offset 4.
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

### 9.0a Which row each map takes

A table's maps share one **primary usage-map page**, one map per row. Rows `0` and `1` are the table's own
owned and free maps. The rest belong to its indexes (one row each, the index's own pages map, named from its
data block at `+0x22`) and to its long-value columns (two rows each, owned then free, named from the
§3.3.2 list) — and **which row each takes follows the order the `CREATE TABLE` statement declares them**, not
the table's shape:

- a long-value column claims its two rows where its **column** is written;
- an index claims its row where its **constraint** is written — at its column for an inline
  `PRIMARY KEY` / `UNIQUE` / `REFERENCES`, or at the constraint itself for a table-level `CONSTRAINT` clause;
- where the two fall at the same point — an inline constraint, or a table-level one written immediately after
  a column — the **constraint** takes the earlier row.

So the same table written two ways lays out differently, and an index's row cannot be derived from the table:

```
CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, M MEMO)     pk row 2,  M rows 3/4
CREATE TABLE T (Id LONG, M MEMO, CONSTRAINT pk PRIMARY KEY (Id))    M rows 2/3,  pk row 4
CREATE TABLE T (M MEMO, A LONG CONSTRAINT u UNIQUE, N MEMO)    M rows 2/3, u row 4, N rows 5/6
```

Index **data blocks** are numbered in the same declaration order (verified): primary key, unique and foreign
key alike take the next ordinal where their constraint is written, not in a fixed order by kind. So among the
indexes, a data-block ordinal and a map row run in the same order.

Once the primary page is full the remaining long-value columns spill to a page of their own (owned = row 0,
free = row 1); an index's map spills the same way rather than being squeezed in.

> **LibRed write behaviour (multi-page growth on insert).** When an insert finds no owned data page
> with room, LibRed allocates a new page (via the global map, §9.1), initialises it as an empty data
> page owned by the table, sets its **owned** bit, and moves the **free** marker to it. Verified:
> Access reads a table LibRed spilled across many data pages and can still insert into it.
> - **Free-pages map = the current append tail only.** Access clears a page from the free-pages map
>   when an insert finds it too full and moves on, so after a sequential fill only the *last* page
>   stays marked free — verified: of equally-full ACE pages only the last is in the free map, because
>   each earlier one had a next-row attempt that didn't fit. LibRed matches this: an insert **clears the free bit
>   of every page in the free map it finds too full** on its way to one with room, and a page it allocates is
>   set, leaving exactly the tail marked free. That holds when the page with room is not the last one tried
>   too: a catalog insert that passes over a full `MSysObjects` page to a later one clears the full page's bit
>   (verified).
> - **A delete puts the bit back.** Space freed in a page that had none makes that page a candidate again, so
>   its free bit is set when the row's space is reclaimed — otherwise the space is there and nothing looks at
>   it. Verified vs ACE: deleting a row from the **first** of many data pages (full, so its bit had been
>   cleared as the fill moved on) has ACE write the map holder as well as the data page, and both engines then
>   write the same pages with the same bytes.
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
>   them; if a bit already set would fall outside the new window it **widens** the record instead — the start
>   drops to the lowest page it must cover (rounded down to a byte, as the released map's move does) and the
>   record is sized to reach the highest — so nothing is silently forgotten. The window can already sit
>   **above** the page being marked, since it follows the append tail while a table's older pages stay where
>   they are; a map in that position must still be able to record the lower page. What ACE writes there —
>   a widened record, or a re-based window — is **not measured**.
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
>   > owned map's budget — LibRed matches this (`IndexTree` marks each index page it allocates).

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

| Page 0 | Map | Where ACE creates it |
| --- | --- | --- |
| `0x18` | **free pages** — a set bit is a page available for allocation | page 1, row 0 |
| `0x1C` | **released pages** — a set bit is a page freed but not yet reusable | page 1, row 1 |

Both are ordinary usage-map records (inline or reference form, §9) on a data page — whose owner field
(`0x04`) reads `0x00000001` where ACE creates it, though nothing checks it (below) — each starting as a
69-byte inline map with start page `0`. In the free map a
**set bit means the page is free / available**, the *opposite* of a per-table owned map (verified).

**ACE follows the pointers; the location is not fixed.** Allocation uses whichever record `0x18` names, row
included: pointed at the released map's row on the same page, ACE allocates from that map and leaves the free
map's row untouched. With both maps
copied to another data page and the pointers aimed there, ACE allocates, releases and reopens entirely on
that page and never reads or writes the original holder; the holder page's owner field (`0x04`) is not checked. The two
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
disk does not change until the file closes (verified). A handle's freed pages are its own business until then:
on disk they are in neither map, and only the close moves them into the free map. At close ACE moves them into the free map. This holds for the
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
type becoming `0x0105`. The merge then clears every bitmap page, each keeping its `05 01 00 00` header.

So at rest the released-pages map has no bits set, though it may have grown, moved its start page or converted
to reference form. A non-empty one is *inferred* to be a release interrupted before close.

**Page allocation works through the free-pages map.** Access does **not** simply grow the file: it finds a
set bit (a free page), **clears it** (marking the page used), and reuses that page — only growing
the file when its selection policy cannot find an eligible page or group. A partial group can therefore
remain free while the file grows. Verified: an ACE `CREATE TABLE` reuses free pages (for the TDEF,
usage map, etc.), and the only change to the free map's holder page is one cleared bit per page taken.

**Free-page selection is stateful, not simply lowest-first.** The explicit
`PageGroupProbeTest.Statement_size_reservations` probe measures the same ordered 1,000-row Bulk workload
as the whole-file parity probe's known bulk-insert difference, on a smaller independent schema. It varies
statement size and whether the connection closes between statements, and reads a copy after the final
statement's flush while the session remains open, then reads the file after close. The reservation
classification here is limited to the fixture's inline 512-page map: a clear bit with an unwritten page,
including pages beyond EOF, identifies a reservation; it is not a general classifier for recycled pages.

Measured on 2026-10-15 with the installed 64-bit ACE OLE DB provider:

| Rows inserted, one session | 1 or 50 rows per statement | 250 or 1,000 rows per statement |
| --- | --- | --- |
| 240 | 89 file pages; reserved 89–119; free hole 81–87 | same |
| 480 | 120 file pages; no reservation; free hole 82–87 | same |
| 610 | 132 file pages; reserved 132–151; no free hole | 137 file pages; reserved 132–135 and 144–159; free hole 127 |
| 1,000 | 187 file pages; free hole 165–167 | 194 file pages; free holes 141–143 and 177–183 |

The one-session measurements reproduced in a second run. They describe resulting placement, not
an index-split rule: an operation requests new pages, and the allocator chooses their placement and
reservation. Closing returns unused reservations to the free map. The isolated measurements below
separate those allocator decisions from the bulk workload.

Closing and reopening between batches changes the result further:

| Rows per statement, reopen between statements | File pages at 240 / 480 / 610 / 1,000 rows | Free holes at 1,000 rows |
| --- | --- | --- |
| 50 | 89 / 121 / 134 / 184 | none |
| 250 | 89 / 117 / 137 / 192 | 143 and 169–175 |

For example, after reopening between 50-row batches, pages 102–103 are reserved at 610 rows and
returned at close; at 1,000 rows they hold data. This demonstrates reuse of an internal group separately
from growth at EOF. Session close is therefore another allocation input, independent of statement size.

**Isolated allocator measurements (2026-10-15).**
`PageGroupProbeTest.Controlled_free_page_selection` starts with an ACE-created heap, already holding one
row, then materializes a tail through page 255 and sets only selected unused tail pages free. Pages below
the original EOF, including all live metadata, are untouched; non-offered tail pages are synthetic
occupied placeholders. Pages 256–511 remain the normal free growth region. Nine fixed `BINARY(255)`
columns make each additional row need exactly one data page (the probe checks the owned-page count).
There are no indexes in the heap cases. Separate cases create a new small index root, without splitting.
For each prefix, the probe compares free bits before, after a 3-second flush while open, and after close:
`taken = beforeFree − openFree`, `used = beforeFree − closedFree`,
`returned = closedFree − openFree`. This does not infer reservations from page types.

| Free pages offered below EOF | First allocation uses | Bits cleared while open | Returned at close |
| --- | --- | --- | --- |
| 64–255 | 64 | 64–95 | 65–95 |
| 65–95 | 65 | 65–95 | 66–95 |
| 65, 80–111 | 65 | 65, 80–95 | 80–95 |
| 65, 67, 70, 73, 75, 78, 96–111 | 65 | 65, 67, 70, 73, 75, 78 | all except 65 |
| 67–69, 80–87 | 67 | 67–69, 80–87 | all except 67 |
| none (growth begins at 256) | 256 | 256–287 | 257–287 |
| 72–167 | 72 | 72–103 | 73–103 |
| 80–175 | 80 | 80–111 | 81–111 |
| 93–188 | 93 | 93–119 | 94–119 |
| 97–192 | 97 | 97–127 | 98–127 |

These support the shared-reservation rule: find the lowest free page `p`, reserve **every currently free
bit in the 32-page window `[p & ~7, (p & ~7) + 31]`**, and serve requests from that reservation in ascending
page order. The reservation is a bitmap window, not a contiguous run or necessarily 32 available pages:
occupied gaps are skipped, and a longer run is cut at the window boundary. Creating a fresh index root
instead of a heap page gave the same result for the contiguous, singleton-before-run, fragmented, and
growth layouts, so this rule is not specific to a data page or an index split.
The shifted starts distinguish **8-page alignment** from 32-page alignment; the window still spans
32 pages (four groups), and is not expanded to collect 32 free bits. It is not an unconditional
first-allocation-per-session rule: the heap's existing owned-page count can select another path below.

On exhaustion, heap allocations continue one page at a time: with 64–255 free, prefixes of 32, 33, 64, 65,
and 97 requests used exactly 64–95, 64–96, 64–127, 64–128, and 64–160 respectively, with no unused
reservation. Starting at EOF 256 gave the same pattern through page 352. With six scattered initial free
pages in 64–95, the seventh request reserved all sixteen free bits at 96–111; this case therefore differs
from exhausting a full 32-page window and must not be collapsed into an unconditional single-page rule.

The reservation is shared: after one heap allocation at 64, creating an index root or growing a second
heap uses 65 out of the same reservation. After 32 heap allocations consume 64–95, either of those
consumers uses 96 and reserves 96–127. If the first heap has already continued singly onto 96, the other
consumer uses 97 and reserves the free portion 97–127. Conversely, creating an index root at 64 first,
then inserting 32 heap rows, uses 65–96 without an extra reservation. Thus the distinction is not
"data pages versus index pages": continuing a heap and asking for another consumer's page can enter
different allocation paths on the same connection.

An independent check, `Growth_alignment_follows_the_session`, starts with EOF 121 and grows a heap to
pages 121–123: the remaining reservation is 124–151, exactly the free portion of the four-group window
120–151. Two later index-page requests take 124–125 from that same reservation.

**The heap switches at eight owned data pages (measured).** With a fresh session and free pages 128–511,
preloads of 1, 2, 4, or 7 data pages reserve 128–159 on the next page request. Preloads of 8, 16, 24, 31,
32, 33, or 64 data pages instead take only page 128, leaving 129–159 free even while the session is open.
Changing to five fixed binary columns puts three rows per page: 21 rows on seven pages still reserve
128–159, whereas 24 rows on eight pages take only 128. Thus the threshold tracks owned pages, not rows.

The group path first reuses free pages in the heap's current 8-page group: with data pages 44–51 and
free pages 52–55, 129, 136–167, the next two allocations take 52 and 53 singly. Otherwise it prefers a
**fully free aligned 8-page group**, skipping partially occupied groups:

| State before request | Seven-page heap | Eight-page heap |
| --- | --- | --- |
| Free 129 and 136–167 | uses 129; reserves 129 and 136–159 | uses 136; only bit 136 cleared |
| Free 129, 137, 145 and growth region 256–511 | uses 129; reserves 129, 137, 145 | uses 256; only bit 256 cleared |
| No free pages inside file; EOF 257 | uses 257; reserves 257–287 | uses 264; only bit 264 cleared |

So a large heap can grow the file despite lower free pages existing: those pages lie in partial groups.
For an existing session whose heap consumed 64–95, free page 97 competes with full group 104–111:
the heap uses 104, then 105, leaving 97 free. Creating an index root in that same state uses 97 and
reserves the free bits through 127. The seven/eight-page distinction and consumer difference reproduce
the page-choice effect without relying on an index split.

An important distinction: these heap snapshots are taken **after statement commit**. Unused members
of the owner's eight-page group have already returned to the global free map at that point. The
four-group shared reservation still clears its unused bits, and close returns them. Calling both an
"8-page allocation batch" would lose this observable distinction.

**The same boundary applies to an index's own B-tree page count, including its root.** The `tree7`,
`tree8`, and `tree9` cases build indexes through ACE until their owned maps name exactly 7, 8, and 9
pages, respectively (96, 115, and 134 text-key rows in this fixture). Each is then given the same free
space, 128–511, with incidental old free holes removed. At a prefix of 18 more rows, only two data
pages have been allocated, at 128–129. The 19th row requests one new index page, with no new data page:

| Index pages before request | New index page | Bits cleared while open after this request | Returned at close |
| --- | --- | --- | --- |
| 7 | 130 | 128–159 | 131–159 |
| 8 | 136 | 128–129 and 136 | none |
| 9 | 136 | 128–129 and 136 | none |

This isolates the choice made **for** a new index page. Splitting is only the stimulus that requests it;
the allocation policy is keyed to how many pages that owner already has. A table's data map and each
index's own map therefore cross the boundary independently. Seven-page indexes still use the shared
reservation even while their table already has 10 data pages and uses group selection.

**An existing shared reservation takes precedence.** The `tree7-prime` and `tree8-prime` cases first
make a one-page heap allocation at 128, reserving 128–159. The other table's data pages then take
129–130, and either the seven-page or eight-page index takes 131. Both leave 132–159 reserved until
close. Thus the eight-page boundary selects the group path only when the shared reservation cannot
satisfy the request; it does not force the caller to abandon an existing reservation for a full group.

**Group ordering before and after the owner (measured).** The `order-*` fixtures use an ACE-written
heap of eight pages at 172–179, with current group 176–183. A dropped padding table supplies real free
pages below it; the probe then limits the free map to the selected groups and the growth region.
With groups 64–71, 160–167, 184–191 and 200–207 free, the allocations choose 64–71, then 160–167,
then 184–191. With only earlier groups 64–71 and 160–167, both are used before growing at 256.
With nearer earlier group 160–167 and farther later group 208–215, 160 is chosen first. This is
**lowest eligible group**, not nearest or forward-from-owner order, within the inline map.
Free pages 180–183 in the owner's current group take priority over 64–71; once those four are used,
the scan returns to 64. This also distinguishes current-group reuse from the general group search.

**Long values use shared reservations, not the eight-page owner switch (measured).** Separate cases
preload a column with exactly 7, 8, or 64 LVAL pages. Packed binary values of 3,000 bytes, chained binary
values of 9,000 bytes, and 1,500-character memo values all continue through the shared reservation.
With free 128–511 and 64 LVAL pages already owned, packed values use 128–159 for the first 32 new
pages; the 33rd reserves 160–191, and the 65th reserves 192–223. A chain needing three pages crosses
the same boundary: eleven 9,000-byte values use 128–160 and leave 161–191 reserved until close.
The row records stay on one data page, so no new table data page accounts for these reservations.
Prebuilding the column's owned and free maps in reference form does not change this long-value policy.

**Reference-form owner maps and global maps are separate decisions.** `owner-ref` retains an inline
global free map and a heap owning only page 44, but gives that heap a prebuilt reference owned map.
With page 65 and run 80–111 free, ACE chooses 80 singly, leaving 65 free. Thus the group path is also
selected by a reference-form owned map; "eight pages" alone is not the complete condition.

For the global reference map, `ref-*` fixtures cover two 32,736-page bitmap ranges. Synthetic bitmap
pages are placed on verified unused pages, and the relevant owner maps' bitmap pages are prebuilt,
so their promotion does not add unrelated allocations. The `GlobalFree` observer now uses Core's
existing inline/reference decoder rather than silently treating a reference map as empty.

Shared reservations **stop at the current bitmap's coverage boundary**: with 32729–32767 free, a new
index root uses 32729 and reserves only 32729–32735, not through 32759. Packed and chained long values
do the same, then refill from 32736–32767. A chained value can cross that boundary: nine 9,000-byte
values use 32729–32755 and return 32756–32767 at close. With only 32729 free in the first range,
the first packed value uses it without a remainder; the second reserves 32736–32767.

The selected bitmap is session state: with singleton 32729 in range 0 and a full group at 32736 in
range 1, a group allocation takes 32736. A subsequent new index root on that connection uses 32737
and reserves through 32767, leaving 32729 free. Closing and reopening between the heap allocation
and index creation resets the selection: the index then uses 32729. This rules out an unconditional
scan from bitmap slot zero for every request.

An empty earlier slot is skipped: with slot 0 absent (pointer zero) and 32736–32767 free in slot 1,
an index root uses 32736, reserves through 32767, and returns 32737–32767 at close.

The shared-reservation scan **does not wrap backward** when its selected bitmap is exhausted.
`ref-exhaust` materializes both bitmap ranges through page 65471, offering only singleton 32729 and
group 32736–32743. Eight group allocations consume 32736–32743 and select bitmap 1. A new index root
then grows at 65473 (65472 is the new global bitmap page), reserving the free portion through 65503
and returning 65474–65503 at close. Singleton 32729 remains free. Thus the forward scan can grow
despite a reusable page existing in an earlier bitmap; reopening resets the scan and makes it reusable.

The **full-group search does reconsider earlier bitmaps**, even without reopening. `lv8-release`
first fills group 32736–32743, then updates the long-value column to NULL. A 32,000-byte chain makes
group 56–63 free immediately. After the UPDATE's incidental shared reservation is exhausted (heap
pages 32745–32767), the next group allocation takes 56. Reopening before continuing also finds 56.
The snapshots explicitly confirm 56–63 are free while the original session remains open. The older
packed LVAL pages 47 and 49–55 in this same UPDATE return at close instead; empty packed-page
release and chain-page reuse must not be conflated.

The naturally ACE-grown `ref-native-order` fixture confirms cross-bitmap group ordering on an owner
near the boundary. ACE itself grows a padding table until the global free map is reference form;
after that table is dropped, Bulk owns 32757–32764, in current group 32760–32767. Offered groups
64–71 and 160–167 (bitmap 0) are selected before 32768–32775 and 32784–32791 (bitmap 1), then growth
at 32800. Bitmap-holder placements are controlled only after this ACE-authored transition and are
checked against genuinely free pages before being overwritten.

The measured model is therefore:

1. Serve a request from the session's existing shared reservation when available.
2. Without one, a data/B-tree owner with an inline map and fewer than eight pages, or a long-value
   allocation regardless of the measured owned-map form/count, uses a shared reservation. Reserve
   free bits in the four-group window starting at the selected page's 8-page boundary, clipped to the
   selected global bitmap's end. Search that bitmap, then later bitmap slots; do not wrap backward.
   A data map and each B-tree map are separate owners for this decision.
3. A data/B-tree owner already at eight pages, or with a reference owned map, can continue in its
   current group, then search from the beginning for the lowest eligible fully free aligned 8-page
   group across the global map, reserving its free members privately for that owner. Lower partial groups can be skipped, including
   the unfinished group at EOF; this explains alignment gaps and growth despite free holes.
4. Unused shared-reservation bits return at close. Unused owner-private group members return at
   statement commit, so post-statement snapshots show them free even while the connection stays open.

The ordered Bulk insert (statement 118 in the 336-statement parity corpus) distinguishes these
lifetimes. Data reserves 104–111 and has used only 104–108 when the index needs a page. ACE skips
109–111 and selects 112, starting shared reservation 112–143. Data consumes that shared reservation
first, then resumes its private 109–111. LibRed reproduces the complete resulting 215-page file
byte for byte with the existing parity comparison masks; no allocation-difference exemption is used.

**Continuous-session limitation (measured):** that result starts the statement on ACE's prior file
with a fresh session. Keeping the entire workload in one session matches through statement 117
(81 pages), but diverges at 118: ACE ends at 226 pages and LibRed at 218. The placement trace shows
LibRed using index leaf 149 where ACE leaves 149–151 free and later puts that leaf at 208; ACE also
leaves 209–215 free before continuing data at 216. LibRed instead continues data through 200–217.
Thus session-history allocation policy is not completely reproduced yet. By statement 140 LibRed
has 248 pages to ACE's 240, before the known isolated-statement differences starting at 151. The
gap falls to five pages by 168 and stays five through 336 (368 versus 373). These are closed-session
prefix snapshots, not live reservation counts. The explicit prefixes of
`WholeFileParityProbeTest.The_whole_run_leaves_libred_and_ace_with_the_same_file` reproduce the trace.

Further controls narrow the unresolved state (all measured, not an implemented replacement policy):

- Splitting the bulk insert into two statements at row 900, on the same open connection, produces an
  identical 218-page file. The single-statement version produces 226 ACE pages versus 218 LibRed pages.
- A separate `Indexed(K TEXT(200))` fixture starts with eight index pages and twelve data pages. Its
  single-statement ascending append uses index pages 136, 144, 137, 145, 138 in that order, with data
  taking 128–135 and then 152 onward. Individual autocommit inserts instead fill index 136–143 before
  selecting 160. An explicit transaction around the individual inserts gives data two groups
  (128 and 136) and index one (144). DAO transaction depths one, two and three give the same result;
  nesting depth alone is therefore not the selector. A persistent DAO recordset reproduces the
  single-statement append pattern, with or without moving to its `LastModified` bookmark.
- An Access-hosted VBA callback, with a small buffer to expose uncommitted global-map writes, shows
  unused bits returning **during the statement** when its index group changes. In the equivalent
  fixture offered free pages from 512, index group 520–527 is reserved first. At the next split,
  unused 521–527 become free and 528–535 become reserved. The following split returns unused
  529–535 and reserves the remaining free portion of 520–527 again. The callback, no-callback,
  flush and no-flush controls have the same final allocation pattern. Owned-map pages remain pinned
  and do not expose their in-progress changes in these snapshots; the evidence is the global map.

These observations rule out a single permanently reserved group per index for the entire statement.
They do **not** yet establish how ACE selects the current group/cursor on every request. A trial with
two alternating per-index hints matched the original 150-character-payload case but introduced a
difference in a previously matching 200-character variant, so it was removed. Payloads of 80 and 200
characters match with the existing allocator; a 120-character variant also exposes a pre-existing
allocation difference. `Controlled_free_page_selection`'s `tree8-bulk`, `tree8-tx`, `tree8-dao*` and
`tree8-rs*` cases, and `Index_groups_observed_inside_Access_statement`, reproduce these controls.

This is measured allocation policy, not an on-disk-format requirement. The inline map is one bitmap;
a global reference map additionally keeps a selected bitmap across requests and resets it at reopen.
Do not collapse the distinction into a blanket "always take the lowest free page" rule.

> LibRed allocates **through** this map (the `PageChannel`'s single `PageAllocator`, which also owns file growth), found as ACE finds it — through page 0's `0x18`
> pointer, row included — and never takes a page set in the released-pages map named at `0x1C`. A released
> page at the end of the file is materialized, so the file stays contiguous, but not handed out. It takes a
> free page using the shared-reservation and owner-group policy above, growing when no eligible page
> or group is found. Reservation masks, owner positions and the selected global bitmap participate in
> transaction and savepoint rollback. Free bits at the current file end are the pre-allocated growth region; LibRed materializes
> intervening pages contiguously before returning the selected page — free, released, or reserved, its own
> session's or, with the free bit clear, another handle's. A session's reservation and an owner's group take
> pages from the map before they are written (measured above: page 256's allocation clears 256–287 at EOF 256;
> the bulk insert's index skips the data group's unwritten 109–111). *Unverified:* that a second handle — a
> second ACE process, or a second LibRed connection — sees and treats another's past-EOF reservation this way;
> multi-user allocation has not been probed. Repeated allocations can therefore consume an ACE-authored run
> of future bits without creating a sparse file. For the same reason a covered page past the end of the file
> whose bit is clear is never handed out as unused: when nothing eligible is free, the map is grown past every
> page it covers and the page comes from the new free bits, as ACE skips 109–111 for 112. In reference form
> that is the first range with no bitmap page, whose bitmap is the range's own first page, as ACE places it
> (65472, measured where the file ended at the range's start); a promotion from inline form places its bitmap
> pages past every page the inline map covered and carries the inline bits into them. *Unverified:* where ACE
> puts a new bitmap when held pages lie between the end of the file and the range — LibRed writes them out and
> takes the range's first page.
> **Both map forms are handled.** For an inline (`0x00`) map it scans the record's
> bitmap directly; for a **reference (`0x01`)** map — as a very large pre-existing ACE file carries —
> it scans each slot's dedicated bitmap page (type `0x0105`), where a **set bit is a free page** (the
> global map's sense), clears the bit on that bitmap page, and returns `slot × (pageSize−4)×8 + bit`.
> `Free` is the inverse (sets the bit).
>
> **The map covers the whole file (verified).** The global free map grows with the file, by the same in-place
> rule as a table's owned map and always from start page 0, so its coverage is never behind the frontier — a
> file of 1,761 pages carries a 229-byte record, covering 1,792 — and a page freed above the original 512-page
> window is marked in it like any other. A page the map has no bit for is therefore a malformed map, not an
> ordinary state, and cannot be recorded as free: nothing else remembers it, so a writer that drops it there
> loses it for good.
>
> **Release at close.** The frees ACE holds go through `Release`, which keeps the page in a list on the handle
> — staged with the open transaction, kept on commit, dropped on rollback or on a rollback to a savepoint
> taken before it. Closing a writable `JetDatabase` returns those pages and any already set in the
> released-pages map to the free map, clears the released map and first sizes it as above, all in one
> transaction. A close that released nothing and wrote nothing writes
> nothing. Only an `UPDATE`'s replaced long value goes through `Free` at once — after the new value is written,
> so, as in ACE, the `UPDATE` that frees those pages does not reuse them (the whole file then matches ACE's).
>
> **Where LibRed differs from ACE.** Held pages live in the handle, not in the released-pages map — which is
> ACE's own behaviour (above), the difference being only that each LibRed handle releases its own at its own
> close, while other handles stay open.
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

**Dropping one index is the same act in miniature (verified).** `DROP INDEX`, and `DROP CONSTRAINT` for the
index a relationship owns, free **every page of that index's B-tree** — the pages past the root are recorded
only in the index's own owned map, so freeing the root alone leaves them owned by nothing — and retire that
map's record: bits cleared, row tombstoned, the holder's free space back, and the holder itself freed if
nothing live is left on it. The record's row **number** is not reused afterwards; the next `CREATE INDEX`
appends past it ([long-values](long-values.md)). `DROP CONSTRAINT` additionally deletes the relationship's
`MSysRelationships` rows outright — the row's space returns to its page and the table's row count falls —
rather than only flagging the slots.

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

**The released definition page is marked.** Access sets the dropped table's TDEF page type to **`0x0108`**
and changes nothing else on it; the other pages a drop frees (data, long-value, map holders, bitmap pages, and
a wide definition's continuation pages) keep their original types. The marker, what survives on the page
and how close a LibRed drop lands to an ACE one are in [page-08](page-08-released-tdef.md).

#### A page emptied of its contents is released on its own terms

Deleting the last live row on a table's data page, or the last value that shared a packed long-value page,
releases the page and stamps it **`0x09`**, clearing it from the owning maps here — the table's for a data
page, the column's for a long-value one. A table's **first** data page is the exception and is never
released. That mechanism, and the page it leaves behind, are in [page-09](page-09-released-data.md).
