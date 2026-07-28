# LibRed production-code audit

Date: 2026-07-18

## Executive summary

The five LibRed production projects are promising but should still be treated as an
early-stage database engine when opening files or executing SQL supplied by a less
trusted party. The audit found no native-memory-safety defect and no Critical or High
security vulnerability after deployment-aware calibration. Managed span/array checks
usually turn malformed binary structures into exceptions rather than memory corruption.
The main risks are instead process availability, persistent database integrity, and
incomplete ADO.NET/EF Core semantics.

Twenty-six focused fixes have been implemented from the audit and its verification:

- an open `LibRedConnection` now rejects `ConnectionString` changes;
- `LibRedDataReader.GetBytes/GetChars` now validate ranges, return zero past the end,
  and `GetChars` no longer allocates a full `char[]` for every chunk;
- `JetFormatBase.Detect` now restores the caller's stream position in a `finally`;
- connection strings now use the framework parser, preserving quoted semicolons and
  applying explicit `Data Source` > `DataSource` > `DBQ` precedence;
- a failed `JetDatabase.Open` now disposes its acquired channel and cache lease;
- `HasTablesAsync` now honors an already-cancelled token before doing synchronous work;
- standalone catalog expressions now require EOF, so CHECK/default expression prefixes
  cannot silently ignore trailing tokens;
- every `ALTER COLUMN` type/length path now rejects both sides of a relationship before
  any descriptor, row, or index mutation;
- declared result types now flow from catalog/schema and expression planning into the
  ADO.NET reader, including empty results, first-row nulls, derived tables, and aggregates;
- native database creation now atomically refuses an existing path instead of truncating it;
- page-0 detection now rejects identifier/version mismatches and unsupported Jet 3 layouts;
- the verified `0x2000` delete-set-null relationship flag is synchronized into both format references;
- Office Standard descriptors now require supported exact key, salt, and verifier-hash sizes and compare the
  complete verifier in constant time;
- Agile discovery now obeys the page-0 descriptor frame and rejects anything outside the exact verified Access
  AES-256/SHA-512 profile before allocation, KDF, or cipher work;
- SQL DDL now rejects zero/negative Text/Binary sizes and invalid Decimal precision/scale before table creation;
- TDEF readers and writers now preflight count-derived geometry and narrowing fields before allocation or parsing;
- reference usage maps now require the exact 69-byte shape and validated in-file type-`0x05` bitmap targets;
- all active TDEF continuation consumers now share a declared-length-bounded, cycle-safe, page-validated reader;
- TDEF variable regions now use the declared-length boundary and validate names, column identities/types,
  variable indexes, and the complete terminated long-value usage-map list before publishing metadata;
- data-page slots and row trailers now have shared checked boundaries, and fixed-width scalar decoding
  rejects incorrect widths before primitive codecs are reached;
- all row-relocation consumers now share an exact-width, in-file, same-owner resolver that validates
  the hidden target's row and flag shape while retaining zero-copy index seeks;
- active index descent, leaf following, full-tree enumeration, and final page mutations now share checked
  page/entry geometry and owner/type validation; traversal detects cycles and the full cursor is iterative.
- long-value descriptors and chains now require exact framing, owned LVAL pages, progress, unique nodes,
  and strict declared-length completion before reads or reclamation;
- property blobs now use one exact checked representation for read/add/remove and preflight every narrowed
  serializer field while preserving unknown raw values;
- TDEF writing now uses exact encoded sizing and preflights verified name, index, and long-value-map domains;
- global allocation and LVAL append/reclamation validate physical targets, page roles, map geometry, and
  column ownership before mutation, while preserving ACE's contiguous next-page growth convention.

The highest-value next work is not 49 independent patches. It is five coherent efforts:

1. introduce checked, reusable parsers/traversers for TDEF, data-page, index, LVAL,
   usage-map, row, and property structures;
2. make statement and schema mutations atomic;
3. impose explicit budgets on SQL parsing/evaluation, view expansion, recursion,
   cancellation, and crypto descriptors;
4. repair query/DML correctness gaps, especially RIGHT JOIN, cascade cycles, and
   parameter lowering;
5. define cross-platform path identity and connection/file ownership semantics.

## Scope and method

The audit read every non-test source file in:

- `src/LibRed/LibRed.Core`
- `src/LibRed/LibRed.Engine`
- `src/LibRed/LibRed.Sql`
- `src/LibRed/LibRed.Ado`
- `src/LibRed/LibRed.EFCore`

Coverage was **134/134 files**: 45 files in audit group A, 45 in group B, and 44 in
group C. All receipts state full-file reads through EOF. Tests were excluded from the
review scope, but selected existing tests were inspected or run as supporting evidence.
Generated ANTLR sources were included; shard 0026 regenerated and compared them rather
than treating generated volume as unreviewed code.

The work combined full-file static review, cross-layer source-to-sink tracing, duplicate
reconciliation, format checks against `src/LibRed/docs/format/`, and attack-path severity
calibration for an embeddable library. It did **not** fuzz all binary structures, execute
malicious stack/OOM payloads, or dynamically reproduce findings unless explicitly stated.

Security discovery produced 53 raw candidates and 49 deduplicated candidates. Final
attack-path policy classified 42 as reportable, 2 as deferred, and 5 as ignored. These
are mostly sibling instances of fewer architectural root causes. Raw evidence remains in:

- `.codex-security-work/synthesis-a.md`
- `.codex-security-work/synthesis-b.md`
- `.codex-security-work/synthesis-c.md`
- `.codex-security-work/dedupe-report.md`
- the scan artifact `artifacts/05_findings/attack_path_analysis_report.md`

## Changes already implemented

