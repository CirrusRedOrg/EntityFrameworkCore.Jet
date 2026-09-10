# Calculated columns — semantics

Column **`AS <expression>`** *semantics*: the expression language ACE accepts, how the cached result is
encoded, and when it is recomputed. Like [page-02c-default-values.md](page-02c-default-values.md) this
describes **engine behaviour** rather than on-disk layout; the 25-byte descriptor, the value envelope and the
long-value route live in [page-02b-columns §3.4a](page-02b-columns.md), and the `LvProp` property blob that
carries the expression is in [system-catalog.md](system-catalog.md).

Everything here is **measured** — against ACE-authored columns of every type DAO will create
(`CalculatedColumnAccessTests`), against `AdventureWorks_Learn_To_Write_DAX.accdb` from the wild, and from
the write side by having ACE read back what LibRed produced.

> **There is no single "what ACE does" to match.** Four independent gatekeepers enforce different rules and
> do not share a rulebook: **DAO** is the only thing that can author a calculated column and refuses to
> modify one afterwards; the **SQL/OLE DB** layer cannot declare one but will happily `ALTER` around it, and
> is the only layer that refuses to write one directly; the **storage engine** enforces the function
> whitelist, and errors on every read of a column whose expression it dislikes; and Access's **Expression
> Builder** offers `$` name variants the other three cannot sustain. Several behaviours below are therefore a
> *choice* of which layer to follow — each is flagged where it arises.

## What LibRed does

Reads, writes, creates and alters them. It decodes the cached value by the column's `ResultType`, evaluates
the expression itself, and refreshes the cache on exactly the writes ACE would — no more, because the stored
value is a cache of ACE's own evaluation and anything else there produces a file that disagrees with itself.

- the envelope is decoded and encoded (`CalculatedValue`), including the long-value route a calculated Memo
  takes, and the long-value map admits a calculated column whatever its declared type;
- `ResultType` decides how the payload is read, rather than it being inferred from the payload's width;
- `CalculatedExpression` parses and validates, `CalculatedEvaluator` evaluates — every function on the
  whitelist below, with Access's null semantics and its per-function argument rules;
- `RowEncoder` recomputes on insert; `RowInserter.Update` does so only when a column the expression reads is
  in the changed set;
- a Memo result too large to inline spills to a long-value page through a callback `RowInserter` hands the
  encoder — a calculated column is the one value the encoder *derives* rather than receives, so it cannot
  have been materialised before the row arrived the way a plain memo is. Recomputing frees the pages the
  previous result held;
- `ColumnSpec.Calculated` authors one, and `DELETE` frees a calculated Memo's pages.

