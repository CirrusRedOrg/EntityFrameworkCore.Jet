# Column DEFAULT values in Jet/ACE (and LibRed)

Reference for how a column's `DEFAULT` behaves in the Jet/ACE engine and how LibRed matches it. Everything here
is **verified against ACE** (probed via `Microsoft.ACE.OLEDB.16.0`) and, where noted, corroborated by the DAO
[`Field2.DefaultValue`](https://learn.microsoft.com/office/client-developer/access/desktop-database-reference/field2-defaultvalue-property-dao)
reference. Companion tests live in `LibRed.Engine.Tests` (engine semantics) and `LibRed.Core.Tests` (ACE
round-trips); the on-disk storage of the default text is covered in
[`jet-ace-file-format.md`](jet-ace-file-format.md) (LvProp / `DefaultValue`).

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
  [`jet-ace-file-format.md`](jet-ace-file-format.md).
- **`Now` (bare)** — `Now` is a niladic function callable without parentheses (`DATETIME DEFAULT Now`). `Date`
  and `Time` are **not** — they are reserved type keywords, so they require parentheses (`Date()`, `Time()`).
- **Date/time defaults** — `Now()` → timestamp, `Date()` → today midnight, `Time()` → current time on the Jet
  epoch (1899-12-30).
- **Conditionals** — `IIf`, `Choose`, `Switch` all work as defaults (and anywhere else an expression is allowed).

## Function whitelist — LibRed vs ACE

A full cross-check of Access scalar functions through ACE's expression service and LibRed's evaluator.

**Aligned — both implement (~54):** `CBool CByte CCur CDate CDbl CInt CLng CSng CStr CVar` · `Len LCase UCase
Trim LTrim RTrim Left Right Mid InStr Replace` · `Chr Space String StrReverse StrComp Str Val Hex Oct InStrRev` ·
`Abs Int Fix Sgn Round Sqr Sin Cos Tan Atn Exp Log Rnd Timer` · `Now Date Time Year Month Day Hour Minute Second
Weekday DatePart DateAdd DateDiff DateSerial TimeSerial DateValue TimeValue IsDate MonthName` · `IIf Choose Switch
IsNull IsNumeric IsError TypeName VarType`.

(The second half of that list — `Chr Space String StrReverse StrComp Str Val Hex Oct InStrRev Rnd Timer MonthName
IsNull IsNumeric IsError TypeName VarType`, plus `Choose`/`Switch` — were **added to LibRed** as a result of this
cross-check.)

**Deferred — ACE has, LibRed does not (yet):**

| Function | Why deferred |
|---|---|
| `Format` | Full VBA format-string engine — large, and **locale-sensitive** (see below). Still deferred. |

`Partition`, `StrConv`, and `WeekdayName` are now **implemented** (`DeferredFunctionsTests`):

- **`Partition(number, start, stop, interval)`** — a `"lower:upper"` range label, both sides right-justified to a
  fixed width = `max(len(str(start-1)), len(str(stop+1)))`. Below range → `"   :  0"` (lower blank, upper=start-1);
  above range → `"101:   "` (lower=stop+1, upper blank); in range → the interval bucket (`Partition(5,1,100,10)`
  → `"  1: 10"`, `Partition(100,1,100,10)` → `" 91:100"`). Fully deterministic — matches ACE.
- **`StrConv(string, conversion)`** — `1`=UpperCase, `2`=LowerCase, `3`=ProperCase (title case: `"mixed CASE
  text"` → `"Mixed Case Text"`). Modes ≥4 (Wide/Unicode) → "Invalid procedure call". Matches ACE.
- **`WeekdayName(weekday, [abbreviate=False], [firstdayofweek])`** — day name for `weekday` (1–7) counting from
  `firstdayofweek` (1=Sun … 7=Sat). `abbreviate` → 3-letter. Matches ACE with an explicit `firstdayofweek`
  (`WeekdayName(1,,1)`→"Sunday", `WeekdayName(1,,2)`→"Monday"). **Caveat:** ACE's *omitted* `firstdayofweek`
  follows the OS regional first-day (ACE gave "Monday" on an en-AU host); LibRed fixes the omitted default to
  `vbSunday` for determinism, so the no-third-arg case can differ from a given ACE host.

Still deferred:

- **`Format(value, format)`** — a large VBA format-string engine. Two classes:
  - **Custom format strings are deterministic** and mostly map to .NET numeric formats directly (`'0.00'`,
    `'#,##0.00'`, `'0%'`, `'000'`, `'\#0'`→`"#255"`). Date custom formats need **VBA→.NET token translation**
    (VBA `mm`=month/`nn`=minutes/`hh`=hour vs .NET `MM`/`mm`/`HH`; plus `q`=quarter which .NET lacks):
    `'yyyy-mm-dd'`→`2020-06-15`, `'hh:nn:ss'`→`13:05:09`, `'mmmm d, yyyy'`→`June 15, 2020`, `'ddd'`→`Mon`.
    String: `'>'`→upper, `'<'`→lower.
  - **Named formats are OS-locale-sensitive**: `'Currency'`→`$1,234.50`, `'Short Date'`→`15/06/2020`,
    `'Long Date'`→`Monday, 15 June 2020` (all en-AU on the probe host). Also `'Fixed'`/`'Standard'`/`'Percent'`
    (`25.00%`)/`'Scientific'` (`1.23E+03`)/`'General Number'`/`'Yes/No'`→`Yes`/`No`. Implementable but the
    named date/currency forms won't be byte-identical across locales.

**Divergences — LibRed more permissive than ACE:**

| Function | ACE | LibRed | Note |
|---|---|---|---|
| `GenUniqueID()` in a `SELECT` | rejected ("Undefined function") | evaluates (random Long) | ACE restricts it to a default; LibRed allows it as a general function. Harmless. |
| `CDec(x)` | rejected in a query expression | evaluates | ACE's query engine doesn't expose `CDec`; low concern. |

**Aligned rejection:** `Nz` — ACE's *engine* has no `Nz` ("Undefined function"; it's an Access-application
function), and neither does LibRed. Correctly **not** added.

