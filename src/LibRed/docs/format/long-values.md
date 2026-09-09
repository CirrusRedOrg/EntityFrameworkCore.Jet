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

ACE accepts an OLE/binary payload of `0x3FFFFFFF` bytes (1 GiB − 1) and rejects `0x40000000`. That is a
**byte** limit, so the Memo **character** limit is it divided by the two bytes a character costs: Jet 4
stores text as UTF-16LE, and compressed Unicode (§7) is an optimisation on top of that rather than a
different encoding (`LongTextStorageAccessTests`).

Both ends measured directly rather than inferred, with ACE authoring and both engines verifying every
character:

| | bytes | outcome |
| --- | ---: | --- |
| binary `0x3FFFFFFF` | 1,073,741,823 | accepted |
| binary `0x40000000` | 1,073,741,824 | rejected |
| Memo 536,870,911 chars, non-ASCII | 1,073,741,822 | accepted |
| Memo 536,870,912 chars, non-ASCII | 1,073,741,824 | rejected, *"field too small"* |
| Memo 536,870,911 chars, **ASCII** | 1,073,741,822 | accepted |
| Memo 536,870,912 chars, **ASCII** | 1,073,741,824 | rejected |

So a Memo holds at most **536,870,911 characters**, and LibRed's always-UTF-16 writer matches ACE rather
than being half its capacity.

**The limit is content-independent, and that follows from the type.** Jet 4 has one text representation —
**UTF-16LE** — and compressed Unicode (§7) is a space optimisation layered on it, not a second encoding. A
character costs two bytes; whether some of them can be squeezed to one on the way to disk is a property of
the *storage form*, and at this scale the answer is always no, because a chained value is never compressed
(see below) and anything near the ceiling is chained many times over. So "how many characters fit" is the
byte ceiling divided by two, whatever the text holds.

Worth measuring anyway, because the alternative was cheap to believe: if compression had applied, an
all-ASCII Memo would have reached twice as far. It does not — ASCII and non-ASCII agree character for
character at both ends, 536,870,911 accepted and 536,870,912 refused, and the accepted one takes the same
~2 hours to write. Confirmation of the model rather than a surprise in it.

> **The rejection is not cheap and not lossy.** ACE took ~109 minutes to refuse the over-long Memo — it does
> not pre-check the declared length, it processes the whole value and fails at the end — and the database
> then reopened with **zero rows**, in every rejected case, so the failed insert rolled back rather than
> leaving a partial chain. For scale: writing the 1 GiB value took ACE ~110 minutes (~118 for the ASCII one)
> against LibRed's ~3, while reading it back took ACE 6–10 seconds and LibRed 4–5. The asymmetry is entirely
> in ACE's write path.
>
> Every accepted value was read back in full and verified character by character through **both** engines,
> including the ACE-written 1 GiB − 1 binary — so the ceiling is where values stop being *storable*, not
> where they stop being retrievable.

> **`WITH COMPRESSION` does not raise that ceiling.** The attribute is reachable from SQL
> (`M MEMO WITH COMP`) and does set the capable flag, but compression is decided *after* the storage form,
> and a **chained** value is never compressed. Anything near the byte ceiling is chained by a wide margin,
> so the character limit is unaffected however the column was declared. LibRed implements the attribute and
> matches ACE's choice byte-for-byte (`CompressedTextAccessTests`); the eligibility rules are in
> [data-types.md §7](data-types.md#7-compressed-unicode).

**Choosing the storage form.** All three forms are chosen on the value's **uncompressed** UTF-16 length:

| uncompressed length | form | flag |
| --- | --- | --- |
| ≤ 64 bytes | inline, payload follows the descriptor | `0x80` |
| 66 … 3816 bytes | one LVAL page | `0x40` |
| > 3816 bytes | chained across LVAL pages | `0x00` |

> **3816 is not the same number as the 4076-byte chunk row**, and conflating them was a real bug: LibRed used
> 4076 as its single-page threshold and so kept 3818–4076 byte values on one page where ACE chains them.
> Measured both ways — a plain `LONGCHAR` and a `WITH COMP` one behave identically, 1908 characters (3816
> bytes) staying single-page and 1909 (3818) chaining. What fixes the boundary at 3816, rather than the 4076
> a row can actually hold, is **not established**; the ~260-byte margin is unexplained.

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
> Categories and against an ACE-created 80-memo-column table).
>
> **That order is the DDL's, not a fixed rule — ACE assigns the rows in declaration order.** An *inline*
> `PRIMARY KEY` is declared before the long-value columns and takes row 2, giving the layout above; a
> trailing `CONSTRAINT pk PRIMARY KEY (…)` clause is created *after* them, so on a two-memo table ACE gives
> the columns rows 2–5 and the index row 6. Measured both ways at 1, 2, 5, 26, 27, 28 and 40 long-value
> columns. LibRed always writes the inline order — the declaration position is lost between the parser and
> `CreateTable` — so it matches ACE byte-for-byte for inline keys and differs by the row numbering alone for
> a named constraint. Both files are self-consistent and ACE reads either.
> `TdefByteParityAccessTests.Usage_map_rows_follow_declaration_order` holds both measurements.
>
> The spill rule applies here too, and to whichever comes last: at 27 memo columns the index still fits the
> primary page (row 56, the 57th record), and at 28 it goes to **row 0 of a page of its own** — the same
> behaviour `CREATE INDEX` shows on an already-full page. **Multi-page distribution (wide tables):**
> a usage-map page holds ~57 of the 69-byte inline records, so a table with many memo/OLE columns can't
> fit all its used/free maps on one page. Access fills the primary page (data + indexes + as many *whole*
> columns as fit — 27 columns alongside a single index), then gives **each remaining long-value column its
> own dedicated usage-map page** with owned = row 0, free = row 1. LibRed reproduces this exactly (verified:
> an 80-memo table lands 27 columns on the primary page at rows 3–56, then one page each for the rest;
> ACE opens it and round-trips an 8000-char value written to an overflow column). **`CREATE INDEX` on such
> a table spills too**: with the primary page full, ACE does not compact or reuse it but allocates a page
> holding the new index's map alone, at row 0 (verified on a 40-memo table: after `CREATE INDEX` the
> primary page still has its 57 rows and the new index block's `+0x22` pointer reads row 0 of a fresh
> page). Only when the primary page still has room for another 69-byte record does the new index's map go
> there, appended after the existing rows. Each column's §3.3.2
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