**Not done:** changing an existing calculated column's expression or result type (see [Still
open](#still-open)), and any SQL syntax for declaring one — Access SQL has none, so it would need LibRed's
extended mode.

## What the file actually carries

Three places, and all three must agree. Measured with DAO-authored columns of every type it will create
(`CalculatedColumnAccessTests`).

**1. The column descriptor** — a *promoted storage type*, the `0xC0` extended flag, always variable-length,
declared length `39` for a value type and `0` when the result is a Memo.

**2. The value envelope** in the variable slot — `[reserved:16][length:4][payload:n][padding:3]`.

**3. The table's property blob (`LvProp`)**, which is where the real semantics live:

| Property | Type | Meaning |
| --- | --- | --- |
| `Expression` | Memo | the expression text, e.g. `[Qty]*2`, `[A] & "-memo"` |
| `ResultType` | Byte | the **real** result type as a Jet type code |
| `FCMinReadVer` / `FCMinWriteVer` / `FCMinDesignVer` | Text | `14.0.0000.0000`, non-DDL — the Access floor a calculated column forces |

`ResultType` is the authority on how to read the payload, and it is the Jet type code of the type the
column was **declared** with: `01` Boolean, `02` Byte, `03` Int16, `04` Int32, `05` Currency, `06` Single,
`07` Double, `08` DateTime, `0A` Text, `0C` Memo.

The descriptor's type is something else again — the promoted static type of **the expression**, which need
not agree with the declared type at all. A `CDbl(...)` expression on a column declared `dbLong` gets
descriptor `Double`, `ResultType` `Int32`, and a 4-byte Int32 payload. So:

> **descriptor type = promotion of the expression's type · `ResultType` = the declared type · payload is
> encoded per `ResultType`.**

**This makes Phase 1 a bug fix, not a refinement.** `CalculatedValue.StoredType` infers the payload type
from the declared type and the payload width, which only works while the two agree. Where they diverge it
is wrong, and measurably so:

| column | descriptor | `ResultType` | payload | LibRed today |
| --- | --- | --- | --- | --- |
| `CDbl([Qty])` declared `dbLong` | Double | Int32 | 4 bytes | **`1E-44`** — reads the Int32 as a Single |
| `CDbl([Qty])` declared `dbCurrency` | Double | Currency | 8 bytes | **`3.45846E-319`** — reads the scaled int64 as a Double |
| `CDbl([Qty])` declared `dbInteger` | Double | Int16 | 2 bytes | **throws** `has 2 bytes; expected 8` |
| `CDbl([Qty])` declared `dbText` | Double | Text | *n* | **throws** |
| `CDbl([Qty])` declared `dbBoolean` | Double | Boolean | 1 byte | **throws** |

The corpus did not contain such a column, and every DAO-authored case in `CalculatedColumnAccessTests`
has the declared and expression types agreeing, which is why this passed. Reading `ResultType` removes the
guess entirely.

## The expression language ACE accepts

Enumerated over every scalar function [functions.md](../functions.md) catalogues plus every operator — 147
candidates, **72 accepted**. This is the whole surface, not a sample.

**Operators** — `+` `-` `*` `/` `^` `&`, `+` as concatenation, `=` `<>` `<` `>` `<=` `>=`, `And`, `Or`,
`Not`, `Is Null`, `Like`, `In`, unary minus, parentheses, literal-only expressions.

**Math** — `Abs` `Sgn` `Int` `Fix` `Round` `Sqr` `Exp` `Log` `Sin` `Cos` `Tan` `Atn`.

**String** — `Len` `LCase` `UCase` `Trim` `Left` `Right` `Mid` `InStr` `Space` `String` `Str` `Asc`.

**Date** — `DateSerial` `TimeSerial` `Year` `Month` `Day` `Hour` `Minute` `Second` `Weekday` `MonthName`
`WeekdayName`, plus date arithmetic (`[D1]+1`, `[D1]-[D1]`).

**Conditional and inspection** — `IIf`, `Choose`, `IsNull`, `IsEmpty`.

**Conversion** — `CDbl`, and *only* `CDbl`.

**Financial** — all ten: `Pmt` `FV` `PV` `NPer` `Rate` `IPmt` `PPmt` `SLN` `SYD` `DDB`.

Everything else is refused, in one of **four** distinct ways — a validator has to reproduce all four:

| Message | Cases |
| --- | --- |
| `The expression <X> cannot be used in a calculated column.` | the policy whitelist — 69 cases, listed below |
| `Expression not supported for conversion` | `Eqv`, `Imp`, `Between` |
| `Syntax error in expression.` | a call with a **missing optional argument** (below), and `CDec`, which the parser does not know at all |
| `The expression cannot be saved because it refers to another table.` / `... refers to itself.` | scope violations |

### Cross-checked against Access's own Expression Builder

Access's Expression Builder, opened on a calculated column, lists the functions it will offer. Its list and
the 49 functions measured above agree **exactly** — every function the builder shows was found accepted, and
nothing found accepted is missing from it. That is a genuinely independent confirmation: one list comes from
the UI, the other from driving DAO.

It also exposed two things the sweep never tried, both of which turned out to matter.

**`IsEmpty` is accepted, and is a constant.** VBA's `IsEmpty` asks whether a Variant was never initialised,
which a stored column value never is: ACE returns `False` for a column holding a value *and* for one holding
Null. It is on the whitelist but can only ever be `False`.

**The `$` name variants are a trap.** The builder offers `LCase$` `Left$` `Mid$` `Right$` `Space$` `Str$`
`String$` `Trim$` `UCase$`, and the designer **accepts** them — but ACE then fails **every** insert into that
table with *"Error evaluating CHECK constraint"*, while the plain name works on an otherwise identical
table. So a `$` variant produces a column that cannot be populated in Access either. This is the same shape
as indexing a calculated column: accepted at design time, unusable in practice. LibRed refuses them with a
message pointing at the plain name — that is matching ACE, not falling short of it.

### Optional arguments must be supplied — but only for some functions

[MSDN ff945943](https://learn.microsoft.com/en-us/previous-versions/office/developer/office-2010/ff945943(v=office.14))
states it twice: *"Although the third parameter for the Mid method is optional in VBA, calculated fields
require you to supply all parameter values"*, and *"you must supply all parameters for methods that you
call, even if the parameters are optional."*

It is real, and it is why `InStr` first measured as refused — `InStr([A],"a")` is a **syntax error**, while
`InStr(1,[A],"a")` and `InStr(1,[A],"a",0)` are both **accepted**. `Mid` behaves the same way: `Mid([A],2)`
is a syntax error, `Mid([A],2,3)` is fine, which is the article's own example. A calculated-column parser
therefore has to enforce arity, and must report a wrong count as a *syntax* error rather than a policy
refusal.

> **A syntax error means the wrong ARITY, not an unknown name — the opposite of the obvious reading.**
> Measured with the discriminating pair: `NotAFunction([Qty])`, a name nothing could know, gets the *policy*
> message; `Abs([Qty],2)` and `Abs()`, a whitelisted name with the wrong count, get *"Syntax error in
> expression."* So "syntax error" says nothing about whether ACE knows the function. `CDec` is the one case
> that resists the rule: at one argument it is still a syntax error rather than a policy refusal, which sets
> it apart from every other rejected conversion.
>
> **`InStr` takes three or four arguments here, never two** — its behaviour otherwise is the ordinary one in
> [functions.md](../functions.md). The other three-argument shape, `InStr([A],"a",1)`, is accepted at design
> time and then caches a value ACE cannot read, because `start` receives a non-numeric — the same trap as the
> `$` variants and an index on a calculated column.

But the rule is **not universal**, whatever the article says. `Weekday([D1])`, `MonthName(1)`,
`WeekdayName(1)` and `Round([Price])` are all accepted with their optionals omitted. So it is per-function,
and the only safe course is to record the required arity of each whitelisted function by measurement.

Measured for the financial ten as well, each in its shortest legal VBA form and again with every argument
supplied — `Pmt` `FV` `PV` `NPer` `Rate` `IPmt` `PPmt` `DDB` all accept the short form, and `SLN`/`SYD` have
no optionals to omit; all 18 shapes were accepted
(`Records_which_financial_functions_accept_an_omitted_optional`). So on everything measured so far **`Mid`
and `InStr` are the only two functions that demand every argument** — they are the exception to the
article's rule, not an illustration of it.

Supplying every optional does **not** rescue anything on the policy list: `Replace`, `StrComp`, `InStrRev`,
`StrConv`, `Format`, `FormatCurrency`, `FormatNumber`, `FormatPercent`, `FormatDateTime`, `DatePart`,
`DateDiff`, `DLookup`, `DCount`, `Rnd` and `Nz` were all re-tested in full form and are still refused. The
two rules are independent.

The article also notes that a calculated field **cannot call a user-defined function**, only built-ins —
consistent with everything measured, and with the whitelist being fixed rather than extensible.

Refused by policy: `\` `Mod` `Xor`; every conversion except `CDbl` (`CBool` `CByte` `CInt` `CLng` `CSng`
`CCur` `CStr` `CDate` `CVar`); `Rnd` `Timer`; `LTrim` `RTrim` `InStrRev` `Replace` `StrReverse` `StrComp`
`StrConv` `Val` `Chr` `Hex` `Oct`; `Format` `FormatCurrency` `FormatNumber` `FormatPercent`
`FormatDateTime` `Partition`; `Now` `Date` `Time` `DateAdd` `DateDiff` `DatePart` `DateValue` `TimeValue`;
`Switch`; `IsNumeric` `IsDate` `IsError` `TypeName` `VarType`; `RGB` `QBColor`; every byte/wide variant
(`AscB` `LenB` `LeftB` `RightB` `MidB` `InStrB` `ChrB` `AscW` `ChrW`); `Nz` `Environ` `CurDir`
`CurrentUser` `DLookup` `DCount`; and every aggregate (`Sum` `Count` `Avg` `Min` `Max` `First` `StDev`
`Var`).

**Why `CDbl` and nothing else?** The first sweep gave each conversion a declared type that did *not* match
its result, and only `CDbl` happened to match — so the obvious suspicion was that the rule was about
agreement, not about `CDbl`. Re-running each conversion against its **matching** declared type kills that:
`CBool→dbBoolean`, `CByte→dbByte`, `CInt→dbInteger`, `CLng→dbLong`, `CCur→dbCurrency`, `CSng→dbSingle`,
`CDate→dbDate`, `CStr→dbText` are all still refused, and `CDbl` is accepted against `dbDouble`, `dbLong`,
`dbCurrency` and `dbSingle` alike. It really is `CDbl` and only `CDbl`, regardless of the declared type.

The reading most consistent with the evidence — offered as a hypothesis, not a measured fact — is that the
evaluator's native numeric type **is** Double (OA's `R8`), so `CDbl` is an identity assertion rather than a
conversion: it asks the engine for the type it already computes in. Every other `C*` asks it to *produce* a
different runtime type, and the declared type already does that job through `ResultType`, so they are both
redundant and unsupported. The descriptor evidence fits: `CDbl` against any declared type yields descriptor
`Double`, i.e. the expression's own type. It does not explain everything — `Val` also returns Double and is
refused — so the curation is still partly historical.

"Deterministic, row-local, no I/O" explains the big exclusions — `Rnd`/`Timer`/`Now`/`Date`/`Time` are
volatile, `DLookup`/`DCount`/aggregates and other-table references leave the row, `Environ`/`CurDir`/
`CurrentUser` are environment. It does **not** explain the edges, which have to be taken as measured:
`Trim` is in but `LTrim`/`RTrim` are out; `Right` is in but `InStr` is out; `Asc` is in but `Chr` is out;
`Str` is in but `Val` is out; `CDbl` is in but every other conversion is out; `MonthName`/`WeekdayName`
are in but `Format` is out; `Like` and `In` are in but `Between` is out; and all ten financial functions
are in.

## How it is implemented

**Two evaluators, deliberately.** `LibRed.Engine` has an expression evaluator already, but the dependency
runs `Engine → Core` and this evaluation happens in **Core**, at row-encode time. A dedicated Core evaluator
was chosen over inverting with an interface Engine supplies, because inversion buys nothing here: the layer
that needs the answer is the one that cannot reach the implementation, so a Core-only caller
(`JetDatabase.Insert` is public API) would still have to refuse. The subset is small, frozen by ACE, and
semantically its own language — the Access *expression service*, not Access SQL — and it must match ACE
exactly, including null propagation, Currency scaling and the compressed-vs-raw text choice, so it wants its
own focused tests wherever the code lives. The financial functions and the two name functions are **ported**
from `ExpressionEvaluator` rather than re-derived, so one formula cannot answer differently through SQL than
through a calculated column. Moving the shared primitives down into Core remains the answer if the
duplication starts to bite.

**Authoring.** `ColumnSpec.Calculated` builds one: promoted storage type in the descriptor, `0xC0`, variable
slot, the constant declared length. `CREATE TABLE` and `ADD COLUMN` write the
`Expression`/`ResultType`/`FCMin*Ver` properties. A Memo result gets its long-value maps, keyed off the
**result** type because the declared type is `Text` and says nothing — which also needed the write-side guard
in `TdefBuilder` relaxed, since it demanded a map's owner be Memo/OLE, the same assumption the reader had
already had to drop. The ACE-14 gate goes through `EnsureFormatAtLeast`, raising the file rather than
refusing, as `BIGINT`/`DATETIME2` already do.

**Validation runs before anything is written** (`CalculatedExpression.ParseValidated`): the whitelist below,
plus self-reference, an unknown column, and the `$` name variants. It is mandatory rather than a courtesy —
an expression ACE rejects does not produce a merely odd file, it produces a column ACE refuses to read at
all. Measured end to end: ACE accepts, evaluates and *recomputes* a column LibRed authored, for every result
type including a spilled Memo (`Creates_a_calculated_column_ace_accepts`).

**`ALTER` is measured, not assumed.** `ADD COLUMN`, `DROP COLUMN`, `ALTER COLUMN` (retype) and
`RenameColumn` all leave a calculated column working — the last only since the reference-repointing fix.
Dropping a column an expression reads is **refused**, which ACE does not do; see the probe below.

**Text compression follows the declared type**, and the encoding rule is in
[page-02b-columns §3.4a](page-02b-columns.md) with the rest of the layout. It is called out here because it
is not cosmetic: compressing shrank a 36-character Memo result from 95 bytes to 61, back under the inline
limit, so the value never reached a long-value page at all.

## What the probes settled

**The cache is authoritative, and recomputation is narrow.** Poking a wrong value straight into the stored
envelope and asking ACE for it returns *the wrong value* — ACE does not re-evaluate on read. Updating a
column the expression does **not** reference leaves the poked value in place. Updating one it **does**
reference recomputes, even when the written value is unchanged:

| step | ACE returns |
| --- | --- |
| after `INSERT` with `Qty=7`, `C = [Qty]*2` | `14` |
| after poking `999` into the envelope | `999` |
| after `UPDATE T SET A='changed'` (not referenced) | `999` |
| after `UPDATE T SET Qty=7` (referenced, same value) | `14` |
| after `UPDATE T SET Qty=10` | `20` |

So Phase 3 must recompute **exactly when a referenced column is in the changed set** — `RowInserter.Update`
already receives that set. Recomputing unconditionally would write bytes ACE would not have written, and
recomputing never would leave a stale cache that ACE would have refreshed. It also means a stale cache is a
legitimate on-disk state, so LibRed reading it back verbatim is right.

**A calculated column cannot be written directly, and the refusal is uniform.** ACE rejects an `INSERT` that
names one and an `UPDATE` that sets one with the same message — *"Cannot update 'C'; field not updateable."*
— so a value for it is an error rather than something to override or ignore. LibRed refuses both
(`RejectExplicitCalculatedValues`). The two paths must test different things: on `INSERT` a value in the
slot is the signal, because nothing else puts one there, while on `UPDATE` it has to be membership of the
changed set, since callers hand back the whole row they just read and the slot legitimately still holds the
cached value.

**ACE has no route to change an existing calculated column's expression.** DAO refuses to mutate any field
of a *saved* `TableDef` — *"Operation is not supported for this type of object."* — and that refusal is
generic, reproduced on an ordinary column as a control, so it says nothing about calculated columns
specifically. Access's own designer must reach it by a path DAO does not expose. There is therefore no
oracle for "alter the expression", and anything LibRed offers there is its own extension rather than
ACE-matching behaviour.

**ACE does not protect a reference; every ALTER is accepted, two of them destructively.** Measured through
ACE SQL, each on its own table:

| operation | ACE | the calculated column afterwards |
| --- | --- | --- |
| `ADD COLUMN Extra LONG` | accepted | evaluates |
| `ALTER COLUMN Qty DOUBLE` (a column it reads) | accepted | evaluates |
| `DROP COLUMN Qty` (a column it reads) | accepted | **unreadable** |
| `DROP COLUMN <the calculated column>` | accepted | gone |
| `ALTER COLUMN A TEXT(60)` (unrelated) | accepted | evaluates |

So dropping a column an expression reads is not refused — it simply leaves a calculated column ACE can no
longer evaluate. **LibRed deliberately does not match this**, and refuses the drop:

> Column 'Qty' cannot be dropped because 'C' is a calculated column that reads it. Drop it first.

ACE's permissiveness here is a one-way door. The drop is accepted, the calculated column becomes
unevaluatable, and there is no repair short of recreating it — because, per the finding above, ACE has no
route to edit an expression at all. Refusing keeps the file readable and costs a caller who genuinely wants
the column gone one extra statement. An expression that fails to parse counts as reading nothing rather than
throwing: a safeguard must not become a blanket refusal to drop anything because some unrelated column's
expression is malformed.

**Renaming a referenced column must repoint the expression, and LibRed did not.** `RenameColumn` renamed the
column and left the expression naming the old one, so ACE failed every read of the calculated column — a
table broken by an operation that named a *different* column, and invisible from LibRed's own side, which
went on reading the stale cache happily. Now fixed
(`CalculatedExpression.RenameColumnReference`, `Libred_alter_keeps_a_calculated_column_working`). The rewrite
is textual so the author's spacing and bracketing survive, which means it has to know where a name is *not*
a reference: inside a string or `#date#` literal, before a `(` as a function name, or as a prefix of a
longer name.

**A TDEF rebuild keeps the column calculated.** `ADD COLUMN` and `DROP COLUMN` both rebuild the definition,
and the expression lives in the table's property blob rather than the 25-byte descriptor, so there was a
real risk of carrying the `0xC0` flag across while losing the expression — a column ACE could no longer
evaluate. Measured: both preserve `IsCalculated`, the expression and the result type, and ACE still
evaluates the column afterwards (`Rebuilding_a_tdef_preserves_a_calculated_column`).

**Indexing one is refused, and that matches Access rather than diverging from it.** Access's *designer* does
not offer a calculated column in the Indexes dialog at all, and will not let it be the primary key — the one
field in the table for which that option is absent. The *storage engine* agrees, refusing every insert into a
table where one is indexed. Only **ACE via SQL** accepts it, which is why the trap is reachable at all: you
cannot construct this state by clicking, only by writing SQL — exactly where LibRed lives. LibRed refuses on
every route: `CREATE INDEX`, `CREATE UNIQUE INDEX`, a composite that merely includes one, a table-level
`CONSTRAINT … PRIMARY KEY`/`UNIQUE`, and the inline `col type PRIMARY KEY` form.

**Indexing a calculated column is a trap, not a feature.** ACE accepts `CREATE INDEX` and even
`CREATE UNIQUE INDEX` on one — and then refuses every `INSERT` into the table with *"Operation is not
supported for this type of object."* Measured for Int16 and Int32 result types, and with the index created
both before and after rows exist. So the index-key-type question is moot: such an index can never be
populated. LibRed should refuse to create one rather than reproduce a state that bricks the table.

**Declared length is a constant, and the requested size is discarded.** `39` for every value type, `509` for
Text whatever size was asked for (10, 60 and 255 all give 509), `0` plus a long-value map for Memo. `39` is
not a payload ceiling tied to any type — a calculated GUID lands on Text/39 as well. Because the requested
size is not kept, **ACE does not truncate**: a `Text(5)` calculated column holding a much longer result
stores and returns it in full, and LibRed already agrees byte for byte.

**A zero-length payload means Null — except for text, where it means the empty string.** The two are the
same bytes on disk, because an empty string also encodes to nothing, and ACE resolves the ambiguity by
type: `Left([A],2)` over a Null `[A]` stores an empty payload and reads back `""`, while the same empty
payload under a `DateTime` result type reads back Null. Reading it as Null in both cases is a silent
disagreement about a value nothing ever recomputes.

**The envelope does not consume fixed-region budget.** 250 `Double` columns — 2000 bytes, the documented
record cap — are accepted both with and without a calculated column on top. The true ceiling was not
bracketed, so this rules out a *fixed-region* cost rather than establishing the exact limit.

**The function whitelist is larger than the first sample and arbitrary at the edges.** 81 candidates probed,
43 accepted. Beyond the first sweep, also accepted: `Right`, `LCase`, `Space`, `String`, `Asc`, `Sqr`,
`Exp`, `Log`, `Sin`, `Cos`, `Tan`, `Atn`, `Fix`, `Month`, `Day`, `Hour`, `Minute`, `Second`, `Weekday`,
`DateSerial`, `TimeSerial`, `CDbl`, `Is Null`, `Like`, `In`. Also rejected: `LTrim`, `RTrim`, `StrReverse`,
`InStr`, `InStrRev`, `StrComp`, `Chr`, `Rnd`, `DateValue`, `CDate`, `CInt`, `CCur`, `CBool`, `CVar`,
`IsNumeric`, `IsDate`, `IsError`, `Between`, `Partition`, `Environ`, `CurrentUser`.

The edges do not follow from the "deterministic, row-local" rule and must be enumerated rather than
reasoned about: **`Trim` is accepted but `LTrim` and `RTrim` are not**; `Right` is accepted but `InStr` is
not; `Asc` is accepted but `Chr` is not; `CDbl` is accepted but every other `C*` conversion is not; `Like`
and `In` are accepted but `Between` is not. `Rnd()` being rejected does fit the rule, as do `Date()` and
`Now()`.

**The engine evaluates the stored text, and enforces the whitelist itself.** Authoring a column with an
allowed expression and then patching the stored `Expression` property on the page to another string of the
same length — bypassing DAO entirely — shows both halves:

| patched to | LibRed reads Expression | ACE reads cached row | ACE inserts a row | ACE recomputes |
| --- | --- | --- | --- | --- |
| `[Qty]*3` (allowed) | `[Qty]*3` | `14`, the stale cache | `21` = 7×3 | `30` = 10×3 |
| `CInt(9)` (refused) | `CInt(9)` | **error** | row inserted, value **empty** | **error** |
| `Now()+0` (refused) | `Now()+0` | **error** | row inserted, value **empty** | **error** |

The control proves the engine really reads and evaluates the property text — not a compiled form cached
elsewhere — so the expression string is the source of truth for writes and the envelope is the source of
truth for reads. The other two prove the whitelist is **not** a DAO-side courtesy: ACE raises
*"This calculated column contains an invalid expression."* on every read of that column, and inserts leave
the value empty.

Two consequences. **Validation is mandatory, not optional** — an unvalidated expression does not produce a
merely odd file, it produces a column ACE cannot read at all. And **LibRed is currently more permissive
than ACE**: it read the stale `14` and a `null` from files ACE refuses outright, because it never looks at
the expression. That is arguably right for a reader, but it is a divergence and should be a decision rather
than an accident.

## Still open

1. ~~**Where `39` and `509` come from**~~ — **as far as measurement can take it**
   (`Records_the_declared_length_of_every_calculated_type`). There are **four** constants, not two: `0` for
   Memo, `509` for Text at any requested size, **`510` for Binary** — which nothing had ever created, so the
   earlier sampling missed it — and `39` for everything else, GUID included. Nothing moves them: not the
   requested size, not the length of the expression. *Why* those particular numbers is not something ACE can
   be asked; what matters for writing them is that they are fixed, and now they are known to be.
2. **Should LibRed refuse to read what ACE refuses?** Two cases, and they have now been decided differently.
   A **cached error** throws, matching ACE, which will not hand back such a row at all — settled, and the
   precedent for the rest. An **unwhitelisted expression** is still read: the cached value is perfectly
   well-formed, and nothing has been measured that says ACE's refusal there protects anything on disk.

   The tension between the two — strictness for correctness against leniency for recovery — is better
   resolved by a **lenient "recovery" mode** than by softening the default. Reading a damaged or
   engine-rejected file is a real use, but it is a *different* use from being a faithful engine, and letting
   it dictate the default is what produces a reader that quietly invents values. A recovery mode would be
   engine-wide rather than a calculated-column feature: surface the error rather than throwing on it, keep
   enumerating past rows that cannot be decoded, and report what was skipped. Noted as a future direction,
   not scheduled.
3. ~~**`InStr` and `CDec` fail as a *syntax* error**~~ — **answered**, and the guess behind it was wrong: a
   syntax error means the wrong arity, not an unknown name. See the note above; the finding also fixed
   `InStr`, which LibRed had been refusing at three arguments.
4. ~~**The full ACE value-matrix parity test**~~ — **done**
   (`Computes_the_same_values_as_ace_across_the_matrix`). Fifteen expressions over five rows — nulls, the
   empty string, zeros, negatives, a tiny Currency, a non-ASCII character and two pre-epoch times — agree
   with ACE in 74 of 75 cases.

   The one exception is a **LibRed divergence**, and it is still open. **The VBA conversion functions do not
   propagate Null, they raise on it**, so `CDbl(Null)` is an error rather than a Null result. LibRed applies
   one blanket rule — any Null argument yields Null — which is right for every other whitelisted function and
   wrong for `CDbl`, the only conversion on the list.

   The bytes settle what "an error" means on disk, and it is **not** the same as Null: the envelope's leading
   field holds the **VBA error number** (94 "Invalid use of Null", 13 "Type mismatch"), and an error envelope
   is 38 bytes against 23 for a Null — see [page-02b §3.4a](page-02b-columns.md).

   **Reading** is settled: a non-zero status throws rather than decoding as Null, naming the VBA error and
   the expression that raised it. Zero is that field's success code, so a value and a propagated Null both
   read exactly as before.

   **Writing** is settled too, and diverges deliberately: **LibRed refuses the row.** ACE accepts the insert
   and caches the failure, after which its own reader rejects that row — and the state is **sticky**.
   Measured: a compact-and-repair does *not* clear it (still error 94 afterwards, both rows intact); the only
   thing that does is an `UPDATE` writing a column the expression reads, forcing a recompute — which needs a
   value for the very column that was Null. So writing it produces a row that neither engine can read and
   that the obvious repair does not fix. Refusing costs the caller a guard and keeps the file readable, the
   same trade as refusing to drop a column an expression reads.
5. **Changing an existing column's expression or result type — out of scope by decision, not unfinished.**
   ACE offers no route to it at all, so LibRed would be *defining* the behaviour rather than matching any,
   including having to recompute every existing row at once (nothing re-derives a cached value on read).
   **Drop the calculated column and add it again** instead: both halves are supported, neither touches the
   data columns, and the result is indistinguishable from one ACE authored. Refusing to drop a column an
   expression reads is what keeps that the only way the two can get out of step.
6. **No SQL syntax to declare one.** Access SQL has none, so this needs LibRed's extended mode (something
   like `ADD COLUMN c AS (<expr>)`), and then a decision about whether EF's `HasComputedColumnSql` maps
   onto it.
