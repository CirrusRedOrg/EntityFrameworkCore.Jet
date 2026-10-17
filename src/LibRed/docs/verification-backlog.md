# Verification backlog — what the docs say is unverified, and how to settle it

Every statement in `docs/` that calls itself unverified, unmeasured, inferred, a guess, or `Unknown`, with
whether it can be settled, by what instrument, and a probe design. Swept 2026-10-17. Probes follow the existing
pattern: a test in `LibRed.Core.AccessTests` / `LibRed.Engine.AccessTests`, `[Collection(AceCollection.Name)]`,
working on a `TemporaryDatabase.CopyPath` copy (never the fixture — ACE writes to anything it opens), logging what
ACE did through `ITestOutputHelper` while it is being measured, then moved into the existing class for its subject
as an assertion once known. Page-0 pokes go through
`DatabaseDefinitionPage.ReadMasked`/`WriteMasked` (internal, visible to both AccessTests projects).

Items marked ✔ were re-checked by reading the cited code/test; the rest are as the sweep reported them.

## Settled 2026-10-17

| item | result | where it is pinned |
|---|---|---|
| Hidden `.r` name past `Z` (page-02d) | **LibRed bug, fixed.** ACE writes index_num in base 16 with digits `A`–`P`, least significant first (`.rP` = 15, `.rAB` = 16, `.rOB` = 30). LibRed was wrong from 16 (`.rQ`…, then random hex). | `ForeignKeyLinkageTests.Incoming_blocks_past_fifteen_take_two_letter_names_as_ace_writes_them` (ACE's names and order for 1–30) |
| ComplexID width (page-02b) | Full Int32: id 300 → `2C 01 00 00`, 70000 → `70 11 01 00`; ACE and LibRed resolve both. No code change. | `ComplexColumnCreationAccessTests.A_complex_id_past_one_byte_reads_as_dao_assigned_it` |
| `0x659`/`0x783` markers (page-02a) | The header `0x0C` and column `+1` ones are a **minimum engine build**: 0 … 4518 accepted, 4519+ refused (*"requires a newer version of the … engine"*), bound to the engine's own build, not page 0's `0x6A`. The index-data and index-info ones are not read at all. | measured once (no LibRed behaviour to pin) |
| CHECK placement (page-00) | LibRed = ACE byte for byte, CHECK before/after a table-level UNIQUE and before a table-level PK. | `WholeFileParityProbeTest` #356–358 |
| `CREATE TABLE` on a reused TDEF page (page-02a) | ACE writes it onto a wholly zeroed page; LibRed byte-identical. | `WholeFileParityProbeTest` #359–361 |
| Property flag `0x01` classification (system-catalog) | = DAO's `DDL` argument: built-in ValidationRule/Text, DefaultValue, Required, AllowZeroLength → `0x01`; `CreateProperty` with DDL default (Caption, Description) → `0x00`. Protection/recognition halves still open (§1). | measured once (probe not kept) |
| Index-owned LvProp block (system-catalog, appendix) | Type `0x0002`, owner = the index's name. | measured once (probe not kept) |
| Random backfill into packed leaves (btree) | LibRed = ACE byte for byte. | `WholeFileParityProbeTest` #373–376 |
| No-WHERE DELETE of a compressed root (page-09) | ACE resets the count to 0 (9 → 0); LibRed matches. | `WholeFileParityProbeTest` #380 |
| Leaf prefix over equal keys (found by the above) | **LibRed bug, fixed.** `IndexTree.Share` measured the prefix over keys only; ACE's runs on into the row pointers' shared bytes (500 rows `'same'`: 9, LibRed wrote 7). | `WholeFileParityProbeTest` #379 |
| Flat-table flags (page-02b) | `0x08` on `_<col>` (owner link) is load-bearing — cleared, DAO cannot open the owner table and ACE loses `P.X`; `0x04` on `<table>_<col>` (value id) is not read and not restored. | measured once (probe not kept) |
| Calculated column vs the 4060 cap (page-02e) | A variable column: 2 bytes declared (4020 opens, 4021 refused), its 27-byte Long envelope counted at insert (3993 fits, 3994 too large). LibRed agrees at both. DAO itself writes the over-cap file. | `ColumnWidthLimitAccessTests.A_calculated_column_is_held_to_the_record_cap_as_ace_holds_it` |
| No-WHERE DELETE route (page-09) | Unenforced parent: one go (LibRed agrees). **Complex column: one go, and the flat table's rows are left orphaned** — through DAO as through ACE's OLE DB. **LibRed changed to match** (it deleted row by row): byte-identical to DAO. | `ComplexDeleteByteParityProbeTest.A_complex_delete_with_no_where_leaves_the_bytes_dao_leaves` |
| Property flag `0x01` effect (system-catalog) | ACE ignores a column's `DefaultValue` and `Required` with the bit cleared; `CheckConstraints` is enforced regardless. | measured once (no LibRed behaviour to pin yet — see the open item below) |
| Compressed long value at the page boundary (long-values) | The directory entry counts (1138 shares, 1139 does not). **LibRed bug, fixed:** ACE decides free-pages-map membership on the room the *uncompressed* bytes leave (1009 stays, 1010 leaves); LibRed used the compressed row. | `WholeFileParityProbeTest` Cmp* |
| Kept page on a full row-by-row delete (page-09) | The lowest owned page, not the creation page (created 361, keeps 310). LibRed agrees. | `WholeFileParityProbeTest` #458–464 |
| Free window above the marked page (page-05) | **LibRed bug, fixed.** ACE converts the free-pages map to a reference map with a fresh bitmap page; LibRed widened the inline record. | `WholeFileParityProbeTest` #465–468 |

**New open items found on the way:**

- An unordered cross join returns rows in a different order — ACE ran `SELECT … FROM Tens AS a, Tens AS b WHERE
  a.D < 5` with `b` as the outer loop and the filtered `a` inner; LibRed iterates `a` outer. Only visible through
  `INSERT … SELECT` row placement without `ORDER BY`. One data point; the rule ACE follows is unmeasured.
- **A new table whose first data page is above 512 starts its maps differently.** Its first page 1146: ACE writes
  both the owned and the free record at 69 bytes from `startPage` 1144 (the page rounded down to 8); LibRed starts
  the owned map at 0 and grows it to 153 bytes, and the free window at 1024. Below page 512 the two agree, which is
  why the corpus never showed it. The doc's measured window *slides* land on multiples of 512, so a fresh map and a
  sliding one are placed by different rules — measure both before changing `UsageMap`.
- LibRed's reader applies `DefaultValue`/`Required` whatever the entry flag; ACE ignores them at `0x00`. No writer
  seen produces that, so low priority.

## 0. Stale — fixed 2026-10-17

Every item corrected in place, each re-checked against its code or test first. Two needed a measurement rather
than an edit: Access desktop's open of a LibRed-created file (page-00 §2.3 — measured once through `Access.Application`)
and the data-definition stored query (system-catalog Flag 7 — DAO stores Flag 7 and LibRed reads it back, a new
row in `StoredQueryKindAccessTests`). The rest: page-01 row-reader gap (= record slack), btree `0x1A` height
(verified to 2) and Binary DESC keys (verified), functions.md's missing `FunctionWhitelist*Probe` (reworded; the
sweep is a §2 item), page-02b's error-envelope read (LibRed refuses, not Null), appendix TDEF `0x20`
(uninitialised), system-catalog's complex-column resolution (`ReadComplexValues`), index-key-checksum's test
location, and four mdbtools-TODO entries (`0x1C`, v1 collation, the index header offsets, the LVAL descriptor).

