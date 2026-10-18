# Verification backlog — what the docs say is unverified, and how to settle it

Every statement in `docs/` that calls itself unverified, unmeasured, inferred, a guess, or `Unknown`, with
whether it can be settled, by what instrument, and a probe design. Swept 2026-10-17. Probes follow the existing
pattern: a test in `LibRed.Core.AccessTests` / `LibRed.Engine.AccessTests`, `[Collection(AceCollection.Name)]`,
working on a `TemporaryDatabase.CopyPath` copy (never the fixture — ACE writes to anything it opens), logging what
ACE did through `ITestOutputHelper` while it is being measured, then moved into the existing class for its subject
as an assertion once known. Page-0 pokes go through
`DatabaseDefinitionPage.ReadMasked`/`WriteMasked` (internal, visible to both AccessTests projects).

Items marked ✔ were re-checked by reading the cited code/test; the rest are as the sweep reported them.

## Settled 2026-10-18

| item | result | where it is pinned |
|---|---|---|
| Pass-through storage (system-catalog) | Operation row `Flag 8`: connect string in `Name1`, SQL in `Expression`; **no `0x04` row** (the doc had it there); start row `Flag 0`; `MSysObjects.Flags` `0x70`. **LibRed now reads the connect string and the SQL** (it read only the kind, and took the SQL for an IN clause it then dropped); it still runs none and writes none. | `StoredQueryKindAccessTests.A_dao_written_pass_through_reads_back_its_connect_string_and_sql` |
| Extended flag `0x20` (page-02b) | Marks a table's hidden `VersionHistory_<GUID>` complex column and nothing else. **LibRed now reads it** (`ColumnDef.IsVersionHistory`) and finds a table's version history by it, not by its template's name; it preserves the bit and writes no version history. | `AppendOnlyDropAccessTests` (drops find the history by the bit) |
| Blocked attachment types (system-catalog) | **The engine refuses them through DAO** (`0x800C000E`, any case) — a wide list, inferred to be Windows' dangerous file types. **LibRed now refuses the measured set** under `enforceAccessLimits`. | `ComplexColumnTests.Attachment_types_ace_refuses_are_refused` |
| Type-name spellings (data-types) | Three-word names already parsed and mapped as ACE maps them. **LibRed bug, fixed:** `NATIONAL TEXT` (bare Memo, sized variable Text) was refused. The unmapped names are refused by both. | `TypeAliasTests.Alias_maps_to_ace_storage` |
| DefaultValue/Required without the DDL flag (system-catalog) | **LibRed bug, fixed:** its reader applied them whatever the entry flag; ACE ignores them at `0x00`. | `PropertyBlobTests.Default_and_required_without_the_ddl_flag_are_not_read` |
| Retiring a long value with no free map (long-values) | **LibRed bug, fixed:** an emptied single-page value's page stays owned and allocated in ACE (typed `0x09`, never reused); LibRed cleared the owned bit and freed it. Chained values already matched. | `LongValueNoFreeMapAccessTests` (ACE and LibRed twins byte-identical past page 0) |
| Error envelope (page-02b) | 39 bytes, not 38: the column's declared length, zero after the status, length 0 — every value result type, errors 94 and 13. A Text/Memo result caches none: ACE refuses the insert (*"Error evaluating CHECK constraint."*). LibRed reads by status and refuses every such write. No code change. | measured once (probe not kept) |
| Function list (functions.md) | All 138 functions of the VBA 7.1 type library called through ACE and LibRed. **LibRed gaps, fixed:** `CVDate` (= `CDate`), `IsEmpty`/`IsMissing`/`IsObject` (always False), `IMEStatus()` (0, Long). ACE's `CDec` takes exactly two arguments and gives a Long 0 (LibRed's one-argument `CDec` stays an extension); `Array()` crashes the provider; `MsgBox`/`InputBox` are refused. | `AddedFunctionsTests` |
| Calculated-column index (page-02e) | Same for every value result type. **Doc corrected:** the trap is an empty table only — once rows exist ACE refuses the `CREATE INDEX` itself (DAO- and LibRed-created columns alike). A Memo result is refused at `CREATE INDEX`. LibRed refuses every route; no code change. | measured once (probe not kept) |
| Irish 1084 (btree) | In a fresh DAO-only process ACE creates the database, tables and indexes, then refuses every row insert — indexed text, unindexed text, Long-indexed — with *"Selected collating sequence not supported by the operating system"* (and once the process has indexed rows in another order, refuses even the create). No keys exist to match; LibRed stays refusing. Czech control departed (35), harness check clean. | measured once (probe not kept) |
| Changing a saved expression (page-02e) | Access's own `CurrentDb` refuses `Fields("C").Properties("Expression") = …` with *"Cannot define field more than once."*; LvProp and cached values untouched. Only the designer UI remains, which no automation reaches. LibRed's out-of-scope decision stands; no code change. | measured once (probe not kept) |
| Single-page/chain boundary (long-values) | A constant: 3816 single-page, 3817 chained, in a bare row, beside a full `TEXT(255)`, beside a second 3000-byte long value, and with a value already on the LVAL page. LibRed's constant already matches; why 3816 stays unexplained. | measured once (probe not kept) |
| Page 0 `0x6A` (page-00) | A per-engine constant: 32-bit `DAO.DBEngine.36` over `msjet40.dll` 10.0.28000 stamps 9801, ACE 16.0.20430 stamps 4518 — neither is the running DLL's version. LibRed writes both already; that they are build numbers stays inferred. | measured once (probe not kept) |
| Property flag `0x01` protection (system-catalog) | In a secured workgroup (a copy of System.mdw, user `u1` in Users, table permissions without `dbSecWriteDef`), `u1` changes and deletes a `0x00` column property but is refused on a `0x01` one, as on adding a field. LibRed has no user-level security; doc only. | measured once (probe not kept) |
| Reserved-field poke ladder (§3) | **Rejected** (write zero): page 0 `0x02`; page 0 `0x15` = `0x05` (newer version); column `0x03` — the high half of the 32-bit minimum build at `0x01`. **Ignored, zeroed on a TDEF rewrite:** TDEF `0x20`, index stats `+0x08`, index-data `0x2A`/`0x30`, index-info `0x18`, extended `0x80` alone. **Ignored, zeroed on a recompute:** envelope `0x04` and tail. **Ignored, rewritten by a compact:** page 0 `0x16`, `0x98`, index page `0x08`. **Kept through everything:** extended `0x02`. **Behaviour:** column `0x11` bit `0x01` = write-once (INSERT sets, UPDATE refused; what creates one is open — not DAO, not ADOX); a design change keeps only that bit (Jet 4.0 zeroes all four bytes). Extended `0x40` = calculated, `0x80` = its expression may be evaluated (without it every read fails; a value result is still written, a Text/Memo one refused). Column flag `0x08` = a compact drops that column, those after it and every row. **LibRed bugs, fixed:** it took `0x80` alone for calculated, carried all of `0x11` over a rewrite, and read the calculated result on an update or delete — LibRed now matches ACE on all of it, write-once UPDATE refusal included. | `ColumnDescriptorFlagTests`, `CalculatedEvaluateBitTests` |
| `0x659`/`0x783` markers under Jet 4.0 (page-02a) | Jet 4.0 (32-bit `Microsoft.Jet.OLEDB.4.0`) writes the same markers and bounds header `0x0C` and column `+1` at its own build, **9801** (= its page 0 `0x6A`), as ACE does at 4518. Unlike ACE it also checks the index-info and index-data markers, once a write reaches the index. **Doc corrected:** ACE does not keep a zero marker on a rewrite — both engines write the markers afresh on any design change. **LibRed bug, fixed:** its rewrite carried them over. | `ColumnDescriptorFlagTests.A_design_change_writes_the_record_markers_afresh` |
| Page 0 `0x15` minor byte (page-00) | Signed, validated on open: `0x00` only at versions `0x01`/`0x02` ("Unrecognized database format"); ≤ `0x02` at `0x03` ("requires a newer version"); anything at `0x05`/`0x06`. Kept by writes; a raise writes `0x00`; a compact writes `0x01` at `0x03`, `0x00` elsewhere, refusing `0x02`–`0x7F` at `0x03`/`0x05`. Oddity, unexplained: at `0x05` a negative minor goes to `0x00` when a `BIGINT` table is created, while at `0x06` a `DATETIME2` one leaves it. **LibRed gap, fixed:** it never read the byte; `Detect` now refuses what ACE refuses. Purpose still unknown. | `VersionByteDetectTests.The_minor_version_byte_is_validated_as_ace_validates_it` |
| 20 bytes between 4060 and 4080 (page-01) | **Not row-lock bookkeeping, not a page reserve.** ACE packs pages to 2–12 bytes free. A 4062–4080-byte record gives up no value, its key included ("another user … same data"), under page and row locking, shared, exclusive and read-only alike: scan, seek, `UPDATE` and a `DELETE … WHERE` reading a column fail; `COUNT(*)` and a no-`WHERE` `DELETE` work, as do the table's other rows. Jet 4.0 (32-bit, `Microsoft.Jet.OLEDB.4.0`) on a `.mdb`: the same 4060 cap and the same refusals. A `DELETE` by key splits by format: refused on `.accdb` (ACE- or LibRed-created table); on `.mdb`, ACE and Jet 4.0 remove the row when the index it is found through is the table's only one (a primary key alone, or a plain index alone), and refuse it ("Cannot open database ''") when a second index needs a key from the record. **LibRed gap, fixed:** it capped writes at 4060 but read such a record; it now refuses every value read from one. **Open:** LibRed also refuses the key delete on a `.mdb`. | `RecordSizeAccessTests.A_record_past_the_cap_is_counted_and_truncated_but_never_read` |
| Bitmap page `[2..3]` (page-05) | ACE neither reads nor rewrites them: written zero on a new bitmap page only, a poked value survives a table's free and owned maps and the global free map taking and returning pages, a drop retiring a reference-form map, and the close-time merge of the released map; a compact rebuilds the map. **LibRed bug, fixed:** it refused such a page as not a bitmap; every LibRed path already wrote only the bits past the header. | `BitmapPageHeaderAccessTests` |
| Type codes `0x0D`/`0x0E` (data-types) | In no corpus file. Patched in, ACE opens the table but refuses the column's value, predicates and writes; DAO refuses the table. LibRed matches on writes (Null accepted, a value refused) and reads raw bytes by decision. No code change. | measured once (probe not kept) |