| Change | Location | Result | Spec impact |
|---|---|---|---|
| Reject changing connection identity while open | `src/LibRed/LibRed.Ado/LibRedConnection.cs:24` | Prevents reported/live target divergence | None; API contract only |
| Safe chunked binary/text reads | `src/LibRed/LibRed.Ado/LibRedDataReader.cs:147-186` | Correct past-end/range behavior; removes repeated `ToCharArray()` allocation | None; ADO.NET contract only |
| Exception-safe stream-position restoration | `src/LibRed/LibRed.Core/Formats/JetFormatBase.cs:250-261` | `Detect` restores position even when header reading throws | Checked: no format-document edit is needed because no offset/layout/codec fact changed |
| Framework connection-string parsing | `src/LibRed/LibRed.Ado/LibRedConnection.cs:197-224` | Correctly handles quoted semicolons and gives aliases deterministic precedence | None; ADO.NET parsing only |
| Failed-open ownership cleanup | `src/LibRed/LibRed.Core/JetDatabase.cs:72-88` | Releases the file handle and shared cache lease when database initialization fails | Checked: lifecycle only; no format-document edit needed |
| Pre-cancellation for table discovery | `src/LibRed/LibRed.EFCore/Storage/Internal/LibRedDatabaseCreator.cs:54-59` | Avoids opening/scanning the database after cancellation was requested | None; EF Core async contract only |
| Complete standalone-expression parsing | `src/LibRed/LibRed.Sql/Grammar/AccessSql.g4`; `Parsing/AntlrSqlParser.cs` | DEFAULT/CHECK/validation text must be consumed completely; trailing tokens now raise `SqlParseException` | None; SQL parsing/enforcement only |
| Central relationship-column ALTER guard | `src/LibRed/LibRed.Core/Storage/TableCreator.cs` | Child FK and referenced-parent columns are rejected before specialized in-place or rebuild paths | Updated `docs/format/page-02b-columns.md`; no appendix change because layout is unchanged |
| Stable declared result metadata | `LibRed.Engine/Execution/ResultSet.cs`, `QueryExecutor.cs`, `StatementExecutor.cs`; `LibRed.Ado/LibRedDataReader.cs` | Empty/first-null result sets expose schema/expression CLR types instead of incorrectly reporting `Object` | None; execution/ADO.NET metadata only |
| Non-destructive native creation | `LibRed.Core/Storage/DatabaseCreator.cs` | Uses atomic create-new semantics; an existing database or other file is preserved and creation throws `IOException` | Checked: file lifecycle only; no byte-layout fact changed |
| Coupled page-0 format validation | `LibRed.Core/Formats/JetFormatBase.cs` | Rejects mismatched MDB/ACCDB/workgroup identifiers and version bytes; rejects Jet 3 until its distinct layouts are implemented | Updated `docs/format/page-00-database.md` and `appendix-structures.md` |
| Relationship flag documentation sync | `docs/format/system-catalog.md`; `appendix-structures.md` | Records the Access-verified `MSysRelationships.grbit` `0x2000` delete-set-null flag already implemented symmetrically | Documentation-only; authoritative references now match runtime behavior |
| Office Standard descriptor validation | `LibRed.Core/Crypto/OfficeStandardEncryption.cs` | Rejects unsupported/fractional key sizes, non-16-byte salts, and verifier hashes that are not exactly the selected digest; full verifier comparison is constant-time | Checked `docs/format/page-00-database.md`; existing binary descriptor layout and documented supported variants are unchanged |
| Framed, bounded Agile profile | `LibRed.Core/Crypto/AgileEncryption.cs` | Uses `len@0x299`/blob `0x29B`, ignores stray XML, and validates the exact Access AES-256/SHA-512/16-byte/100000-spin profile before expensive work | Updated `docs/format/page-00-database.md`; no appendix edit because offsets/layout are unchanged |
| Validated SQL type dimensions | `LibRed.Engine/Execution/AccessTypeMapper.cs` | Rejects invalid Text/Binary sizes and Decimal precision/scale before any table is created | Updated `docs/format/data-types.md`; no appendix edit because no field layout changed |
| TDEF count and serializer preflight | `LibRed.Core/Pages/TableDefinitionPage.cs`; `Catalog/TdefBuilder.cs` | Enforces table count limits and assembled-region bounds before count-sized allocation; rejects column/id/length/fixed-offset narrowing before serialization | Updated `docs/format/page-02a-tdef.md`; no appendix edit because existing fields and limits are unchanged |
| Reference usage-map geometry | `LibRed.Core/Storage/UsageMap.cs` | Enforces the exact 17-slot record, in-file pointers, and complete bitmap-page header before expanding bits into owned pages | Updated `docs/format/page-05-usage-maps.md`; no appendix edit because the documented record/header layout is unchanged |
| Shared bounded TDEF continuation reader | `LibRed.Core/Pages/TdefChainReader.cs`; consumers in `TableDefinitionPage.cs`, `IndexWriter.cs`, and `TableCreator.cs` | Rejects cycles, invalid pages/headers, chain-length mismatches, and oversized declared definitions before assembly; copies exactly the declared bytes | Updated `docs/format/page-02a-tdef.md`; the 1 MiB bound is explicitly an implementation safety budget, not a claimed field limit |
| Checked TDEF variable-region parsing | `LibRed.Core/Pages/TableDefinitionPage.cs` | Enforces the declared definition boundary; strictly decodes bounded names; rejects invalid/duplicate column ids, unknown types, invalid variable indexes, and malformed or mis-targeted LVAL map entries | Updated `docs/format/page-02a-tdef.md`; no appendix edit because the established fields, encodings, and limits are unchanged |
| Checked data-page and row boundaries | `LibRed.Core/Pages/DataPage.cs`; `Storage/RowLayout.cs`, `RowDecoder.cs`, `RowInserter.cs`; `Storage/Types/JetTypeCodec.cs` | Full scans and O(1) seeks share page/slot checks; row bitmap/trailer/offset/fixed regions and scalar widths fail deterministically without breaking old rows or zero-length relocation tombstones | Updated `docs/format/page-01-data-and-rows.md` and `data-types.md`; no appendix edit because no established layout changed |
| Shared checked row-relocation resolver | `LibRed.Core/Storage/RowRelocationReader.cs`; consumers in `Table.cs`, `TableCursor.cs`, and `RowInserter.cs` | Validates the live 4-byte source, in-file target page, matching TDEF owner, target row, and nonempty hidden-inline target before scans, seeks, or mutations expose bytes | Updated `docs/format/page-01-data-and-rows.md`; no appendix edit because the established pointer/flag layout is unchanged |
| Checked index-page access | `LibRed.Core/Storage/IndexPageReader.cs`; `IndexWriter.cs`; `IndexCursor.cs` | Validates page/type/owner/pointers, bitmask-derived entries and compression before traversal and final mutation; rejects descent/leaf/tree cycles; replaces recursive full-tree walking with ordered iteration | Updated `docs/format/page-03-04-index-btree.md`; no appendix edit because existing page and entry layouts are unchanged |
| Checked LVAL traversal and reclamation prevalidation | `LibRed.Core/Storage/LongValueReader.cs`; `RowInserter.cs` | Enforces exact descriptor framing, page role/owner, row geometry, progress, uniqueness, and declared-length completion; resolves and validates the complete owned chain before reclamation starts | Updated `docs/format/long-values.md`; no appendix edit because the established descriptor and chain layouts are unchanged |
| Checked property-blob parsing and serialization | `LibRed.Core/Catalog/PropertyBlob.cs` | One exact representation validates signatures, nested block/record/entry bounds, name indexes, observed flags, and narrowing fields for read/add/remove while preserving raw values | Updated `docs/format/system-catalog.md` and `appendix-structures.md` with the Access-verified flag domain |
| Exact TDEF writer sizing and domain preflight | `LibRed.Core/Catalog/TdefBuilder.cs`; `Pages/TdefChainReader.cs` | Computes the encoded definition size exactly and rejects invalid names, duplicate identities, index references, LVAL maps, narrowing fields, and output beyond the shared safety budget before writing | Updated `docs/format/page-02a-tdef.md`; no appendix edit because established fields and encodings are unchanged |
| Validated allocation and LVAL mutation targets | `LibRed.Core/Storage/PageAllocator.cs`; `LongValueWriter.cs`; `RowInserter.cs` | Checks global/reference/owned map geometry, physical target ranges, page roles/owners, row capacity, and declared-versus-actual free space; materializes the Access-observed exactly-next page contiguously | Updated `docs/format/page-05-usage-maps.md` and `long-values.md`; no appendix edit because no field layout changed |

