# System catalog (MSys* tables)

> Part of the [LibRed Jet / ACE file-format reference](README.md). Cross-references use the original **§-numbers**; the [section map](README.md#section-map) says which file each lives in.

## 11. System catalog

- **MSysObjects** (TDEF at page **2**) lists every object. Columns include `Id`, `Name`,
  `Type`, `Flags`, `ParentId`. For a **table** object (`Type == 1`), **`Id` is the table's TDEF
  page number**. An object is excluded from the **user-table** list (as Access's own schema view
  does — it hides system *and* hidden objects) if `Flags & 0x80000002` (system: `0x80000000` +
  `0x00000002`) **or** `Flags & 0x00000008` (**hidden** — observed on nav-pane tables and on
  EFCore.Jet's `#Dual` helper) is set, **or** its name begins with `MSys` / `~` / `#`. Bootstrap:
  build a TableDef for MSysObjects from page 2 and read its rows like any table.

  > **Why the hidden bit / `#` prefix matter.** Missing them makes a hidden helper such as
  > EFCore.Jet's `#Dual` (`Flags = 0x08`) count as a *user* table, so a "has any user tables?" check
  > wrongly reports a schema-less database as populated — which makes EF Core's `EnsureCreated` skip
  > creating the model's tables. Real user tables carry `Flags = 0x00000000`, so excluding the
  > system/hidden bits never drops a genuine table.

  > **The same bits name the object kind in a schema rowset** (verified against ACE's `Tables`, which
  > classifies every object by `Flags` rather than by name): the system bit (`0x80000000`) makes it a
  > **`SYSTEM TABLE`** — the engine's own catalog, `MSysObjects` / `MSysQueries` / `MSysRelationships` /
  > `MSysACEs` / `MSysComplexColumns`; the hidden bit (`0x08`) without it makes an **`ACCESS TABLE`** —
  > Access's application tables, the nav-pane group and `MSysResources`; anything carrying `0x00030000`
  > is **not listed at all** (the `MSysComplexType_*` tables, `Flags 0x80030000`); everything else is a
  > **`TABLE`**, which is why `MSysAccessStorage` (`Flags = 0`) appears among the user tables despite its
  > name. Stored queries are listed in the same rowset as **`VIEW`**.

  **Writing a table object** (verified against Access-written rows). A complete user-table row sets:
  `Id` = TDEF page; `ParentId` = `0x0F000001` (the database's "Tables" container, constant);
  `Type` = `1`; `Name`; `Flags` = `0`; `Owner` = a 2-byte binary SID (`0x69 0x0C` for a
  workgroup-less database, constant across tables); and `DateCreate` / `DateUpdate`. The other
  columns (`Connect`, `Database`, `ForeignName`, `Lv*`, `RmtInfo*`) are null **except `LvProp`**,
  an OLE long-value blob ("MR2"-prefixed) holding the object's **extended properties** — including
  column-level properties such as *Required* (see §3.4) and *DefaultValue*.

  > **Permission rows (`MSysACEs`) — one per object, verified.** Every new object needs
  > `MSysACEs` rows or Access warns about permissions when opening it (a **table** still opens; a **query**
  > opens but pops a permissions warning). The table has exactly **four columns**:
  > `ObjectId` (Int32, the object's id), `SID` (Binary, a security id), `ACM` (Int32, an access mask), and
  > `FInheritable` (Boolean). Each row sets `ObjectId` = the object id, `SID` = a 2-byte binary security id,
  > `ACM` = an access mask, `FInheritable` = false, and the object's `ObjectId` index must be maintained so
  > Access's security check finds them. Access writes **two** rows per object, and the mask
  > **differs by object type**:
  > - **Table:** owner (`0x690C`) and admin/users (`0x680C`) both get full access `ACM = 0xFFEFF` (1048319).
  > - **Query/view:** owner (`0x690C`) gets `ACM = 0xF00FE` (983294, a query-specific mask), admin/users
  >   (`0x680C`) gets full `0xFFEFF`.
  >
  > LibRed writes both rows for tables (`TableCreator.AddPermissionRows`) and for queries/views
  > (`ViewCreator.AddPermissionRows`). (System-table `MSysACEs` rows in an existing file carry restricted
  > masks like `0x60000`/`0x14` and a long per-database owner SID; those are the pre-existing catalog's, not
  > what a writer emits for a new user object.)

  > **Property blob (`LvProp`) format — verified byte-for-byte against ACE.** A 4-byte signature
  > (`MR2\0` on ACE, `KKD\0` on older MDB) then blocks, each `[int length][short type][body]` with the
  > length covering the whole block. Type `0x80` is the **property-name pool** (`[short len][UTF-16
  > name]` repeated, indexed 0,1,…). Other blocks are a **per-owner value map** (owner = a column name,
  > or `""` for the table): `[short ownerRecLen][short 0][short nameLen][owner name]` then property
  > entries `[short entryLen][byte DDL flag][byte dataType][short nameIndex][short valueLen][value]`.
  > The per-entry flag is `0x01` for a **DDL/property-definition property** and `0x00` for an ordinary
  > property. A set flag makes the property definition-protected (`dbSecWriteDef` permission is needed to
  > change/delete it), and Access only recognises some properties when the classification is correct
  > (both unverified against ACE). Observed in files: `DefaultValue`, `Required`, `CheckConstraints`, `GUID`,
  > and `ResultType` are `0x01`, while `Title`, `Author`, `AccessVersion`, and datasheet-layout properties
  > are `0x00`. `ValidationRule`/`ValidationText` are classed as DDL and `Caption`/`Description` as ordinary
  > (unverified). The flag is independent per entry; it is not a file-version, encryption, owner, or
  > data-type marker. LibRed accepts the two observed values, preserves both the flag and raw value read for every property,
  > and defaults newly constructed schema properties to `0x01`. The
  > `dataType` is an ordinary **`JetDataType` code** (the same byte used by column descriptors and
  > MSysQueries): **`0x0C`** (Memo) for a text value stored as **UTF-16**, **`0x01`** (Boolean) for a single
  > **0/1 byte**, and — on the `MSysDb` object's UI/nav settings only — `0x0A` (Text), `0x02`/`0x03`/`0x04`
  > (Byte/Int16/Int32). The value-block **type** is `0x01` for a column-owned map and `0x00` for the
  > table-owned map (empty owner name). A `DefaultValue` (column property) is the expression's **source
  > text** (e.g. `42`, `'hi'`) — its evaluation semantics (what an expression may contain, the
  > DDL-parser-vs-expression-service split) are in [page-02c-default-values.md](page-02c-default-values.md);
  > table-level `CHECK` constraints are a single **table** property named `CheckConstraints` whose value is a
  > `name\0expression\0` list, terminated by an extra `\0` (verified byte-for-byte vs ACE).
  > `ALTER TABLE … DROP CONSTRAINT <ck>` removes the matching entry from that list and rewrites it (dropping
  > the whole table-level property block when it was the last check) — ACE-verified: after the drop ACE stops enforcing the check. (In
  > Jet/ACE `DROP CONSTRAINT` is polymorphic over the name — FK / PK / unique index / CHECK.)
  >
  > The `MSysDb` object — an `MSysObjects` row of `Type=2` with no table behind it — carries the
  > database-level properties, among them `AccessVersion`. Access writes those, not the engine: a DAO-created
  > database has none, at any `dbVersion`, and Access adds them (with `MSysAccessStorage` and the nav-pane
  > tables) the first time it opens the file. Which is why `DatabaseCreator` does not write them either. The
  > one place the value matters is [data-types.md](data-types.md), where it says which files carry the
  > unmodelled `0x11` column.
  >
  > **`ANSI Query Mode`** — an `MSysDb` property, empty owner, `dataType` `0x04` (Int32), holding a 4-byte
  > little-endian `0` or `1`. It is the per-database half of Access's *Object Designers → SQL Server
  > Compatible Syntax (ANSI 92)* option: `0` = ANSI-89, `1` = ANSI-92. (The *"Default for new databases"*
  > checkbox beside it is an application setting and appears nowhere in any file; it only decides what Access
  > stamps into the next database it creates.) The two modes name the `LIKE` wildcard sets — ANSI-89 `*` `?`
  > `#`, ANSI-92 `%` `_`. **Verified** across 38 files: 33 carry `0`, 5 carry `1`.
  >
  > **The engine does not consult it.** Measured over ACE in nine combinations: the property set to absent,
  > `0` or `1` × OLE DB / ODBC / ODBC with `ExtendedAnsiSQL=1` gives identical results, for ad-hoc SQL and for
  > a saved query alike, whether the saved query was written by ACE itself or by LibRed. The wildcard set
  > comes from the **connection**: OLE DB is ANSI-92, plain ODBC reads a saved query's pattern as ANSI-89, and
  > ODBC with `ExtendedAnsiSQL=1` — which `JetConnection` sets on every ODBC connection — is ANSI-92 again.
  > So this property records what Access's own UI will do with the file, not what any engine does with it.
  >
  > **Absent is not the same as `0`.** A clear checkbox is recorded as an explicit `0`; absent means nothing
  > ever wrote the property. Neither DAO nor Access-on-first-open writes it: a pristine DAO 2000 file carries
  > an `MSysDb` row with **zero** properties, and after Access opens it the row has nine — still not this one
  > (verified). Ten of the corpus files carry a populated blob without it, Northwind among them, so absent is
  > an ordinary state rather than an old-format quirk.
  >
  > LibRed neither reads nor writes it, because acting on it would diverge from the engine: its SQL surface is
  > ANSI-92 throughout, which is what every connection into an ACE database uses. Note the trap — Access's
  > saved queries are full of ANSI-89 `*` patterns (17 of the 17 extractable ones in the corpus), which makes
  > it look as though the file's mode must be driving them. It isn't; the connection is.
  >
  > **Property-reader/writer guardrails.** LibRed validates the signature and consumes the blob exactly to
  > its end. Every block, pooled UTF-16 name, owner record, property entry, name-pool index, and value length
  > must remain within its declared parent and use the exact nested lengths above. `Read`,
  > `AddColumnProperties`, and `RemoveOwner` share this representation. Serialization preflights every
  > 16-bit name, owner-record, entry, value, and name-index field plus the 32-bit block lengths before emitting
  > bytes; unmodelled `RawValue` payloads still round-trip verbatim.
  >
  > **`Required` (NOT NULL)** is a per-column **boolean** property (`dataType 0x01`, one `0x01` byte); a
  > **nullable** column simply has **no** `Required` property. An AutoNumber column follows the same rule: one
  > declared `NOT NULL` (`COUNTER NOT NULL`, `INT NOT NULL IDENTITY`) carries `Required`, and one declared
  > without it has none (verified vs ACE). Within a column's map ACE orders `DefaultValue` **before** `Required`; the
  > name-pool order follows first appearance across all properties — **not** alphabetical (verified): names
  > first appearing as `Required`, then `DefaultValue`, then `CheckConstraints` are pooled as
  > `["Required","DefaultValue","CheckConstraints"]`, not `["CheckConstraints","DefaultValue","Required"]`.
  > Example (`Req int NOT NULL, …, Def int DEFAULT 7 NOT NULL`): name pool `["Required","DefaultValue"]`, then `Req`'s `Required`, then `Def`'s `DefaultValue`=`7`
  > and `Required` — reproduced byte-for-byte by `PropertyBlob.Write` (which builds the pool by first
  > appearance via `Distinct()`).
  >
  > **Unmodelled properties round-trip verbatim.** LibRed *interprets* `DefaultValue`, `Required`,
  > `CheckConstraints` and the text `ValidationRule`/`ValidationText` (the last two are **read-only**: surfaced
  > through `INFORMATION_SCHEMA.{TABLES,COLUMNS}.VALIDATION_RULE/VALIDATION_TEXT` to match EFCore.Jet's ADOX
  > `Jet OLEDB:{Table,Column} Validation Rule/Text`, but not yet written or enforced), while a database-first
  > file may carry many more per column (`Format`, `AllowZeroLength`, the numeric `DecimalPlaces`, …). An
  > ALTER that edits one property rewrites the whole blob (`PropertyBlob.Read` → mutate → `Write`), so
  > `PropertyBlob.Property` keeps each value's **exact stored bytes** (`RawValue`) and re-emits them unchanged —
  > a property LibRed doesn't model is never dropped or corrupted by the best-effort UTF-16 value decode (which
  > would mangle a numeric one).
  >
  > LibRed **writes** `DefaultValue`, `Required` and `CheckConstraints` properties (`PropertyBlob.Write`) and
  > **reads** them back (`ColumnDef.DefaultValue`, `ColumnDef.IsNullable`, `TableDef.CheckConstraints`),
  > applying the default when an insert omits the column and **rejecting** an insert that leaves a required
  > column null ("You must enter a value in the '<table>.<column>' field.", matching Access). Access
  > **applies the default**, **enforces Required**, and **enforces the CHECK** on its own inserts —
  > including on a LibRed-created table (verified: ACE rejects an insert omitting a LibRed `NOT NULL`
  > column). `LvProp` is stored on a **single LVAL page** (`LongValueWriter`, descriptor flag `0x40`) — the
  > form Access's property loader requires. **Verified:** Access opens the file and **applies the default** on
  > its own insert that omits the column. (An *inline* value, flag `0x80`, is valid long-value storage but is
  > **not** recognised by Access's property loader.)
  >
  > **`ALTER COLUMN … SET DEFAULT expr` / `DROP DEFAULT`** are LvProp edits only — no TDEF/type change. Both
  > read the `LvProp` blob, mutate the target column's map, and rewrite it: SET replaces (or adds) that
  > column's `DefaultValue`; DROP removes **only** the `DefaultValue` property, leaving `Required` (and the
  > column's type) intact. ACE-verified: after a LibRed `DROP DEFAULT`, ACE no longer applies the default on
  > an omit-insert yet still rejects a null in a `NOT NULL` column; `SET DEFAULT` is applied on ACE's own
  > insert. (EF Core emits `ALTER COLUMN c DROP DEFAULT` in migrations.)
  >
  > **`ALTER COLUMN … NOT NULL` / `NULL`** is likewise an LvProp edit: NOT NULL adds the boolean `Required`
  > property (the write side of what CREATE does at §3.4), NULL removes it — LibRed keeps ACE's
  > `DefaultValue`-before-`Required` order by applying a co-specified DEFAULT first. ACE-verified: after a
  > LibRed `NOT NULL` ACE enforces it (rejects an omitted/NULL value) and reads the column back as
  > non-nullable. **ACE quirk:** ACE's *own* OLE-DB `ALTER COLUMN … NULL` does **not** clear an existing
  > `Required` (the column stays required); LibRed removes the property natively, so the column becomes
  > genuinely nullable and ACE then reads/accepts it as such.
  >
  > **`ALTER COLUMN c COUNTER(seed, increment)` reseed** (KB 884185 fix) — when `c` is already an AutoNumber
  > of the same type, this changes only the *next* id, so LibRed does an **in-place TDEF header edit** (`0x14`
  > = seed − increment, `0x18` = increment), not a table rebuild; ACE reads the reseeded next id (verified).
  > Both ACE and LibRed **reject** reseeding a counter that participates in a relationship ("Cannot change
  > field 'X'. It is part of one or more relationships." — verified both sides). Changing the numeric type
  > still goes through the full column rewrite.
  >
  > **Promoting a plain int column to a counter** (`ALTER COLUMN <int> COUNTER(seed, inc)`) is a **deliberate
  > superset**, and also an **in-place metadata edit** — a counter is stored identically to a Long Integer, so
  > LibRed only sets the descriptor's `0x04` AutoNumber flag and the header seed/increment (`0x14`/`0x18`); the
  > existing values are untouched, no rebuild. ACE rejects the conversion outright (*"Invalid field data
  > type"*, as does SQL Server); PostgreSQL (`ADD GENERATED AS IDENTITY`) / MySQL (`MODIFY … AUTO_INCREMENT`) /
  > LibRed allow it. Verified: ACE reads the promoted counter and assigns next id = seed. Guards:
  > only one column may draw on the table's seed/increment pair (a second is rejected), and a column in a
  > relationship is rejected (matching ACE).
  >
  > That guard is about the **header pair**, not about the `0x04` flag, and the two are not the same set. A
  > complex column carries `0x04` as well but is allocated from `0x1C`, so a table can hold several columns
  > that all read as AutoNumber: `MSysResources` has two (`Id` and the complex `Data`), and a table with four
  > attachment columns beside an `ID` counter has five. Only the non-complex one is described by `0x14`/`0x18`
  > — applying that pair to the others reports a different counter's seed and increment as theirs.
  >
  > **Demoting a counter to a plain int** (`ALTER COLUMN <counter> LONG`) is the reverse in-place edit — clear
  > the `0x04` flag and reset the header to a non-AutoNumber table's state (`0x14` = 0, `0x18` = 1); values are
  > kept and the column stops auto-assigning. Unlike promotion this is **not** a divergence: ACE allows it too
  > (matching the Access UI's AutoNumber→Number change). Verified: ACE reads the demoted column as a
  > plain int and accepts explicit ids.
  >
  > **Default-value interaction** (a "Random" AutoNumber *is* a counter with a `GenUniqueID()` default, so the
  > flag and default combine). The insert path skips defaults for AutoNumber columns and only reads
  > `GenUniqueID()` to mean "random", so: **promotion** to a sequential `COUNTER(seed)` **clears a surviving
  > `GenUniqueID()` default** (otherwise the column would silently become a Random AutoNumber and ignore the
  > seed); a literal default is inert on a counter and left as-is. **Demotion preserves the default** (matching
  > ACE — ALTER-type keeps it): demoting a Random AutoNumber yields a plain int that still generates random ids
  > via its surviving `GenUniqueID()` default (ACE-verified).
  >
  > **"Random" AutoNumber (New Values = Random) is a `DefaultValue` = `GenUniqueID()`.** An AutoNumber column
  > whose *New Values* property is **Random** (rather than Increment) is stored as an ordinary AutoNumber column
  > (descriptor flag `0x04`, TDEF `0x14`/`0x18` at their plain-counter defaults `0`/`1` and **ignored**) plus a
  > **column `DefaultValue` extended-property** holding the built-in expression **`GenUniqueID()`** — the
  > function that returns a random Long. There is **no** dedicated flag or "New Values" property; the
  > Increment-vs-Random distinction lives entirely in this default expression. Verified against an
  > Access-authored file: the ID descriptor and TDEF header are **byte-identical** to an increment counter, so
  > the ordinary DefaultValue read path surfaces it (`ColumnDef.DefaultValue` = `"GenUniqueID()"`) with no
  > special handling. A Random AutoNumber **can** be created in pure SQL (not UI/DAO-only): `CREATE TABLE T
  > (Id COUNTER DEFAULT GenUniqueID(), ...)` — also `AUTOINCREMENT`/`COUNTER PRIMARY KEY` forms — is accepted
  > by ACE and yields genuinely random signed-Long IDs on insert (verified), reading back byte-identical to the
  > UI-authored column. `GenUniqueID()` **is a real ACE default-expression**, not a marker: `SELECT
  > GenUniqueID()` errors ("Undefined function"), yet an **unquoted** `col LONG DEFAULT GenUniqueID()` **is**
  > accepted and generates a **random signed Long per row** (verified). It is accepted **only on a `LONG`
  > (Int32) column** — the same width a `COUNTER` stores; **every other type is rejected** ("Cannot place this
  > validation expression on this field"), verified for BYTE/SHORT/SINGLE/DOUBLE/CURRENCY/DECIMAL/GUID/
  > DATETIME/BIT/TEXT. Quoting it — `DEFAULT 'GenUniqueID()'` — makes it a plain literal string stored
  > verbatim. So a Random AutoNumber is effectively an AutoNumber column carrying the unquoted `GenUniqueID()`
  > default. **LibRed creates and inserts these**: `CREATE TABLE ( Id COUNTER DEFAULT GenUniqueID(), … )`
  > persists the `GenUniqueID()` default to the column's LvProp (byte-identical to a UI/ACE-authored one, so ACE reads it as a Random AutoNumber), and
  > on insert LibRed assigns a random non-zero Int32 per row instead of the sequential counter, leaving the TDEF
  > high-water (`0x14`) unadvanced (as ACE does). `ColumnDef.IsRandomAutoNumber` gates this off the default text.
  > A **plain (non-AutoNumber) `LONG DEFAULT GenUniqueID()`** column works too: `GenUniqueID()` is a real
  > evaluable function in LibRed's expression evaluator (a random non-zero Int32), so an omitted value defaults to
  > a random Long while a supplied value is kept — matching ACE, which reads and applies a LibRed-written one.
  > LibRed also **enforces the LONG-only restriction at CREATE/ADD-COLUMN time** (`GenUniqueID()` on any other
  > type raises "Cannot place this validation expression on this field"), matching ACE.

- **LVAL (long-value) page** — a data page (type `0x01`) whose owner field (`0x04`) is the ASCII marker
  `"LVAL"` instead of a TDEF page number. A single-page long value stores the whole payload in the
  referenced row (row 0 on a fresh page); the in-row reference descriptor is
  `[length-and-flags:4][row:1][page:3][4 reserved]`. The first word is little-endian, with a 30-bit byte
  length and two flag bits: byte `0x03` masked with `0xC0` gives `0x40` = single page
  (`0x80` = inline, payload follows the descriptor; `0x00` = chained across pages). LibRed writes the
  single-page form (`LongValueWriter`) and chained pages for payloads larger than one page.

  > With those fields set, Access **enumerates** a LibRed-created table (it appears in the
  > schema/Tables rowset) — verified via OLE DB. Maintaining MSysObjects' indexes (the composite
  > `ParentId+Name` and `Id` indexes) then lets Access **resolve the table by name** and attempt
  > to open it. Opening it then requires the table's own structures to be byte-valid to Access
  > (see §3.7).

- **Views / queries** are `MSysObjects` rows of **Type 5** with a **negative synthetic `Id`** (queries
  increment from `0x80000000`), `ParentId 0x0F000001`, `Flags 0x10000000`, `LvProp` null.

- **Relationships** are `MSysObjects` rows of **Type 8** too — one per relationship, alongside its
  `MSysRelationships` rows (verified vs ACE: every relationship Access or ACE creates has one, whether from
  `ALTER TABLE … ADD CONSTRAINT` or a `CREATE TABLE` foreign key). `Name` is the relationship's name,
  `ParentId 0x0F000003` (the Relationships container), `Flags 0`, `Owner 0x690C`, `DateCreate` = `DateUpdate` =
  the creation time, and `LvProp`, `Lv`, `LvExtra`, `LvModule`, `Connect`, `Database`, `ForeignName`,
  `RmtInfoShort`, `RmtInfoLong` all null.
  - **`Id`** is the next negative synthetic id: one past the highest in the file, from the sequence queries draw
    on, so relationships and queries interleave (`0x8000002C` relationship, `0x8000002D` view,
    `0x8000002E` relationship), and a dropped relationship's id is taken by the next object.
  - **Two `MSysACEs` rows**: SID `0x690C` with ACM `0xF00FE`, and SID `0x680C` with ACM `0xFFFFF`.
  - **Dropping it** — `DROP CONSTRAINT`, or `DROP TABLE` of the referencing table — removes the object and its
    two `MSysACEs` rows.
  - **Its name** must differ from every other relationship's (*"There is already a relationship named '…' in
    the current database."*), but may equal a table's or a query's.

  > **MSysQueries columns (8, verified).** The table has exactly: `ObjectId` (Int32, the
  > query object's `Id`), `Attribute` (Byte, the row kind — see below), `Flag` (Int16, attribute-specific),
  > `Name1` and `Name2` (Text, attribute-specific names), `Expression` (Memo, attribute-specific text —
  > SQL fragments), `Order` (Binary, a 4-byte big-endian per-attribute sequence counter), and `LvExtra`
  > (Int32) — a long-value/overflow field observed **null** in Access-written query rows and that LibRed
  > leaves null (not needed for the queries it writes). Only index = composite PK `(ObjectId, Attribute,
  > Order)`.

  The query itself is stored in **MSysQueries**, decomposed into rows keyed by `ObjectId`, each with an `Attribute`
  byte (verified vs ACE for the "simple SELECT" a view may contain): `0x00` =
  start record (`Flag 1` = SELECT), **`0x01` = the OPERATION row** (see below), `0x02` = a **declared parameter**
  (`Name1`=parameter name, `Flag`=Jet
  type code — same codes as on-disk column types, e.g. `8`=DateTime; **`Flag 0` is not a type code but Access's
  untyped parameter, rendered as the keyword `Value`**; one row per parameter, `Order`
  1-based), `0x03` = **options** (see below), `0x04` = the connection string of a **pass-through** query,
  `0x05` = FROM source, `0x06` =
  output column (`Expression`=verbatim text; **`Name1`=the column's output alias** when it has one, e.g.
  `Expression=Customers.CompanyName`, `Name1=CustomerName`; a computed column stores its whole verbatim
  expression, `Expression=(FirstName + ' ' + LastName)`, `Name1=Salesperson`), `0x07` = join
  (`Expression`=condition, `Flag`=kind, and **`Name1`/`Name2`=the two tables named in the condition** —
  `Customers.CustomerID = Orders.CustomerID` → `Name1=Customers`, `Name2=Orders`), `0x08` = WHERE
  (`Expression`), `0x09` = a **GROUP BY** column (`Expression`; one row per group column, in order —
  their presence makes it a "totals" query, and the aggregate output columns are ordinary `0x06` rows,
  e.g. `Expression=Sum(...)`), `0x0A` = a **HAVING** predicate over those groups (`Expression`; one row, and — like the `0x08` WHERE
  row — it carries no `Flag` at all, verified), `0x0B` = an **ORDER BY** key (`Expression`=the sort column, `Name1`=`"d"`
  for **descending**, absent for ascending; one row per key, `Order` 1-based — verified), `0x0C` = **complex-type
  data** (`Flag 1` = long-text version history, `Flag 2` = MVF / attachment), `0xFF` = end. A **FROM source** (`0x05`) is either a **named table**
  (`Name1`=table, `Name2`=alias), a **derived table / subquery** (`Expression`=the verbatim inner
  subquery SQL — outer parens and `AS alias` stripped, whitespace preserved — `Name2`=alias, **no `Name1`**;
  verified), or, on a UNION query, one **UNION segment's
  own SQL** (`Name2` = that segment's identifier).

  > **The `0x01` OPERATION row is the query KIND, and SELECT is one of its values** — its presence does *not*
  > mean the query is an action query. Access writes it on plain SELECTs as well, and omits it entirely on
  > others; both shapes are common. `Flag` values against DAO's `QueryDef.Type`:
  >
  > | `Flag` | kind | DAO `QueryDef.Type` | status |
  > |---|---|---|---|
  > | `1` | SELECT | `0` dbQSelect | observed |
  > | `2` | make-table (`SELECT … INTO`; target in `Name1`, external db path in `Name2`) | `80` dbQMakeTable | observed |
  > | `3` | append (`INSERT`; target table in `Name1`) | `64` dbQAppend | observed |
  > | `4` | UPDATE | `48` dbQUpdate | observed |
  > | `5` | DELETE | `32` dbQDelete | observed |
  > | `6` | crosstab (`TRANSFORM`) | `16` dbQCrosstab | observed |
  > | `7` | data definition (whole SQL in `Expression`, leading space) | `96` dbQDDL | *not observed in a stored query; verified by write/read round-trip only* |
  > | `8` | pass-through | `112` dbQSQLPassThrough | *unverified — from the published MSysQueries tables* |
  > | `9` | UNION | `128` dbQSetOperation | observed |
  >
  > On a crosstab, the `0x06` and `0x09` rows carry the extra structure in their own `Flag`: `0` = the
  > `TRANSFORM` value / an ordinary GROUP BY column, `1` = the `PIVOT` column heading, `2` = a row heading.
  > On an append query, `0x06` `Flag` `-32768` (`0x8000`) marks an `INSERT … VALUES` literal, as against
  > `Flag 0` for a column sourced from an `INSERT … SELECT`.

  > **The `0x03` OPTION row's `Flag` is a bit set, and the bits are cumulative** — one row can carry several,
  > and Access may also split them across rows, so read them as a union of every `0x03` row's `Flag`:
  >
  > | bit | meaning |
  > |---|---|
  > | `0x01` | output-all-fields; also `UNION ALL` on a UNION query, and part of the record-source form below |
  > | `0x02` | `DISTINCT` |
  > | `0x04` | `WITH OWNERACCESS OPTION` |
  > | `0x08` | `DISTINCTROW` |
  > | `0x10` | `TOP` (count as text in `Name1`) |
  > | `0x20` | `PERCENT` — only ever alongside `0x10`, i.e. `48` = `TOP n PERCENT` |
  >
  > So `18` = `DISTINCT TOP`, `24` = `DISTINCTROW TOP`, `50` = `DISTINCT TOP PERCENT`, `56` =
  > `DISTINCTROW TOP PERCENT`. `DISTINCT` and `DISTINCTROW` are separate bits and separate keywords —
  > `DISTINCT` dedupes output rows, `DISTINCTROW` dedupes by contributing base rows. **`Flag 9`
  > (`0x08|0x01`) is what Access writes for its auto-generated form/report record-source queries**, the
  > `~sq_f…` / `~sq_r…` / `~sq_c…` objects, which it renders as `SELECT DISTINCTROW * FROM <table>`.

  > **A query with no `0x05` rows at all has no FROM clause.** `SELECT 1 AS n` is a query Access stores (as
  > a view or a procedure) and stores exactly as any other, minus the table rows: the type row, one `0x06`
  > column row (`Name1` = the alias, `Expression` = the value) and the end row, with the ordinary view flags
  > `0x10000000`. ACE will **open** such a query and return its row, but refuses to use it as a *source* —
  > `SELECT n FROM [Q]` fails with "Query input must contain at least one table or query" — on its own files
  > as much as on LibRed's. LibRed stores the same rows and is the more permissive of the two: its engine
  > resolves one as a derived table like any other view.
  >
  > **A query with no `0x06` rows at all is `SELECT *`.** The absence of output columns is the encoding, not a
  > sign of an unreadable query — every auto-generated record-source query takes this shape. **Nested / parenthesised joins are stored
  flat** — one `0x05` per base table and one `0x07` per join condition, no grouping — so Access re-derives
  the join tree from the conditions (verified). `Order` is a 4-byte **big-endian**
  per-attribute counter (stored in the Binary `Order` column). MSysQueries' only index is the composite PK
  `(ObjectId Int32, Attribute Byte, Order Binary)`; its Binary key encodes as `0x7F` + the raw bytes +
  `00 00 00 00` + a length byte.

  > **Row order matters.** Access writes the rows in the order **type, end, parameters (`0x02`), distinct/top,
  > tables (`0x05`), columns (`0x06`), joins (`0x07`), where (`0x08`), group-by (`0x09`), order-by (`0x0B`)** —
  > *tables before columns* (verified). Access tolerates the wrong order for a **named** table, but a
  > **derived** table defines an alias the column expressions reference, so its `0x05` row must precede the `0x06` rows or
  > Access opens the database yet **fails to run the view**.
  >
  > **Long `Expression` lives on an LVAL page.** `Expression` is a Memo, so a subquery longer than the
  > 64-byte inline limit is written to an LVAL page (§8) — required for Access to *run* the view (an
  > inlined long value opens but won't execute). Verified against Access.
  >
  > **CREATE PROCEDURE** is stored identically to a view (Type-5 `MSysObjects` row + `MSysQueries` rows) —
  > a stored query is a stored query — with one `0x02` parameter row per declared parameter. The Access
  > syntax accepts the parameter list either bare or **parenthesised**, and a parameter may be written
  > `@name`; Access stores the **bare** name (the `@` is stripped — `@Beginning_Date` → `Name1=Beginning_Date`)
  > while the body keeps the `@` reference verbatim: `CREATE PROCEDURE name (p1 datatype, p2 datatype) AS
  > select` or `CREATE PROCEDURE name p1 datatype AS select`. Verified: a LibRed-written parameterized query
  > runs in Access and honours supplied parameter values. **Read-back:** LibRed reconstructs a parameterized
  > query with a leading `PARAMETERS name Type, …;` clause (the `0x02` rows) and lowers body references to a
  > declared name into engine parameters, so LibRed's own engine executes the stored procedure when values are supplied.
  >
  > **A declared name wins over a column of the same name — and a `@` prefix does not distinguish them.**
  > Measured against ACE 12 on Northwind: *every* unqualified occurrence of a declared parameter name is the
  > parameter, whether written bare or as `@name`, even where the query's own table has a column by that name.
  > Only a table-qualified reference is read as the column. Northwind's own `CustOrdersOrders` — declared
  > `CustomerID Text(5)`, body `WHERE CustomerID = @CustomerID` — is therefore a tautology in ACE and returns
  > all 830 orders for any supplied value; a body written `Orders.CustomerID = [CustomerID]` returns the 6 that
  > match. LibRed reproduces all four combinations exactly, so the "obvious" fix of resolving the left-hand
  > name to the column would be a divergence, not a repair.
  >
  > **Complex-column system tables (ACE 12+ only).** Access 2007 introduced multi-value and attachment
  > columns, and with them `MSysComplexColumns` (the registry) plus nine `MSysComplexType_*` flat storage
  > tables. **Jet 4 has none of them.** As the engine creates them (verified, DAO-created ACE 12 database):
  >
  > | table | columns (id) | indexes | `MSysObjects.Flags` |
  > | --- | --- | --- | --- |
  > | `MSysComplexColumns` | `ColumnName` Text(510) (0), `ComplexID` Long **AutoNumber** (4), `ComplexTypeObjectID` Long (1), `ConceptualTableID` Long (3), `FlatTableID` Long (2) | `IdxID`(ComplexID, unique+PK), `IdxConceptualTableID`, `IdxFlatTableID` — all required + ignore-nulls | `0x80000000` |
  > | `MSysComplexType_{UnsignedByte,Short,Long,IEEESingle,IEEEDouble,GUID,Decimal,Text}` | a single `Value` of the matching type (0) | none | `0x80030000` |
  > | `MSysComplexType_Attachment` | `FileData` OLE (3), `FileFlags` Long (5), `FileName` Text(510) (1), `FileTimeStamp` DateTime (4), `FileType` Text(510) (2), `FileURL` Memo (0) | none | `0x80030000` |
  >
  > Column **ids are creation order, not descriptor order** — descriptors are stored alphabetically, so the
  > two differ (e.g. `ComplexID` is the 2nd descriptor but id 4). All are `ParentId` = the Tables container,
  > `Type` = 1, owner = the Engine SID.
  >
  > **`MSysComplexColumns` is load-bearing for object creation even when unused.** ACE consults it whenever
  > it creates a new catalog object, and only then (verified):
  >
  > | operation | without `MSysComplexColumns` |
  > | --- | --- |
  > | `SELECT` / `INSERT` / `UPDATE` / `DELETE` | OK |
  > | `CREATE INDEX`, `ALTER TABLE ADD COLUMN`, `DROP TABLE` | OK |
  > | `CREATE TABLE` | *"Cannot find table or constraint."* |
  > | `CREATE VIEW` | *"…could not find the object 'MSysComplexColumns'."* |
  >
  > So it is exactly the two statements that add an `MSysObjects` row that need it — not the DDL surface as a
  > whole, and not a fixed system-table bind (the `CREATE VIEW` error names the table outright). It is
  > **read-only** from ACE's side: `CREATE TABLE` and `CREATE INDEX` leave it at **0 rows**. The dependency is
  > on this table specifically — without `MSysComplexType_Text` the statements still succeed, and without
  > `MSysQueries` they fail with a different error. LibRed creates all ten in `DatabaseCreator.CreateEmpty` for version ≥ `0x02`, which is what lets ACE run DDL in a
  > LibRed-created database.

  > **Action-query procedure bodies** (a CREATE PROCEDURE body that is not a SELECT) are stored with a
  > different MSysObjects `Flags` and an `Attribute=0x01` row (verified vs ACE). **Every kind keeps its
  > sources, predicate and declared parameters exactly where a SELECT keeps them** — one `0x05` row per table,
  > one `0x07` per join condition, `0x08` for the WHERE, `0x02` per declared parameter — and they differ only
  > in the action row and in what the `0x06` column rows mean:
  > - **Data-definition** (CREATE TABLE / DROP TABLE): MSysObjects `Flags=0x10000060`; one `0x01` row with
  >   `Flag 7` and `Expression` = the **whole DDL statement** verbatim (ACE prepends a single space). No other
  >   rows: the statement is not decomposed at all.
  > - **Append** (INSERT): MSysObjects `Flags=0x10000040`; a `0x01` row with `Flag 3` and `Name1` = the
  >   target table, then one `0x06` column row per appended column — `Name2` = target column, `Expression`
  >   = the value; `Flag 0x8000` marks an INSERT … **VALUES** append (an INSERT … **SELECT** instead uses
  >   `Flag 0` on the `0x06` rows plus the usual `0x05` table / `0x08` where rows).
  > - **Update**: the `0x01` action row has `Flag 4` and nothing else on it — the target is the FROM source.
  >   One `0x06` row per SET assignment: **`Name2` = the assigned column**, `Expression` = the new value.
  >   Over a join, `Name2` is **table-qualified** (`Orders.ShipCountry`) and the join is an ordinary `0x07`
  >   row, so an UPDATE over a join stores exactly what the same join in a SELECT stores.
  > - **Delete**: the `0x01` action row has `Flag 5`. A `DELETE <table>.* FROM …` keeps that target as a
  >   single `0x06` row whose `Expression` is the verbatim `<table>.*` and which has no `Name2`; a
  >   `DELETE FROM …` (no named target) stores **no** `0x06` row at all, and ACE renders it back as
  >   `DELETE * FROM …`.
  > - **Make-table** (`SELECT … INTO`): `Flag 2`, with the target table in `Name1` and, when the target is in
  >   another database file, its path in `Name2`. Everything else is stored as the SELECT it is.
  >
  > **The MSysObjects `Flags` low byte is DAO's own `QueryDef.Type`** — not the `0x01` row's `Flag`, which
  > numbers the kinds differently. Measured across six kinds: `0x10000000` plus crosstab `0x10`, delete
  > `0x20`, update `0x30`, append `0x40`, make-table `0x50`, data-definition `0x60` — exactly the DAO values
  > in the table above (16/32/48/64/80/96).
  >
  > **A declared parameter's facets live in the `0x02` row's `LvExtra`**, and Access renders the PARAMETERS
  > clause from them — a row without them reads back as `Text(255)`. Verified against ACE, declaration by
  > declaration:
  >
  > | declared | `Flag` | `LvExtra` |
  > |---|---|---|
  > | `Text(50)` | 10 | `50` — the length |
  > | `Decimal(18,4)` / `Numeric(18,4)` | 16 | `262162` = `(scale << 16) \| precision` |
  > | `Numeric(10,2)` | 16 | `131082` = `(2 << 16) \| 10` |
  > | `Binary(10)` | 9 | *nothing* — a sized binary records no facet |
  > | `Long`, and every type with no declared size | its code | *nothing* |
  >
  > On every *other* row `LvExtra` holds nothing: for one statement ACE left it null with no parameter
  > declared and wrote `0` and `226` on those same rows once one was, and Northwind's designer-authored query
  > carries `936840680` throughout. Don't model it outside a parameter row.
  >
  > **The `0x02` `Flag` is the Jet on-disk type code**, the same code a column of that type carries — not
  > DAO's type constants, which agree with it only up to `15` (GUID) and then diverge. Measured across every
  > declarable type: `Bit 1`, `Byte 2`, `Short 3`, `Long 4`, `Currency 5`, `Single 6`, `Double 7`,
  > `DateTime 8`, `Binary 9`, `Text 10`, `LongBinary 11`, `Memo 12`, `GUID 15`, `Decimal 16`, **`BigInt 19`**,
  > **`DateTime2 20`**, and `0` for Access's untyped `Value` parameter. The last two are worth noting twice
  > over: DAO numbers `dbBigInt` 16, which is Decimal's code here, and ACE accepts a `BigInt` or `DateTime2`
  > **parameter** on an ACE 12 file, where a *column* of either type is refused — a parameter declares no
  > storage, so nothing forces the format's hand.
  >
  > (A plain view/SELECT query uses `Flags=0x10000000` and no `0x01` row.) LibRed **writes** every kind it
  > reads: CREATE TABLE verbatim, INSERT from VALUES or from a SELECT, UPDATE (joins included), DELETE (with
  > or without a `table.*` target) and make-table, each with its declared parameters and their facets — row
  > for row what ACE writes for the same statement, including the object flags, and ACE runs the result. **Read-back:** LibRed reconstructs and runs
  > every kind whose statement its engine can execute — DDL (verbatim), INSERT from VALUES or from a SELECT,
  > UPDATE (joins included), DELETE and make-table — rebuilt with a leading `PARAMETERS` clause when the query
  > declares parameters, so `EXECUTE name arg, …` binds them. Crosstab, pass-through and UNION read back with
  > an "unsupported" reason naming the kind, and throw when executed.

- **MSysRelationships** defines foreign keys (one row per relationship column): `szRelationship`
  (name), `szObject` (child/referencing table), `szColumn` (child column), `szReferencedObject`
  (parent table), `szReferencedColumn`, `icolumn` (0-based column order within the key),
  `ccolumn` (total column count of the key, repeated on every row), `grbit` (flags: `0x02`
  don't-enforce, `0x100` cascade-update, `0x1000` cascade-delete, `0x2000` delete-set-null). Verified: an
  enforced, no-cascade single-column FK stores `ccolumn = 1`, `icolumn = 0`, `grbit = 0`; a relationship
  cascading both update and delete stores `grbit = 0x1100`.

  > **Writing a relationship.** Access records a relationship purely in `MSysRelationships` (there is
  > **no** `MSysObjects` row for it) **plus** a non-unique index on the child table's FK column(s) —
  > enforcement requires the child FK to be indexed and the parent key to be uniquely indexed (the
  > parent PK). LibRed writes the `MSysRelationships` rows, creates that child-side index, **and** the
  > byte-faithful relationship logical-index linkage in *both* tables' TDEFs (§3.6: outgoing block on
  > the child, incoming block on the parent, cross-referenced by `index_num`) at `CREATE TABLE` time.
  > Verified: a LibRed-created relationship is byte-identical to an ACE-created one (bar index *names*),
  > Access opens the file without repair, and `GetOleDbSchemaTable(Foreign_Keys)` enumerates it.
  >
  > **`ALTER TABLE … ADD CONSTRAINT … FOREIGN KEY`** writes the *same* linkage, but **surgically** onto the
  > two existing (empty) TDEFs: it inserts the child's backing index + outgoing block into the child TDEF
  > (the shared index-insert path, name-sorted) and appends the incoming block to the parent TDEF, then the
  > `MSysRelationships` rows — no format difference from the inline case (and the child index is
  > back-filled if the table already has rows). Verified: Access reads and **enforces** a LibRed-`ALTER`-added
  > FK (RI rejects an orphan child row). A **self-reference** (child = parent, e.g. Employees.ReportsTo →
  > EmployeeID) hosts both ends in the one TDEF: the outgoing block links to an incoming block numbered one
  > past it (`Fk_number = outgoing index_num + 1`), and the incoming block's `index_num2` = the table's own
  > referenced-key (PK) data block — verified read+enforced vs ACE. `FOREIGN KEY NO INDEX` via `ALTER` is
  > not written yet.
  >
  > **`DROP INDEX` vs a relationship (ACE-verified).** ACE refuses to drop an index only if it *is* a
  > relationship's enforcement index, keyed on the **specific index**, not its columns: on the child, the
  > FK's own backing index (named after the relationship); on the parent, the referenced unique/primary key.
  > A *redundant* index over the same column(s) — e.g. an explicit `IX_child_col` alongside the FK's index,
  > which EF creates then drops once the FK provides its own — **is** droppable while the relationship
  > stands (`"used in a relationship"` fires only for the enforcement index). LibRed matches this: the
  > `DropIndex` guard protects the child index whose name equals a relationship name and the parent's
  > unique/PK referenced key, and scaffolding hides the child FK index (so a database cleaner drops the
  > relationship via the table, not by dropping that index).
  >
  > **Renaming a table or column (ACE-verified).** Because this table stores its tables and columns **by
  > name**, a rename has to repoint them — and ACE does. Verified against ACE (via the DAO/ADOX rename path):
  > renaming a table rewrites `szObject` / `szReferencedObject`, renaming a column rewrites `szColumn` / `szReferencedColumn`, and in both cases the
  > relationship keeps its own `szRelationship` name and its enforcement — the rename is **not** refused for a
  > table in an enforced relationship. Nothing else moves: indexes (including the PK) keep their own names and
  > need no fixup because they reference the table and its columns **by id**, and a renamed column keeps its
  > `DEFAULT` (ACE rewrites the name-keyed entry in the table's `LvProp` blob). Stored queries are **not**
  > rewritten — a view naming the old object is left dangling and fails with *"cannot find the input table or
  > query"* (Name AutoCorrect is an Access *application* feature, so it never runs for an engine-level rename).
  > LibRed reproduces exactly this, deliberately including the dangling query.
  >
  > **Name collisions.** Tables and saved queries share **one namespace**: ACE rejects renaming a table onto
  > the name of an existing table *or* an existing query (both verified). Note the unique `(ParentId, Name)`
  > index does **not** enforce the table/query half of that on its own — the two object kinds sit in different
  > containers, so they differ in `ParentId`. A rename therefore has to pre-check `MSysObjects` for a matching
  > `Name` with `Type` 1 (table) or 5 (query), which is what LibRed does — **excluding the object being
  > renamed**, which cannot collide with itself: ACE allows renaming a table to its own name, and allows a
  > case-only change (both verified). The self-rename case is not hypothetical — EF models "move a table to
  > another schema" as a rename, and on a schema-less engine that degrades to `RENAME TO` the *same* name.


---

## Complex columns (attachment / multi-value) — `MSysComplexColumns`, `MSysComplexType_*`, `f_<GUID>_*`

The Access **Complex** column (on-disk type `0x12`, len 4 — see [data-types](data-types.md)) implements
attachment and multi-value columns. Its in-row value is a 4-byte **complex id**; the actual data lives in a
per-column backing table, wired up through hidden system tables. LibRed **reads every piece as an ordinary
table but does not yet auto-resolve** a `0x12` column to its backing rows (a `SELECT` of it returns the raw 4-byte id as `byte[]`).

Four layers — the user table, then three kinds of ordinary hidden/system table:

1. **The user table** holds the `Complex` column; each row's value is the 4-byte complex id.
2. **`MSysComplexColumns`** maps each complex column to its backing table:
   `(ColumnName, ComplexID, ComplexTypeObjectID, ConceptualTableID, FlatTableID)`. E.g.
   `Attachments | 3 | 39 | 79 | 150` = the `Attachments` column of table `79`, element type **39**
   (`MSysComplexType_Attachment`), backed by flat table **150** = `f_<GUID>_Attachments`.
3. **`MSysComplexType_*`** — nine schema templates, one per element subtype, matching the DAO `dbComplex*`
   codes: `UnsignedByte, Short, Long, IEEESingle, IEEEDouble, GUID, Decimal, Text, Attachment`.
   `MSysComplexType_Attachment` = `FileData:Ole, FileFlags:Int32, FileName:Text, FileTimeStamp:DateTime,
   FileType:Text, FileURL:Memo`.
4. **`f_<GUID>_<Column>`** — the actual data, one row per value/attachment. Two bookkeeping `Int32` columns
   plus the subtype's value columns:
   - **`_<Column>`** is the **link back to the owning row** — it holds that row's complex id, and it
     **repeats once per value**, which is what makes the column multi-valued. Indexed, **not** unique.
   - **`<Table>_<Column>`** is the **per-value id**: unique, carries the `MSysComplexPKIndex` **primary**
     index, and is an ordinary **AutoNumber** column whose high-water is the flat table's own
     [`0x14`](page-02a-tdef.md) — there is no complex-specific counter on this side.
   - **`IdxFKPrimaryScalar`** is unique over `[_<Column> + <first value column>]` (`Value` for a scalar,
     `FileName` for an attachment): one record cannot hold the same value twice, and **ACE enforces it**
     (verified — `ComplexDuplicateValueProbeTest`). Adding a value a record already holds is refused with
     *"You cannot enter that value because it duplicates an existing value in the multi-valued lookup or
     attachment field"*, while a fresh value on the same record is accepted. The same value on a **different**
     record is fine, which is what the composite key says and what the corpus shows. A writer must therefore
     reject the duplicate itself rather than relying on the value id's own uniqueness.

   For an Attachments column the rows are the real files (`FileData` = OLE `byte[]`, `FileName`, `FileType`);
   a multi-value scalar column is the same shape with a single `Value` column.

> **Verified, and the reverse reading is excluded.** `PasesDeSalida.Acompaña`: six rows with inline ids
> `1,2,3,4,7,14`, six flat rows, and `_TempField*0` takes the values `2,3,3,4,4,7` — two records holding two
> values each — while `PasesDeSalida_TempField*0` runs `1..6` unique. `XSDFiles.XMLSchemaFiles`: one record,
> `_XMLSchemaFiles` = `1` on all **37** rows. A unique key on the owner link could not produce either.

> **The names are creation-time and do not follow renames.** `<Table>_<Column>` is whatever the table and
> column were called when the complex column was made, and so is the `f_<GUID>_<Column>` table itself:
> `Borrow.BRW_book`'s value-id column is still `Table1_BRW_book`, `Book.BK_category` is backed by
> `f_…_TempField*7` with columns `_TempField*7` / `Book_TempField*7`, and `COVER.Attachments` by `f_…_Field1`.
> **Never build these names from the current schema** — resolve the table through `MSysComplexColumns.FlatTableID`,
> then the two columns through the index shape (primary index → value id; the lone non-unique single-column
> index → owner link).

> **One record id serves every complex column of the table.** The counter is the table's single `0x1C`, so a
> row gets one id when it is created and all of its complex columns carry that same id — `complex1.accdb`'s
> `Table1` has four attachment columns, and records 1, 2 and 3 hold ids 1, 2 and 3 in every one of them. A
> writer allocates once per row, not once per column.

> **Deleting the owning record cascades to every flat table, and rolls no counter back** (verified through
> DAO — `ComplexDeleteCascadeProbeTest`). Deleting `Table1`'s record 3, which held three attachments in
> `Attachment` and two more in `att4`, removed all five flat rows across both tables. The owner's `0x1C`
> stayed at 3 and each flat table's `0x14` kept its value, so the next row still gets id 4 and the freed
> value ids are never reissued. A delete path must therefore remove the flat rows of **every** complex column
> on the table for that id, and must leave both high-waters alone.

> **Both id spaces are sparse high-water counters**, so neither is dense or ordered: record ids run
> `1,2,3,4,7,14` over six rows, and `XSDFiles`' value ids reach `116` over 37 values. And an id is allocated
> when the **row** is created, with or without values — `complex1.accdb`'s `Table1` has three rows with ids
> `1,2,3` and an empty flat table, which Access itself renders as `(0)` in every row's attachment cell. A
> non-null inline id is therefore **not** evidence that any value exists.

> **The in-row complex id is an AutoNumber.** The `0x12` column's descriptor carries the auto-number flag
> (`0x0F` bit `0x04`) and its high-water is the owner table's [`0x1C`](page-02a-tdef.md), separate from the
> table's ordinary counter at `0x14`. Its descriptor also names its catalog row: `0x0B` holds the
> `ComplexID`. See [page-02b-columns.md](page-02b-columns.md).

### Catalog rows for the hidden tables

All three kinds sit under `ParentId` `0x0F000001` (the Tables container) with `Type=1` — they are ordinary
tables as far as the catalog is concerned, distinguished only by `Flags` and `Owner`:

| object | `Flags` | `Owner` |
| --- | --- | --- |
| an ordinary **user** table | `0x00040000` | user SID |
| **`f_<GUID>_<col>`** flat table | `0x800A0000` | **the same user SID** |
| **`MSysComplexType_*`** template | `0x80030000` | `NULL` |
| **`MSysComplexColumns`** | `0x80000000` | `NULL` |
| an ordinary **system** table | `0x80000000` | engine SID |

A flat table is thus system-flagged (`0x80000000`) yet owned by the *user* SID, unlike a real system table —
consistent with it holding user data. `0x00020000` is common to flat and template tables; flat adds
`0x00080000` and templates `0x00010000`, while the plain user-table bit `0x00040000` is on neither. A flat
table can carry its own `LvProp`. Verified in `complex1.accdb` and `LIBRARY.accdb`.

### Attachment payload — `FileData`

An attachment's `FileData` is an OLE long value wrapping the file, with an 8-byte outer header, optional
deflate, and a 20-byte inner header naming the extension. Verified on a `pdf`, an `mp3` and a `png`, across
both storage modes, with the file's own magic number checked after the inner header:

```
outer header, 8 bytes
  [0..3]  compression flag: 1 = a zlib stream follows, 0 = raw bytes follow
  [4..7]  length of the body once decompressed

body  (inflate when the flag is 1 — the inflated length matched [4..7] exactly)
  inner header, 20 bytes
    [0..3]   = 20, the inner header's own length
    [4..7]   = 1                     (constant on every sample)
    [8..11]  = 4                     (extension length in UTF-16 units; 12 + 8 = 20)
    [12..19] extension, UTF-16, null-terminated — "pdf", "mp3", "png"
  then the file's bytes verbatim
```

The stream is standard zlib (RFC 1950, opening `78 5E`), so `ZLibStream` reads it without skipping a header.
`FileFlags` and `FileTimeStamp` were `NULL` on every attachment Access wrote.

> **Capability and policy are different here.** The *format* stores a payload either way — the outer flag
> says which, ACE reads both, and a long value's stored length runs to `0x3FFFFFFF` (ACE accepts that and
> rejects `0x40000000`). Everything below is what **Access** chooses on top of that: which extensions it
> deflates, its 256 MB per file — a quarter of what the format holds — its naming rules, and its
> blocked-extension list. A reader needs none of it; a writer wanting files that look like Access's does.
>
> **When Access compresses is an extension list, not a size rule.** Microsoft documents it on the
> [Attachment object](https://learn.microsoft.com/en-us/office/vba/api/access.attachment): *"Access will
> compress your attached files unless those files are compressed natively."* The listed exceptions are
> `.jpg .jpeg .gif .png .zip .cab .docx .xlsx .xlsb .pptx`; `.tif .exif .bmp .emf .wmf .ico` are compressed,
> and the table is explicitly partial, so anything absent from the exception list is compressed. The three
> measured samples agree exactly: `png` is on the list and was stored raw, while `pdf` (3,397,525 →
> 3,303,763) and `mp3` (5,480,476 → 5,442,917) are not on it and were deflated despite saving almost
> nothing — which a size rule would not explain. ACE honours the flag either way, so getting it wrong still
> reads; matching it is about writing what Access writes.
>
> The same page gives a writer three more limits: an individual file may not exceed **256 MB**, a name may
> not exceed **255 characters** including the extension, and a name may not contain `? " / \ < > * | :` or a
> paragraph mark. Access also blocks a long list of executable extensions (`.exe`, `.bat`, `.vbs`, `.mdb`,
> …) — that is an Access **application** policy, and whether the engine refuses them through DAO is
> untested here.

**To materialize**: read the row's `0x12` id → find the column's row in `MSysComplexColumns` (by `ComplexID`
from the descriptor's `0x0B`, or by name) → open `FlatTableID`'s `f_` table → select the rows whose
**owner-link** column equals that id. All readable today by hand; LibRed does not yet do it for you.