## 1. Affects what LibRed writes — probe first

| where | claim | instrument | probe |
|---|---|---|---|
| page-05:540 | New global bitmap page placement when held pages lie between EOF and the range. | ACE OLE DB | `PageGroupProbeTest` `ref-*` fixture with EOF below 65472 and a reservation up to the range edge; force an allocation in range 1 before close; read slot 1's pointer. LibRed takes the range's first page. |
| page-05:137 | Owned-map conversion point with a PK (ACE converts before 31,409; model says 31,456). | ACE OLE DB | Fill a keyed table in batches, copying the file after each; log owned-record type/length and index-map length; bisect the flip against `4096−14−6−69−indexMap`. |
| long-values:275 | Retiring a value from a column with an owned map but no free map — not measured against ACE. | ACE OLE DB + patch | Patch a table's §3.3.2 free pointer to 0; insert >64-byte values through LibRed; delete one through ACE and one through LibRed on twins; diff owned map, page type, global free bit. |
| system-catalog:114 | A set `0x01` makes a property definition-protected (its effect on DEFAULT/Required/CHECK is settled above). | DAO with a secured workgroup | Under a secured `.mdw`, try deleting a `0x01` vs a `0x00` property as a user without `dbSecWriteDef`. |

page-05:540 needs a file near 65,472 pages (~268 MB) and page-05:137 one near 31,400 (~128 MB) with a bisection
over batches — both background-run material. long-values:275 needs the §3.3.2 free pointer patched to 0.