These changes address localized defects only. They do not imply the larger parser,
transaction, or resource-budget findings are fixed.

## Prioritized correctness findings

Severity below describes product correctness/data-loss impact, not CVSS. Confidence is
based primarily on static control-flow or contract proof; items needing destructive or
process-isolated reproduction are called out.

### LibRed.Core: binary format, storage, and crypto

| Priority | Severity / confidence | Root cause and exact locations | Consequence / recommended action |
|---|---|---|---|
| Resolved | High / High dynamic | Three TDEF continuation readers followed unvalidated pointers until zero and appended whole page bodies without cycle or declared-length budgets: former loops in `TableDefinitionPage`, `IndexWriter`, and `TableCreator` | All three now use `TdefChainReader`: in-file unique pages, `[02 01]` headers, exact declared chain length/bytes, and a pre-allocation 1 MiB safety budget. Seven malformed-chain variants reject and real wide-table read/index/DDL controls pass. |
| Resolved | High / High dynamic | Active B-tree descent, leaf following, and the recursive full cursor lacked cycle/ownership/entry guards: `IndexWriter.cs`; `IndexCursor.cs` | All now use `IndexPageReader`; descent and leaf chains track visited pages, and the full cursor performs an iterative ordered walk. Twelve corrupt owner/type/pointer/entry/compression/cycle variants reject while real multi-level, split, usage-map, and Access controls pass. |
| Partially resolved: transaction redesign | Medium-High / High dynamic | LVAL reclamation formerly freed while following unvalidated pointers: `RowInserter.cs`; shared validation in `LongValueReader.cs` | The complete cycle-safe chain and column owned-pages map are now validated before the first free. Subsequent map/free writes still require the planned transaction/savepoint layer for atomic rollback on I/O failure. |
| P0 | High correctness / High static | Row insertion and other mutations publish multiple structures without statement atomicity: `RowInserter.cs:30-75`; `UsageMapWriter.cs:94-108,227-262`; `ViewCreator.cs:32-43,65-192`; `TableCreator.cs:443-468` | Late failures can leave heap, TDEF, index, usage-map, LVAL, query-object, or constraint metadata divergent. Require an internal transaction/savepoint or explicit compensation contract. |
| Resolved | Medium / High dynamic | Property blobs trusted nested sizes and narrowed lengths: `PropertyBlob.cs` | One exact parser now validates signatures, blocks, owner records, entries, name indexes, and values for read/add/remove; serializer preflight rejects every 16/32-bit overflow while raw values remain byte-faithful. |
| Resolved | Medium / High dynamic | Data-page slot, row trailer, scalar width, and relocation shape were consumed before validation: `DataPage.cs`; `RowLayout.cs`; `RowDecoder.cs`; `JetTypeCodec.cs`; relocation consumers in `Table.cs`, `TableCursor.cs`, and `RowInserter.cs` | Shared boundaries now validate page/slot geometry, row regions, scalar widths, and the complete live-source → same-owner hidden-target relocation shape. Corruption regressions reject deterministically while real LibRed- and Access-authored relocation controls pass. |
| Resolved | Medium / High dynamic | Allocator and LVAL append trusted hostile usage-map targets: `PageAllocator.cs`; `LongValueWriter.cs`; `RowInserter.cs` | Global map shape/pointers/free targets and LVAL page/row/free-space/map ownership are validated before mutation; the one-past-end ACE growth bit is materialized contiguously and larger gaps reject. |
| Resolved | Medium / High dynamic | TDEF counts, variable regions, and serializer casts were consumed before validating format limits or count-derived geometry: `TableDefinitionPage.cs`; `TdefBuilder.cs` | Reader geometry and variable regions are checked; writing now computes exact encoded size and preflights names, indexes, LVAL ids/pointers, duplicates, narrowing fields, and the shared 1 MiB budget before serialization. |
| Resolved | Medium / High dynamic | SQL DDL narrowed Text/Binary sizes and Decimal precision/scale without validating their domains: `AccessTypeMapper.cs` | Positive Text/Binary sizes and Decimal precision `1..28`, scale `0..precision`, are now enforced before table creation; ten invalid-shape and two valid-boundary regressions pass. Broader hostile TDEF parsing remains above. |
| Resolved | Medium / High dynamic | Office Standard trusted descriptor key/salt/verifier dimensions and accepted a zero-length verifier prefix: `OfficeStandardEncryption.cs` | Exact supported sizes are validated before allocation/crypto and the complete verifier is compared in constant time. Thirteen malformed cases now reject, while the valid Office variant sweep remains green. |
| Resolved | Medium / High dynamic | Agile searched page 0 for XML and trusted caller-controlled KDF/cipher dimensions: `AgileEncryption.cs` | Discovery now obeys the declared descriptor frame and the exact verified Access profile is validated before KDF/AES. Stray XML and three hostile-dimension regressions reject without expensive work; valid/wrong/missing-password controls pass. |
| P1 | Medium / High static | Password changes publish plaintext before replacement encryption succeeds: `DatabaseEncryption.cs:29-55,62-94,200-212` | A validation/I/O failure can leave an originally encrypted database plaintext. Use same-directory atomic replacement and failure-injection tests. |
| Resolved | Medium / High deterministic | Jet 3 was accepted while inheriting Jet 4/ACE offsets; identifier/version were independently allowlisted: `JetFormatBase.cs` | Detection now validates the pair before selecting a layout, and Jet 3 throws until implemented. Mismatch, Jet 3, valid Jet 4/workgroup, known ACE, and future-ACE regressions pass. |
| Resolved | Medium / High dynamic | Usage-map reference rows were expanded according to attacker-controlled row length and their pointers were not required to target bitmap pages: `UsageMap.cs` | Reference records now require exactly 17 slots/69 bytes; nonzero pointers must be in-file type-`0x05` pages with the complete header. Oversized-row and wrong-page-type regressions reject before expansion; exact empty and large real reference maps pass. |
| Resolved | Medium / High dynamic | LVAL reads lacked progress/visited/strict-completion checks: `LongValueReader.cs` | Descriptor, page type/owner, row, pointer, progress, cycle, and exact-length checks now reject malformed inline/single/chained values deterministically. |
| P2 | Medium / High static | Unique/primary index backfill publishes metadata without probing existing duplicates: `TableCreator.cs:443-468` | A database may claim a constraint that its data violates. Decide ACE null/IGNORE NULL semantics, validate first, publish second. |