> The whitelist is now closely aligned but **not proven exhaustive** — `Choose`/`Switch` and the string/type
> batch were gaps this sweep surfaced; only `Format` remains deferred. Re-run the sweep (`FunctionWhitelist*Probe`
> pattern) when in doubt.

### VBA function-name variants (`$` / `B` / `W`)

Many string functions have VBA variant spellings. Which ones ACE's expression service exposes, and how LibRed
handles them (`FunctionVariantTests`):

- **`$` — string-returning** (`Left$`, `UCase$`, `Chr$`, `Space$`, `String$`, `Str$`, `Hex$`, `Oct$`,
  `Format$`, …). ACE exposes them for the classic functions (**not** newer ones — `StrReverse$` is undefined).
  They compute the same value as the base function. LibRed: the lexer allows a trailing `$` on an identifier
  (longest-match makes `Left$` an identifier, not the `LEFT` keyword) and the evaluator strips it before
  dispatch — so every base function gains its `$` form automatically.
- **`B` — byte-based** on the UTF-16 layout, 2 bytes/char. ACE exposes `AscB LenB LeftB RightB MidB InStrB`
  (`LenB('abc')`=6, `InStrB(1,'abc','b')`=3, `LeftB('abc',2)`='a') — **not** `ChrB`. LibRed implements the
  supported six with byte↔char mapping; `ChrB` is intentionally omitted to match ACE.
- **`W` — wide/Unicode code point**: `AscW` (first char's code point) and `ChrW` (char for a code point, not
  limited to a byte — `ChrW(233)`='é'). Both implemented.

Base **`Asc`** required a grammar fix (`ASC` is the index-direction keyword; `functionName` now also accepts
`ASC`, unambiguous because a call is always followed by `(`).

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
