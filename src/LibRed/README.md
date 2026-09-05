# LibRed — native managed Jet/ACE engine

A from-scratch, fully managed (cross-platform) implementation of the Microsoft
Jet/ACE database engine — the format behind Access `.mdb` and `.accdb` files.
Unlike the `EntityFrameworkCore.Jet` projects (which rely on Windows-only ODBC/OleDb),
LibRed reads and writes the file format directly.

> **Format spec:** [`docs/format/`](docs/format/README.md) is LibRed's authoritative, verified
> reference for the on-disk Jet 4 / ACE format — one file per page type, plus an
> [`appendix-structures.md`](docs/format/appendix-structures.md) field-layout quick reference.
> Treat it as the source of truth (it supersedes ad-hoc reads of mdbtools/Jackcess) and update it
> whenever the format understanding changes. **Any change to `LibRed.Core`'s read/write code must
> check the relevant `docs/format/` file for needed updates in the same change** — see the rule in
> the repo-root `CLAUDE.md`. Record only facts verified against real files or Access's own engine.
>
> **SQL surface:** [`docs/functions.md`](docs/functions.md) catalogs the supported VBA/Access functions
> (usable in `SELECT`/`WHERE`/`ORDER BY`/`DEFAULT`/`CHECK`); [`docs/format/page-02c-default-values.md`](docs/format/page-02c-default-values.md)
> covers column `DEFAULT` semantics.

## Projects

| Project | Responsibility | Depends on |
| --- | --- | --- |
| **LibRed.Core** | File format: IO/page channel, version formats, typed pages, catalog, storage (tables, rows, indexes), crypto, memo/OLE | — |
| **LibRed.Sql** | SQL front end: ANTLR grammar, AST, parser, binder. No engine dependency; binds through an injected `ISchemaProvider` | — |
| **LibRed.Engine** | Plans and executes bound statements over Core; bridges the catalog to the SQL binder | Core, Sql |
| **LibRed.Ado** | ADO.NET surface: `DbConnection`/`DbCommand`/`DbDataReader`/`DbParameter`/`DbTransaction`/`DbProviderFactory` | Engine |
| **LibRed.EFCore** | EF Core provider over the ADO layer: `AddEntityFrameworkLibRed()` / `UseLibRed()`, its own options, type mappings, connection, database creator, transactions, convention set builder, history repository, code generator, design-time services, and — in extended mode — its own SQL generator | Ado, EFCore.Jet.Common |

Dependency graph (a clean DAG, no cycles):

```
EFCore → Ado → Engine → Sql
     ↘                ↘ Core
      EFCore.Jet.Common
```

`EntityFrameworkCore.Jet.Common` is outside this folder (`src/EFCore.Jet.Common`) and holds the Jet-dialect
EF Core services **both** providers share. Nothing under `src/LibRed` references `EFCore.Jet` or
`EFCore.Jet.Data` any more, which is what keeps LibRed off the ACE-bound, Windows-only path.

## Layering inside LibRed.Core

```
Storage  (Table, TableCursor, RowDecoder, UsageMap, Types/JetTypeCodec)
   ↓
Catalog  (JetCatalog → TableDef / ColumnDef / IndexDef, JetDataType)
   ↓
Pages    (DatabaseDefinition, TableDefinition, Data, Index, UsageMap, Lval)
   ↓
IO       (PageChannel, PageBuffer)   ← Crypto decrypts pages here
   ↓
Formats  (JetFormatBase — Jet 4 / ACE offsets & constants; a future Jet3Format overrides)
```

## Status