### LibRed.Sql and LibRed.Engine

| Priority | Severity / confidence | Root cause and exact locations | Consequence / recommended action |
|---|---|---|---|
| P0 | High / High static | Multi-row DML interleaves validation and irreversible writes: `StatementExecutor.cs:237-287,895-929,951-958`; `QueryEngine.cs:72-98` | A later constraint/storage failure can commit a prefix. Make each statement atomic even without an explicit user transaction. |
| P0 | High / High static | Cascade deletion recurses before recording an in-progress/deleted row: `StatementExecutor.cs:218-266,942-958` | Cyclic relationships/rows can exhaust the stack; diamonds can double-mutate. Define ACE-compatible cycle semantics and use an explicit worklist/in-progress set. |
| P0 | High / High static | Stored view expansion has no active-name or work budget: `ViewExpander.cs:15-75`; source `JetCatalog.cs:180-221` | Cyclic or branching stored views can exhaust stack/CPU/memory. Carry one expansion context with cycle, depth, node, byte, and cancellation limits. |
| Resolved | High / High deterministic static | Standalone expression parsing did not require EOF: `AntlrSqlParser.cs`; `AccessSql.g4`; enforcement `StatementExecutor.cs:83-114,557-560` | Fixed with a dedicated `standaloneExpression : expression EOF` entry and regenerated parser artifacts. Parser and persisted-CHECK regressions prove trailing suffixes are rejected. |
| P1 | High potential / Medium pending sink test | Unmatched RIGHT JOIN mutation targets use `RowId(0,0)`: `StatementExecutor.cs:660-665,770-774,879-958`; `RowId.cs:4` | UPDATE/DELETE can address an absent physical row/page 0 or fail unexpectedly. Replace sentinel identity with explicit absence after disposable-copy validation. |
| P1 | Medium / High static | Non-hash RIGHT JOIN uses LEFT-only preservation: `QueryExecutor.cs:350-355,406-436`; `IndexSelection.cs:168-177` | Non-equi RIGHT JOIN silently drops unmatched right rows. Normalize carefully to swapped LEFT JOIN while preserving output column ordering. |
| P1 | Medium / High static | PARAMETERS lowering omits UPDATE/DELETE/EXECUTE/IF and `InListExpression`: `AstBuilder.cs:13-24,355-420` | Declared parameters can remain column references or bind incorrectly. Make lowering exhaustive and table-driven. |
| P2 | Medium / High static | INFORMATION_SCHEMA advertises VIEW but enumerates only physical tables: `InformationSchema.cs:48-78,128-133`; `JetCatalog.cs:96-111,204-213` | Scaffolding/generic clients cannot discover views accurately. Add TABLES rows first, then define view-column metadata behavior. |

