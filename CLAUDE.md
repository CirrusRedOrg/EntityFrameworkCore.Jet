# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

EntityFrameworkCore.Jet is an EF Core provider for Microsoft Jet/ACE databases (Microsoft Access `.mdb`/`.accdb` files). The **Jet** provider runs **Windows only** and bridges EF Core to the Access database engine via either ODBC or OLE DB. Alongside it, **LibRed** (also in this repo, on `master`) is a from-scratch managed engine that reads/writes the file format directly and is **cross-platform** — see the LibRed section below.

Current version: `11.0.0-alpha.1` (`Version.props`) targeting EF Core 11 and `net11.0`; `global.json` pins an 11.0.100 preview SDK with `rollForward: latestFeature`. The test projects use **xunit v3**.

### Which layer am I touching?

- `src/EFCore.Jet*` — Windows-only, ACE driver required, `[SupportedOSPlatform("windows")]`, tests need a real Access driver.
- `src/LibRed/*` — no ACE, no COM, runs on Linux/macOS/ARM64; its tests either need no driver at all or exist specifically to cross-check LibRed's output against the real ACE engine.

Nothing under `src/LibRed` is referenced by the Jet provider; the dependency runs the other way (`LibRed.Ado` → `EFCore.Jet.Data`, `LibRed.EFCore` → `EFCore.Jet`), so a Jet-side change has to re-run the LibRed suites but not vice versa.

## Build

```powershell
dotnet build EFCore.Jet.sln
```

Assemblies are **strong-name signed** using `Key.snk`. `TreatWarningsAsErrors=True` is set globally — fix all warnings.

### Local EFCore Repository (optional)

To develop against a local EF Core build instead of NuGet packages, copy `Development.props.sample` to `Development.props` and set `LocalEFCoreRepository` to your EF Core checkout. That local build must be compiled with `AssemblyVersion=11.0.0.0` to avoid binding conflicts.

## Tests

**Jet** tests require a real Microsoft Access driver installed (ODBC or OLE DB) and an actual `.accdb` file — no mocks. The connection string is configured via:
- `test/EFCore.Jet.FunctionalTests/config.json` (OLE DB example present)
- `test/EFCore.Jet.Tests/config.json` (bare filename; picks up default provider)
- `test/EFCore.LibRed.FunctionalTests/config.json` (LibRed connection)
- Or env var `EFCoreJet_DefaultConnection`

**LibRed** tests split in two: `LibRed.Engine.Tests` and `EFCore.LibRed.FunctionalTests` need **no driver at all** and CI runs them on Linux/Windows/macOS plus ARM64 legs — that matrix is what proves the cross-platform claim, so don't add an ACE dependency to them. `LibRed.Core.Tests`, `LibRed.Engine.AccessTests`, `LibRed.Ado.Tests` and `LibRed.EFCore.Tests` deliberately cross-check LibRed's output against the real engine over OLE DB, so they need Windows + ACE.

**Run all tests** (requires x86 or x64 matching your driver bitness):

```powershell
dotnet test EFCore.Jet.sln --configuration Debug
```

**Run a single test class:**

```powershell
dotnet test test\EFCore.Jet.FunctionalTests\EFCore.Jet.FunctionalTests.csproj --filter "FullyQualifiedName~NorthwindQueryJetTest"
```

**Run a single test method:**

```powershell
dotnet test test\EFCore.Jet.FunctionalTests\EFCore.Jet.FunctionalTests.csproj --filter "FullyQualifiedName=EntityFrameworkCore.Jet.FunctionalTests.Query.NorthwindQueryJetTest.Where_simple"
```

**When running a suite, capture the failing test *names* in the same run** — don't reduce the output to just the `Passed!/Failed!` count line and then re-run the whole suite to find which failed. Grep a pattern that catches both, e.g. `grep -iE "Passed!|Failed!|\[FAIL\]|error CS"` (xUnit prints `… [FAIL]` and `Failed <FullyQualifiedName>` lines as it goes), or tee the full output to a file and inspect it. Only re-run after changing something.

Tests run in **fixed order by default** (`FIXED_TEST_ORDER` compile constant, set unless `-p:FixedTestOrder=false`; see `test/Directory.Build.props`). All tests lock culture to `en-US` via a module initializer (`test/Shared/ModuleInitializer.cs`).

Tests that require features Jet doesn't support are skipped with a reason on the test.

**`EFCore.Jet.Tests` contains no tests.** Its only test files have been `<Compile Remove>`d since the EF 9 update, so the project builds to an empty assembly and CI does not run it. Most of what was in there is covered by `EFCore.Jet.FunctionalTests` now. Don't read a green run of it as coverage.

