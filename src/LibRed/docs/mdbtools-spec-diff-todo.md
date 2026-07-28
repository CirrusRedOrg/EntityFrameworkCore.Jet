# TODO — mdbtools `HACKING.md` vs our `jet-ace-file-format.md`

Differences between the mdbtools format notes and our spec that warrant investigating or
confirming against real files. **Source:** mdbtools `HACKING.md` (github.com/mdbtools/mdbtools,
`dev`) cross-checked against `jet-ace-file-format.md`, 2026-06-30.

> **Caveat — mdbtools rarely pins the data value.** Its tables list a field's *name, length, and
> order* but usually leave the "data" column as `????`. So a value we assert and mdbtools leaves
> blank is **not** a conflict — only treat genuine name/length/offset/semantic mismatches as items
> here. Most structures already agree (data/index/usage-map page headers, row format, column types,
> the §3.3.2 variable/LVAL-column-tracking terminator, index-info FK/cascade/type fields); those
> are omitted.

## Items to confirm

- [x] **TDEF `0x0C` — constant `0x659`, or a per-TDEF "definition id"?** **Resolved: a fixed
  constant.** Surveyed `0x0C` across all 33 Northwind tables (user/system/complex-type/hidden) and
  freshly ACE-created tables — every one reads `1625` (`0x659`), and the header value equals the
  first column-descriptor marker in each. So mdbtools' "Matches definition block" is just a shared
  constant appearing in several spots, not a table-scoped id. Our writer's fixed `0x659` is correct
  (no write bug). The mdbtools "*or 0*" variant never appeared (possibly Jet 3 / degenerate).
  Spec §3.1 note updated.

- [x] **TDEF header `0x18`–`0x27` semantics.** **Investigated** (read Northwind + ACE-created
  autonumber vs plain tables). Findings, now in §3.1:
  - `0x18` is **not** a per-table autonumber flag — it's `0x01` on *every* table (autonumber,
    non-autonumber, text-PK), i.e. a plain constant. mdbtools' "autonum_flag" label is misleading.
  - `0x14` (which we already document) is the **highest AutoNumber assigned**, not the "next":
    Categories (8 rows) = 8, an ACE table after 2 inserts = 2, non-autonumber tables = 0. Next id is
    `+1`. Corrected the label.
  - `0x1C` (`ct_autonum`, complex-type autonumber) split out as its own 4-byte field; `0` in every
    table observed. **Not** positively confirmed non-zero — OLE DB DDL can't create a complex
    (multi-value/attachment) column, so this needs a fixture built another way to see it populated.
  - `0x20`–`0x27` reserved, zero observed.

- [x] **Index-data block (§3.5) — flags offset.** mdbtools puts `flags`(1 byte) at block **`+0x2A`**;
  we document the index flags at **`+0x2E`**. **Resolved:** in real files `+0x2A`–`+0x2D` is **zero**
  (4 bytes) and the effective flags (`0x80` always-set on Orders' first index, `0x89` on a PK) are at
  `+0x2E`. So our `+0x2E` is correct; mdbtools' `+0x2A` flags byte is zero/unused in ACE. §3.5 now
  documents `+0x2A` (4 reserved) and `+0x30` (4 reserved) explicitly.

- [x] **Index statistics block (§3.3.1) — field meaning.** **Resolved** via an ACE
  insert/delete/insert probe. `+0x04` (mdbtools `num_idx_rows`) is the **unique-entry count**:
  maintained live per insert, cumulative, never decremented (3 → 3 after delete → 4). `+0x00`
  ("total") is **not** live-maintained — Access leaves it `0` through inserts and only writes the
  row count on compact (saved Northwind reads `total == rowCount`, a live insert reads `0`), which
  corrected our spec's "total = row count" claim. LibRed now maintains `+0x04` on insert and leaves
  `+0x00` at 0 (fix committed).

- [x] **Column descriptor `0x0D` — text sort-order version.** **Resolved.** `0x0B`–`0x0E` is a 4-byte
  sort-order descriptor: locale `0x0409` at `0x0B`, **version** at `0x0D`. mdbtools states it
  explicitly (note after its index-record section): the encoding is Access 2000–2007 **General** =
  (1033, **version 0**); Access 2010 renamed that **"General legacy"** and made a *new* default
  **General** = (1033, **version 1**) with different key bytes. Observed `0x0D = 0` on every ACE-2007
  Northwind column and on a text column freshly created by the ACE 16 engine (the file is ACE-2007
  format, so the engine writes v0). **Implication:** LibRed's `JetTextCollation` is the version-0
  order; a v1 file (Access 2010+, version byte ≥ 3) uses a different text-key encoding LibRed does not
  handle. Documented in spec §3.4 + §10.4 and in `JetTextCollation`. See memory
  `libred-text-collation-v1-gap`. (No v1 fixture available to reverse-engineer.)

- [x] **Logical index block (§3.6) — `index_num` / `index_num2`.** **Resolved** against Northwind.
  `+0x04` (`index_num`) is the logical index's unique id (`0..logicalCount-1`); `+0x08` (`index_num2`,
  our reader's `DataNumber`) is the ordinal of the real index-data block it maps to
  (`0..realIndexCount-1`). They differ because multiple logical indexes share one real block: a
  relationship reuses the real index on *this table's* side of the FK (child column for outgoing, the
  referenced PK for incoming), so the PK block is referenced by its own PRIMARY logical block *and* by
  incoming relationships — verified on Orders (real block 3 = OrderID PK, shared). mdbtools' "index
  into index cols list" = index into the real index-data blocks. Spec §3.6 note added.