## 2. Engine/catalog surface — ACE answers in one statement each

| where | claim | instrument | probe |
|---|---|---|---|
| functions.md:57 | Function list "not proven exhaustive". | ACE OLE DB | Every member of the VBA type library modules → `SELECT fn(<args>) FROM MSysObjects WHERE 1=0`; bucket "Undefined function" vs anything else; diff with `ExpressionEvaluator`'s dispatch. Commit as the missing `FunctionWhitelist*ProbeTest`. |
| system-catalog:404, 436 | Pass-through = Flag 8, connect string in the `0x04` row (from published tables). | DAO | QueryDef with `.Connect = "ODBC;DSN=none;"`, `.ReturnsRecords`, `.SQL = "SELECT 1"`; append (no live DSN needed); read rows and `MSysObjects.Flags` (expect `0x70`). |
| data-types:322 | Three-word type names (`NATIONAL CHARACTER VARYING`) unparsed; `HYPERLINK`/`XML`/`SQL_VARIANT`/`VARIANT`/`COMP` rejected "as ACE does" (not marked verified). | ACE OLE DB | One `CREATE TABLE` per spelling; record accept + resulting descriptor, or ACE's refusal text; compare. |
| page-02c:151 | Create-time DEFAULT validation messages (LibRed rejects at INSERT). | ACE OLE DB | `CREATE TABLE` with `DEFAULT [A]`, `Sum(1)`, `DCount('*','X')`, `Foo()`, a subquery; capture ACE's message for each. |
| system-catalog:1141 | Does the engine refuse Access's blocked attachment extensions through DAO? | DAO | `Recordset2` on an attachment column; `LoadFromFile` with `x.exe` then `x.txt`; record refusal + error. |
| page-02b:41 | Version-history column extended flag `0x20` (mdbtools). | DAO, existing fixture | Run `AppendOnlyDropAccessTests.Build`; dump `0x10` on the `VersionHistory_…` column, the `MSysComplexTypeVH_` template and flat-table columns. |
| page-02b:180 | Error envelope is 38 bytes vs 23 for Null; the extra 15 undescribed. | DAO, existing fixture | From `Records_what_ace_stores_for_a_conversion_over_null`: diff error-94 vs error-13 envelopes under Text and Long result types; decode the length field and the 15 bytes. |
| data-types:28 | Type codes `0x0D`/`0x0E` never observed. | corpus + patch | Scan corpus TDEFs; patch a Binary column's type to `0x0D`; ACE open/select, DAO `Field.Type`. |
| transactions:347 | `.laccdb` lifecycle (create on first open, delete on last close). | ACE OLE DB | `File.Exists` before/during/after two overlapping connections, and after a killed child process. |
| btree:913 | Irish 1084 unmeasured — ACE OLE DB refuses it in-process. | DAO only, **child process** | Fresh process doing nothing else first: DAO creates the db, an indexed `TEXT`, the Latin blocks + 193 samples; LibRed reads `RawEntries()` and diffs against General v0. The survey notes the refusal depends on what the process did first — confirm the child gets past `CREATE INDEX`. |
| page-02e:322 | Calculated-index trap measured for Int16/Int32 only. | DAO + ACE | Text/Double/Date/Memo calculated columns; `CREATE INDEX` + `INSERT` through ACE. Low value — LibRed refuses every route. |
| page-02e:269 | Access's designer changes an expression by a path DAO lacks (inferred). | Access.Application | Inside Access: `CurrentDb.TableDefs("T").Fields("C").Properties("Expression") = "[Qty]*3"`; diff LvProp and row envelopes — does Access recompute rows? |

## 3. Reserved / undecoded fields — one parameterised poke probe

Fields: page 0 `0x02`, `0x15` (minor — purpose), `0x16`, `0x98` (also 0 and `0x655`); bitmap page `[2..3]`; TDEF
`0x20`; column descriptor `0x03`, `0x11`, flag `0x0F` bit `0x08`, extended `0x02`/`0x20`/one of `0x40`/`0x80` alone;
calculated envelope `0x04` (12) and its 3-byte tail; index stats `+0x08`; index-data `0x2A`, `0x30`; index-info
`0x18`; index page `0x08`; page types `0x0106`/`0x0107` (corpus scan first).