### LibRed.Ado and LibRed.EFCore

| Priority | Severity / confidence | Root cause and exact locations | Consequence / recommended action |
|---|---|---|---|
| Resolved | Medium / High static | Connection strings were manually split on semicolons: `LibRedConnection.cs`; correct producer `LibRedConnectionStringBuilder.cs` | Fixed with `DbConnectionStringBuilder`; canonical `Data Source` wins over `DataSource`, then `DBQ`. Focused regression tests pass. |
| Deferred: transaction redesign | Medium / High static | Command transaction is stored but not enforced: `LibRedCommand.cs:27,39-55,68-74`; `LibRedConnection.cs:33-50,179-188` | A foreign/stale transaction association can execute under unexpected connection-scoped state. Do not patch this independently: define ownership/liveness as part of the planned ground-up transaction and ACE-interoperability design. |
| Resolved | Medium / High deterministic | Result metadata was inferred from the first runtime row: `LibRedDataReader.cs`; `ResultSet.cs`; plan schemas in `QueryExecutor.cs` and `StatementExecutor.cs` | Declared catalog and expression types now propagate to `ResultSet`. Empty and first-null base/computed/aggregate regressions pass. Unknown runtime-dependent coercions retain a conservative fallback. |
| Resolved | Medium / High static | `JetDatabase.Open` transferred channel ownership only after a constructor that can throw: `JetDatabase.cs`; `PageChannel.cs:34-42,297-302` | Fixed with local ownership and catch/dispose. A malformed page-0 regression verifies the failed open releases the file on Windows. |
| P1 | Medium correctness / High dynamic on Windows | Database deletion can retain an externally opened physical handle: `LibRedDatabaseCreator.cs:32-35`; `LibRedConnection.cs:119-123,158-172` | Delete fails with sharing violation/stale ownership. Four open-connection variants failed in the shard validation; fix only with ownership-state tests. |
| P2 | Low / High static | Batch splitting lacks comment/escaped-bracket states: `LibRedCommand.cs:83-106`; grammar `AccessSql.g4:437-439` | Valid SQL batches split incorrectly. Prefer token-aware splitting and avoid substring/trim allocation. |
| Partially resolved | Low / High static | `HasTablesAsync` still uses synchronous catalog work: `LibRedDatabaseCreator.cs:41-59`; `LibRedConnection.cs:138-155` | Pre-cancellation is now honored and covered. Genuine cancellable async I/O remains a larger design item. |

## Security assessment

### Calibration

LibRed is an embedded library, not a network service. A malicious database file and
untrusted SQL are valid trust boundaries, but this repository does not prove that an
attacker can reach them through a privileged server. Consequently:

- **0 Critical, 0 High** security findings after attack-path calibration;
- conditional **Medium** issues are those capable of process denial or persistent
  integrity damage when hostile files/SQL reach an in-process engine;
- conditional **Low** issues are bounded managed exceptions, service degradation, or
  impacts requiring an authorized mutation operation;
- two candidates were deferred and five ignored because current reachability or impact
  was insufficient. They remain traceable in the raw ledger.

### Conditional Medium security families

- Recursive/cyclic hostile structures: stored views (`ViewExpander.cs:64-69`), active index
  descent (`IndexWriter.cs:180-194`), LVAL reclamation (`RowInserter.cs:273-297`), and
  cascade deletion (`StatementExecutor.cs:218-266,942-958`). These can hang, consume
  memory, or exhaust the process stack.
- The previously reportable pre-authentication crypto descriptor family is resolved for
  LibRed's supported Office Standard variants and exact Access Agile profile: key, salt,
  block, verifier, and KDF dimensions are validated before allocation or expensive work.
- SQL resource exhaustion: unlimited LIKE backtracking (`ExpressionEvaluator.cs:1173-1231`),
  caller-sized string allocations (`ExpressionEvaluator.cs:202-318,980-1011`), and
  unbounded parser/binder/planner shapes (`AntlrSqlParser.cs:13-44`, `Binder.cs:26-29`,
  `QueryPlanner.cs:18-24,71-171`, `IndexSelection.cs:21-345`). These are Medium only
  where applications expose SQL/patterns; they are ordinary hardening in trusted-local use.
- Persistent-integrity paths: non-atomic multi-row DML, destructive LVAL reclaim, forged
  free-map allocation, and plaintext-first password changes. All require writable access
  or an authorized mutation path.
- Cross-database page-cache aliasing on case-sensitive hosts: `PageCache.cs:42,48-109`
  lowercases paths unconditionally. This is not a normal Windows issue, but matters to
  LibRed's cross-platform goal.

### Conditional Low and bounded findings

Nested property/row/index/codec bounds generally terminate as managed exceptions;
command timeout/cancellation is advertised but inert; and malformed LVAL/usage-map
structures can cause bounded-to-large resource use. These deserve fixes, but should not
be described as native memory corruption or as remote vulnerabilities without an
embedding application's exposure evidence. Quoted-semicolon path parsing and the
zero-length Office Standard verifier are now fixed and covered dynamically.

`lr-sec-013` (zero-length Office Standard verifier) is resolved. The malformed size 0,
19, and 21 cases now deterministically throw `NotSupportedException`; supported real and
synthetic descriptors still authenticate correctly and wrong passwords still reject.

### Deferred and ignored

- `lr-sec-001`, create-database truncation (`LibRedConnection.CreateDatabase` ->
  `DatabaseCreator.CreateEmpty`), was originally deferred as a security issue because no
  less-trusted caller able to invoke explicit creation was established. The correctness/data-loss
  defect is now fixed with atomic create-new semantics and a sentinel-preservation regression.
- `lr-sec-043`, unmatched RIGHT JOIN mutation to `RowId(0,0)`, was deferred pending a
  disposable-copy sink reproduction. It remains a P1 correctness/integrity item.
