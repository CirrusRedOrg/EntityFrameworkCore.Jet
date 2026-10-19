# Page type `0x0109` — a released data page

A data page whose last live row has gone. Two events produce one:

- **A table's data page emptied by DELETE** — every row on it deleted, with live rows possibly remaining on
  other pages of the same table.
- **A packed long-value page whose last tenant was deleted** — values in the **single-page form**
  (≤ 3,816 bytes — see [long-values](long-values.md)) are packed several to an LVAL page, and the page is
  released when no live record is left on it.

Both are stamped the same way, so the type alone does not say which produced a given page. The page's
**owner** at `0x04` does: a released LVAL page still carries the `LVAL` signature, a released ordinary data
page still carries its table's TDEF page number.

They are **released, not orphaned**: the bit is set in the global map, so ACE reuses them and Compact
reclaims them. Structurally the page is an emptied data page — every row slot a **0-length deleted +
overflow tombstone**, the rest free — still carrying whatever it carried at `0x04`, because that is what it
was.

| Offset | Size | Value on a released page |
| --- | --- | --- |
| `0x00` | 2 | Page type `0x0109` (bytes `09 01`) — only the low byte changes from a data page's `0x0101` |
| `0x02` | 2 | Free-space count — `PageSize − 14 − 2N` for `N` slots, i.e. everything below the directory |
| `0x04` | 4 | Unchanged: `LVAL` (`0x4C41564C`) for a long-value page, the owning TDEF for a data page |
| `0x08` | 4 | Zero — the chain stamp is only set on a chain's first page ([long-values](long-values.md)) |
| `0x0C` | 2 | Row count `N` — how many rows the page had; they all remain, as tombstones |
| `0x0E` | 2×N | Slot directory, every entry `0xD000`: deleted + overflow, offset `0x1000` (0-length) |

## What produces one

### An emptied table data page

Deleting the **last live row** on the page. Each delete tombstones that row and slides the rows below it up;
the type changes only once nothing live is left. The page is then taken out of **both** of the table's
maps — owned and free — and released. Exactly one page is released per page emptied. The free-pages map is
given the page's bit first, as for any page a delete frees space on, and loses it with the release: so a free-pages
map that cannot take the bit inline — not empty, the page outside the window it can slide to — turns reference
form, though the bit then goes (measured: deleting every seventh row of a one-row-a-page table spanning two
ranges leaves its free-pages map in reference form with a bitmap for each; the order is inferred from that).

**A table always keeps its first data page.** It is never stamped and never given back, however empty it
gets, and stays in both of the table's maps with all its slots tombstoned like any other emptied page. This
is not "a table keeps one page": it is kept even while later pages still hold live rows. It is **the lowest
page in the table's owned map**, not the page the table was created with: a table that has grown into pages
below the one it started on keeps the lowest of them and releases its original first page when every row is
deleted (verified; LibRed agrees byte for byte).

All of this is the **row-by-row** delete. A `DELETE` with no `WHERE` takes the other route below, and keeps
nothing.

### A DELETE with no WHERE

ACE empties the table in one go, without visiting a row, and **no page it releases is stamped or rewritten**:

- **Every data page** in the table's owned map — the first one included — is cleared from the owned **and**
  free maps and released to the global map. Its bytes stay exactly as they were: type `0x0101`, row count,
  slots and rows all live-looking, nothing tombstoned. No reader reaches it, because nothing owns it.
- **Every long-value page** in each long-value column's owned map — chained and packed alike — is cleared from
  that column's owned and free maps and released, its bytes untouched (a packed page keeps `0x0101`; it is not
  stamped `0x0109` as a row-by-row delete of its last value stamps it).
- **Every index** keeps only its root, which is rewritten as an **empty leaf**: type `0x0104` (a node root
  becomes a leaf), free space everything past the header, owner unchanged, previous, next, child-tail,
  compressed-byte count and level `0`, entry mask cleared. The entry bytes past the header stay as they were
  ([page-03-04 §10.4c](page-03-04-index-btree.md)). Every other page of the tree is cleared from the index's
  own pages map and released, untouched. The root keeps its bit.
