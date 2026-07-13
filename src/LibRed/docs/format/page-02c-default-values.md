# Column DEFAULT values in Jet/ACE (and LibRed)

> Part of the [LibRed Jet / ACE file-format reference](README.md). This page covers `DEFAULT` **semantics**
> (engine behaviour); the on-disk storage of the default text is in [system-catalog.md](system-catalog.md),
> and the function catalog in [`../functions.md`](../functions.md).

Reference for how a column's `DEFAULT` behaves in the Jet/ACE engine and how LibRed matches it. Everything here
is **verified against ACE** (probed via `Microsoft.ACE.OLEDB.16.0`) and, where noted, corroborated by the DAO
[`Field2.DefaultValue`](https://learn.microsoft.com/office/client-developer/access/desktop-database-reference/field2-defaultvalue-property-dao)
reference. Companion tests live in `LibRed.Engine.Tests` (engine semantics) and `LibRed.Core.Tests` (ACE
round-trips); the on-disk storage of the default text is covered in
[`system-catalog.md`](system-catalog.md) (LvProp / `DefaultValue`).

## The mental model: more than a constant, less than a computed value

A Jet default is **a per-row expression, not a stored value**. It is kept as source text in the column's
extended-properties (`LvProp`) blob (e.g. the literal strings `"0"`, `"Now()"`, `"GenUniqueID()"`) and
**evaluated afresh on every insert** that omits the column. A constant literal is simply the degenerate case of
that expression.

```
constant literal   →   Jet DEFAULT expression   →   calculated column
 (static value)         (dynamic, row-blind)         (dynamic, row-aware)
```

- **More than a constant:** it can call functions and build expressions — `Now()`, `GenUniqueID()`,
  `"INV-" & Year(Now())`, `IIf(...)`. The value is produced per row, not baked in at create time.
- **Less than a computed value:** it is **row-blind and context-free**. It sees only the *environment* (the
  clock, the RNG, constants, pure scalar functions). It cannot see other columns, other tables/queries, or
  aggregates. It also fires **once** — the result becomes an ordinary, user-editable stored value (a calculated
  column, by contrast, is bound and recomputes).

Access has both ends of that spectrum as distinct features: `DEFAULT <expr>` (this document) and **calculated
columns** (ACE 2010+, an expression column that *can* reference sibling columns) — the genuine "computed value".

## What a default may and may not contain

Verified against ACE and matching the DAO doc ("can't contain user-defined functions, … SQL aggregate
functions, or references to queries, forms, or other Field2 objects"):

| Allowed | Forbidden |
|---|---|
| Constants (numbers, strings, dates) | **Other columns** (`[A]`, `A + 2`) |
| Scalar functions (`Now()`, `UCase()`, `IIf()`, …) | **Tables / queries** (a subquery) |
| Operators: arithmetic, `&` concat, comparisons | **Aggregates** — SQL (`Sum`, `Count`) *and* domain (`DCount`) |
| Nested / compound expressions | **User-defined / unknown functions** |

LibRed rejects **every** forbidden category, matching ACE (see the parity table under "LibRed specifics").

## Two parsers: DDL front-end ⊊ expression service

The single most important structural fact: ACE has **two** places that touch a default, with different
capabilities.

1. **The OLE DB *DDL parser*** (`CREATE TABLE … DEFAULT <expr>`) is narrow and inconsistent. It accepts a
   literal or a simple function call, but rejects concatenation, **literal arithmetic** (`1+2` → "Syntax error in
   CREATE TABLE statement"!), and nested calls (`Year(Now())`, `CBool(Choose(...))`). Quirks abound: `Now()+30`
   parses but `1+2` doesn't; `(UCase('hi'))` parses but `UCase('hi')` doesn't. **Parentheses are not an
   expression-escape** (unlike SQL Server's `DEFAULT (expr)`) — `(1+2)` is still rejected.

2. **The expression *service*** (used to *evaluate* a stored default at insert, and by field validation rules)
   handles the full expression language.

Consequence: a compound default that the DDL parser refuses can still be **stored** (via the table designer, DAO,
or LibRed writing `LvProp` directly) and is then **read and applied** by ACE at insert. Verified:
`"INV-" & Year(Now())` → `INV-2026`, `1 + 2` → `3`, `Year(Now())` → `2026`, `CBool(Choose(1,0,1,2))` → `False`,
all written by LibRed and applied by ACE. **LibRed's own SQL front-end (ANTLR) is a superset of ACE's DDL** — it
parses these uniformly — so LibRed can create defaults ACE's DDL can't, and ACE consumes them.

The forbidden categories, by contrast, are enforced by the **expression service** at evaluation time, so they
cannot be smuggled past it: a column-ref default written to `LvProp` opens cleanly but is rejected on the insert
(`[A]+2` → "does not recognize … the field 'A' … or the default value"), and no row is written.

## The `DefaultValue` text itself

- Stored **verbatim**, preserving quote style (`'hello'` vs `"hello"` are kept as typed).
- The DAO **255-character** cap on `DefaultValue` is a **DAO-API limit, not an engine/file-format one**: ACE
  accepts and applies a 300-char default expression; LibRed round-trips 300+ char defaults. (A many-operator
  expression separately hits "Expression too complex" — an operator-count limit, unrelated to length.)

## Special defaults

- **`GenUniqueID()`** — Access's random-`Long` generator and the mechanism behind a **"Random" AutoNumber**
  (`New Values = Random`). Not callable in a `SELECT` (ACE: "Undefined function"), but valid as a **`LONG`-only**
  default — every other type is rejected ("Cannot place this validation expression on this field"). A Random
  AutoNumber is therefore an ordinary `Long` (AutoNumber or plain) column carrying `DEFAULT GenUniqueID()`, not a
  distinct field kind. LibRed reads, writes, and evaluates it (random non-zero Int32 per row); see
  [`system-catalog.md`](system-catalog.md).
- **`Now` (bare)** — `Now` is a niladic function callable without parentheses (`DATETIME DEFAULT Now`). `Date`
  and `Time` are **not** — they are reserved type keywords, so they require parentheses (`Date()`, `Time()`).
- **Date/time defaults** — `Now()` → timestamp, `Date()` → today midnight, `Time()` → current time on the Jet
  epoch (1899-12-30).
- **Conditionals** — `IIf`, `Choose`, `Switch` all work as defaults (and anywhere else an expression is allowed).

## Functions in a default

A default may call any of LibRed's supported scalar functions — the full catalog (with the JES-vs-Access
two-services distinction, the `$`/`B`/`W` variants, and the aggregate set) now lives in its own page:
**[functions.md](../functions.md)**. Defaults are the *narrowest* place functions are used; the same evaluator
serves `SELECT` / `WHERE` / `ORDER BY` / `CHECK`.

Default-specific points: `GenUniqueID()` / `GenGUID()` are valid **only** as a default (ACE rejects them in a
`SELECT`); the niladic `Now` works bare; and Access forbids a handful of categories in a default even though
they are otherwise valid expressions (see the table below).
## LibRed specifics

- **One evaluator.** Defaults, `SELECT` projections, `WHERE`, and `ORDER BY` share `ExpressionEvaluator`. Adding
  a function for defaults makes it available everywhere (and vice-versa).
- **Row scope decides row-awareness.** A query evaluates against a **row scope** (so `IIf(N>10,…)` can read the
  column `N`); a default evaluates against an **empty scope** (so a column reference throws "Column 'A' was not
  found"). Same function, different scope — that is exactly the "row-blind default vs row-aware query" line.
- **Rejection timing.** ACE rejects forbidden defaults (columns, aggregates, bad types) at **CREATE**; LibRed
  stores the text and rejects at **INSERT** (defaults are parsed lazily when applied). Net behaviour matches
  (both refuse); the messages and timing differ. *TODO:* optional create-time validation with ACE-like messages.
- **Superset grammar.** LibRed's ANTLR front-end parses compound/nested defaults ACE's DDL parser rejects; ACE
  still reads and applies them from `LvProp`.

### Parity of the forbidden categories

| Category | ACE | LibRed | Test |
|---|---|---|---|
| Column reference | reject | reject ("Column not found") | `DateTimeDefaultTests`, `AceSmuggledColRefDefaultTests` |
| Table / query (subquery) | reject | reject (parse) | `DateTimeDefaultTests` |
| SQL aggregate (`Sum`/`Count`) | reject | reject | `AceDefaultExpressionLimitsTests`, `DateTimeDefaultTests` |
| Domain aggregate (`DCount`) | reject | reject | same |
| Unknown / user-defined function | reject | reject ("not supported") | — |