- Ignored as current security findings: the unused direct `IndexCursor` cycle path,
  oversized-row allocation leakage, LVAL descriptor truncation, row-insertion publication
  without demonstrated amplification, and index-maintenance TDEF cycle duplicate. These
  remain correctness/hardening backlog items where applicable.

## Performance opportunities

### Quick wins

1. Add a case-insensitive first-ordinal map in `LibRedDataReader` instead of linear
   `GetOrdinal` scans (`LibRedDataReader.cs:56-62`). Preserve duplicate-name behavior.
2. Replace repeated batch substrings/trims with span/token boundaries
   (`LibRedCommand.cs:83-106`), ideally alongside the correctness fix.
3. Cache or reuse parameter lookup only with clear mutation/version semantics
   (`LibRedCommand.cs:115-118`).
4. Pre-size small collections where counts are already validated; this is secondary to
   rejecting hostile sizes.

The largest simple reader allocation issue, full-string `ToCharArray()` on every
`GetChars` call, has already been removed.

### Medium investments

1. Parse each property blob once, materialize an indexed representation, and reuse it for
   all columns (`PropertyBlob.cs:182-238`; currently O(columns x blob bytes)).
2. Derive the block-independent 50,000-round Office Standard KDF prefix once rather than
   per page (`OfficeStandardEncryption.cs:301-315`; `PageChannel.cs:143,161,204,275`).
   Require known-answer crypto tests before merging.
3. Stream validated usage-map and row enumeration instead of eagerly materializing large
   page lists (`UsageMap.cs:95-153`).
4. Compute exact TDEF output size after validation instead of heuristic over-allocation
   (`TdefBuilder.cs:106,207-246,396`).
5. Cache parsed stored views/expressions with bounded expansion contexts and invalidation,
   avoiding repeated parse/rewrite work.

### Architectural investments

1. Replace whole-file in-memory encryption/password mutation with streaming same-directory
   temporary output, fsync/flush policy, and atomic replace (`DatabaseEncryption.cs`).
2. Add a unified execution budget propagated through parser, binder, planner, executor,
   storage iterators, scalar functions, regex, and lazy reader enumeration. This should
   carry deadline/cancellation plus node/byte/depth/allocation limits.
3. Introduce validated immutable binary views (TDEF/page/index/LVAL/property/row) so every
   consumer does not reimplement offsets, ownership, cycle, and geometry checks.
4. Add transaction/savepoint infrastructure for implicit statement atomicity and staged
   schema publication.

## Larger direction decisions

### 1. Hostile-file support level

| Option | Tradeoff |
|---|---|
| Trusted files only | Fastest path to feature coverage, but must document that malformed/imported files can crash operations or the host process through recursion/OOM. Poor fit for a general cross-platform engine. |
| Fail-safe imported files (recommended) | Build checked parsers/traversers and deterministic corruption exceptions. Moderate near-term cost, substantial simplification and security benefit across Core. |
| Recovery/forensics tolerant | Continue past localized corruption and salvage data. Much larger semantic and testing burden; defer until fail-safe parsing is mature. |

### 2. Transaction model

| Option | Tradeoff |
|---|---|
| Require callers to begin a transaction | Minimal implementation, but surprising for ADO.NET/SQL because a failed statement can leave a committed prefix. |
| Implicit per-statement savepoint/transaction (recommended) | Matches expected database semantics and fixes many integrity findings; requires careful interaction with explicit transactions and page cache rollback. |
| Operation-specific compensation | Smaller initial changes but proliferates fragile rollback code and misses process/interruption failures. |

### 3. Resource limits and compatibility

| Option | Tradeoff |
|---|---|
| Hard fixed limits | Simple and safe, but may reject unusually large valid databases/queries. |
| Configurable budgets with conservative defaults (recommended) | Supports trusted batch workloads while protecting service embeddings; adds public configuration and test matrix. |
| Cancellation only | Useful but insufficient for stack overflow, regex backtracking, giant single allocations, and work before a cancellation check. |

### 4. Cross-platform file identity

Use platform/file-system-aware canonical identity (or a handle/file-ID based cache key)
rather than unconditional lowercase strings. A string comparer selected by OS is simpler
but still imperfect for case-sensitive volumes on Windows and aliases/symlinks. A handle-
identity design is stronger but more platform work. Whichever option is chosen should also
return an immutable acquire token so release does not recanonicalize a relative path after
current-directory changes.

## Format-spec synchronization

The format documentation was used as authoritative for binary layouts. Most proposed
Core fixes enforce already documented geometry and therefore require a spec **check** but
not necessarily a spec change. Any newly verified offsets, ownership rules, limits, or
write mechanics must update the matching file under `src/LibRed/docs/format/` and
`appendix-structures.md`.

The previously confirmed relationship-flag documentation drift is now fixed:

- `src/LibRed/LibRed.Core/Formats/RelationshipFlags.cs:18-19` implements the
  Access-verified `0x2000` delete-set-null flag, while
  `src/LibRed/docs/format/system-catalog.md` and
  `src/LibRed/docs/format/appendix-structures.md` now record it. Runtime read/write was
  already symmetric; this was documentation drift, not a code-behavior change.

The `JetFormatBase.Detect` stream-position `finally` change needed no format edit because
it changes exception/lifetime behavior only, not bytes, offsets, types, or interpretation.

The crypto and DDL dimension remediation was checked against the matching references.
`page-00-database.md` now records authoritative Agile framing and the exact supported
Access profile; `data-types.md` records the enforced positive Text/Binary dimensions and
Decimal precision/scale bounds. `appendix-structures.md` did not need an edit because no
offset or field layout changed. Office Standard validation enforces the already documented
binary descriptor and supported variants, so no new format fact was added for that path.

The TDEF/usage-map batch updated `page-02a-tdef.md` and `page-05-usage-maps.md` with the
new enforcement behavior. Both changes enforce structures and limits already present in
the authoritative specification, so `appendix-structures.md` needed no layout change.

