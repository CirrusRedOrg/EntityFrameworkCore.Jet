# Supported functions (VBA / Access expression surface)

LibRed's expression evaluator (`LibRed.Engine/Execution/ExpressionEvaluator.cs`) implements the Access /
Jet scalar function surface. This is the authoritative catalog of what's supported.

**Where functions can be used.** There is **one** `ExpressionEvaluator`, shared by every place an
expression appears — so a function added for one context is available in all of them:

- `SELECT` projections, `WHERE`, `ORDER BY`, `GROUP BY` / `HAVING`
- column `DEFAULT` expressions (see [page-02c-default-values.md](format/page-02c-default-values.md))
- table/column `CHECK` constraints

The only difference between contexts is the **scope**: a query evaluates against a **row scope** (so
`IIf([N] > 10, …)` can read column `N`), while a `DEFAULT` / niladic-context evaluates against an **empty
scope** (a column reference throws "Column not found" — a default is row-blind by design).

**Conventions.** All functions **propagate NULL** (a NULL argument yields NULL) unless noted; string
positions are **1-based**; string comparisons default to **case-insensitive** (Access "Option Compare
Database" = Text) and take an optional compare argument. Values follow VBA sign/rounding conventions
(`CInt`/`CLng`/`CByte` use banker's rounding). **Dates compare by their OLE Automation serial**, not
chronologically: below the 1899-12-30 epoch the day count goes negative while the time fraction stays
positive, so 1899-12-29 06:00 is -1.25 and 18:00 is -1.75 and ACE orders the later time first. LibRed
matches that, because `IndexKeyEncoder` writes the same serial as the index key and the two paths must
agree (see `AcePreEpochDateProbeTest`). Date *functions* are unaffected — they work in date space.

> **Two expression services — what "ACE has it" means.** Access has (1) the **Jet/ACE OLE DB Expression
> Service (JES)**, the built-in set the ACE OLE DB provider carries **standalone**, and (2) the **Access
> Application Expression Service**, the full VBA runtime available only inside `MSACCESS.EXE`. LibRed targets
> the **JES (standalone)** surface — the correct reference for a standalone engine. Functions that live only
> in the application service (`Nz`, `Split`, `Environ`, `CurDir`, `CurrentUser`, the domain aggregates
> `DCount`/`DLookup`/…) are therefore **correctly absent**, matching the OLE DB provider ("Undefined
> function"), not a gap. The whole surface below is **verified against ACE's JES** via a function-whitelist
> probe sweep (`FunctionWhitelist*Probe`); it is close but *not proven exhaustive* — re-run the sweep when in
> doubt.

---

## Scalar functions

**Type conversion** — `CBool` `CByte` `CInt` `CLng` `CSng` `CDbl` `CCur` `CDec` `CStr` `CDate` `CVar`
(`CVar` is a pass-through — LibRed has no distinct Variant type; `CCur` rounds to 4 dp). A **Boolean**
converts as VARIANT_BOOL, so True is **-1**, not 1 — `CInt`/`CLng`/`CSng`/`CDbl`/`CCur` all yield -1, and
`CByte` overflows because a byte cannot hold it. `CStr` renders a Double at **15 significant digits** and a
Single at **7** (the OA/VB convention, not .NET's shortest round-trippable form), and a Boolean as `"-1"` —
note that is the Jet Expression Service's answer, where the VBA runtime proper would say `"True"`. `CBool`
accepts a numeric string (`"-1"`) and a non-integral number. `CDec` has **no ACE equivalent** — the
expression service has no such function — so it is a LibRed extension with no parity contract; `CCur` is
ACE's route to a decimal. (All verified against ACE in
`LibRed.Core.Tests.AceVbaConversionProbeTest`.)

**Math** — `Abs` `Sgn` `Int` (floor, toward −∞) `Fix` (truncate, toward zero) `Round` (banker's) `Sqr`
`Exp` `Log` (natural) `Sin` `Cos` `Tan` `Atn` `Rnd` `Timer`.

**String** — `Len` `LCase` `UCase` `Trim` `LTrim` `RTrim` `Left` `Right` `Mid` `InStr` `InStrRev` `Replace`
`Space` `String` `StrReverse` `StrComp` `StrConv` `Str` `Val` `Chr` `Asc` `Hex` `Oct`.

**Formatting** — `Format` (VBA→.NET custom + named formats; culture-driven, so date/currency named formats
are locale-dependent by design) · `FormatCurrency` `FormatNumber` `FormatPercent` `FormatDateTime` ·
`Partition` (a `"lower:upper"` range-bucket label).

**Date / time** — `Now` `Date` `Time` · `DateAdd` `DateDiff` `DatePart` `DateSerial` `TimeSerial`
`DateValue` `TimeValue` · `Year` `Month` `Day` `Hour` `Minute` `Second` `Weekday` (Sunday = 1) · `MonthName`
`WeekdayName` · `IsDate`. (`Time`/`TimeValue` sit on the Jet epoch 1899-12-30.)

**Logical / selection** — `IIf` `Choose` (1-based; out-of-range → NULL) `Switch` (first true condition's
value; even arg count required).

**Inspection / predicates** — `IsNull` `IsNumeric` `IsDate` `IsError` (always False — LibRed has no error
value type) `TypeName` `VarType`.

**Financial** (closed-form annuity/depreciation; `Rate` by Newton–Raphson; verified vs ACE to ~1e-6) —
`Pmt` `FV` `PV` `NPer` `IPmt` `PPmt` `Rate` `SLN` `SYD` `DDB`. (`IRR`/`NPV` need an array argument — no
scalar-SQL form.)

**Colour** — `RGB` (`r + g·256 + b·65536`) · `QBColor` (16-entry BGR table).

### Name variants (`$` / `B` / `W`)

- **`$` — string-returning** (`Left$`, `UCase$`, `Chr$`, `Str$`, `Format$`, …): the lexer allows a trailing
  `$` on an identifier and the evaluator strips it before dispatch, so **every** base function gains its `$`
  form and computes the same value. (ACE exposes `$` only for classic functions; the alias is harmless where
  ACE doesn't.)
- **`B` — byte-based** on the UTF-16 layout (2 bytes/char): `AscB` `LenB` `LeftB` `RightB` `MidB` `InStrB`
  (`LenB('abc')` = 6). `ChrB` is intentionally **absent** (ACE's JES has none either).
- **`W` — wide / Unicode code point**: `AscW` (first char's code point) · `ChrW` (char for a code point, not
  limited to a byte — `ChrW(233)` = 'é').

### Niladic (callable without parentheses)

Only **`Now`** is niladic — ACE accepts bare `Now` (e.g. `DATETIME DEFAULT Now`). Bare `Date` / `Time` are
**reserved type keywords** in Jet SQL and must be written `Date()` / `Time()` (parsed as calls). A real
column named `Now` still shadows the function.

### Default-only generators

These are **not** callable in a `SELECT` (ACE errors "Undefined function") but are valid as a column
`DEFAULT`, evaluated per inserted row:

- **`GenUniqueID()`** — a random signed `Long` (Int32); valid only on a `LONG` column. It is the mechanism
  behind a **"Random" AutoNumber** (see [system-catalog](format/system-catalog.md)).
- **`GenGUID()`** — a fresh `Guid` per row; EF Core models it as `HasDefaultValueSql("GenGUID()")` for
  store-generated GUID keys.

(LibRed is slightly more permissive than ACE here — it *will* also evaluate `GenUniqueID()` / `CDec()` in a
`SELECT`, which ACE rejects. Harmless: it never produces a file ACE can't read.)

---

## System variables

Connection-scoped, written `@@NAME`, usable anywhere an expression is allowed:

- **`@@IDENTITY`** — the last AutoNumber generated on this connection (NULL if none). Only overwritten by an
  insert that actually generates one, so an intervening keyless insert leaves it intact — which is why EF
  Core reads it in the `SELECT` immediately after an `INSERT`.
- **`@@ROWCOUNT`** — rows affected by the previous statement.

A bare `SELECT @@IDENTITY` / `SELECT @@ROWCOUNT` (a comma list of system vars, optionally aliased) is
allowed without a `FROM` — the one all-`@@var` projection that may omit it, matching ACE.

---

## Aggregate functions

The complete Access SQL aggregate set (`QueryPlanner` + `QueryExecutor`), each supporting `DISTINCT`:

`Count` · `Sum` · `Avg` · `Min` · `Max` · `First` / `Last` (first/last row's value in scan order — **not**
null-filtered) · `StDev` / `Var` (sample: ÷ n−1, NULL for n < 2) · `StDevP` / `VarP` (population: ÷ n).
`StdDev` / `StdDevP` are accepted spellings.

**Result-type contract** (matches Access + LINQ so the EF provider round-trips without a cast): `Sum`
**preserves the input type** (int→int, long→long, decimal→decimal); `Avg` is `Double` unless the input is
Currency/Decimal; `Min`/`Max` keep the column's own value and type; `Sum`/`Avg`/`Min`/`Max` of no rows is
NULL (`Count` returns 0).

---

## Not supported (by design)

- **Access-application-only** (JES-undefined, so correctly absent): `Nz`, `Split`, `CurDir`, `CurrentUser`,
  `Environ`, `Randomize`, and the domain aggregates. `Split` also returns a Variant array (no scalar-SQL
  representation).
- **No scalar-SQL form:** `IRR` / `NPV` (array argument); `Array` / `Join` / `CVErr` (VBA-only).
- **Argument arity is not validated** — LibRed reads the arguments it needs and ignores extras, where ACE
  errors "Wrong number of arguments". A cross-cutting lenience, not per-function.

See [page-02c-default-values.md](format/page-02c-default-values.md) for how these functions behave specifically in a column
`DEFAULT` (the DDL-parser-vs-expression-service split, and the forbidden categories).
