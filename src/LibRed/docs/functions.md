# Supported functions

LibRed's expression evaluator (`LibRed.Engine/Execution/ExpressionEvaluator.cs`) implements the Access / Jet
scalar function surface, and on top of it a set of standard-SQL functions Access does not have. This is the
authoritative catalog of both.

- [Access / VBA functions](#access--vba-functions) — the surface ACE has, verified against it.
- [Extended functions](#extended-functions) — LibRed extensions, which ACE does not have.
- [Not supported](#not-supported-by-design) — what is absent on purpose.

---

## How functions work

**Where functions can be used.** There is **one** `ExpressionEvaluator`, shared by every place an expression
appears, so a function added for one context is available in all of them:

- `SELECT` projections, `WHERE`, `ORDER BY`, `GROUP BY` / `HAVING`
- column `DEFAULT` expressions (see [page-02c-default-values.md](format/page-02c-default-values.md))
- table/column `CHECK` constraints

The only difference between contexts is the **scope**: a query evaluates against a **row scope** (so
`IIf([N] > 10, …)` can read column `N`), while a `DEFAULT` evaluates against an **empty scope** (a column
reference throws "Column not found" — a default is row-blind by design). The exception is the
[extended functions](#extended-functions): anything stored in the file must use only the Access / VBA ones.

**Conventions.**

- Every function **propagates Null** (a Null argument gives Null) unless noted.
- String positions are **1-based**.
- String comparisons are **case-insensitive** by default (Access "Option Compare Database" = Text) and take an
  optional compare argument.
- Values follow VBA sign and rounding conventions (`CInt`/`CLng`/`CByte` use banker's rounding).
- **Dates compare by their OLE Automation serial**, not chronologically: below the 1899-12-30 epoch the day count
  goes negative while the time fraction stays positive, so 1899-12-29 06:00 is -1.25 and 18:00 is -1.75, and ACE
  orders the later time first. LibRed matches that, because `IndexKeyEncoder` writes the same serial as the index
  key and the two paths must agree (see `AcePreEpochDateProbeTest`). Date *functions* are unaffected — they work
  in date space.
- **Argument counts are validated** against a per-function range, including optional arguments and Jet quirks:
  `IIf` accepts two or three arguments (an omitted false branch gives Null), `Choose` needs an index and at least
  one choice, and `Switch` needs at least one complete condition/value pair. Aggregate calls are checked by the
  same contract. A wrong count is refused rather than silently ignored.

> **Two expression services — what "ACE has it" means.** Access has (1) the **Jet/ACE OLE DB Expression
> Service (JES)**, the built-in set the ACE OLE DB provider carries **standalone**, and (2) the **Access
> Application Expression Service**, the full VBA runtime available only inside `MSACCESS.EXE`. LibRed targets
> the **JES (standalone)** surface — the correct reference for a standalone engine. Functions that live only
> in the application service (`Nz`, `Split`, `Environ`, `CurDir`, `CurrentUser`, the domain aggregates
> `DCount`/`DLookup`/…) are therefore **correctly absent**, matching the OLE DB provider ("Undefined
> function"), not a gap.

---

## Access / VBA functions

The JES surface, **verified against ACE** via a function-whitelist probe sweep (`FunctionWhitelist*Probe`). It is
close but *not proven exhaustive* — re-run the sweep when in doubt.

### Type conversion

`CBool` `CByte` `CInt` `CLng` `CSng` `CDbl` `CCur` `CStr` `CDate` `CVar`

- A **Boolean** converts as VARIANT_BOOL, so True is **-1**, not 1: `CInt`/`CLng`/`CSng`/`CDbl`/`CCur` all give
  -1, and `CByte` overflows because a byte cannot hold it.
- `CStr` renders a Double at **15 significant digits** and a Single at **7** (the OA/VB convention, not .NET's
  shortest round-trippable form), and a Boolean as `"-1"` — the Jet Expression Service's answer, where the VBA
  runtime proper would say `"True"`.
- `CBool` accepts a numeric string (`"-1"`) and a non-integral number.
- `CCur` rounds to 4 decimal places, and is ACE's route to a decimal.
- `CVar` is a pass-through — LibRed has no distinct Variant type.

(Verified against ACE in `LibRed.Core.Tests.AceVbaConversionProbeTest`.)

### Math

`Abs` `Sgn` `Int` (floor, toward −∞) `Fix` (truncate, toward zero) `Round` (banker's) `Sqr` `Exp` `Log`
(natural) `Sin` `Cos` `Tan` `Atn` `Rnd`

`Abs`, `Int`, `Fix` and `Round` keep their operand's type; `Int` and `Fix` of a date give a date. `Sgn` is a
Long (Int32) where ACE's is an Integer (Int16); the rest are Doubles. An argument outside a function's domain (`Sqr(-1)`, `Log(0)`) is an invalid
procedure call, and a result past a Double an overflow.

### String

`Len` `LCase` `UCase` `Trim` `LTrim` `RTrim` `Left` `Right` `Mid` `InStr` `InStrRev` `Replace` `Space` `String`
`StrReverse` `StrComp` `StrConv` `Str` `Val` `Chr` `Asc` `Hex` `Oct`

- `Trim`/`LTrim`/`RTrim` strip the space and the ideographic space U+3000 — nothing else.
- **`LTrim(x, characters)` / `RTrim(x, characters)`** (a LibRed extension, SQL Server 2022's): the second
  argument is a **set** of characters, not a substring — every leading (or trailing) character that appears
  anywhere in it is removed, stopping at the first that does not. An empty set strips nothing; either argument
  Null gives Null. ACE takes only the one argument, and `Trim` takes only the one here too.
- `Asc` and `Chr` work in the system ANSI code page (`Chr` takes 0–255).
- A value that is not text is read as `CStr` writes it.

### Date and time

- **Current** — `Now` `Date` `Time` `Timer`
- **Arithmetic** — `DateAdd` `DateDiff` `DatePart` `DateSerial` `TimeSerial`
- **Parsing** — `DateValue` `TimeValue`
- **Parts** — `Year` `Month` `Day` `Hour` `Minute` `Second` `Weekday` (Sunday = 1)
- **Names** — `MonthName` `WeekdayName`

`Time` and `TimeValue` sit on the Jet epoch 1899-12-30. LibRed keeps milliseconds where ACE rounds to the second.

### Formatting

`Format` `FormatCurrency` `FormatNumber` `FormatPercent` `FormatDateTime` `Partition`

`Format` maps VBA custom and named formats onto .NET; it is culture-driven, so the date and currency named formats
are locale-dependent by design. `Partition` gives a `"lower:upper"` range-bucket label.

### Logical and selection

- `IIf` — only the branch taken is evaluated.
- `Choose` — 1-based; an index out of range gives Null.
- `Switch` — the first true condition's value; the arguments come in pairs.

### Inspection

`IsNull` `IsNumeric` `IsDate` `IsError` `TypeName` `VarType`

`IsError` is always False — LibRed has no error value type — but its argument is still evaluated, so an error in
it is raised.

### Financial

`Pmt` `FV` `PV` `NPer` `IPmt` `PPmt` `Rate` `SLN` `SYD` `DDB`

Closed-form annuity and depreciation, and `Rate` by Newton–Raphson; verified against ACE to ~1e-6. `IRR`/`NPV` need
an array argument, so have no scalar-SQL form.

### Colour

`RGB` (`r + g·256 + b·65536`) · `QBColor` (the 16-entry BGR table).

### Name variants (`$` / `B` / `W`)

- **`$` — string-returning** (`Left$`, `UCase$`, `Chr$`, `Str$`, `Format$`, …): the lexer allows a trailing `$` on
  an identifier and the evaluator strips it before dispatch, so **every** function gains its `$` form and computes
  the same value. (ACE exposes `$` only for classic functions; the alias is harmless where ACE doesn't.)
- **`B` — byte-based** on the UTF-16 layout (2 bytes/char): `AscB` `LenB` `LeftB` `RightB` `MidB` `InStrB`
  (`LenB('abc')` = 6). `ChrB` is intentionally **absent** (ACE's JES has none either).
- **`W` — wide / Unicode**: `AscW` (the first character's UTF-16 code unit, signed) · `ChrW` (the character for a
  code unit, -32768 to 65535 — `ChrW(233)` = 'é').

### Niladic (callable without parentheses)

Only **`Now`** is niladic — ACE accepts bare `Now` (e.g. `DATETIME DEFAULT Now`). Bare `Date` / `Time` are
**reserved type keywords** in Jet SQL and must be written `Date()` / `Time()`. A real column named `Now` still
shadows the function.

### Default-only generators

These are **not** callable in an ACE `SELECT` ("Undefined function") but are valid as a column `DEFAULT`, evaluated
per inserted row:

- **`GenUniqueID()`** — a random signed `Long` (Int32); valid only on a `LONG` column. It is the mechanism behind a
  **"Random" AutoNumber** (see [system-catalog](format/system-catalog.md)).
- **`GenGUID()`** — a fresh `Guid` per row; EF Core models it as `HasDefaultValueSql("GenGUID()")` for
  store-generated GUID keys.

LibRed is slightly more permissive than ACE here — it *will* also evaluate them in a `SELECT`. Harmless: it never
produces a file ACE can't read.

### System variables

Connection-scoped, written `@@NAME`, usable anywhere an expression is allowed:

- **`@@IDENTITY`** — the last AutoNumber generated on this connection (Null if none). Only overwritten by an insert
  that actually generates one, so an intervening keyless insert leaves it intact — which is why EF Core reads it
  in the `SELECT` immediately after an `INSERT`.
- **`@@ROWCOUNT`** — rows affected by the previous statement.

A bare `SELECT @@IDENTITY` / `SELECT @@ROWCOUNT` (a comma list of system variables, optionally aliased) is allowed
without a `FROM` — the one all-`@@var` projection that may omit it, matching ACE.

### Aggregates

`Count` · `Sum` · `Avg` · `Min` · `Max` · `First` / `Last` · `StDev` / `Var` · `StDevP` / `VarP`

- Each takes `DISTINCT`.
- `First` / `Last` are the first/last row's value in scan order — **not** Null-filtered.
- `StDev` / `Var` are the sample statistics (÷ n−1, Null for n < 2); `StDevP` / `VarP` the population ones (÷ n).
  `StdDev` / `StdDevP` are accepted spellings.

**Result types** (matching Access and LINQ, so the EF provider round-trips without a cast): `Sum` **keeps the input
type** (int→int, long→long, decimal→decimal); `Avg` is a Double unless the input is Currency/Decimal; `Min`/`Max`
keep the column's own value and type. `Sum`/`Avg`/`Min`/`Max` of no rows is Null; `Count` is 0.

---

## Extended functions

LibRed extensions. ACE has none of these, so there is no parity contract to honour; where a standard exists they
follow it, and otherwise SQL Server or PostgreSQL. The engine accepts them in any query, whichever SQL mode EF is
using; SQL written to run on ACE as well must leave them out.

> **Queries only — never in anything stored in the file.** Extended functions are not allowed in views, procedures
> (saved queries), `CHECK` constraints, column `DEFAULT`s or calculated columns. Some of those may work while the
> file is only ever opened by LibRed, but the expression is stored in the file, and ACE or Access opening it will
> not understand it.

### Type conversion

- **`CLngLng`** — VBA's LongLong conversion (the JES reports it undefined). Reads its argument as `CLng` does, into
  an **Int64**, reading text exactly rather than through a Double.
- **`CDec`** — to a Decimal. The JES has no such function; `CCur` is ACE's route to a decimal.

### Math

`Floor` `Ceiling`/`Ceil` `Sign` `Sqrt` `Ln` `Log10` `Log(base, x)` `Power(x, y)` `Asin` `Acos` `Atan`
`Atan2(y, x)` `Sinh` `Cosh` `Tanh` `Degrees` `Radians` `Pi()`

- Where Access has the function under another name it *is* that function, reading its arguments and failing the
  same way: `Floor` is `Int`, `Sqrt` is `Sqr`, `Ln` is `Log`, `Atan` is `Atn`, `Sign` is `Sgn`, and `Power` is the
  `^` operator.
- `Floor` and `Ceiling` keep their operand's type; `Sign` is a Long, as `Sgn` is; the rest are Doubles.
- `Log` with one argument is still Access's natural log; the two-argument form takes the base first, as the
  standard and PostgreSQL do (SQL Server's `LOG(x, base)` is the other way round).
- An argument outside a function's domain (`Asin(2)`, `Log(1, 5)`) is an invalid procedure call, and a result past
  a Double an overflow.

### Null handling and selection

- **`Coalesce(x, …)`** — the first non-Null argument. One argument or more.
- **`NullIf(x, y)`** — Null when the two are equal, otherwise `x`; typed as `x`.
- **`Greatest(x, …)` / `Least(x, …)`** — the largest / smallest argument, ignoring Null arguments as SQL Server
  and PostgreSQL do. Extended mode translates `Math.Max`/`Math.Min` to them.

`Coalesce`, `Greatest` and `Least` declare the type their arguments unify to, as `CASE` does (`CASE` itself is
syntax, not a function).

### Date and time

- **`DatePart`** also takes `"ms"`, `"mcs"` and `"ns"`: the millisecond, microsecond and nanosecond of the time.
- **`DateAdd`** also takes `"ms"`.
- **`DateDiff`** also takes `"ms"`, counting in **Int64** — a millisecond difference passes Int32 after 25 days.

ACE's interval list stops at `"s"`.

### Storage size

**`DataLength(x)`**, SQL Server's — the bytes a value takes as Access stores it. Text is 2 per character (UTF-16,
trailing spaces counted, on-disk compression ignored), binary its length, Byte 1, Integer 2, Long/Single 4,
Double/Currency/Date/BIGINT 8, GUID 16, Decimal 17; a Boolean — a bit of the null bitmap on disk — counts 1, as SQL
Server counts a `bit`. A Long.

### Aggregates

- **Standard statistic names** — `STDDEV_SAMP`, `STDDEV_POP`, `VAR_SAMP` and `VAR_POP`: the same aggregates as
  `StDev`, `StDevP`, `Var` and `VarP`.
- **Binary set functions** over `(y, x)` pairs, using only the pairs where neither is Null — `CORR`, `COVAR_POP`,
  `COVAR_SAMP`, `REGR_COUNT`, `REGR_AVGX`, `REGR_AVGY`, `REGR_SXX`, `REGR_SYY`, `REGR_SXY`, `REGR_SLOPE`,
  `REGR_INTERCEPT` and `REGR_R2`. `REGR_COUNT` is a Long, the rest Doubles; they take no `DISTINCT`. Every x the
  same makes the slope, intercept, R² and correlation Null; every y the same makes the correlation Null and R² 1.
- **Ordered-set aggregates** — `PERCENTILE_CONT(p)` and `PERCENTILE_DISC(p) WITHIN GROUP (ORDER BY x [DESC])`: the
  value at fraction `p` (0 to 1) of the ordered non-Null values. `PERCENTILE_CONT` interpolates linearly (a
  Double, or a date for dates); `PERCENTILE_DISC` is the first value whose cumulative share reaches `p`, in the
  key's own type. Neither takes `DISTINCT`.
- **`LISTAGG([DISTINCT] x [, 'separator']) WITHIN GROUP (ORDER BY k [DESC], …)`** — the non-Null values as text
  (each written as `&` writes it) in that order, joined by the separator: a string literal, as the standard has
  it, and none when left out. Null when there are no values.
- **`STRING_AGG([DISTINCT] x, 'separator') [WITHIN GROUP (ORDER BY k [DESC], …)]`** — SQL Server's spelling of
  the same aggregate, and it computes the same thing. The two differ only in what each insists on: `LISTAGG`
  needs the `WITHIN GROUP` and lets the separator go, `STRING_AGG` needs the separator and lets the order go.
  Without a `WITHIN GROUP` the values list in the order the rows arrive — over a window, in window order.
- **`FILTER (WHERE condition)`** — every aggregate, Access's included, takes it after the call (and after `WITHIN
  GROUP`): only the rows the condition is true for go in, so `COUNT(*) FILTER (WHERE x > 1)` counts those rows,
  and a group none of whose rows pass has what an empty group has.

### Window functions

Called with `OVER ([PARTITION BY …] [ORDER BY …] [frame])`:

- **Ranking** — `ROW_NUMBER()` `RANK()` `DENSE_RANK()` `NTILE(n)` `PERCENT_RANK()` `CUME_DIST()`
- **Offset** — `LAG(x [, offset [, default]])` `LEAD(x [, offset [, default]])`
- **Value** — `FIRST_VALUE(x)` `LAST_VALUE(x)` `NTH_VALUE(x, n) [FROM FIRST | FROM LAST]`
- **Aggregates** — every aggregate above, Access's and the extended ones, over a window. The one-argument
  aggregates also take `DISTINCT` there.

`LAG`, `LEAD` and the value functions take `RESPECT NULLS` or `IGNORE NULLS` before the `OVER`. The frame and the
behaviour over a grouped query are described in the [README](../README.md).

---

## Not supported (by design)

- **Access-application-only** (JES-undefined, so correctly absent): `Nz`, `Split`, `CurDir`, `CurrentUser`,
  `Environ`, `Randomize`, and the domain aggregates. `Split` also returns a Variant array (no scalar-SQL
  representation).
- **No scalar-SQL form:** `IRR` / `NPV` (array argument); `Array` / `Join` / `CVErr` (VBA-only).

See [page-02c-default-values.md](format/page-02c-default-values.md) for how these functions behave specifically in a
column `DEFAULT` (the DDL-parser-vs-expression-service split, and the forbidden categories).