The continuation-chain remediation further updated `page-02a-tdef.md` with the shared
reader's authoritative-length behavior and clearly labels the 1 MiB allocation ceiling as
a LibRed safety policy rather than a verified on-disk limit. The chain/header layout itself
was already present, so the appendix still required no structural edit.

The variable-region remediation also updated `page-02a-tdef.md` to record enforcement of
the existing name encoding/length, column identity/type/high-water, and terminated LVAL-map
rules. No offset or structure changed, so `appendix-structures.md` again needed no edit.

The data-page/row remediation updated `page-01-data-and-rows.md` and `data-types.md` with
reader enforcement of the already documented page, slot, trailer, and scalar-width geometry.
The functional corpus exposed tolerated dead space between variable data and its offset table;
the documentation records that compatibility behavior and explicitly marks its cause as not yet
Access-verified. No field or structure changed, so the appendix required no edit.

The relocation follow-up added the verified forward-pointer and hidden-target rules to
`page-01-data-and-rows.md`. These document existing bytes and flags, so no appendix change
was required.

The index traversal and mutation remediation added implementation guardrails to
`page-03-04-index-btree.md`. They enforce the documented page types, owner, pointers, entry
mask, trailers, and compression layout, including revalidation immediately before a page is
mutated, without changing any field, so the appendix needed no edit.

## Verification status

- `LibRed.EFCore` was built during the review with **0 warnings and 0 errors**.
- `LibRed.Sql` was regenerated/compared and built with **0 warnings and 0 errors**; all
  four committed generated artifacts matched modulo the provenance comment.
- Crypto remediation first reproduced all malformed cases: Office verifier sizes 0/19/21,
  invalid RC4 key sizes 1/39/41/127/129, AES key size 257, Agile keyBits 257,
  blockSize 15, spinCount 100001, and stray XML outside the declared frame. After the
  fix, **39/39 focused Office Standard, Agile, and real Office-variant tests passed**.
- Database creator delete tests in shard 0019 dynamically established the open-handle
  ownership problem on Windows; four open variants failed and closed variants passed.
- The first remediation batch added four focused regressions: **2 ADO, 1 Core, and
  1 EF Core test passed**. The `LibRed.EFCore` dependency chain then built with
  **0 warnings and 0 errors** using `--no-restore` (restore was already complete).
- Standalone-expression remediation: the pre-fix regression reproduced silent suffix
  acceptance; after the fix, **64/64 relevant parser, CHECK, and DEFAULT tests passed**.
  ANTLR regeneration was deterministic, and the full LibRed dependency chain built with
  **0 warnings and 0 errors**.
- The two relationship-column ALTER failures exposed by that run were traced to specialized
  paths bypassing branch-local guards. After centralizing the guard, the original numeric
  child/parent cases and new text-length child/parent cases passed; **32/32 related ALTER,
  default, and AutoNumber tests passed**, followed by a clean complete Engine run:
  **725 passed, 0 failed**.
- Declared-result metadata remediation added four ADO regressions covering empty base and
  computed projections, a first-row NULL, and a NULL aggregate. The complete ADO suite
  now passes **41/41**, the complete Engine suite passed **725/725**, and the complete LibRed
  EF Core suite passed **15/15**.
- Create/format remediation first reproduced truncation, three mismatched identifier/version
  pairs, and Jet 3 acceptance. After the fixes, **10/10 focused format tests** and both
  destructive/legitimate create controls passed. Complete suites passed **Core 345/345**,
  **ADO 41/41**, and **EF Core 15/15**. The Core run also exposed and corrected one invalid
  synthetic page-0 test fixture; ADO fixture tests are serialized to avoid `File.Copy`
  transiently denying the write-sharing their parallel peers require.
- SQL type-dimension remediation first reproduced ten invalid Text/Binary/Decimal shapes
  creating tables. After the fix, **23/23 focused size/boundary tests passed** and invalid
  DDL leaves no table behind.
- Final complete project suites passed **Core 358/358**, **Engine 737/737**,
  **ADO 41/41**, and **EF Core 15/15**. The `LibRed.EFCore` dependency chain built during
  the EF Core run with no compiler warnings or errors other than the SDK's preview-support message.
- TDEF/usage-map remediation first reproduced eight unsafe outcomes: silent writer narrowing,
  accepted invalid counts/records, and incidental overflow/range exceptions. After the fix,
  **24/24 focused TDEF and usage-map tests passed**, including exact 69-byte, wrong-target,
  255-column, inline-growth, and Access reference-map controls. Complete suites then passed
  **Core 370/370**, **Engine 737/737**, **ADO 41/41**, and **EF Core 15/15**.
- TDEF continuation remediation safely reproduced wrong-page acceptance, declared-length
  mismatch acceptance, and an out-of-file incidental exception. After consolidation, seven
  corruption variants (including cycle, missing/extra page, invalid root/continuation header,
  and oversized length) reject before assembly. Focused wide-table read, index, and DDL controls
  passed **17/17**. Complete suites then passed **Core 377/377**, **Engine 737/737**,
  **ADO 41/41**, and **EF Core 15/15**.
- TDEF variable-region remediation first reproduced nine unsafe outcomes: six malformed
  structures were silently accepted and three failed through incidental framework exceptions.
  After the fix, all **14/14 focused cases** passed, including four additional bypass cases and
  a valid Memo usage-map control; the broader TDEF set passed **38/38**. Complete suites then
  passed **Core 391/391**, **Engine 737/737**, **ADO 41/41**, and **EF Core 15/15**.
