# Long values (Memo / OLE) and LVAL pages

> Part of the [LibRed Jet / ACE file-format reference](README.md). Cross-references use the original **§-numbers**; the [section map](README.md#section-map) says which file each lives in.

## 8. Long values (Memo / OLE)

The in-row value for a Memo/OLE column is a **12-byte descriptor**, not the data:

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Little-endian word: byte length in bits 0–29, flags in bits 30–31 |
| `0x04` | 1 | Row |
| `0x05` | 3 | Page |
| `0x08` | 4 | **Chain stamp** — must equal the first chain page's header `0x08`. Non-zero only on the multi-page form; zero on inline and single-page |

Flags (byte `0x03` masked with `0xC0`; its low six bits belong to the length):
- `0x80` **inline** — the payload follows the descriptor in the row.
- `0x40` **single LVAL page** — the row at (page, row) *is* the whole payload. Several such values **share**
  a page; deleting one retires its row to a 0-length deleted + overflow tombstone and re-lays the page, and
  the page is released as type `0x09` once the last of them is gone
  ([page-05 §9](page-05-usage-maps.md)).
- `0x00` **multi-page** — the payload is chained across LVAL pages; each chunk's row begins
  with a 4-byte pointer (`[row:1][page:3]`) to the next chunk (zero on the last), followed by chunk
  data. Each chunk row is **`MAX_LONG_VALUE_ROW_SIZE` = 4076 bytes** (Jet4; Jet3 = 2032) — a 4-byte
  pointer + up to 4072 data bytes — except the last, which is shorter. Verified against ACE's own
  chained OLE (chunk rows of 4076, 4076 and 2606 bytes).

> **The chain stamp binds a descriptor to its first chain page, and ACE enforces it.** A multi-page
> descriptor's `0x08` and the header `0x08` of the **first** page of its chain hold the same four bytes;
> later chunk pages, single-page (`0x40`) LVAL pages, and ordinary data pages all hold zero. Only the first
> page is bound: patching every *later* chunk page's `0x08` while leaving the descriptor and the first page
> alone changes nothing ACE notices, and patching the first page alone is refused. Which is what the check
> is for — the descriptor is the only way into the chain that a stale pointer can arrive by, since every
> chunk after it is reached from a page already validated. ACE writes
> `GetTickCount()` there — milliseconds since the writing machine booted, so it is machine- and
> boot-relative and reproduces nowhere, like the database creation date. **The value is arbitrary; only the
> agreement matters.** In an ACE-written file, setting *both* copies to `DEADBEEF` reads back fine, setting
> *both* to zero reads back fine, and changing *either one alone* — to any value, zero included — makes ACE
> refuse the record. It refuses it as *"you and another user are attempting to change the same data at the
> same time"*, the same misleading concurrency message an
> oversized record gets, and `CompactDatabase` does not repair such a row: it drops the value.
>
> **The tag is minted per write of the chain**, not per write of the row: inserting stamps both copies,
> rewriting the value writes a new chain and a fresh stamp in both, updating another column of the same row
> leaves it untouched, and a value that shrinks to the single-page form drops both to zero. The check also
> fires only when the long value is **materialised** — ACE will happily `UPDATE` another column of a row
> whose stamp disagrees, and refuse the moment anything reads the memo.
>
> That reading also explains the choice of clock. A chain's pages can be freed and reused by a later value,
> and a stale descriptor would then point at a page holding someone else's data; a stamp that differs
> between successive uses of the same page catches exactly that, and needs to be distinct rather than
> meaningful. **LibRed writes the same stamp in both places and checks it on read**, refusing a chain whose
> entry page disagrees with the descriptor that reached it. Like the database creation date, the value does
> not reproduce between two runs and is not expected to. LibRed is single-writer and so cannot produce the
> interleaving ACE guards against, but it can be handed a file another engine wrote — and the guard is
> groundwork for multi-user concurrency, where this is exactly the check that has to exist.

ACE accepts an OLE/binary payload of `0x3FFFFFFF` bytes (1 GiB − 1) and rejects `0x40000000`. That is a
**byte** limit, so the Memo **character** limit is it divided by the two bytes a character costs: Jet 4
stores text as UTF-16LE, and compressed Unicode (§7) is an optimisation on top of that rather than a
different encoding.

Both ends measured, with ACE authoring and both engines verifying every character:

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

An all-ASCII Memo therefore does **not** reach twice as far: ASCII and non-ASCII agree character for
character at both ends, 536,870,911 accepted and 536,870,912 refused.

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
> matches ACE's choice byte-for-byte; the eligibility rules are in
> [data-types.md §7](data-types.md#7-compressed-unicode).

**Choosing the storage form.** All three forms are chosen on the value's **uncompressed** UTF-16 length:

| uncompressed length | form | flag |
| --- | --- | --- |
| ≤ 64 bytes | inline, payload follows the descriptor | `0x80` |
| 65 … 3816 bytes | one LVAL page | `0x40` |
| ≥ 3817 bytes | chained across LVAL pages | `0x00` |

> **3816 is not the same number as the 4076-byte chunk row**: a writer that uses 4076 as its single-page
> threshold keeps 3817–4076 byte values on one page where ACE chains them. A plain `LONGCHAR` and a
> `WITH COMP` one behave identically, 1908 characters (3816 bytes) staying single-page and 1909 (3818)
> chaining. What fixes the boundary at 3816, rather than the 4076 a row can actually hold, is **not
> established**; the ~260-byte margin is unexplained.

LVAL pages are data pages (type `0x01`) whose owner field (`0x04`) is the ASCII marker `LVAL`.

> **Reader and reclamation guardrails.** LibRed requires the complete 12-byte descriptor before reading
> its fields, accepts only the three flags above, and bounds inline data against the bytes actually present.
> Every external pointer must name an in-file type-`0x01` page with the `LVAL` owner marker and a live,
> ordinary row slot. Chained rows must contain their 4-byte next pointer, make payload progress, never repeat
> a `(page,row)`, terminate at zero exactly when the declared length is reached, and neither underfill nor
> overrun that length. Before reclaiming a replaced chain, LibRed validates the complete chain and requires
> every page to be present in that column's owned-pages map; only then does it begin clearing maps/free bits.
> Those subsequent writes are atomic whenever a transaction is open (`docs/design/transactions.md`), and the
> engine opens one per statement, so a failed reclamation rolls back with the statement. A direct
> `LibRed.Core` caller that opens none gets the same non-atomic behaviour as any other multi-page write.

> **How aggressively the engine reclaims is a setting, and its default differs by engine.** Jet's
> `RecycleLVs` (the OLE DB property `Jet OLEDB:Recycle Long-Valued Pages`) controls whether freed LVAL pages
> are reclaimed aggressively; it ships as **1 on ACE** (both 14 and 16) and **0 on Jet 4.0**. So the same
> delete can leave different free/owned-map state behind depending on which engine wrote the file — worth
> pinning before treating an ACE-vs-Jet4 reclamation difference as a format one.

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
> type: every entry is a Memo or OLE column, and **a table with Text columns but no memo/OLE has an
> empty list**. So mdbtools' name is imprecise; the list is keyed to long-value columns.

### Dropping a long-value column

`DROP COLUMN` of a memo or OLE column is a metadata edit like any other drop ([page-02a](page-02a-tdef.md)) —
existing rows keep the column's bytes, now dead, and its LVAL pages keep their contents — plus three steps for
the long values:

1. its §3.3.2 entry leaves the definition; the other long-value columns keep theirs, and their map records
   keep their row numbers;
2. its owned-map and free-map records are retired from their holder exactly as `DROP TABLE` retires a
   long-value column's ([page-05 §9](page-05-usage-maps.md)): the owned pages' bits cleared except the page
   still in its free map, then each row tombstoned, the rows below sliding up;
3. every page in its owned map — single-value pages and chain pages alike — goes back to the global free-pages
   map at close.

## Writing long values

> **Writing.** LibRed inlines a memo/OLE value only up to **64 bytes** (same for Jet3/Jet4): the 12-byte
> descriptor with length + the `0x80` flag (bytes `0x04`–`0x0B` zero) then the payload (memo = UTF-16LE,
> OLE = raw bytes). A value of **65–3816 bytes** is written as one row on an **LVAL page** (`0x40`
> descriptor, `LongValueWriter`; rows share a page, see below) — `RowInserter` materialises it before
> encoding. This matters for Access, not just LibRed: Access
> tolerates an inline value its reader resolves, but **rejects an over-64-byte value inlined** (e.g. it
> opens the database yet fails to *run* a view whose subquery `Expression` was inlined; on an LVAL page
> it runs — verified against the derived-table view, §11). A value of **3817 bytes or more** is written as
> a **chain** (`0x00` descriptor): the payload is split into 4072-byte data chunks,
> each on its own page with a 4-byte next-pointer, matching ACE byte-for-byte (verified: LibRed and
> Access both read back memo values from 65 bytes to 100 KB — single-page and multi-page).
>
> **LibRed writes the §3.3.2 entry + empty usage maps for every memo/OLE column** — byte-faithful with
> ACE, whose usage-map page lays the records out as: row 0 table-owned, row 1 table-free, then one row
> **per index**, then two rows (owned/free) **per long-value column** (verified against ACE-created
> tables).
>
> **That order is the DDL's, not a fixed rule — ACE assigns the rows in declaration order.** An *inline*
> `PRIMARY KEY` is declared before the long-value columns and takes row 2, giving the layout above; a
> trailing `CONSTRAINT pk PRIMARY KEY (…)` clause is created *after* them, so on a two-memo table ACE gives
> the columns rows 2–5 and the index row 6. Measured both ways. LibRed always writes the inline order — the
> declaration position is lost between the parser and `CreateTable` — so it matches ACE byte-for-byte for
> inline keys and differs by the row numbering alone for a named constraint. Both files are self-consistent
> and ACE reads either.
>
> The spill rule applies here too, and to whichever comes last: at 27 memo columns the index still fits the
> primary page (row 56, the 57th record), and at 28 it goes to **row 0 of a page of its own** — the same
> behaviour `CREATE INDEX` shows on an already-full page. **Multi-page distribution (wide tables):**
> a usage-map page holds ~57 of the 69-byte inline records, so a table with many memo/OLE columns can't
> fit all its used/free maps on one page. Access fills the primary page (data + indexes + as many *whole*
> columns as fit — 27 columns alongside a single index), then gives **each remaining long-value column its
> own dedicated usage-map page** with owned = row 0, free = row 1. LibRed reproduces this exactly (verified:
> 27 columns land on the primary page at rows 3–56, then one page each for the rest; ACE opens the table and
> round-trips a value written to an overflow column). **`CREATE INDEX` on such a table spills too**: with
> the primary page full, ACE does not compact or reuse it but allocates a page holding the new index's map
> alone, at row 0 (verified: after `CREATE INDEX` the primary page still has its 57 rows and the new index
> block's `+0x22` pointer reads row 0 of a fresh page). Only when the primary page still has room for
> another 69-byte record does the new index's map go there, appended after the existing rows. Each column's
> §3.3.2 `used_pages`/`free_pages` pointers, and the index blocks' `+0x22` pointers, carry the resolved
> (row, page). For a fresh table all these maps are empty. When LibRed writes a value to an LVAL page (§8),
> it **sets that page's bit in the column's owned-pages *and* free-pages maps** — both §3.3.2
> pointers are parsed from the TDEF (`TableDefinitionPage.LongValueOwnedMaps` / `LongValueFreeMaps`, keyed
> by column id) and the inline bitmap bit is set. **Pages are packed like Access:** a value up to one row
> is appended to the first **free-map** page with room (many small values share a page as separate rows);
> only when none has room is a fresh page allocated (owned + free). A page is dropped from the free map
> once it can't hold the smallest long value (65-byte payload + its 2-byte slot). This reproduces Access's
> layout — a column **owns** every page it has filled but **frees** only the current append target, so
> medium memos share a few pages (full ones owned-only, the current one owned+free), not one each. The same
> packing is used for the MSysObjects **LvProp** property blob (via `RowInserter.StorePackedLongValue`) —
> but always to a page, never inline (Access reads object properties only from a page), so two tables'
> DEFAULT/CHECK blobs share one LvProp page. A chained value uses dedicated pages. A page outside the inline
> map's window is handled by the shared `UsageMapWriter.SetBit`, which grows the inline record in place and
converts it to a reference map when it no longer fits — a long-value column's maps are not a special case,
and `MapPages` reads either form back.

> **The terminating `0xFFFF` is mandatory on write — even for a table with no long-value
> columns** (where the list is empty and the `0xFFFF` is the only bytes here). Omitting it makes
> Access reject the whole table with *"Unrecognized database format"* even though every other byte
> of the TDEF is valid — verified by byte diff against an ACE-created table. LibRed's reader doesn't
> consume this list (it stops after the named indexes; long values are located via the in-row LVAL
> pointer, not these maps), but the terminator **must be written**. A table with memo/OLE columns must
> additionally allocate the usage-map records and emit a real `{col_num, used, free}` entry per long-value
> column — verified against ACE-authored tables.
>
> The §3.3.2 entry is only strictly *required* once a value spills to LVAL pages — an entry-less table
> still round-trips inline values through both LibRed and Access, but Access fails *"Not a valid bookmark"*
> writing a 6000-char value into one (nowhere to record the LVAL page). LibRed writes it regardless, so
> its memo tables match ACE's structure and are already LVAL-ready.