### Green-tests baseline

`EFCore.Jet.FunctionalTests` is gated by a committed pass-list rather than by "everything must pass":
`test/EFCore.Jet.FunctionalTests/GreenTests/ace_<version>_<odbc|oledb>_<arch>.txt` lists the tests that passed
previously for that matrix leg. CI merges the shards' `.trx` files and **fails if any listed test stops passing**;
newly-passing tests are appended and pushed back by the `auto_commit` workflow. So the meaningful question for a
change is "did anything that used to pass stop passing", not the raw failure count.

CI splits the functional suite into three shards (query core / Northwind+GearsOfWar / non-query) and retries a shard
up to three times if the runner crashes. `EFCore.LibRed.FunctionalTests` is `continue-on-error` for now — it still
runs on every push, but its remaining failures don't block.

### Docker images

`.docker/` builds Windows images preloaded with a given ACE version and bitness
(`windows-ace-{12.0,16.0,none}-{x64,x86}.dockerfile`, built by `BuildAllDockerfiles.ps1`) — useful for reproducing a
matrix leg locally without installing that ACE on your own machine.

## Project Structure

```
src/
  EFCore.Jet.Data/      ADO.NET driver — JetConnection, JetCommand, JetDataReader,
                        schema management, DUAL table simulation, connection pooling
  EFCore.Jet/           EF Core provider — query pipeline, migrations, scaffolding,
                        type mappings, value generation, conventions
  EFCore.Jet.Odbc/      Provider factory for ODBC data access
  EFCore.Jet.OleDb/     Provider factory for OLE DB data access
  Shared/               Shared source files compiled into multiple src projects
  LibRed/               Native, fully-managed Jet/ACE engine (cross-platform) —
                        see "LibRed" section below and src/LibRed/README.md

test/
  EFCore.Jet.Data.Tests/          Unit tests for the ADO.NET driver layer            [Windows + ACE]
  EFCore.Jet.FunctionalTests/     EF Core specification tests (adapted from EF Core's
                                  own suite); owns GreenTests/ and Northwind fixtures [Windows + ACE]
  EFCore.Jet.Tests/               EMPTY — its test files are <Compile Remove>d        [Windows + ACE]
  EFCore.Jet.IntegrationTests/    Integration scenario tests                          [Windows + ACE]
  JetProviderExceptionTests/      Exception-path tests; also hosts Northwind.accdb,
                                  which every LibRed suite links to as its fixture    [Windows + ACE]
  LibRed.Core.Tests/              File-format read/write, cross-checked against ACE   [Windows + ACE]
  LibRed.Engine.Tests/            Planner/executor, no engine dependency           [cross-platform]
  LibRed.Engine.AccessTests/      Engine tests that cross-check against ACE           [Windows + ACE]
  LibRed.Ado.Tests/               ADO.NET surface                                     [Windows + ACE]
  LibRed.EFCore.Tests/            LibRed EF Core provider: query round-trip,
                                  database-first scaffolding                          [Windows + ACE]
  EFCore.LibRed.FunctionalTests/  EF Core specification suite over LibRed          [cross-platform]
  LibRed.Benchmarks/              BenchmarkDotNet harness (not a test project)
  Shared/                         ModuleInitializer.cs — locks culture to en-US

tools/
  sortkey-table/        Generates the Windows NLS sort-weight table LibRed's index keys use
  JetLockTrace/         Decodes ACE .laccdb/.ldb locking from a ProcMon capture
  Resources.tt          Resource generation template
```

Sibling instruction files: `AGENTS.md` (Codex) and `.github/copilot-instructions.md` mirror parts of this file —
when you change guidance here, check whether they need the same edit. `LIBRED_AUDIT_REPORT.md` (2026-07-18) records
a security/robustness audit of the LibRed production code and the fixes made from it.

## Architecture: Two-Layer Design

**Layer 1 — `EFCore.Jet.Data`** wraps the raw ODBC/OLE DB driver:
- `JetConnection` detects whether the connection string is ODBC or OLE DB and delegates to the appropriate inner `DbConnection`.
- `JetCommand` rewrites SQL at runtime: handles `SELECT SKIP`, emulates `@@ROWCOUNT`, rewrites `TOP @param`, parses `IF NOT EXISTS ... THEN ...` syntax, and intercepts stored-procedure creation.
- `JetConfiguration` holds global settings: `TimeSpanOffset` (Jet has no TimeSpan; dates are offset from 1899-12-30), `CustomDualTableName`, `IntegerNullValue`, `UseConnectionPooling`.
- Schema operations (create/drop database, list tables) have three implementations: ADOX, DAO, and Precise, selected based on available COM libraries.

