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
maps — owned and free — and released. Exactly one page is released per page emptied.

**A table always keeps its first data page.** It is never stamped and never given back, however empty it
gets, and stays in both of the table's maps with all its slots tombstoned like any other emptied page. This
is not "a table keeps one page": it is kept even while later pages still hold live rows. It is identified as
the lowest page in the table's owned map.

> **Unsettled:** whether the kept page is the lowest page in the owned map or the page the table was created
> with. The two coincide wherever they have been compared, so either reading fits; a table whose first page
> came out of a hole in the free map would separate them.

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

A page is "emptied" when every slot is a deleted **and** zero-length tombstone, not merely a deleted one: a
hidden relocation target ([page-01](page-01-data-and-rows.md)) is a live row carrying the deleted flag, so a
page holding one is not empty.

## Related

- [long-values](long-values.md) — the three storage forms, and which one packs.
- [page-05 §9](page-05-usage-maps.md) — the owned/free maps this page is cleared from.
- [page-03-04 §10.4d](page-03-04-index-btree.md) — the index leaf a delete empties, released the same way.
- [page-08](page-08-released-tdef.md) — the other released-page marker, from a different mechanism.