- The TDEF's row count and **both** counts of every real index read `0`. The AutoNumber counter is untouched.

**When ACE takes it** (verified):

- The test is the **missing `WHERE`**, not every row matching: `WHERE 1 = 1` and `WHERE D >= 0` delete row by
  row. `DELETE * FROM t` is the same statement as `DELETE FROM t`.
- A table an **enforced** relationship names as its **parent** is deleted row by row, cascading or not. A table
  that is only a **child** (holds a foreign key) takes this route, and so does a parent only through an
  **unenforced** relationship, its rows left live-looking as a plain table's are (verified) — which LibRed
  also empties in one go.
- A table with a **CHECK constraint** is deleted row by row, its pages exactly as a row-by-row delete leaves them,
  and its checks evaluated as each row goes — one that fails only once some of the rows are gone refuses the
  statement (verified).
- It holds for multi-level indexes, chained and packed long values, a definition on continuation pages, a table
  already emptied row by row (its kept first page goes too), and tables with no index or a composite key. A
  table with a **complex column** takes it too, and the engine **leaves the flat table alone**: the owner's pages
  are released untouched, while the flat table keeps every row and its row count, each value now owned by no
  row (verified). LibRed does the same, byte for byte.
- The reset root's compressed-byte count is `0`, whether or not the root was compressed beforehand: a root
  compressed at 9 reads 0 after `DELETE FROM` (verified).

### An emptied packed long-value page

Deleting the **last** value sharing the page. Each delete tombstones that value's row and re-lays the page,
the survivors packing from the page end in slot order; the type changes only once nothing live is left. The
page is taken out of the **column's** owned and free maps and released. Exactly one page is released per page
emptied.

**A chained value never produces one.** Those own their pages outright and are freed at `0x0101`.

Released pages exist at **every format version** — Jet 4, ACE 12, ACE 14, ACE 16 — so the mechanism predates
ACE. Their presence tracks a file's *history* rather than its format.

### What does not produce one

An **index** page that leaves its tree. An index leaf a delete empties is unlinked and released like any
other page, but keeps its `0x0104` type and is not stamped — see
[page-03-04 §10.4d](page-03-04-index-btree.md). A released `0x0109` page is therefore always a data page, never
an index one.

**Reclaiming a hidden relocation target**, either. A relocated row's target ([page-01](page-01-data-and-rows.md))
is already flagged deleted, and taking its bytes back when its owner is deleted leaves the page an ordinary
data page: type `0x0101`, its one slot a tombstone, free space back to `4080`, still in both of the table's
maps. Only the deletion of a **live** row releases the page it empties.

## Reading

**Nothing needs to handle it.** Reading is unaffected — no live row or descriptor points at a released page
— and allocation selects on the free map without consulting the type, so one can be handed out and
overwritten normally. LibRed names it `PageType.ReleasedDataPage` so a page walk can report it. ACE's table
scan, meeting one in a table's owned map, reads it as rows like a live data page (README).

## Writing

LibRed does the same on both routes, marking the existing page through `DataPage.MarkReleased`. Every page a row has been reclaimed from goes through
`RowInserter.WriteReclaimedPage`, which sets the type, clears the page from the owning maps and returns it to
the allocator once nothing live is left and the page is not the table's first; `RowInserter.ReleasePackedValue`
applies the same rule to an LVAL page against the column's maps. A long-value page that survives goes back
into the column's **free** map, having room again.

A `DELETE` with no `WHERE` goes through `RowInserter.Truncate` instead, which writes none of the pages it
releases; `StatementExecutor` decides which route a statement takes.

A page is "emptied" when every slot is a deleted **and** zero-length tombstone, not merely a deleted one: a
hidden relocation target ([page-01](page-01-data-and-rows.md)) is a live row carrying the deleted flag, so a
page holding one is not empty.

## Related

- [long-values](long-values.md) — the three storage forms, and which one packs.
- [page-05 §9](page-05-usage-maps.md) — the owned/free maps this page is cleared from.
- [page-03-04 §10.4d](page-03-04-index-btree.md) — the index leaf a delete empties, released the same way.
- [page-08](page-08-released-tdef.md) — the other released-page marker, from a different mechanism.