Well past scaffolding — LibRed reads, writes and **creates** real `.accdb` files, runs SQL end-to-end, and
EF Core runs its specification suite against it in two SQL modes. The binary layout is documented and
**verified** in [`docs/format/`](docs/format/README.md) (against real files and Access's own
engine), cross-checked with [mdbtools](https://github.com/mdbtools/mdbtools) and
[Jackcess](https://jackcess.sourceforge.io/).

The cross-platform claim is tested rather than asserted: CI runs the engine suite and both EF Core
specification suites on Linux, Windows, macOS, ubuntu-arm and windows-arm, with no Access engine installed on
any of them. The suites that *do* install ACE exist to cross-check LibRed's output against the real engine,
which is a different job.

**Working today:**

- **Read** — every page type; catalog bootstrap from `MSysObjects`; full table scan; index B-tree
  traversal (leaf + node, prefix compression); row decode (fixed/variable split, null bitmap,
  in-bitmap booleans); data types incl. Text (compressed-Unicode common case), Memo/OLE long
  values, Currency, DateTime, GUID, Numeric/Decimal, and ACE-16 `BIGINT`/`DATETIME2`; inline and
  reference usage maps.
- **Views & stored queries** — a query is written the way Access does: an `MSysObjects` type-5 row
  (negative synthetic id) plus the query decomposed byte-faithfully into `MSysQueries` rows — and Access
  opens the file and runs it. Covered: `CREATE VIEW` (`SELECT` with joins, `WHERE`, `DISTINCT`, `BETWEEN`,
  `#date#` literals, column aliases, `Table.*`, nested/parenthesised joins), **GROUP BY totals**, **`TOP`
  + `ORDER BY`**, and **derived-table / `UNION` sources** (the long subquery text lands on an LVAL page so
  Access executes it); **`CREATE PROCEDURE`** (parameterized — bare or parenthesised list, `@name`),
  including **action-query** bodies (`CREATE TABLE` / `INSERT … VALUES`); and **`EXECUTE`/`EXEC`**.
  LibRed's own engine **reads them all back** — reconstructing the SQL from `MSysQueries` and expanding a
  view referenced in `FROM` (or inside an expression subquery) to a derived table — so they run through
  LibRed too. (`HAVING`, and the INSERT…SELECT/UPDATE/DELETE action-query write-back, are still TODO.)
- **Write** — row insert with order-preserving index-key encoding and **full B-tree maintenance**
  (descend to the target leaf, insert with prefix compression, **leaf/node splitting and root growth**);
  `CREATE TABLE` (heap + primary key) that **Access opens and round-trips**; AutoNumber generation
  and high-water tracking (including the two's-complement wrap past `int32`, which ACE does not treat as an
  error either); unique-index statistics; allocation through the global free-pages map;
  `MSysObjects` / `MSysACEs` catalog rows.
- **The ACE 16/17 types** — `Int64`/BIGINT (`0x13`) and `DateTimeExtended`/DATETIME2 (`0x14`), end to end:
  read, write, `CREATE TABLE`, index keys (ascending and descending), and native creation at the format each
  one needs. Every step is verified against ACE, ending with Access's own engine reading values out of files
  LibRed synthesised from nothing (`DateTime2CreatedDatabaseAccessTests`, `BigIntCreatedDatabaseAccessTests`).
  Two traps worth knowing: BIGINT is stored **variable**-length despite always being 8 bytes, and the two
  types sit at **different** formats — `0x05` for BIGINT, `0x06` for DATETIME2, so the natural assumption
  that ACE 16 added both at once is wrong.
  The **format auto-upgrade** comes with them: DDL that introduces either type raises the open file's version
  byte rather than refusing, which is what Access itself does (`docs/format/page-00-database.md`, where both
  routes are measured). The raise joins the statement's transaction, so a failed `CREATE`/`ALTER` takes it
  back down. ACE opens a file LibRed upgraded this way and reads the value that forced it. The upgrade is
  one-way and unavoidable — an older Access cannot open the result, but neither could it read the column —
  so `CreateDatabase(…, version:)` remains the way to *start* at a format rather than arrive at one.
- **Database creation** — LibRed synthesises a new `.accdb` page by page, with no DAO, no ADOX and no
  packaged template file, and **Access opens the result cleanly**. Worth knowing that this is the one area
  where the usual reference implementations cannot help: mdbtools and Jackcess both create a database by
  copying a packaged empty file, so ACE itself was the only oracle. `LibRedConnection.CreateDatabase(…,
  version:)` picks the format, defaulting to ACE 12 so an ordinary database still opens in every Access from
  2007 onward.
- **Text collation** — index keys are Windows NLS sort keys, and LibRed encodes them for **both sort-order
  versions across the whole Basic Multilingual Plane**: General Legacy (version 0, Access 2000–2007) and
  General (version 1, the order Access 2010 made default). The v1 table is the Windows Server 2008 sorting
  weight table ACE froze, embedded rather than transcribed — but it is only *nearly* that table, and the
  measured departures are carried as raw overrides. On top of General sit the **locale sort orders**, each a
  tailoring over the base weights rather than a separate table: contractions (Czech `ch`, Hungarian `cs`,
  Thai's leading vowels), doubled digraphs, and a **reversed diacritic section** for French, where no letter
  is retailored at all and a word needs two accents before the order diverges. Covered: French, German
  (incl. Phone Book), Spanish Traditional and Modern, Czech, Slovak, Polish, Hungarian (incl. Technical),
  Croatian, Bosnian, Serbian, Slovenian, Romanian, Turkish, Estonian, Latvian, Lithuanian, Icelandic,
  Norwegian/Danish, Swedish/Finnish, Ukrainian, Macedonian, Vietnamese, Georgian Modern, Indic and Thai.
  The collation itself is **one 32-bit LCID** — LANGID plus a sort id that distinguishes an alternate order
  for the same language (Hungarian Technical from Hungarian, German Phone Book from German) — plus the
  version byte. Five orders DAO still offers (Arabic, Greek, Hebrew, Dutch, Cyrillic) are **inert**: ACE
  records them faithfully and then encodes General keys anyway, verified over 82 samples. Also handled:
  the 510-byte index-entry limit with ACE's own truncation checksum, characters above the BMP, and the
  16-bit inline word-sort position. See `docs/format/page-03-04-index-btree.md` and
  `tools/sortkey-table/generate.ps1`, which generates the tables from ACE rather than transcribing them.
- **Constraints** — `CREATE TABLE` `PRIMARY KEY`, `UNIQUE` (column- and table-level), and foreign keys
  in every documented Access form: table-level `FOREIGN KEY [NO INDEX] (…) REFERENCES …`, column-level
  `… REFERENCES …`, and `ON UPDATE`/`ON DELETE` in either order. A relationship is persisted to
  `MSysRelationships` with a child-side FK index and **byte-faithful** logical-index linkage on both
  tables' TDEFs (Access opens the file and enumerates it), with referential-integrity enforcement on
  `INSERT`. `UNIQUE` creates a unique non-primary index. Self-referencing foreign keys are handled
  inline. Column `DEFAULT` values are persisted to the table's `LvProp` property blob (on an LVAL page),
  read back onto the column, and applied when an insert omits the column — **and Access honors them too**.
  Table-level `CHECK` constraints are persisted to the `LvProp` `CheckConstraints` property (verbatim
  expression text) and read back onto `TableDef.CheckConstraints` — **and Access enforces them**.
- **SQL** — ANTLR front end (parser → binder via `ISchemaProvider` → planner → executor). Statements:
  `CREATE TABLE`, `CREATE [UNIQUE] INDEX … [WITH {PRIMARY|DISALLOW NULL|IGNORE NULL}]`, `CREATE VIEW`,
  `CREATE PROCEDURE`, `ALTER TABLE` (ADD/DROP COLUMN, ADD PK/FK/UNIQUE/CHECK, ALTER COLUMN incl. the
  byte-faithful in-place type change, DROP CONSTRAINT), `DROP {TABLE|INDEX|VIEW|PROCEDURE}`, `INSERT`
  (with AutoNumber) — from `VALUES`, from a multi-row table value constructor, or from a query
  (`INSERT INTO … SELECT`, the append query) — `SELECT … INTO` (the make-table query), `UPDATE`, `DELETE`,
  `EXECUTE`, `IF … THEN`, and `SELECT` with `WHERE`, joins, `GROUP BY`/aggregates, `HAVING`, `ORDER BY`,
  `TOP [PERCENT]`, `ALL`/`DISTINCT`/`DISTINCTROW`, `UNION`/`INTERSECT`/`EXCEPT`, subqueries, and parameters.
  Plan nodes: Scan / IndexScan / IndexSeek / IndexRangeSeek / Filter / Project / Join / HashJoin / Aggregate /
  Sort / Limit / Distinct / DistinctRow / SetOperation / DerivedTable / Values / Window / SingleRow.
- **SQL beyond what ACE has** — the engine deliberately accepts a superset of the Access dialect, which is
  what extended mode generates against (see the EF Core section below):
  - `CROSS APPLY` / `OUTER APPLY` — a lateral join, with the right side re-evaluated per left row. ACE has
    no syntax for either.
  - **Window functions** — `ROW_NUMBER()`, `RANK()` and `DENSE_RANK()` with
    `OVER (PARTITION BY … ORDER BY …)`. `OVER` hangs off any function call, so adding another is a registry
    entry rather than a grammar change.
  - `FULL [OUTER] JOIN` — ACE offers only inner/left/right, and its query designer cannot express a full one.
  - **`OFFSET … ROWS FETCH NEXT … ROWS ONLY`** paging, where the count may be any expression, not just a
    literal. Access has only `TOP n`, and only with a literal.
  - **Standard scalar syntax** ACE lacks: `CASE`, `COALESCE`, `NULLIF`, and the `VALUES` table value
    constructor standing in for a query.
  - **Set operations in a subquery predicate** — `IN (… UNION …)`, `EXISTS (… EXCEPT …)`, and a scalar
    subquery over a set operation.
  - **`ORDER BY` bound to the query expression**, so it applies to a whole set operation rather than to its
    last operand; an operand carries its own ordering only when parenthesised. ACE silently accepts and then
    ignores an operand's `ORDER BY`, which is a wrong-answer bug rather than an error.
- **ADO.NET** — connection / command / reader / parameter / transaction / factory over the engine.
  Transactions commit/roll back for real via a page-level undo log in `PageChannel` (snapshot pages on
  first write, restore on rollback, truncate pages the txn allocated) — this is what gives EF Core's
  shared-database functional tests their per-test isolation.
- **EF Core** — the `LibRed.EFCore` provider (`AddEntityFrameworkLibRed` / `UseLibRed`) over `LibRed.Ado`
  and `EntityFrameworkCore.Jet.Common`, in two SQL modes. Beyond query round-trips and database-first
  scaffolding (`LibRed.EFCore.Tests`), it runs EF Core's own **specification suite** in both modes —
  migrations infrastructure, bulk updates, precompiled queries, complex types, inheritance mapping and the
  Northwind/GearsOfWar query batteries. See the EF Core section below.

**Done since (previously listed here as "not yet"):** DML `UPDATE` / `DELETE` (in-place, row relocation,
index maintenance, multi-table over joins, `WHERE EXISTS`/scalar subquery, LVAL reclamation) and the
generated-AutoNumber `@@IDENTITY` round-trip up through Engine → Ado → EFCore; the **full `ALTER TABLE`**
surface (unblocking cyclic/self-referencing FKs EF emits as a separate operation) and
`DROP {TABLE|INDEX|VIEW|PROCEDURE}`; `CREATE PROCEDURE` / `EXECUTE`; foreign-key **enforcement + cascade /
set-null** referential actions on `UPDATE`/`DELETE`; leaf/node B-tree splitting with root growth;
**transactions** (real commit/rollback via a page-level undo log — see above); the **locale text
collations**, which were listed here as encodable for the two General orders only; the **ACE 16/17 types**
`BIGINT` and `DATETIME2` with the format auto-upgrade; and `INSERT INTO … SELECT` plus `SELECT … INTO`,
which the grammar previously had no form for.

**Not yet.** (Format-level details of each on-disk gap live in `docs/format/`; this is the working
worklist. Much of the earlier "not yet" list is now done — the whole of `ALTER TABLE`
(ADD/DROP COLUMN, ADD UNIQUE/CHECK, ALTER COLUMN incl. the byte-faithful in-place type change, DROP of
a PK/unique constraint), `CREATE INDEX` on non-empty tables with back-fill, chained multi-page LVAL,
LibRed-side `CHECK` enforcement, self-pointing self-references, and writing Memo/OLE values.)

*On-disk / write gaps:*

- **Composite index key encoding** — single-column keys are byte-verified vs ACE; a genuine
  **multi-column** key is not (no column separator confirmed — Northwind's only composite is usually
  empty). Verify against a created composite-key `.accdb` before relying on it.
- **`ON UPDATE SET NULL`** — pathway threaded but throws; its Jet storage bytes are unverified because the
  ACE OLE DB provider rejects the DDL (needs a UI/DAO-created sample to probe). `ON DELETE SET NULL` and
  both `CASCADE` directions work.
- **1:1 relationships** (`dbRelationUnique` = `0x01`) — never written; a 1:1 migration would render as
  1:many in Access (the unique index still enforces uniqueness). Probe a real 1:1's `grbit` first.
- **Computed / calculated columns** (ACE 14) — the evaluation half exists; the gap is the on-disk
  TDEF/`LvProp` storage of the expression (and persisted-vs-virtual semantics).
- **`LvProp` properties not modelled** — `ValidationRule`/`ValidationText` (UI-authored validation,
  distinct from a SQL `CHECK`), `AllowZeroLength`, and `UnicodeCompression` (storage-affecting). Column-level
  `CHECK` persistence is likewise unprobed (its ACE storage differs).
- **`DROP TABLE` leaks until Compact** — multi-page TDEFs, non-root index pages, LVAL pages, and dedicated
  usage-map pages aren't freed; byte-faithful **child-in-relationship** `DROP TABLE` (ACE cascades the FK;
  LibRed requires dropping the FK first).
- **Jet 3** format; **password/encryption** write; strict **DAO Compact & Repair** compatibility (checklist
  captured — only relevant if targeting DAO C&R rather than "ACE opens + queries").
- **`CREATE TEMPORARY TABLE` / `WITH COMPRESSION`** — parsed only to throw `NotSupportedException`.

*SQL surface / engine gaps:*

- **Stored action queries** — INSERT…VALUES + DDL bodies are written and read back; INSERT…SELECT /
  UPDATE / DELETE bodies are not. The gap is the `MSysQueries` write-back, not the grammar: all three parse
  and execute as statements. `HAVING` in a stored view needs its `MSysQueries` attribute probed.
- **`!` bang notation** (`[Table]![Col]`, `Forms![f]![ctl]`) — grammar gap; and the stored-query
  reconstructor only rebuilds simple SELECTs + a few action kinds, so a real app's parameterized/combo-box
  query layer reads back as unsupported.
- **Function surface** — the evaluator's whitelist isn't proven identical to ACE's JES; `Format` named
  date/currency formats are locale-dependent by design (not byte-identical cross-locale). Argument **arity**
  *is* now checked against a per-function range table, so a wrong count raises rather than being ignored.
- **Non-unique index statistics** — only unique indexes advance the live unique-entry count (`+4`) today.
- **Deferred-write transactions** — writes are eager (write-through + undo log); a future `PageChannel`
  refactor could buffer changed pages and materialize only on commit (cheaper rollback, never half-applied).
- **Single-writer concurrency** — LibRed is a **single-writer engine that merely tolerates extra open
  handles**, not a concurrent multi-user one. `PageChannel.Open` opens the file `FileShare.ReadWrite`
  (a Jet/ACE file is a shared-file database — Access/ODBC/OLE DB all open it with multiple handles, and
  EF's own test infra keeps a store connection open alongside per-context connections), but there is **no
  concurrency control**: no lock file (Access coordinates multi-user access via a side-car `.laccdb`/`.ldb`
  with page/record locks — LibRed writes and honours none of it), no read isolation, and the transaction
  undo log is **per-`PageChannel`**. Safe: any number of readers with no writer; one writer plus readers
  when access is **serialized** (the single-threaded app / EF case). Unsafe: truly concurrent readers and a
  writer (torn/dirty reads — a reader can see a half-applied multi-page operation or another handle's
  uncommitted transaction); **two or more concurrent writers → file corruption** (unarbitrated page writes,
  racing usage-map allocation, and — worst — one channel's rollback *truncates the file* back to its own
  transaction start, discarding pages another channel committed past that point). True multi-user support
  is its own project: a lock file, page/record locking, and a shared or WAL-based write path.

## SQL pipeline

Always run through the full pipeline, even for trivial queries, so adding features
later (joins, aggregates, subqueries, then newer SQL) means adding node types rather
than rewriting:

```
text → ISqlParser → AST → Binder(ISchemaProvider) → BoundStatement
     → QueryPlanner → PlanNode tree → QueryExecutor → ResultSet
```

### ANTLR grammar

The grammar lives at `LibRed.Sql/Grammar/AccessSql.g4` and **is** the active parser
(`AntlrSqlParser` → `AccessSqlLexer`/`AccessSqlParser`). The lexer/parser/visitor are **pre-generated
and committed** under `Grammar/Generated/`, and only the managed `Antlr4.Runtime.Standard` package is
referenced — so the solution builds without the ANTLR code-generation tool. After editing
`AccessSql.g4`, regenerate the sources with `generate.ps1` and commit them. It needs Java, and downloads
the matching ANTLR jar on first run. **Run it from the `Grammar` directory** — it passes
`-o Generated AccessSql.g4`, both relative to the working directory rather than to the script, so running
it from anywhere else either fails to find the grammar or writes the output to the wrong place.

## EF Core provider (LibRed.EFCore)

`AddEntityFrameworkLibRed()` / `UseLibRed()` register the provider. It no longer registers EFCore.Jet's
services and then overrides them: `EntityFrameworkCore.Jet.Common` was extracted to hold the Jet-dialect
services **both** providers share, and LibRed registers those directly. Where LibRed needs different
behaviour it owns a 1:1 copy rather than subclassing or branching — `LibRedConventionSetBuilder`,
`LibRedHistoryRepository`, `LibRedCodeGenerator`, `LibRedDesignTimeServices`, its own options, type mappings,
connection (`LibRedRelationalConnection` over `LibRed.Ado`), `LibRedDatabaseCreator`, transactions, and the
catalog-backed `LibRedDatabaseModelFactory` scaffolding.

### SQL modes

`UseLibRed` takes an optional `LibRedSqlMode`:

| Mode | SQL generator | Why |
| --- | --- | --- |
| **`Extended`** (default) | `LibRedQuerySqlGenerator` | LibRed owns the engine that parses the result, so the generator has nobody to please. It emits standard SQL. |
| **`Compatible`** | Common's `JetQuerySqlGenerator` | The same SQL the Jet provider emits, so the statements also run against ACE. |

Extended mode exists because most of what a Jet SQL generator does is work around the dialect, and every one
of those workarounds is a place the SQL can be wrong or slow. The method is a strip loop: remove a workaround
from the generator, run the extended suite, and where the plainer SQL is not understood, **extend the engine**
— never paper over it in the generator. That is where `CROSS`/`OUTER APPLY`, the window functions,
`OFFSET`/`FETCH`, `CASE`/`COALESCE`/`NULLIF`, the table value constructor, set operations in subquery
predicates and the query-expression `ORDER BY` all came from: each was a Jet workaround removed, then a
capability added underneath it.

The mode is not visible to the engine. Anything the generator emits is ordinary SQL the parser accepts from
any caller, so a hand-written Access query keeps behaving the way Access does — where the two disagree, the
distinction is carried by **syntax**, not by a mode flag. `LibRedParameterBasedSqlProcessor` is the one place
the mode is read in the pipeline, to skip the Jet-dialect rewrites (and `JetCompatibilityExpressionVisitor`,
whose rejections — `ROW_NUMBER`, `CROSS`/`OUTER APPLY`, `EXCEPT`, `INTERSECT` — are exactly what extended
mode is for).

Each mode has its own EF Core specification suite, because the same LINQ query produces different SQL and so
needs different baselines: `test/EFCore.LibRed.FunctionalTests` (compatible) and
`test/EFCore.LibRed.Extended.FunctionalTests` (extended). Both run on every push across all five platforms.