## Settled 2026-10-17

| item | result | where it is pinned |
|---|---|---|
| Hidden `.r` name past `Z` (page-02d) | **LibRed bug, fixed.** ACE writes index_num in base 16 with digits `A`–`P`, least significant first (`.rP` = 15, `.rAB` = 16, `.rOB` = 30). LibRed was wrong from 16 (`.rQ`…, then random hex). | `ForeignKeyLinkageTests.Incoming_blocks_past_fifteen_take_two_letter_names_as_ace_writes_them` (ACE's names and order for 1–30) |
| ComplexID width (page-02b) | Full Int32: id 300 → `2C 01 00 00`, 70000 → `70 11 01 00`; ACE and LibRed resolve both. No code change. | `ComplexColumnCreationAccessTests.A_complex_id_past_one_byte_reads_as_dao_assigned_it` |
| `0x659`/`0x783` markers (page-02a) | The header `0x0C` and column `+1` ones are a **minimum engine build**: 0 … 4518 accepted, 4519+ refused (*"requires a newer version of the … engine"*), bound to the engine's own build, not page 0's `0x6A`. ACE does not read the index-data and index-info ones at all (Jet 4.0 does — see 2026-10-18). | measured once (no LibRed behaviour to pin) |
| CHECK placement (page-00) | LibRed = ACE byte for byte, CHECK before/after a table-level UNIQUE and before a table-level PK. | `WholeFileParityProbeTest` #356–358 |
| `CREATE TABLE` on a reused TDEF page (page-02a) | ACE writes it onto a wholly zeroed page; LibRed byte-identical. | `WholeFileParityProbeTest` #359–361 |
| Property flag `0x01` classification (system-catalog) | = DAO's `DDL` argument: built-in ValidationRule/Text, DefaultValue, Required, AllowZeroLength → `0x01`; `CreateProperty` with DDL default (Caption, Description) → `0x00`. Its effect and its protection are settled separately. | measured once (probe not kept) |
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
| A map's start past page 512 (page-05) | **LibRed bug, fixed.** An inline map that marks no page, asked for one outside its window, is re-based on that page rounded down to 8 — data, index and long-value, owned and free alike. A free map's window "slide" is that rule applied to a map just emptied, so it lands on the tail's byte, not a 512 boundary (600 → 1112, 1152 → 1672). LibRed grew the owned map from 0 and slid free windows to 512 boundaries. | `WholeFileParityProbeTest` #469–473 |
| Owned-map conversion point (page-05) | **LibRed bug, fixed.** A grown record must leave its holder 4 bytes free, as the global map's must: a key-less table converts at page 32,000 (4005 bytes inline, not 4009), and a key only shrinks the budget by its index map (1,785 bytes → page 17,696). LibRed converted only when the record no longer fitted at all. Both measured to the row against ACE; LibRed now converts at the same row. | measured once (probe not kept) |
| New global bitmap past held pages (page-05) | The range's own first page, as with the file ending at the range: a table's group holding 65465–65471 while a long value in the same statement opened range 2 put its bitmap at 65472. LibRed byte-for-byte the same placement throughout (bitmap, long value, row, owned-map bitmap, file length). No code change. | `PageGroupProbeTest.Controlled_free_page_selection` `ref-held8` / `heap-then-long` |
| A fresh data page and the free map (page-05) | **LibRed bug, fixed.** ACE marks a newly allocated data page free only if its first row leaves room for another as large: a 2019-byte first row (2061 left) is marked, a 2059-byte one (2021 left) is not, and a table of one-page rows keeps an empty free map. LibRed marked every new page free, which on a reference-form free map also cost a bitmap page ACE never allocates. Found while probing page-05:540. | `WholeFileParityProbeTest` Half*, `WideTableUsageMapTests` |
| Free window above the marked page (page-05) | **LibRed bug, fixed.** ACE converts the free-pages map to a reference map with a fresh bitmap page; LibRed widened the inline record. | `WholeFileParityProbeTest` #465–468 |

**New open items found on the way:**

- An unordered cross join returns rows in a different order — ACE ran `SELECT … FROM Tens AS a, Tens AS b WHERE
  a.D < 5` with `b` as the outer loop and the filtered `a` inner; LibRed iterates `a` outer. Only visible through
  `INSERT … SELECT` row placement without `ORDER BY`. One data point; the rule ACE follows is unmeasured.
- A single `INSERT … SELECT` of 30,500 one-page rows into a keyed table leaves ACE's index map at 3445 bytes and
  LibRed's at 3597: their index pages land differently over that long a statement (14,000 rows still agree). The
  continuous-session placement drift of page-05 at a larger scale; it moves the owned map's conversion point.

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

Nothing open.

## 2. Engine/catalog surface — ACE answers in one statement each

| where | claim | instrument | probe |
|---|---|---|---|
| page-02c:151 | Create-time DEFAULT validation (LibRed rejects at INSERT). | measured, not yet implemented | ACE at CREATE / ADD COLUMN: `[A]` → "The database engine does not recognize either the field 'A' in a validation expression, or the default value in the table 'T'."; bare `A` into a LONG → "Type mismatch"; `Sum(1)`, `Count(*)`, `Foo()` (and `DCount(…)` in ADD COLUMN) → "Unknown function 'Sum' in validation expression or default value on 'T.C'."; a subquery, and `DCount('*','X')` in CREATE (its comma, inferred), → "Syntax error in CREATE/ALTER TABLE statement." Needs the evaluator to answer "is this function known" without running it. |
| transactions:347 | `.laccdb` lifecycle (create on first open, delete on last close). | ACE OLE DB | `File.Exists` before/during/after two overlapping connections, and after a killed child process. |

## 3. Reserved / undecoded fields — one parameterised poke probe

Every other field was run 2026-10-18 (results in the settled table). Open: page types `0x0106`/`0x0107`, which
occur in no corpus file.

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
| page-05:316 | A second handle and another handle's past-EOF reservation; multi-user allocation. | Two processes (ACE shares one engine per process). Deferred with multi-writer. |
| page-00:355; transactions:55, 154, 323 | The two commit-slot values; page-0 byte-range lock; lock bands, poll interval, `.ldb` record shape. | Frida on `NtWriteFile`/`NtLockFile`, ProcMon for lock offsets (user-run). `.ldb` shape: share-read the lock file while ACE holds it. |
| page-00:457 | Creating under a non-default workgroup (Access desktop's open of a default-workgroup file is now verified). | DAO `SystemDB` + `CreateUser` on a copied System.mdw, then `Access.Application` under that user. OLE DB does not check SIDs. |
| page-01:160 | What writes overflow slots wider than 4 bytes (`MSysAccessStorage`). | `Access.Application`: save/resize forms and modules; scan for live `0x4000` slots > 4 bytes. |
| page-05:265 | A non-empty released map = a release cut off before close (inferred). | Child process `DROP TABLE` then `TerminateProcess`; read the map; reopen with ACE. |
| system-catalog:120 | Entry flag `0x80` (only ever on a query's `ReplicableBool`) comes from replication or an `.mdb` conversion (inferred). | DAO and Access's `CurrentDb` both write it `0x00`. Needs a replicated Jet 4 `.mdb` converted to `.accdb` — 32-bit `DAO.DBEngine.36` replication. LibRed preserves the byte. |
| system-catalog:622, 635, 640 | The `Lv` query image (sort-order array, join layout, field meanings). | ACE stored queries varied one feature at a time; low value (LibRed never writes `Lv`). |
| system-catalog:902–915 | `MSysNameMap` Id, record kind, GUID slot. | `Access.Application` with Name AutoCorrect: create/rename/drop; diff. LibRed preserves the bytes. |
| btree:555, 685 | The kana constant `02 80 FF 80`; per-locale sub-position bytes. | `LCMapStringEx(LCMAP_SORTKEY)` alignment / NLS tables. No correctness value while the literal matches. |
| btree:20; README:131; appendix:36, 42, 225 | Jet 3 layouts (mdbtools only). | A real Access 97 file — the user has one (~200 MB) — or 32-bit `DAO.DBEngine.36` `dbVersion30`. |

## 5. Not verifiable — leave as stated

Japanese/Korean Unicode sorts (ACE refuses to open them on current Windows); internal rationales with no
observable — `CDbl` whitelisted as the evaluator's native type, the constants `39`/`509`/`510`, the 2-byte
fixed-region floor's intent, "low byte" vs `PRIMARYLANGID` (no assignable LANGID separates them), the `DBTIMESTAMP`
month bug's cause, the `WCHAR[64]` name limit.
