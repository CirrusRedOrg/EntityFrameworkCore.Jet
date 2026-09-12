# Page type `0x09` — an emptied packed long-value page

A long-value page whose last tenant was deleted. Values in the **single-page form** (≤ 3,816 bytes — see
[long-values](long-values.md)) are packed several to an LVAL page; each delete retires that value's row and
re-lays the page, and when no live record is left the page is stamped `0x09` and given back.

They are **released, not orphaned**: the bit is set in the global free map, so ACE reuses them and Compact
reclaims them. Structurally the page is an emptied data page — every row slot a **0-length deleted + overflow
tombstone**, the rest free — still carrying the `LVAL` signature at offset `0x04`, because that is what it was.

| Offset | Size | Value on a released page |
| --- | --- | --- |
| `0x00` | 1 | `0x09` |
| `0x01` | 1 | `0x01` (page flags, unchanged) |
| `0x02` | 2 | Free space — `PageSize − 14 − 2N` for `N` slots, i.e. everything below the directory |
| `0x04` | 4 | `LVAL` (`0x4C41564C`), as when it was live |
| `0x08` | 4 | Zero — the chain stamp is only set on a chain's first page ([long-values](long-values.md)) |
| `0x0C` | 2 | Row count `N` — how many values had shared the page; they all remain, as tombstones |
| `0x0E` | 2×N | Slot directory, every entry `0xD000`: deleted + overflow, offset `0x1000` (0-length) |

## What produces one

Deleting the **last** value sharing the page. Each delete tombstones that value's row and re-lays the page,
the survivors packing from the page end in slot order; the page type changes only when nothing live is left.

> Measured against ACE — 12 rows of 400-character memos spread over three pages, deleting 4 and then all 12:
>
> ```
> start   0x01 n=5 free=72    [3296,2496,1696,896,96]
> 4 gone  0x01 n=5 free=3272  [4096DO,4096DO,4096DO,4096DO,3296]   the survivor slid to the top
> all     0x09 n=5 free=4072  [4096DO x5]
> ```
>
> It reproduces through OLE DB SQL, DAO SQL and a DAO recordset alike, so no Access UI is involved, and the
> count of released pages tracks the number of pages emptied exactly (4 rows of 12 → one page; all 12 → three;
> 40 rows → eight).

**A chained value never produces one.** Those own their pages outright and are freed at `0x01`. That is why
the type went unexplained for so long: every sweep that used a memo large enough to chain found nothing, and
the one size band that would have shown it was recorded as producing none.

> Measured over a 39-file corpus: 3,401 such pages in 27 files, of which 3,206 carry the `LVAL` signature and
> 195 do not. They appear at **every format version** — Jet 4, ACE 12, ACE 14, ACE 16 — and the Jet 4 members
> are files created in 2001–2004, so the mechanism long predates ACE. Their presence tracks a file's *history*
> rather than its format: heavily-edited applications hold hundreds (822, 806, 328), while freshly created or
> untouched files hold none. The 195 without the signature are not yet accounted for.

## Reading

**Nothing needs to handle it.** Reading is unaffected — no live descriptor points at a released page — and
allocation selects on the free map without consulting the type byte, so one can be handed out and overwritten
normally. LibRed names it `PageType.ReleasedLongValuePage` so a page walk can report it.

## Writing

LibRed does the same, in `RowInserter.ReleasePackedValue`: tombstone the value's row, re-lay the page,
and when the last one goes set the type, clear the page from the column's owned and free maps, and return it
to the allocator. A page that survives goes back into the column's **free** map, having room again.

Pinned by `PackedLongValueReleaseAccessTests`, which compares every byte of every page against ACE's own
delete for the partial, full, multi-page and chained cases.

## Related

- [long-values](long-values.md) — the three storage forms, and which one packs.
- [page-05 §9](page-05-usage-maps.md) — the per-column owned/free maps this page is cleared from.
- [page-08](page-08-released-tdef.md) — the other released-page marker, from a different mechanism.