- Data-page/row remediation first reproduced eleven unsafe outcomes: silent page/row acceptance,
  incidental range/argument exceptions, and failure to treat an older row's absent column as NULL.
  After the fix and bypass review, **16/16 focused corruption and compatibility cases** passed.
  One initial full-Core run captured the sole failing corpus test; its 11 schema-evolved tables
  established that a bounded pre-table gap must remain accepted. After narrowing that assumption,
  the 137-file corpus and complete suites passed **Core 407/407**, **Engine 737/737**,
  **ADO 41/41**, and **EF Core 15/15**.
- Row-relocation remediation reproduced five scan failures and one index-seek failure:
  malformed pointers were silently accepted or escaped as incidental exceptions. The shared
  resolver plus owner/mutation bypass coverage passed **8/8 corruption cases**; combined with
  five existing update/Access controls, the focused set passed **13/13**. Complete suites then
  passed **Core 415/415**, **Engine 737/737**, **ADO 41/41**, and **EF Core 15/15**.
- Index traversal remediation first reproduced five unsafe outcomes: two malformed structures
  were silently accepted and three escaped as incidental exceptions. Post-fix bypass coverage added
  descent/leaf/full-cursor cycles, child ownership, non-leaf chaining, and oversized compression;
  **12/12 corruption cases** and the **24/24** broader index/control set passed. Complete suites then
  passed **Core 427/427**, **Engine 737/737**, **ADO 41/41**, and **EF Core 15/15**.
- Index mutation follow-up removed the remaining unchecked page parser and revalidates leaf insertion,
  deletion, parent separator propagation, and old-next-leaf relinking at their final read-modify-write
  boundary. The TOCTOU window was established statically because deterministic reproduction requires an
  external concurrent file writer, which LibRed does not yet support. Focused index/Access controls passed
  **29/29** and indexed insert/update/delete controls passed **16/16**; sequential complete suites passed
  **Core 427/427**, **Engine 737/737**, **ADO 41/41**, and **EF Core 15/15**.
- LVAL-reader remediation first reproduced six unsafe outcomes: three malformed values were silently
  accepted and three escaped as incidental exceptions; cycle reachability was encoded without running the
  pre-fix hang. All **8/8** corruption/round-trip tests and eight real chained/update/Access controls passed.
- Property remediation first reproduced nine malformed blobs accepted or incidentally failing plus unchecked
  serializer narrowing. The shared representation and preflight passed **11/11** new cases and **7/7**
  byte-exact, raw-value, add/remove, and Access name-order controls. A complete Core run then exposed real
  ACE blobs using flag `0`; accepting the verified `{0,1}` domain passed all affected ACE/encrypted fixtures.
- TDEF writer remediation reproduced heuristic-buffer overflow and six unvalidated LVAL/name/index cases.
  Exact sizing and preflight passed **15/15** builder cases and **11/11** continuation/wide-table controls.
- Allocator/LVAL ownership remediation reproduced five unsafe writes or incidental failures. Seven focused
  corruption/ownership cases and six global-reference, packing, update, reclamation, and Access controls
  passed **13/13**. A real Northwind free bit established that exactly-next-page growth is legitimate; the
  allocator now materializes it contiguously and rejects larger gaps.
- Final sequential complete suites passed **Core 460/460**, **Engine 737/737**, **ADO 41/41**, and
  **EF Core 15/15**, with no compiler warnings or errors other than the SDK preview-support message.
- The complete 38,046-test functional suite was not rerun for this localized batch.
- Remaining cascade/view cycle, stack-exhaustion, huge-allocation, destructive page-0,
  and partial-publication scenarios were not executed in-process. They should use bounded
  fakes, subprocesses, fault injection, or disposable database copies.

## Recommended roadmap

### Phase 1: low-risk correctness and guardrails

Completed: focused regressions and fixes for Office Standard verifier/key dimensions,
Agile framing/profile bounds, and SQL scalar/type dimensions. Keep future isolated,
low-risk findings in this phase; do not fold transaction semantics into piecemeal patches.

### Phase 2: shared checked-format layer

Completed: checked TDEF reading/writing, index access/mutation, row/relocation boundaries,
LVAL traversal/reclamation prevalidation, usage-map/global-allocation targets, and property blobs.

1. Add broader format-fixture, fuzz/property, fault-injection, and cross-platform cache-identity tests.

### Phase 3: integrity and execution architecture

1. Add implicit statement savepoints/transactions and atomic DDL/catalog publication.
2. Add the end-to-end execution/cancellation/resource budget.
3. Replace recursive view/cascade/query-tree walks with bounded contexts or iterative worklists.
4. Redesign encryption mutation as streaming atomic replacement.

### Phase 4: compatibility and performance

1. Repair RIGHT JOIN mutation/selection, parameter lowering, and INFORMATION_SCHEMA view
   support.
2. Apply property, usage-map, ordinal, query-cache, and KDF performance improvements with
   benchmarks.
3. Decide whether to implement Jet 3 correctly or keep it explicitly unsupported.

## Evidence appendix

This report deliberately consolidates duplicate instances. The complete candidate record
is preserved in `.codex-security-work/`:

| Artifact | Contents |
|---|---|
| `synthesis-a.md` | 45 ADO/Core catalog, format, and crypto files; normalized findings and performance notes |
| `synthesis-b.md` | 45 Core storage and EF Core files; 24 canonical issue families |
| `synthesis-c.md` | 44 EF Core, Engine, SQL, and generated files; 18 canonical groups |
| `dedupe-report.md` | 53 raw -> 49 stable candidates and exact merge decisions |
| `validation-lr-sec-001.md` | Deferred create/truncate security assessment; static correctness proof |
| `validation-lr-sec-013.md` | Reportable zero-length verifier assessment and focused test evidence |
| `raw-a.jsonl`, `raw-b.jsonl`, `raw-c.jsonl`, `deduped.jsonl` | Machine-readable raw and reconciled candidate ledgers |

The external scan artifact `artifacts/05_findings/attack_path_analysis_report.md` contains
the 49 deployment-calibrated attack paths, including all report/defer/ignore decisions.
