# Long values (Memo / OLE) and LVAL pages

> Part of the [LibRed Jet / ACE file-format reference](README.md). Cross-references use the original **§-numbers**; the [section map](README.md#section-map) says which file each lives in.

## 8. Long values (Memo / OLE)

The in-row value for a Memo/OLE column is a **12-byte descriptor**, not the data:

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Little-endian word: byte length in bits 0–29, flags in bits 30–31 |
| `0x04` | 1 | Row |
| `0x05` | 3 | Page |
| `0x08` | 4 | reserved |

Flags (byte `0x03` masked with `0xC0`; its low six bits belong to the length):
- `0x80` **inline** — the payload follows the descriptor in the row.
- `0x40` **single LVAL page** — the row at (page, row) *is* the whole payload.
- `0x00` **multi-page** — the payload is chained across LVAL pages; each chunk's row begins
  with a 4-byte pointer (`[row:1][page:3]`) to the next chunk (zero on the last), followed by chunk
  data. Each chunk row is **`MAX_LONG_VALUE_ROW_SIZE` = 4076 bytes** (Jet4; Jet3 = 2032) — a 4-byte
  pointer + up to 4072 data bytes — except the last, which is shorter. Verified against ACE's own
  chained OLE (Northwind Employee photos: 4076, 4076, 2606-byte chunk rows).

ACE accepts an OLE/binary payload of `0x3FFFFFFF` bytes (1 GiB − 1) and rejects `0x40000000`
bytes. This verifies the binary limit; it does not establish the Memo character limit.

LVAL pages are data pages (type `0x01`) whose owner field (`0x04`) is the ASCII marker `LVAL`.

> **Reader and reclamation guardrails.** LibRed requires the complete 12-byte descriptor before reading
> its fields, accepts only the three flags above, and bounds inline data against the bytes actually present.
> Every external pointer must name an in-file type-`0x01` page with the `LVAL` owner marker and a live,
> ordinary row slot. Chained rows must contain their 4-byte next pointer, make payload progress, never repeat
> a `(page,row)`, terminate at zero exactly when the declared length is reached, and neither underfill nor
> overrun that length. Before reclaiming a replaced chain, LibRed validates the complete chain and requires
> every page to be present in that column's owned-pages map; only then does it begin clearing maps/free bits.
> Those subsequent writes are atomic whenever a transaction is open — the page-level undo log exists now
> (`docs/design/transactions.md`), and the engine opens one per statement — so a failed reclamation rolls
> back with the statement. A direct `LibRed.Core` caller that opens none gets the same non-atomic behaviour
> as any other multi-page write.

### 3.3.2 Column usage-map list (trailing the index names)

After the index names (in the TDEF body, §3.3) comes a list of per-**long-value-column** (memo/OLE)
usage-map pointers, terminated by a `col_num` of `0xFFFF`. Iterate reading 10-byte records *until*
`col_num == 0xFFFF`:

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 2 | `col_num` — the column's index; `0xFFFF` terminates the list |
| `0x02` | 4 | `used_pages` pointer (1-byte row + 3-byte page) to the column's owned-pages usage map |
| `0x06` | 4 | `free_pages` pointer (1-byte row + 3-byte page) to its free-pages usage map |

The **definition length** (`0x08`) points just *past* the terminating `0xFFFF`, so the whole
list (terminator included) counts toward the definition, not free space.

> **LVAL-only, despite mdbtools calling it "Variable Column Tracking".** Only **Memo (`0x0C`) and
> OLE (`0x0B`)** columns appear here — *not* plain **Text (`0x0A`)**, even though Text is
> variable-length — because only memo/OLE have their own long-value (LVAL) page chains that need
> usage maps; Text is stored inline in the row. Verified by correlating each entry with its column
> type: Categories → `{col2 Memo, col3 OLE}`, Employees → `{col14 OLE, col15 Memo}`, Suppliers →
> `{col11 Memo}`, and — the clincher — **Customers, with 11 Text columns and no memo/OLE, has an
> empty list**. So mdbtools' name is imprecise; the list is keyed to long-value columns.

## Writing long values

> **Writing.** LibRed inlines a memo/OLE value only up to **64 bytes** (Jackcess
> `MAX_INLINE_LONG_VALUE_SIZE`, same for Jet3/Jet4): the 12-byte descriptor with length + the `0x80`
> flag (bytes `0x04`–`0x0B` zero) then the payload (memo = UTF-16LE, OLE = raw bytes). A value **larger
> than 64 bytes** is written to its own **single LVAL page** (`0x40` descriptor, `LongValueWriter`) —
> `RowInserter` materialises it before encoding. This matters for Access, not just LibRed: Access
> tolerates an inline value its reader resolves, but **rejects an over-64-byte value inlined** (e.g. it
> opens the database yet fails to *run* a view whose subquery `Expression` was inlined; on an LVAL page
> it runs — verified against the derived-table view, §11). A value **larger than one LVAL row** (4076
> bytes) is written as a **chain** (`0x00` descriptor): the payload is split into 4072-byte data chunks,
> each on its own page with a 4-byte next-pointer, matching ACE byte-for-byte (verified: LibRed and
> Access both read back memo values from 65 bytes to 100 KB — single-page and multi-page).
>
> **LibRed writes the §3.3.2 entry + empty usage maps for every memo/OLE column** — byte-faithful with
> ACE, whose usage-map page lays the records out as: row 0 table-owned, row 1 table-free, then one row
> **per index**, then two rows (owned/free) **per long-value column** (verified against Northwind
> Categories and against an ACE-created 80-memo-column table). **Multi-page distribution (wide tables):**
> a usage-map page holds ~57 of the 69-byte inline records, so a table with many memo/OLE columns can't
> fit all its used/free maps on one page. Access fills the primary page (data + indexes + as many *whole*
> columns as fit — 27 columns alongside a single index), then gives **each remaining long-value column its
> own dedicated usage-map page** with owned = row 0, free = row 1. LibRed reproduces this exactly (verified:
> an 80-memo table lands 27 columns on the primary page at rows 3–56, then one page each for the rest;
> ACE opens it and round-trips an 8000-char value written to an overflow column). Each column's §3.3.2
> `used_pages`/`free_pages` pointers, and the index blocks' `+0x22` pointers, carry the resolved (row, page).
> For a fresh table all these maps are empty. When LibRed writes a value to an LVAL page
> (§8), it now **sets that page's bit in the column's owned-pages *and* free-pages maps** — both §3.3.2
> pointers are parsed from the TDEF (`TableDefinitionPage.LongValueOwnedMaps` / `LongValueFreeMaps`, keyed
> by column id) and the inline bitmap bit is set. **Pages are packed like Access:** a value up to one row
> is appended to the first **free-map** page with room (many small values share a page as separate rows);
> only when none has room is a fresh page allocated (owned + free). A page is dropped from the free map
> once it can't hold the smallest long value (65-byte payload + its 2-byte slot). This reproduces Access's
> layout — MSysQueries.Expression **owns** {42, 282} but **frees** only {282}, the current append target;
> and 20 medium memos land on ~2 pages (full one owned-only, current one owned+free), not 20. The same
> packing is used for the MSysObjects **LvProp** property blob (via `RowInserter.StorePackedLongValue`) —
> but always to a page, never inline (Access reads object properties only from a page), so two tables'
> DEFAULT/CHECK blobs share one LvProp page. A chained value uses dedicated pages. A page outside the inline
> map's window is handled by the shared `UsageMapWriter.SetBit`, which grows the inline record in place and
converts it to a reference map when it no longer fits — a long-value column's maps are not a special case,
and `MapPages` reads either form back.

> **The terminating `0xFFFF` is mandatory on write — even for a table with no long-value
> columns** (where the list is empty and the `0xFFFF` is the only bytes here). Omitting it makes
> Access reject the whole table with *"Unrecognized database format"* even though every other byte
> of the TDEF is valid — verified by byte-diffing an ACE-created single-index table against a LibRed
> one whose only difference was the missing terminator. LibRed's reader doesn't consume this list
> (it stops after the named indexes; long values are located via the in-row LVAL pointer, not these
> maps), but the terminator **must be written**. A table with memo/OLE columns must additionally
> allocate the usage-map records and emit a real `{col_num, used, free}` entry per long-value column
> — verified against Northwind's Categories (cols 2/3) and Employees (cols 14/15).
>
> The §3.3.2 entry is only strictly *required* once a value spills to LVAL pages — an entry-less table
> still round-trips inline values through both LibRed and Access, but Access fails *"Not a valid bookmark"*
> writing a 6000-char value into one (nowhere to record the LVAL page). LibRed writes it regardless, so
> its memo tables match ACE's structure and are already LVAL-ready.
