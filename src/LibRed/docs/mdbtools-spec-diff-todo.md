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

- [ ] **TDEF `0x0C` — constant `0x659`, or a per-TDEF "definition id"?** mdbtools frames the 4-byte
  field at TDEF `0x0C`, column-descriptor `+0x01`, and logical-index `+0x00` as **one recurring
  value** ("Matches definition block unknown field"), not a fixed constant. We assert it is always
  `0x0659` (1625). Both can be true (constant *and* recurring), but confirm whether it can differ
  between tables/files — i.e. is it a universal magic number or a tdef-scoped id that is merely
  `0x659` in the files checked? Check across several tables and a second database.

- [ ] **TDEF header `0x18`–`0x27` semantics.** mdbtools splits this as: `autonum_flag`(1)@`0x18`
  (`0x01` "makes autonumbers work"), unknown(3), `ct_autonum`(4)@`0x1C` (**complex-type
  autonumber**), unknown(8). Our §3.1 has `0x18` as a 4-byte const `0x01` and `0x1C`–`0x27` as a
  12-byte reserved/zero blob. Confirm `0x1C` is the complex-type autonumber (4 bytes) and `0x18` is
  a 1-byte autonumber-enable flag — test with a table that has an AutoNumber and/or a multi-value
  (complex) column so `ct_autonum` is non-zero.

- [x] **Index-data block (§3.5) — flags offset.** mdbtools puts `flags`(1 byte) at block **`+0x2A`**;
  we document the index flags at **`+0x2E`**. **Resolved:** in real files `+0x2A`–`+0x2D` is **zero**
  (4 bytes) and the effective flags (`0x80` always-set on Orders' first index, `0x89` on a PK) are at
  `+0x2E`. So our `+0x2E` is correct; mdbtools' `+0x2A` flags byte is zero/unused in ACE. §3.5 now
  documents `+0x2A` (4 reserved) and `+0x30` (4 reserved) explicitly.

- [ ] **Index statistics block (§3.3.1) — field meaning.** mdbtools labels the 12 bytes as
  unknown(4) / `num_idx_rows`(4)@`+0x04` / unknown(4). We read `+0x00` = total entry count
  (= row count) and `+0x04` = unique entry count. Our read is more specific and verified; just
  confirm `+0x00` really is total-entries (mdbtools leaves it unknown) and that mdbtools'
  `num_idx_rows`@`+0x04` is our unique-entry count.

- [ ] **Column descriptor `0x0D` — text sort-order version, not "unknown".** mdbtools calls the
  2 bytes at col `+0x0D` `misc_ext` = "text sort order version number". Our §3.4 lists `0x0D` as
  "Unknown (zero observed)". Confirm `0x0D` is the text-collation/sort-order version (likely 0 for
  Jet4 General, which is why we see zero) rather than truly reserved.

- [ ] **Logical index block (§3.6) — `index_num` / `index_num2`.** mdbtools has `index_num`(4)@`+0x04`
  and `index_num2`(4)@`+0x08` ("index into index cols list"). Confirm our §3.6 documents both and
  that we understand `index_num2`'s role (mapping a logical index to its real-index column list).

- [ ] **§3.3.2 naming — "variable column" vs LVAL-only.** mdbtools calls our trailing column
  usage-map list "Variable Column Tracking" (implying *any* variable-length column). We observed it
  is populated only for **memo/OLE (LVAL)** columns — Text-only tables (Customers, Suppliers, Orders)
  had empty lists. Confirm plain Text variable columns never appear here (strong evidence already);
  if confirmed, our LVAL-only framing is the more accurate one and the naming divergence is just
  mdbtools being loose.

- [x] **Jet4 data-page header — the extra 4 bytes.** mdbtools notes the Jet4 data page adds an
  unknown 4-byte field after `tdef_pg` (before `num_rows`) vs Jet3. **Resolved:** §4 now documents
  `0x08` (4 bytes, Jet4-only, observed zero across all data/usage-map/LVAL pages; Jet3 has the row
  count here). Our writer already leaves it zero.

## Offset-gap audit (resolved)

Audited every offset table in the spec for silent skips and made each explicit (observed values
verified against Northwind):

- [x] **Index B-tree page header (§10.1) `0x10`–`0x13`** — zero in ACE; this is the extra field
  that puts ACE's child-tail at `0x14` where mdbtools' Jet3 layout has `tail_page` at `0x10`. Tail
  pointer reads correctly at `0x14` (e.g. 243), zero at `0x10`. Also documented `0x1A` (1 byte,
  observed `0x01`).
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
