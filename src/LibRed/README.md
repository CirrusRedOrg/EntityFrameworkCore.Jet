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
| **LibRed.EFCore** | EF Core provider over the ADO layer: `AddEntityFrameworkLibRed()` / `UseLibRed()`, connection, database creator, database-first scaffolding | Ado |

Dependency graph (a clean DAG, no cycles):

```
EFCore → Ado → Engine → Sql
                      ↘ Core
```

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

Well past scaffolding — LibRed reads and writes real `.accdb` files, runs SQL end-to-end, and an
EF Core `DbContext` round-trips through it. The binary layout is documented and **verified** in
[`docs/format/`](docs/format/README.md) (against real files and Access's own
engine), cross-checked with [mdbtools](https://github.com/mdbtools/mdbtools) and
[Jackcess](https://jackcess.sourceforge.io/).

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
  and high-water tracking; unique-index statistics; allocation through the global free-pages map;
  `MSysObjects` / `MSysACEs` catalog rows; version-0 "General legacy" text index keys.
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
  (with AutoNumber), `UPDATE`, `DELETE`, `EXECUTE`, and `SELECT` with `WHERE`, joins, `GROUP BY`/aggregates,
  `HAVING`, `ORDER BY`, `TOP`, `UNION`/`INTERSECT`/`EXCEPT`, subqueries, and parameters. Plan nodes:
  Scan / IndexScan / Filter / Project / Join / Aggregate / Sort / Limit / SetOperation / DerivedTable.
- **ADO.NET** — connection / command / reader / parameter / transaction / factory over the engine.
  Transactions commit/roll back for real via a page-level undo log in `PageChannel` (snapshot pages on
  first write, restore on rollback, truncate pages the txn allocated) — this is what gives EF Core's
  shared-database functional tests their per-test isolation.
- **EF Core** — `LibRed.EFCore` provider (`AddEntityFrameworkLibRed` / `UseLibRed`) over `LibRed.Ado`,
  reusing most of `EFCore.Jet` and overriding the connection + scaffolding. Query round-trips and
  database-first scaffolding pass (`LibRed.EFCore.Tests`).

**Done since (previously listed here as "not yet"):** DML `UPDATE` / `DELETE` (in-place, row relocation,
index maintenance, multi-table over joins, `WHERE EXISTS`/scalar subquery, LVAL reclamation) and the
generated-AutoNumber `@@IDENTITY` round-trip up through Engine → Ado → EFCore; the **full `ALTER TABLE`**
surface (unblocking cyclic/self-referencing FKs EF emits as a separate operation) and
`DROP {TABLE|INDEX|VIEW|PROCEDURE}`; `CREATE PROCEDURE` / `EXECUTE`; foreign-key **enforcement + cascade /
set-null** referential actions on `UPDATE`/`DELETE`; leaf/node B-tree splitting with root growth; and
**transactions** (real commit/rollback via a page-level undo log — see above).

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
- **ACE 16/17 types — `Int64`/BIGINT (`0x13`) and `DateTimeExtended`/DATETIME2 (`0x14`) are both done**: read,
  write, `CREATE TABLE`, index keys (ascending and descending), and native creation at the format each needs.
  `LibRedConnection.CreateDatabase(…, version:)` takes a `JetVersion`, defaulting to ACE 12 so an ordinary
  database still opens in every Access from 2007. Every step is verified against ACE, ending with Access's own
  engine reading values out of files LibRed synthesised from nothing
  (`DateTime2CreatedDatabaseAccessTests`, `BigIntCreatedDatabaseAccessTests`).
  Two traps worth knowing: BIGINT is stored **variable**-length despite always being 8 bytes, and the two
  types sit at **different** formats — `0x05` for BIGINT, `0x06` for DATETIME2.
  The **format auto-upgrade** is implemented too: DDL that introduces either type raises the open file's
  version byte rather than refusing, which is what Access itself does (`docs/format/page-00-database.md`,
  where both routes are now measured). The raise joins the statement's transaction, so a failed CREATE/ALTER
  takes it back down. ACE opens a file LibRed upgraded this way and reads the value that forced it. The
  upgrade is one-way and unavoidable: an older Access cannot open the result, but neither could it read the
  column. `CreateDatabase(…, version:)` remains the way to start at a format rather than arrive at one.
- **Non-English text collations** — index-key weights exist for the two General (1033) orders only:
  General-Legacy (v0) and General (v1). Any other locale is refused by `IndexKeyEncoder` rather than encoded
  with the English table. *(The v1 gap is closed: v1 keys are the Windows NLS weights verbatim, from the
  Windows Server 2008 sorting weight table that ACE froze — see `docs/format/page-03-04-index-btree.md` §10.4
  and `tools/sortkey-table/generate.ps1`. v0's ancestor table is still unidentified: its ordering matches
  every published Windows version across the range LibRed covers, so discriminating it needs characters whose
  weights actually moved.)*

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
  UPDATE / DELETE bodies are not (UPDATE/DELETE also need grammar). `HAVING` in a stored view needs its
  `MSysQueries` attribute probed.
- **`!` bang notation** (`[Table]![Col]`, `Forms![f]![ctl]`) — grammar gap; and the stored-query
  reconstructor only rebuilds simple SELECTs + a few action kinds, so a real app's parameterized/combo-box
  query layer reads back as unsupported.
- **Function surface** — the evaluator's whitelist isn't proven identical to ACE's JES; argument **arity**
  isn't validated (extra args ignored where ACE errors); `Format` named date/currency formats are
  locale-dependent by design (not byte-identical cross-locale).
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
`AccessSql.g4`, regenerate the sources with `Grammar/generate.ps1` (which needs the ANTLR tool) and
commit them.

## EF Core provider (LibRed.EFCore)

`LibRed.EFCore` mirrors EFCore.Jet's DI registration and is wired up: `AddEntityFrameworkLibRed()` /
`UseLibRed()` register the provider, reusing EFCore.Jet's services (added via
`JetServiceCollectionExtensions.AddEntityFrameworkJet()`,
`src/EFCore.Jet/Extensions/JetServiceCollectionExtensions.cs`, through an
`EntityFrameworkRelationalServicesBuilder`) and **overriding only the LibRed-specific pieces on top**.
Query round-trips and database-first scaffolding pass today (`LibRed.EFCore.Tests`).

**Overridden today:**

- **The connection** (`IJetRelationalConnection` → `LibRedRelationalConnection`) — a LibRed
  connection over `LibRed.Ado` instead of the OLE DB/ODBC `JetConnection`.
- **`IRelationalDatabaseCreator`** (→ `LibRedDatabaseCreator`) and the **database-first scaffolding**
  (`LibRedDatabaseModelFactory`, catalog-backed, plus `LibRedDesignTimeServices`).

So EF queries currently flow through EFCore.Jet's `JetQuerySqlGenerator` (which emits Jet SQL), and
LibRed.Ado parses that SQL with its ANTLR front end.

**Planned next — `IQuerySqlGeneratorFactory`** (today `JetQuerySqlGeneratorFactory` → creates
`JetQuerySqlGenerator`), the biggest remaining override. Owning both SQL generation and the
engine/parser means the generator can drop the ACE-pleasing contortions (parenthesised multi-way
joins, comma-vs-JOIN quirks, `CBOOL`/`CLNG`/`TOP`-`SKIP` gymnastics) that exist only to satisfy the
OLE DB/ODBC → ACE path — subclass/customise rather than rewrite. Until then, grow the SQL layer
against the real EF-generated (Jet) queries it must accept. (Note: the Jet builder uses `TryAdd` /
add-if-absent, so confirm the override ordering.)
