# LibRed — native managed Jet/ACE engine

A from-scratch, fully managed (cross-platform) implementation of the Microsoft
Jet/ACE database engine — the format behind Access `.mdb` and `.accdb` files.
Unlike the `EntityFrameworkCore.Jet` projects (which rely on Windows-only ODBC/OleDb),
LibRed reads and writes the file format directly.

> **Format spec:** [`docs/jet-ace-file-format.md`](docs/jet-ace-file-format.md) is LibRed's
> authoritative, verified reference for the on-disk Jet 4 / ACE format — every page type,
> structure, and encoding we implement. Treat it as the source of truth (it supersedes
> ad-hoc reads of mdbtools/Jackcess) and update it whenever the format understanding changes.
> **Any change to `LibRed.Core`'s read/write code must check the spec for needed updates in the
> same change** — see the rule in the repo-root `CLAUDE.md`. Record only facts verified against
> real files or Access's own engine.

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
[`docs/jet-ace-file-format.md`](docs/jet-ace-file-format.md) (against real files and Access's own
engine), cross-checked with [mdbtools](https://github.com/mdbtools/mdbtools) and
[Jackcess](https://jackcess.sourceforge.io/).

**Working today:**

- **Read** — every page type; catalog bootstrap from `MSysObjects`; full table scan; index B-tree
  traversal (leaf + node, prefix compression); row decode (fixed/variable split, null bitmap,
  in-bitmap booleans); data types incl. Text (compressed-Unicode common case), Memo/OLE long
  values, Currency, DateTime, GUID, Numeric/Decimal, and ACE-16 `BIGINT`/`DATETIME2`; inline and
  reference usage maps.
- **Write** — row insert with order-preserving index-key encoding and B-tree maintenance;
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
  `CHECK` constraints (table- and column-level) are parsed and accepted (currently ignored, not enforced).
- **SQL** — ANTLR front end (parser → binder via `ISchemaProvider` → planner → executor). Statements:
  `CREATE TABLE`, `CREATE [UNIQUE] INDEX … ON … (col [ASC|DESC], …) [WITH {PRIMARY|DISALLOW NULL}]`,
  `INSERT` (with AutoNumber), and `SELECT` with `WHERE`, joins, `GROUP BY`/aggregates,
  `HAVING`, `ORDER BY`, `TOP`, `UNION`/`INTERSECT`/`EXCEPT`, subqueries, and parameters. Plan nodes:
  Scan / IndexScan / Filter / Project / Join / Aggregate / Sort / Limit / SetOperation / DerivedTable.
- **ADO.NET** — connection / command / reader / parameter / transaction / factory over the engine.
- **EF Core** — `LibRed.EFCore` provider (`AddEntityFrameworkLibRed` / `UseLibRed`) over `LibRed.Ado`,
  reusing most of `EFCore.Jet` and overriding the connection + scaffolding. Query round-trips and
  database-first scaffolding pass (`LibRed.EFCore.Tests`).

**Not yet (see `docs/` TODOs and code `TODO(...)` markers):**

- DML `UPDATE` / `DELETE` (only `INSERT` so far); returning the generated AutoNumber id
  (`@@IDENTITY`) up through Engine → Ado → EFCore.
- **Foreign keys — `ALTER TABLE ADD CONSTRAINT`** (TODO #2): cyclic and self-referencing FKs that EF
  emits as a *separate* `AddForeignKeyOperation` instead of inline in `CREATE TABLE`. The grammar has
  no `ALTER TABLE`; `TableCreator` throws `NotSupportedException` on an inline self-reference. Blocked
  on adding `ALTER TABLE` to the SQL front end. (Inline, acyclic FKs work today.)
- **Foreign keys — cascade actions** (TODO #3): the `ON UPDATE`/`ON DELETE CASCADE` flags are persisted
  correctly (in `MSysRelationships.grbit` and the index-info action bytes), but nothing cascades at
  runtime because there is no `UPDATE`/`DELETE` executor yet — do this when DML `UPDATE`/`DELETE` lands.
- **Chained LVAL pages**: `LongValueWriter` writes a **single** LVAL page (used for `LvProp` and available
  for memo/OLE), so a long value must fit in one page. Payloads larger than a page need a chained (`0x00`)
  descriptor across multiple LVAL pages, which isn't written yet. (Memo/OLE column *values* still write
  inline; switching them to LVAL pages for large values is the follow-up.)
- **`CREATE TEMPORARY TABLE` / `WITH COMPRESSION`**: parsed only to throw a clear `NotSupportedException`
  (out of scope).
- **`CHECK` constraints**: parsed (as balanced-paren token soup) and **ignored** — not enforced on
  insert/update, and not persisted so Access sees them. Follow-up: evaluate the expression per row and/or
  persist it as a validation-rule property.
- **`CREATE INDEX` — non-empty table**: works for ascending/descending, `WITH PRIMARY`,
  `WITH DISALLOW NULL`, and `WITH IGNORE NULL` indexes on an **empty** table (EF's case: indexes are
  created right after `CREATE TABLE`, before seeding). Not yet: adding an index to a **non-empty** table
  (needs B-tree population over existing rows) — throws a clear `NotSupportedException`.
- **Foreign keys — self-pointing row on a self-reference**: insert-time referential-integrity checks the
  parent *before* inserting the row, so a row that references itself on a self-referencing FK (e.g.
  `Mgr = its own Id`) is wrongly rejected (Access allows it). Needs the row's own key counted as part of
  the parent set for a self-reference, or deferred/post-insert checking. See
  `StatementExecutor.EnforceReferentialIntegrity`. (Chains that reference *other* rows work fine.)
- **Writing** Memo/OLE (long-value) columns (read-only today); non-unique index statistics.
- **Version-1** "General" text collation (Access 2010+); **Jet 3** format; password/encryption write;
  reference-type usage maps for very large *new* tables.

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
