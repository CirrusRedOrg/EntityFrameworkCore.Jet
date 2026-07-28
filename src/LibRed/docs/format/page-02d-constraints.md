# TDEF: indexes, keys and constraints

> Part of the [LibRed Jet / ACE file-format reference](README.md). Cross-references use the original **§-numbers**; the [section map](README.md#section-map) says which file each lives in.

### 3.3.1 Index statistics block (12 bytes, one per real index)

The block at `0x3F`, in the same order as the index-data blocks (§3.5), holds per-index
statistics:

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Total entry count — **compact-time only** (see note): the row count in a *saved/compacted* file, but `0` in a live-edited one |
| `0x04` | 4 | **Unique entry count** — distinct entries ever added; maintained live on every insert (see note) |
| `0x08` | 4 | Reserved (zero observed) |

**These two fields are maintained very differently — verified with an ACE
insert/delete/insert sequence and against saved Northwind tables:**

- **Total entry count (`+0`) is *not* maintained on insert.** Access leaves it `0` through live
  inserts and only writes the row count on **compact/repair**. Saved Northwind tables read
  `total == rowCount` (Categories 8, Orders 830, Order Details 2155) precisely because they were
  compacted; a freshly SQL-inserted table reads `total == 0` while `rowCount` climbs. A writer
  should therefore **leave `+0` at `0`** on insert (LibRed does), not set it to the row count —
  doing so would falsely mark the file as compacted.
- **Unique entry count (`+4`) *is* maintained live and is cumulative** — Access increments it per
  insert and **never decrements** it. Verified: after 3 inserts it is `3`; after deleting a row it
  stays `3` (not decremented); after one more insert it is `4`. It equals the current
  distinct-value count only with no deletions. A **unique** index gains one distinct key per row,
  so a writer increments `+4` by one per insert per unique index (LibRed does this in
  `RowInserter`). A **non-unique** index should advance `+4` only when the inserted key is
  genuinely new — not yet handled (LibRed creates only unique indexes; see the
  `TODO(non-unique-index-stats)` marker). LibRed exposes `+4` as `IndexDef.UniqueEntryCount`.


### 3.5 Index-data block (52 bytes)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Marker (`0x00000783` = 1923, or 0) |
| `0x04` | 30 | **Exactly 10** column slots × (2-byte column id + 1-byte flags); column id `0xFFFF` = unused; flag `0x01` = ascending. This fixed array (no count field, no continuation) is why a Jet/ACE index — and therefore any `PRIMARY KEY`, `UNIQUE`, or `FOREIGN KEY` built on one — is **limited to 10 columns**. |
| `0x22` | 1 | Usage-map row |
| `0x23` | 3 | Usage-map page |
| `0x26` | 4 | **B-tree root page** |
| `0x2A` | 4 | Unknown / reserved (zero observed). mdbtools places a 1-byte index-flags field at `+0x2A`, but ACE's effective flags are at `0x2E` and this is zero in every file checked |
| `0x2E` | 2 | Flags: `0x01` unique, `0x02` ignore-nulls (`WITH IGNORE NULL` — null-keyed rows excluded from the index), `0x08` required (`WITH DISALLOW NULL` / part of a primary key), `0x80` always-set (Access 2000+). Verified vs ACE: a plain index is `0x0080`, `IGNORE NULL` `0x0082`, `DISALLOW NULL` `0x0088`, a PK `0x0089`. |
| `0x30` | 4 | Unknown / reserved (zero observed) — trailing bytes of the 52-byte block |

> **Unique (`0x01`) enforcement treats NULLs as distinct (verified vs ACE).** A `UNIQUE` index (that is
> **not** `WITH IGNORE NULL`) rejects a duplicate **non-null** key but permits **multiple NULL** keys — two
> rows may both be null in the indexed column(s). So uniqueness is enforced only over the non-null keys; a
> row with a null in any indexed column is exempt (matching SQL's "nulls are distinct"). LibRed enforces
> this on insert/update (`IndexWriter.KeyExists`, skipping null-keyed rows), and rejects
> `CREATE UNIQUE INDEX` when the rows that already exist are not unique — scanned up front, before the
> TDEF is written, so a rejected statement leaves the file untouched. A `WITH IGNORE NULL` index
> (`0x02`) additionally leaves null-keyed rows out of the B-tree entirely; a PK (`0x08` required) forbids
> nulls, so the question doesn't arise.

A table has **at most 32 index-data blocks** (the `0x33` count, §3.1) — the Jet/ACE "32 indexes per
table" limit, counting the indexes that back primary keys, unique constraints and the child side of
relationships. (Incoming relationships add *logical* index-info blocks, §3.6, which reuse an existing
data block and so do not count toward this.)


### 3.6 Index-info block (28 bytes) — one per *logical* index

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | Marker (`0x00000659` = 1625, or 0) |
| `0x04` | 4 | **Logical index number** (`index_num`) — a unique id per logical index, `0 … logicalCount-1` |
| `0x08` | 4 | **Real index-data block ordinal** (`index_num2`) this logical index uses, `0 … realIndexCount-1` |
| `0x0C` | 1 | Foreign-key index type: `0x00` = none, `0x01` = **incoming** (this table is the parent/referenced end), `0x02` = **outgoing** (this table is the child/referencing end), `0x03` = **outgoing, `FOREIGN KEY NO INDEX`** (verified vs ACE: identical to `0x02` — same data block and same parent incoming `0x01` block — only this type byte differs) |
| `0x0D` | 4 | Foreign-key index number: the `index_num` (`0x04`) of the **matching logical block on the other table**; `0xFFFFFFFF` when not a relationship |
| `0x11` | 4 | Foreign-key table page (the *other* table's TDEF page; non-zero ⇒ a relationship index) |
| `0x15` | 1 | Update action: `0x04` plain index; on a relationship `0x00` = no cascade, `0x01` = cascade update |
| `0x16` | 1 | Delete action: `0x04` plain index; on a relationship `0x00` = no cascade, `0x01` = cascade delete |
| `0x17` | 1 | Index type: `0x00` = plain secondary, `0x01` = primary, `0x02` = foreign/relationship |
| `0x18` | 4 | Unknown / reserved (zero observed) — trailing bytes of the 28-byte block |

The index **name** read at the same ordinal applies to this logical index. To name the
physical (data-block) index, prefer a real index's name over a foreign-key relationship's
(distinguished by `0x11` ≠ 0), and take `IsPrimaryKey` from the type byte `0x17`.

> **Writing a relationship's logical blocks — verified by having ACE create a minimal `P1(Id PK)` /
> `C1(Id PK, Pid FK→P1.Id)` pair and diffing.** The **child** (referencing) table gives its FK-column
> index a *single* logical block that **is** the relationship: `index_num2` → the FK-column data
> block, `0x0C = 0x02` (outgoing), `0x11` = parent page, `0x17 = 0x02`, name = the constraint name.
> The **parent** (referenced) table gains an **extra** logical block beyond its data blocks:
> `index_num2` → its referenced-key (PK) data block, `0x0C = 0x01` (incoming), `0x11` = child page,
> `0x17 = 0x02`, name = an auto-generated hidden `.r?` name. The two ends cross-reference: each block's
> `0x0D` holds the other block's `index_num` (`0x04`). Logical blocks are stored **sorted by name**;
> `index_num` is assigned in creation order (a table's own indexes first, then relationships as added —
> so a parent's Nth incoming relationship gets `index_num` = its logical count before the insert).
> Cascade `ON UPDATE`/`ON DELETE` set `0x15`/`0x16` to `0x01` on **both** ends' blocks.
>
> **Self-reference** (a table whose FK targets itself): both ends live in the **one** TDEF, each with
> `0x11` = the table's own page — an outgoing `0x02` block (`index_num2` → the FK-column index) and an
> incoming `0x01` block (`index_num2` → the referenced-key index), cross-referenced by `index_num`. The
> incoming block is numbered after the data-block logical indexes (`index_num` = data-block count).
> Verified byte-for-byte against an ACE-created self-reference.

> **`index_num` (`0x04`) vs `index_num2` (`0x08`) — verified against Northwind.** `0x04` is the
> logical index's own unique number; `0x08` is the ordinal of the **real index-data block** (§3.5)
> it maps to. They differ because **several logical indexes share one real block**: a relationship
> (`0x11` ≠ 0) reuses the real index on *this table's* side of the foreign key rather than owning its
> own. Confirmed on Orders (7 real / 13 logical): real block 3 = `OrderID` `PK_Orders` is referenced
> both by its own PRIMARY logical block (`index_num2 = 3`) and by an **incoming** relationship
> (`index_num2 = 3`, `fkTablePage` = Order Details), while the **outgoing** FK relationships map to
> the child-column real indexes (CustomerID/EmployeeID/ShipVia). So `index_num2` points to the real
> index on this table's side — the child FK column for an outgoing FK, the referenced key (PK) for an
> incoming one — which is exactly mdbtools' "index into index cols list". LibRed's reader keys off
> `0x08` (its `DataNumber`) and does not use `0x04`.

