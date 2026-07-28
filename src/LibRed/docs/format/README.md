# Jet / ACE file format — LibRed reference

This is LibRed's own specification of the Microsoft Jet 4 / ACE (Access `.mdb` / `.accdb`)
on-disk format. **Every offset and structure here has been verified byte-for-byte against
real database files** (the Northwind ACE-2007 sample, a generated 200-column wide table,
and a generated ~150 MB large table) and cross-checked against mdbtools (its `HACKING.md`)
and Jackcess. Several structures are additionally verified from the **write** side: LibRed
produces them and Access's own OLE DB engine reads the result back (a LibRed-inserted row is
found by an Access indexed primary-key seek; encoded index keys match Access's stored bytes).
Where something is assumed or unverified, it says so explicitly.

Unless noted, everything here describes **Jet 4 and ACE (12/14/16/17)**, which share one
structural layout. **Jet 3** (Access 97) differs in many of these and is *not yet
implemented* — see [Version differences](#version-differences).

Implemented by `src/LibRed/LibRed.Core/`. The canonical offsets live in
`Formats/JetFormatBase.cs`.

> **This reference is split across several files** — one per page type, plus cross-cutting topics; each is
> self-contained (its structures *and* its read/write mechanics live together). Most describe the **on-disk
> format**; [page-02c-default-values.md](page-02c-default-values.md) is the exception — it covers `DEFAULT`
> **semantics** (engine behaviour), sitting with the column pages because it's a column feature. This README
> is the map. For a bare field-layout lookup with no prose, see the [structures appendix](appendix-structures.md).

---

## Files

| File | Covers |
| --- | --- |
| [page-00-database.md](page-00-database.md) | Page 0 — the database-definition page (format id, version byte) |
| [page-01-data-and-rows.md](page-01-data-and-rows.md) | Data page (type `0x01`) header + slot directory, and the inline **row record** format |
| [page-02a-tdef.md](page-02a-tdef.md) | Table-definition page (type `0x02`): header, multi-page chaining, body layout, and writing a TDEF Access accepts |
| [page-02b-columns.md](page-02b-columns.md) | TDEF **columns**: the 25-byte descriptor, and column maintenance incl. the in-place **`ALTER COLUMN`** type/length change |
| [page-02c-default-values.md](page-02c-default-values.md) | Column **`DEFAULT`** value *semantics* — what an expression may contain, the DDL-parser-vs-expression-service split (engine behaviour; the on-disk `LvProp` storage is in [system-catalog.md](system-catalog.md)) |
| [page-02d-constraints.md](page-02d-constraints.md) | TDEF **indexes / keys / constraints**: index-data, index-info and stats blocks (PK / unique / FK metadata) |
| [page-03-04-index-btree.md](page-03-04-index-btree.md) | Index B-tree pages (types `0x03` node / `0x04` leaf): header, entries, prefix compression, key encoding, splitting |
| [page-05-usage-maps.md](page-05-usage-maps.md) | Per-table owned/free usage maps, `0x05` bitmap pages, and the global free-pages map (allocation) |
| [long-values.md](long-values.md) | Memo / OLE long values, LVAL pages, and the per-column usage-map list |
| [data-types.md](data-types.md) | Data-type codes and their decode, plus compressed Unicode |
| [system-catalog.md](system-catalog.md) | `MSysObjects` / `MSysACEs` / `MSysQueries` / `MSysRelationships`, the `LvProp` property blob, views & procedures, relationships |
| [appendix-structures.md](appendix-structures.md) | Consolidated field-layout tables for every on-disk structure — quick reference, no prose |

The supported VBA/Access **functions** (usable in `SELECT`/`WHERE`/`ORDER BY`/`DEFAULT`/`CHECK`) are
catalogued one level up in [`../functions.md`](../functions.md).

---

## 1. Conventions

- **Endianness:** little-endian for all integers, **except** index-page key/pointer values,
  which are big-endian (noted in the index B-tree file).
- **Page size:** 4096 bytes (Jet 4 / ACE). Jet 3 is 2048.
- **Pages** are numbered from 0; a page's byte offset in the file is `pageNumber * pageSize`.
- **Page type** is the first byte of every page:

  | Byte | Page type | LibRed | File |
  | --- | --- | --- | --- |
  | `0x00` | Database definition (page 0 only) | `DatabaseDefinitionPage` | [page-00](page-00-database.md) |
  | `0x01` | Data page (also long-value/LVAL pages) | `DataPage` | [page-01](page-01-data-and-rows.md) / [long-values](long-values.md) |
  | `0x02` | Table definition (TDEF) | `TableDefinitionPage` | [page-02a](page-02a-tdef.md) |
  | `0x03` | Index B-tree node (intermediate) | `IndexCursor` | [page-03-04](page-03-04-index-btree.md) |
  | `0x04` | Index B-tree leaf | `IndexCursor` | [page-03-04](page-03-04-index-btree.md) |
  | `0x05` | Page-usage bitmap | `UsageMap` | [page-05](page-05-usage-maps.md) |

---

## Section map

Cross-references throughout use the original **§-numbers** from the single-file spec. This
table says which file each section now lives in.

| § | Section | File |
| --- | --- | --- |
| §1 | Conventions | this README (above) |
| §2 | Page 0 — database definition | [page-00-database.md](page-00-database.md) |
| §3.1 | TDEF header | [page-02a-tdef.md](page-02a-tdef.md) |
| §3.2 | Multi-page TDEFs | [page-02a-tdef.md](page-02a-tdef.md) |
| §3.3 | TDEF body layout | [page-02a-tdef.md](page-02a-tdef.md) |
| §3.3.1 | Index statistics block | [page-02d-constraints.md](page-02d-constraints.md) |
| §3.3.2 | Column usage-map list | [long-values.md](long-values.md) |
| §3.4 | Column descriptor | [page-02b-columns.md](page-02b-columns.md) |
| §3.5 | Index-data block | [page-02d-constraints.md](page-02d-constraints.md) |
| §3.6 | Index-info block | [page-02d-constraints.md](page-02d-constraints.md) |
| §3.7 | Writing a TDEF | [page-02a-tdef.md](page-02a-tdef.md) |
| §3.8 | In-place `ALTER COLUMN` | [page-02b-columns.md](page-02b-columns.md) |
| §4 | Data page | [page-01-data-and-rows.md](page-01-data-and-rows.md) |
| §5 | Row record format | [page-01-data-and-rows.md](page-01-data-and-rows.md) |
| §6 | Data types | [data-types.md](data-types.md) |
| §7 | Compressed Unicode | [data-types.md](data-types.md) |
| §8 | Long values (Memo / OLE) | [long-values.md](long-values.md) |
| §9 | Usage maps | [page-05-usage-maps.md](page-05-usage-maps.md) |
| §9.1 | Global free-pages map | [page-05-usage-maps.md](page-05-usage-maps.md) |
| §10 | Index B-tree pages | [page-03-04-index-btree.md](page-03-04-index-btree.md) |
| §11 | System catalog | [system-catalog.md](system-catalog.md) |
| §12 | Version differences | this README (below) |

---

## Version differences

**Jet 3 (Access 97) is the odd one out** and is *not yet implemented*. Known differences from
the Jet 4 / ACE layout documented across these files:

- 2048-byte pages.
- 18-byte column descriptors (vs 25).
- 1-byte ASCII name lengths (vs 2-byte UTF-16).
- 1-byte row column-count (vs 2-byte), and 1-byte variable-offset entries **with a jump table**
  for rows > 256 bytes.
- Different data-page and TDEF header offsets.

Jet 4 and all later ACE versions (12/14/16/17) share the structural layout documented here;
differences between *those* are additive at the type/feature level (new data types, encryption
schemes), not the page offsets. In LibRed this is reflected by `JetFormatBase` virtual members
with Jet 4/ACE defaults; a future `Jet3Format` overrides the ones that differ.

---

## Provenance

Verified against: `Northwind.accdb` (ACE 2007), a generated 200-column ACCDB (multi-page TDEF),
and a generated ~150 MB ACCDB (reference usage map). Cross-referenced with mdbtools
(`HACKING.md`, and its `table.c` / `data.c` / `index.c`) and Jackcess (`TableImpl`, `ColumnImpl`,
`IndexData`, `IndexCodes`) — consulted upstream, not vendored here. The LibRed test suite
(`test/LibRed.Core.Tests/`) pins these structures, including whole-database golden dumps.
Write-side structures (row insertion, order-preserving key encoding, leaf-entry layout) are
additionally cross-checked against Access's own engine via OLE DB: insert the same row through
LibRed and through Access, then confirm the row sets match and that Access seeks the
LibRed-written index entry.
