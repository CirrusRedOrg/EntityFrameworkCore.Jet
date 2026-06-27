# LibRed — native managed Jet/ACE engine

A from-scratch, fully managed (cross-platform) implementation of the Microsoft
Jet/ACE database engine — the format behind Access `.mdb` and `.accdb` files.
Unlike the `EntityFrameworkCore.Jet` projects (which rely on Windows-only ODBC/OleDb),
LibRed reads and writes the file format directly.

> **Format spec:** [`docs/jet-ace-file-format.md`](docs/jet-ace-file-format.md) is LibRed's
> authoritative, verified reference for the on-disk Jet 4 / ACE format — every page type,
> structure, and encoding we implement. Treat it as the source of truth (it supersedes
> ad-hoc reads of mdbtools/Jackcess) and update it whenever the format understanding changes.

## Projects

| Project | Responsibility | Depends on |
| --- | --- | --- |
| **LibRed.Core** | File format: IO/page channel, version formats, typed pages, catalog, storage (tables, rows, indexes), crypto, memo/OLE | — |
| **LibRed.Sql** | SQL front end: ANTLR grammar, AST, parser, binder. No engine dependency; binds through an injected `ISchemaProvider` | — |
| **LibRed.Engine** | Plans and executes bound statements over Core; bridges the catalog to the SQL binder | Core, Sql |
| **LibRed.Ado** | ADO.NET surface: `DbConnection`/`DbCommand`/`DbDataReader`/`DbParameter`/`DbTransaction`/`DbProviderFactory` | Engine |
| **LibRed.EFCore** | EF Core provider built on the ADO layer (placeholder) | Ado |

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
Formats  (JetFormatBase + Jet3/4/12/14/16/17 — offsets & constants only)
```

## Status

This is a structural scaffold. Almost every method body is a documented `TODO`.
The binary-layout work (steps 3–8 below) is best driven from the
[mdbtools](https://github.com/mdbtools/mdbtools) (`src/libmdb/`) and
[Jackcess](https://jackcess.sourceforge.io/) sources, which thoroughly document the
on-disk structures.

### Suggested build order

1. `PageBuffer` + `PageChannel` — read raw bytes from a file
2. Fill in `JetFormatBase` constants for Jet 3 / Jet 4
3. `DatabaseDefinitionPage` — parse page 0, confirm version & page size
4. `TableDefinitionPage` + `ColumnDef` — parse a table's column layout
5. `DataPage` + `RowDecoder` — read rows from a known table
6. `UsageMap` — enumerate all of a table's data pages
7. `JetCatalog` — bootstrap from `MSysObjects`
8. `Table` + `TableCursor` — full table scan end-to-end
9. ANTLR grammar + a `SELECT * FROM t` executor through the full pipeline
10. ADO.NET wrapper, then the EF Core provider

## SQL pipeline

Always run through the full pipeline, even for trivial queries, so adding features
later (joins, aggregates, subqueries, then newer SQL) means adding node types rather
than rewriting:

```
text → ISqlParser → AST → Binder(ISchemaProvider) → BoundStatement
     → QueryPlanner → PlanNode tree → QueryExecutor → ResultSet
```

### Enabling ANTLR

The grammar lives at `LibRed.Sql/Grammar/AccessSql.g4` but is **not** wired into the
build yet (so the solution compiles without the ANTLR tool). To turn it on, uncomment
the `Antlr4BuildTasks` block in `LibRed.Sql.csproj`.