- [x] **§3.3.2 naming — "variable column" vs LVAL-only.** **Resolved: LVAL-only.** Correlated each
  trailing-list entry with its column type: only Memo (`0x0C`) and OLE (`0x0B`) appear — never plain
  Text (`0x0A`). Clincher: **Customers (11 Text columns, no memo/OLE) has an empty list**; Categories
  → {col2 Memo, col3 OLE}, Employees → {col14 OLE, col15 Memo}, Suppliers → {col11 Memo}. Only
  memo/OLE have LVAL page chains needing usage maps (Text is inline), so mdbtools' "Variable Column
  Tracking" name is imprecise. Spec §3.3.2 note added.

- [x] **Jet4 data-page header — the extra 4 bytes.** mdbtools notes the Jet4 data page adds an
  unknown 4-byte field after `tdef_pg` (before `num_rows`) vs Jet3. **Resolved:** §4 now documents
  `0x08` (4 bytes, Jet4-only, observed zero across all data/usage-map/LVAL pages; Jet3 has the row
  count here). Our writer already leaves it zero.

## Offset-gap audit (resolved)

Audited every offset table in the spec for silent skips and made each explicit (observed values
verified against Northwind):

- [x] **Index B-tree page header (§10.1) `0x10`–`0x13`** — zero in ACE; ACE's child-tail reads
  correctly at `0x14` (e.g. 243), zero at `0x10`, whereas mdbtools puts `tail_page` at `0x10`.
  **Corroborated:** mdbtools *version-labels* the entry bitmask offset — `0x16` (Jet3) / `0x1B`
  (Jet4) — and that +5 Jet3→Jet4 shift is accounted for *exactly* by the inserted 4-byte field at
  `0x10` plus the 1-byte field at `0x1A` (observed `0x01`). So `0x10`/`0x1A` are Jet4 insertions and
  our `0x14` tail / `0x1B` bitmask offsets are confirmed. (A Jet3 file would still let us read the
  pre-insertion layout directly.)
- [x] **Index-data block (§3.5) `0x2A`–`0x2D` and `0x30`–`0x33`** — both zero; documented (see flags
  item above).
- [x] **Index-info block (§3.6) `0x18`–`0x1B`** — zero; documented as trailing reserved bytes.
- [x] **Page 0 (§2) `0x01`–`0x03`, `0x13`, `0x15`–`0x17`** — small gaps in the otherwise-undecoded
  page-0 header; now shown explicitly (incl. the note that mdbtools reads the version as a 4-byte
  word at `0x14`).

## For later (not present in LibRed yet — record for when implemented)

- [ ] **Memo/LVAL in-row descriptor (12 bytes).** For memo/OLE write+read: `memo_len`(3) /
  `bitmask`(1: `0x80` inline, `0x40` unique-LVAL, `0x00` LVAL) / `lval_dp`(4) / unknown(4). Ties to
  the §3.3.2 per-column usage maps and the LVAL ('LVAL'-owner) page chain.

- [ ] **Jet3 row jump table.** mdbtools' Jet3 row format has a `jump_table` (used when a row exceeds
  256 bytes) and 1-byte `num_cols`/`eod`/`var_len`; Jet4 drops the jump table and widens those to
  2 bytes. Note for eventual Jet3 support (see the Jet3 fixture plan).
