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

> Verified against an ACE-built 134 MB table (34,000 full-page rows): owned map record
> `01 017D0000 E17F0000 00…` — type `0x01`, slot 0 → page 32,001, slot 1 → page 32,737, remaining
> 15 slots zero, record length 69. Bitmap page 32,737's first bitmap byte is `0xFC`: pages 32,736 and
> 32,737 clear (32,737 *is* the bitmap page), 32,738–32,743 set.

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
>   the bitmap **record in place** — still type `0x00`, same `startPage` — extending it in **32-bit (4-byte)
>   steps** and repacking the usage-map page's records (the `owned`/`free` maps and any index/column maps)
>   from the end backward. So the record length is exactly
>   `5 + roundUp(ceil((maxPage + 1 − startPage) / 8), 4)`.
>   Verified against ACE owned-map lengths on a 255-column table whose data pages start at 353:
>   8,000 rows → **1053**, 12,000 → **1553**, 16,000 → **2053**, 30,000 → **3801**, 31,000 → **3925** — the
>   formula reproduces every one. (An earlier reading of this as *256-bit / 32-byte* chunks fitted the one
>   small data point it was drawn from — a table spanning to page 753 carries a **96**-byte bitmap, record
>   length **101** — because 96 is a multiple of both. The large-table lengths discriminate: 32-byte rounding
>   would give 1056 / 1568 / 3808.) Access opens a LibRed-grown table, counts every row, and reads one living
>   past page 512.
> - **Inline → reference conversion.** Access keeps growing the inline record until it no longer fits its
>   usage-map page, then rewrites the map as type `0x01`. Verified by owned-map record length against
>   page count: 8,000 pages → 1053, 12,000 → 1553, 16,000 → 2053, 30,000 → **3801, still type `0x00`**;
>   at 34,000 pages the map is type `0x01`. So the switch is driven purely by *record fits the page*, not
>   by a fixed page-count threshold. LibRed applies the same rule: grow inline while the repacked record
>   fits, otherwise convert — re-marking every previously-owned page into freshly allocated bitmap pages
>   (grouped by slot, one write per bitmap page) and shrinking the record to the fixed 69 bytes.
>   ACE reads a LibRed-written reference map: a 255-column, 400,000-row, 126.8 MB table counts back
>   exactly through `Microsoft.ACE.OLEDB.16.0`.
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
>   | Primary key | owned + free + the index's own map | `4096 − 14 − 6 − 69 − 69` = **3938** | page **31,456** |
>
>   Both verified end-to-end. LibRed's no-PK table converts at exactly page 32,032 with a last inline record
>   of 4009 bytes; ACE's no-PK table is still inline at page 31,354 (record 3925) and reference by 32,356,
>   bracketing the same value. With a primary key ACE likewise carries a third record and has already
>   converted by page 31,409, against LibRed's 31,456.
>
>   > Beware comparing thresholds across table shapes: an earlier note here claimed LibRed converted "at
>   > ~31,000 pages vs Access at 34,000". Those were a **primary-keyed** LibRed table and a **key-less** ACE
>   > one. The rule is identical; only the budget differed. A keyed table's index usage map also *grows* with
>   > the index's pages (its own B-tree), further shrinking the owned map's budget — LibRed now matches this
>   > (`IndexWriter` marks each index page it allocates), where it previously left the index map at 69 bytes.

> **Owned-row recycle on an index rebuild (verified vs ACE, §3.8).** When ACE rebuilds an index (e.g. an
> `ALTER COLUMN` on an indexed column) it gives the index a **new** owned-pages usage-map row rather than
> editing the old one in place, in two steps whose leftover is observable on disk: **(1)** append a fresh row
> at the end of the usage-map data page and set the new root's bit; **(2)** **move** that map into the *old*
> row's freed slot (its row-directory entry now points there) and turn the **old** row into a **0-length
> deleted + overflow tombstone** — leaving the bytes at the *appended* slot **stale in free space** (never
> reclaimed). The index-data block's usage-map row field (`0x22`, §3.5) is re-pointed to the recycled row
> number. LibRed reproduces this exactly (`RecycleOwnedMapRow`), including the stale appended bytes, so the
> whole file matches ACE byte-for-byte.


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
> grows the file. **Both map forms are handled.** For an inline (`0x00`) map it scans the record's
> bitmap directly; for a **reference (`0x01`)** map — as a very large pre-existing ACE file carries —
> it scans each slot's dedicated bitmap page (type `0x05`), where a **set bit is a free page** (the
> global map's sense), clears the bit on that bitmap page, and returns `slot × (pageSize−4)×8 + bit`.
> `Free` is the inverse (sets the bit on the range's bitmap page). A page in a range with no bitmap
> page (e.g. one grown past the map's coverage) is simply left unrecorded — it won't be reused, the
> same as the inline-window edge. (`GlobalReferenceFreeMapTests`; the bit↔page math is the one the
> per-table reference map is byte-verified against, §9.)
>
> **Create-table side effects.** An ACE `CREATE TABLE` *also* (1) adds two rows to **`MSysACEs`**
> (the new object's permission entries) and updates its `ObjectId` index, and (2) bumps a counter in
> page 0's obfuscated region at `~0xE02` (not yet decoded). **(1) is now done** —
> `TableCreator.AddPermissionRows` writes both permission rows (§11), and ACE opens LibRed-created
> tables without repair (`CreateTableAccessTests`). **(2) appears not to be required:** LibRed does not
> touch the page-0 counter, yet ACE opens/queries the created tables — so it's either unused for
> table open or benign when stale. (Views likewise get their two `MSysACEs` rows now — §11.)