**Probe.** One DAO-built fixture (PK, secondary index, FK, memo, calculated column). Per field, a copy with a
distinctive pattern (`A5 5A …`, or the single bit for flags) in that field only. Each copy runs one ladder: ACE open
→ `SELECT *` → indexed seek → `INSERT` → `ALTER TABLE ADD COLUMN` (forces a TDEF rewrite) → `UPDATE` of a calculated
column's input (rewrites the envelope) → DAO `CompactDatabase`; re-read the bytes after each step. Classify:

- **rejected** — ACE validates it → writers must emit zero;
- **kept** — ignored → pass through verbatim (the faithful round-trip rule);
- **zeroed on rewrite/compact** — ACE regenerates it → zero is a valid write;
- **behaviour changes** (results, OLE DB `COLUMN_FLAGS`, DAO `Attributes`) → a meaning to document.

## 4. Partly verifiable — needs a second process, Access automation, or an external tool

| where | claim | what it takes |
|---|---|---|
| page-05:532 | A second handle and another handle's past-EOF reservation; multi-user allocation. | Two processes (ACE shares one engine per process). Deferred with multi-writer. |
| page-05:486, 511 | ACE's current-group/cursor choice over a long session (LibRed drifts from statement 118). | Extend `Index_groups_observed_inside_Access_statement` to snapshot the global map after every split; align with LibRed's trace. |
| page-00:355; transactions:55, 154, 323 | The two commit-slot values; page-0 byte-range lock; lock bands, poll interval, `.ldb` record shape. | Frida on `NtWriteFile`/`NtLockFile`, ProcMon for lock offsets (user-run). `.ldb` shape: share-read the lock file while ACE holds it. |
| page-00:457 | Creating under a non-default workgroup (Access desktop's open of a default-workgroup file is now verified). | DAO `SystemDB` + `CreateUser` on a copied System.mdw, then `Access.Application` under that user. OLE DB does not check SIDs. |
| page-00:201 | `0x6A` is the creating engine's build (inferred). | 32-bit process with `DAO.DBEngine.36`: create an `.mdb`, compare with `msjet40.dll`'s `FileVersionInfo`. |
| page-00:14 | `0x15` minor byte's purpose. | In the §3 ladder; plus whether it changes how ACE raises the version. |
| page-01:95 | 20 bytes between 4060 and 4080 — row-lock bookkeeping? | LibRed writes 4061/4070/4080-byte rows; ACE reads them exclusive vs shared at locking modes 0/1. |
| page-01:160 | What writes overflow slots wider than 4 bytes (`MSysAccessStorage`). | `Access.Application`: save/resize forms and modules; scan for live `0x4000` slots > 4 bytes. |
| page-05:250 | A non-empty released map = a release cut off before close (inferred). | Child process `DROP TABLE` then `TerminateProcess`; read the map; reopen with ACE. |
| long-values:115 | Why the single-page/chain boundary is 3816. | Whether it moves with other long values in the row or LVAL tenants (a moving number = a budget). |
| system-catalog:120 | Entry flag `0x80` set by Access, by no DAO call. | Same property through `Access.Application`'s `CurrentDb` vs standalone DAO; diff flags; repeat for a saved query. |
| system-catalog:622, 635, 640 | The `Lv` query image (sort-order array, join layout, field meanings). | ACE stored queries varied one feature at a time; low value (LibRed never writes `Lv`). |
| system-catalog:902–915 | `MSysNameMap` Id, record kind, GUID slot. | `Access.Application` with Name AutoCorrect: create/rename/drop; diff. LibRed preserves the bytes. |
| btree:555, 685 | The kana constant `02 80 FF 80`; per-locale sub-position bytes. | `LCMapStringEx(LCMAP_SORTKEY)` alignment / NLS tables. No correctness value while the literal matches. |
| btree:20; README:131; appendix:36, 42, 225 | Jet 3 layouts (mdbtools only). | A real Access 97 file — the user has one (~200 MB) — or 32-bit `DAO.DBEngine.36` `dbVersion30`. |

## 5. Not verifiable — leave as stated

Japanese/Korean Unicode sorts (ACE refuses to open them on current Windows); internal rationales with no
observable — `CDbl` whitelisted as the evaluator's native type, the constants `39`/`509`/`510`, the 2-byte
fixed-region floor's intent, "low byte" vs `PRIMARYLANGID` (no assignable LANGID separates them), the `DBTIMESTAMP`
month bug's cause, the `WCHAR[64]` name limit.