**Layer 2 — `EFCore.Jet`** is the EF Core provider:
- `JetServiceCollectionExtensions.AddEntityFrameworkJet()` registers all provider services.
- `JetQuerySqlGenerator` extends `QuerySqlGenerator` to produce Jet-compatible SQL — converts `CAST` to Jet VBA functions (`CBOOL`, `CINT`, `CLNG`, etc.), handles boolean/numeric null semantics.
- `JetQueryTranslationPostprocessor` applies Jet-specific query rewrites in this order: skip/take transformation → base postprocessing → **append the query's last identifier column to `ORDER BY`** (deterministic tie-breaking, only when the query already orders) → optional millisecond support → ORDER BY lifting. `JetSkipTakePostprocessor` emulates `SKIP`/`OFFSET` since Jet only supports `SELECT TOP n`. Note it reaches `SelectExpression._identifier` by reflection, so an EF Core update can break it at runtime rather than at compile time.
- `JetMigrationsSqlGenerator` generates DDL for Access. It **does** emit `ALTER TABLE … ALTER COLUMN` (Jet's form folds the default value into the `ALTER COLUMN` rather than taking a separate operation); constraint support is still limited.
- `JetHistoryRepository` implements migration locking via a `__EFMigrationsLock` table with `LockReleaseBehavior.Explicit`.
- `JetRelationalConnection` creates an "empty" (masterless) connection for database creation/drop operations.

## Key Jet SQL Constraints

These shape much of the query pipeline complexity:
- No `OFFSET` — emulated via subquery or `TOP`+skip in the data layer
- `SELECT TOP n` only supports a literal integer, not a parameter (rewritten at command level)
- Subqueries in `SELECT` list are limited; scalar subqueries only work in `FROM`
- No parallel transactions (OLE DB)
- No millisecond precision in `DateTime`
- `CROSS JOIN` and mixed `JOIN`/comma syntax must be ordered correctly
- Booleans stored as `-1`/`0` (numeric), not `TRUE`/`FALSE`
- `GUID` support is indirect
- No `rowversion`, no `DateTimeOffset`, no nullable `BIT`

## Heritage: Jet Is Built on OLE Automation

Most of the constraints above are not arbitrary Jet choices — they are **OLE Automation semantics**, inherited
because Access is a VBA host built on OA/COM types, with DAO and ADOX as COM libraries over the top. Recognising
this makes the behaviour predictable rather than mysterious, and it tells you *where the spec lives*: when Access
and .NET disagree, the OA definition is usually the tiebreaker, and matching Access means matching a mid-90s COM
contract.

- **Dates** — the 1899-12-30 epoch is the OA `DATE` type: a `double`, integer part days, fraction time of day.
  OA has no TimeSpan, hence `JetConfiguration.TimeSpanOffset`.
- **Booleans as `-1`/`0`** — `VARIANT_BOOL`, where `VARIANT_TRUE` is `0xFFFF`. A VARIANT choice, not a storage one.
- **Currency** — OA's `CY`: an `int64` scaled by 10,000, so exactly four decimal places.
- **`GUID` being indirect**, and the general awkwardness of type coercion — everything funnels through VARIANT.
- **VBA functions are OA-era Basic runtime**: `ROUND` widens Currency to Double; `Rnd` is the VB6 24-bit LCG
  (see `SessionState.RandSeed`); `VarType`/`TypeName` return VARTYPE codes and VB type names, where `Integer`
  is Int16 and `Long Integer` is Int32 — a 16-bit-era naming hangover that regularly confuses.

> **Two kinds of OA dependency — know which you are touching.** Behaviour that is *contractually pinned* is safe:
> what we implement ourselves (the epoch constants, CY scaling, `VARIANT_BOOL`, the `Rnd` LCG, `VarType`), and
> BCL APIs defined against an OA type — `DateTime.FromOADate`/`ToOADate` specify the 1899-12-30 epoch and the
> days-plus-fraction double, so they cannot drift without breaking their own contract. The risk is *unspecified
> rounding or precision policy inside a general-purpose coercion* — `Convert.ToDecimal(double)` and friends —
> where the OA behaviour was implementation detail rather than contract, and can therefore change
> underneath us. It did: dotnet/runtime#130566 (.NET 11 preview 7) dropped `Convert.ToDecimal`'s 15-significant-digit
> rounding, which came from OA's own `VarDecFromR8` and had been stable since the 1990s. That turned
> `SUM(ROUND(UnitPrice, 2))` into `58.600000000000001421085471520`. The fix was to own it:
> `src/EFCore.Jet.Data/JetDecimalConverter.cs`. **When a long-stable conversion suddenly misbehaves with no code
> change on our side, suspect the runtime's OA-era compatibility behaviour before suspecting the provider.**

## LibRed — Native Managed Engine

(No longer a side branch — LibRed is on `master` and builds as part of `EFCore.Jet.sln`.)

A from-scratch, **fully managed and cross-platform** reimplementation of the Jet/ACE
engine under `src/LibRed/`. It reads and writes the `.mdb`/`.accdb` file format
**directly** — no ODBC, OLE DB, DAO, or ADOX — so it removes the Windows-only and
driver-bitness constraints that the rest of the repo lives with. Eventually it
subsumes the COM-based database creators (DAO/ADOX) and the OLE DB/ODBC quirk handling
in `EFCore.Jet.Data`.

**Five projects, clean dependency DAG** (`EFCore → Ado → Engine → Sql`, and `Engine → Core`):

```
src/LibRed/
  LibRed.Core/      File format: IO (PageChannel/PageBuffer), Formats (version offsets),
                    Pages, Catalog (MSysObjects → TableDef/ColumnDef/IndexDef), Storage
                    (Table/TableCursor/RowDecoder/UsageMap), Crypto; JetDatabase entry point
  LibRed.Sql/       SQL front end: ANTLR grammar (AccessSql.g4), AST, parser, binder.
                    NO Jet dependency — binds via the ISchemaProvider abstraction
  LibRed.Engine/    Logical Plan nodes, QueryPlanner, CatalogSchemaProvider (bridges the
                    catalog to the binder), QueryExecutor, QueryEngine facade
  LibRed.Ado/       ADO.NET surface: DbConnection/Command/DataReader/Parameter/Transaction/Factory
  LibRed.EFCore/    EF Core provider over LibRed.Ado: AddEntityFrameworkLibRed/UseLibRed,
                    connection, database creator, database-first scaffolding (query round-trips
                    + scaffolding pass; IQuerySqlGeneratorFactory override still planned)
```

**Format version gating and auto-upgrade — LibRed already does this.** Don't grep for `JetCapabilities`;
the names are `JetVersion` / `RequiredVersion` / `EnsureFormatAtLeast`:

- `LibRed.Formats.JetVersion` enumerates the on-disk version byte (`Version3` = 0x00 … `Version17_2019` = 0x06).
- `AccessTypeMapper.RequiredVersion(typeName)` is the capability table: `BIGINT` needs `Version16_2016` (0x05),
  `DATETIME2` needs `Version17_2019` (0x06). **Different thresholds** — the natural assumption that ACE 16 added
  both at once is wrong, and it's measured in `docs/format/page-00-database.md`.
- `AccessTypeMapper.MapType` **refuses** a type the open file is too old for, so a caller that can't upgrade
  (read-only database) fails loudly instead of writing a column Access couldn't read.
- `StatementExecutor.MapColumn` → `JetDatabase.EnsureFormatAtLeast` → `PageChannel.RaiseFormatVersion`
  **raises the file's version byte** rather than refusing the DDL, which is what ACE itself does. The raise goes
  through `WritePage`, so it joins the statement's transaction and a failed `CREATE`/`ALTER` takes it back down;
  both rollback paths re-derive the in-memory `Format` from the version byte then visible.

The Jet provider (`src/EFCore.Jet*`) has **no** equivalent — it does no ACE version detection, and
`JetTypeMappingSource` keeps `{"bigint", …}` commented out, so `long` maps to `decimal(20,0)` regardless of the
installed engine. That asymmetry is deliberate for now; don't "fix" one side by assuming the other behaves the same.

**Build configuration:** `src/LibRed/Directory.Build.props` deliberately bypasses
`src/Directory.Build.props` (it imports the repo-root props directly) so these assemblies
are **not** stamped `[SupportedOSPlatform("windows")]`. Strong-naming is preserved.

**SQL pipeline** (always run end-to-end, even for trivial queries, so new features add
node types rather than rewrites):
`text → ISqlParser → AST → Binder(ISchemaProvider) → BoundStatement → QueryPlanner → PlanNode → QueryExecutor → ResultSet`

**Format spec:** `src/LibRed/docs/format/` is LibRed's own verified reference for the on-disk
Jet 4 / ACE format, **split one file per page type** (plus cross-cutting topics). Start at
`format/README.md` — it maps every page type and the original §-numbers to their file, and links
the `appendix-structures.md` bare field-layout reference. It is the source of truth — keep it
updated as the format understanding grows. (`docs/jet-ace-file-format.md` is now just a redirect
stub to that folder.) Alongside it: `docs/functions.md` catalogs the supported VBA/Access function surface,
`docs/design/transactions.md` covers the page-level undo log, and `docs/mdbtools-spec-diff-todo.md`
tracks where our spec and mdbtools' still disagree.

> **Rule — spec sync on every `LibRed.Core` change.** Whenever you touch the actual on-disk
> read/write code in `src/LibRed/LibRed.Core/` (page/row/index/TDEF/usage-map parsing or
> writing, type codecs, key encoding), you **must** check whether the relevant file under
> `src/LibRed/docs/format/` needs updating in the same change, and update it if so — structure/field
> changes in the matching `page-0X-*.md` (and the `appendix-structures.md` table). New offsets,
> structures, type behaviours, or write mechanics go in the spec; only record facts **verified**
> against real files (or Access's own engine) — mark anything assumed as such. If a `LibRed.Core`
> change genuinely needs no spec edit, that's fine — but the check is not optional.

Reference implementations for the binary layouts: **mdbtools** (its `HACKING.md` documents the
on-disk structures) and **Jackcess** — neither is vendored in this repo; consult them upstream.
The project detail and current status live in `src/LibRed/README.md`. ANTLR **is** the active SQL
parser: the lexer/parser are pre-generated and committed under `LibRed.Sql/Grammar/Generated/`
(managed `Antlr4.Runtime.Standard` runtime only, no build-time codegen), regenerated via
`LibRed.Sql/Grammar/generate.ps1` after editing `AccessSql.g4`.

> **LibRed is a single-writer engine.** It tolerates extra open handles (a `.accdb` is a shared-file database and
> EF's own test infra keeps a store connection open), but there is no lock file, no read isolation, and the
> transaction undo log is per-`PageChannel`. Two concurrent writers corrupt the file. See `src/LibRed/README.md`
> for the full statement of what is and isn't safe before designing anything concurrent on top of it.

## Working in This Repo

`.claude/settings.json` installs `PreToolUse` hooks that **deny** two things in `Bash`/`PowerShell`:

- **Reading files through the shell** (`cat`, `head`, `grep`, `ls`, `find`, `Get-Content`, `Select-String`, …) —
  use `Read`, `Grep`, `Glob` instead. Shell text tools stay allowed on paths containing `scratchpad`, `.log` or `/tmp/`.
- **Editing source files through the shell** (`sed -i`, redirects/`tee` into `.cs`/`.md`/`.csproj`/`.props`/`.json`/
  `.ps1`/`.g4`, and any `python` invocation) — these bypass file checkpointing, so `/rewind` cannot undo them.
  Use `Edit`/`Write`.

`dotnet build`/`test`/`restore` and read-only `git` commands are pre-allowed, so don't work around the hooks — the
denial message is telling you which tool to use, not that the action is forbidden.

## CI

`.github/workflows/push.yml` runs on every branch. A `Changes` job path-filters the push's **own** delta into two
flags — `jet` and `libred` — and skips the jobs that don't apply:

- **BuildAndTest** — the Jet matrix: ACE 2010/2016 × x64/x86 × ODBC/OLE DB on `windows-latest`. The x86 legs patch
  `IMAGE_FILE_LARGE_ADDRESS_AWARE` onto `dotnet.exe` and the test hosts, because ACE in a 2GB address space dies
  partway through the largest shard.
- **LibRed** — `LibRed.Engine.Tests` on Linux/Windows/macOS + ubuntu-arm/windows-arm, no ACE anywhere.
- **LibRedAccess** — the ACE cross-check suites on `windows-latest` with ACE 2016.
- **LibRedFunctional** — `EFCore.LibRed.FunctionalTests` on the five-platform matrix, `continue-on-error`.
- **NuGet** — packs and pushes to MyGet/NuGet for `master`, `*-servicing`, `*-wip` and release tags.

## Versioning

`Version.props` owns `VersionPrefix` and `PreReleaseVersionLabel`. Bump `VersionPrefix` after each release. Valid labels: `alpha`, `beta`, `silver`, `preview`, `rc`, `rtm`, `servicing`. CI sets `OfficialVersion`, `ContinuousIntegrationTimestamp`, and `BuildSha` automatically.
