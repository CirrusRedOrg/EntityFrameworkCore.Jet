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

  **Writing a table object** (verified against Northwind rows). A complete user-table row sets:
  `Id` = TDEF page; `ParentId` = `0x0F000001` (the database's "Tables" container, constant);
  `Type` = `1`; `Name`; `Flags` = `0`; `Owner` = a 2-byte binary SID (`0x69 0x0C` for a
  workgroup-less database, constant across tables); and `DateCreate` / `DateUpdate`. The other
  columns (`Connect`, `Database`, `ForeignName`, `Lv*`, `RmtInfo*`) are null **except `LvProp`**,
  an OLE long-value blob ("MR2"-prefixed) holding the object's **extended properties** — including
  column-level properties such as *Required* (see §3.4) and *DefaultValue*.

  > **Permission rows (`MSysACEs`) — one per object, verified against Northwind.** Every new object needs
  > `MSysACEs` rows or Access warns about permissions when opening it (a **table** still opens; a **query**
  > opens but pops a permissions warning). The table has exactly **four columns** (verified vs Northwind):
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
  > property. Jackcess independently names it `isDdl`: a set flag makes the property definition-protected
  > (`dbSecWriteDef` permission is needed to change/delete it), and it notes that Access only recognises
  > some properties when the classification is correct. The fixture corpus matches that semantic:
  > `DefaultValue`, `Required`, `CheckConstraints`, `GUID`, and `ResultType` are `0x01`, while `Title`,
  > `Author`, `AccessVersion`, and datasheet-layout properties are `0x00`. Jackcess's built-in classifications
  > additionally mark `ValidationRule`/`ValidationText` as DDL and `Caption`/`Description` as ordinary.
  > The flag is independent per entry; it is not a file-version, encryption, owner, or data-type marker.
  > LibRed accepts the two observed values, preserves both the flag and raw value read for every property,
  > and defaults newly constructed schema properties to `0x01`. The
  > `dataType` is an ordinary **`JetDataType` code** (the same byte used by column descriptors and
  > MSysQueries): **`0x0C`** (Memo) for a text value stored as **UTF-16**, **`0x01`** (Boolean) for a single
  > **0/1 byte**, and — on the `MSysDb` object's UI/nav settings only — `0x0A` (Text), `0x02`/`0x03`/`0x04`
  > (Byte/Int16/Int32). The value-block **type** is `0x01` for a column-owned map and `0x00` for the
  > table-owned map (empty owner name). A `DefaultValue` (column property) is the expression's **source
  > text** (e.g. `42`, `'hi'`) — its evaluation semantics (what an expression may contain, the
  > DDL-parser-vs-expression-service split) are in [page-02c-default-values.md](page-02c-default-values.md); table-level
  > `CHECK` constraints are a single **table** property named
  > `CheckConstraints` whose value is a `name\0expression\0` list, terminated by an extra `\0` (verified
  > byte-for-byte vs ACE for `CONSTRAINT CK_BD CHECK ([BirthDate] < NOW())`). `ALTER TABLE … DROP CONSTRAINT
  > <ck>` removes the matching entry from that list and rewrites it (dropping the whole table-level property
  > block when it was the last check) — ACE-verified: after the drop ACE stops enforcing the check. (In
  > Jet/ACE `DROP CONSTRAINT` is polymorphic over the name — FK / PK / unique index / CHECK.)
  >
  > **Property-reader/writer guardrails.** LibRed validates the signature and consumes the blob exactly to
  > its end. Every block, pooled UTF-16 name, owner record, property entry, name-pool index, and value length
  > must remain within its declared parent and use the exact nested lengths above. `Read`,
  > `AddColumnProperties`, and `RemoveOwner` share this representation. Serialization preflights every
  > 16-bit name, owner-record, entry, value, and name-index field plus the 32-bit block lengths before emitting
  > bytes; unmodelled `RawValue` payloads still round-trip verbatim.
  >
  > **`Required` (NOT NULL)** is a per-column **boolean** property (`dataType 0x01`, one `0x01` byte); a
  > **nullable** column simply has **no** `Required` property, and an AutoNumber column is left without one
  > too (verified vs ACE). Within a column's map ACE orders `DefaultValue` **before** `Required`; the
  > name-pool order follows first appearance across all properties — **not** alphabetical. Verified with a
  > deliberately non-alphabetical discriminator: a table whose names first appear as `Required` (a NOT NULL
  > column), then `DefaultValue` (a later `DEFAULT` column), then `CheckConstraints` (an added CHECK) stores
  > the pool in exactly that `["Required","DefaultValue","CheckConstraints"]` order — alphabetical would be
  > `["CheckConstraints","DefaultValue","Required"]`. Example (`Req int NOT NULL, …, Def int DEFAULT 7 NOT
  > NULL`): name pool `["Required","DefaultValue"]`, then `Req`'s `Required`, then `Def`'s `DefaultValue`=`7`
  > and `Required` — reproduced byte-for-byte by `PropertyBlob.Write` (which builds the pool by first
  > appearance via `Distinct()`).
  >
  > **Unmodelled properties round-trip verbatim.** LibRed *interprets* `DefaultValue`, `Required`,
  > `CheckConstraints` and the text `ValidationRule`/`ValidationText` (the last two are **read-only**: surfaced
  > through `INFORMATION_SCHEMA.{TABLES,COLUMNS}.VALIDATION_RULE/VALIDATION_TEXT` to match EFCore.Jet's ADOX
  > `Jet OLEDB:{Table,Column} Validation Rule/Text`, but not yet written or enforced), while a database-first
  > file may carry many more per column (`Format`, `AllowZeroLength`, the numeric `DecimalPlaces`, …). An ALTER that edits one property rewrites the whole
  > blob (`PropertyBlob.Read` → mutate → `Write`), so `PropertyBlob.Property` keeps each value's **exact stored
  > bytes** (`RawValue`) and re-emits them unchanged — a property LibRed doesn't model is never dropped or
  > corrupted by the best-effort UTF-16 value decode (which would mangle a numeric one). `PropertyBlobRoundTripTests`.
  >
  > LibRed **writes** `DefaultValue`, `Required` and `CheckConstraints` properties (`PropertyBlob.Write`) and
  > **reads** them back (`ColumnDef.DefaultValue`, `ColumnDef.IsNullable`, `TableDef.CheckConstraints`),
  > applying the default when an insert omits the column and **rejecting** an insert that leaves a required
  > column null ("You must enter a value in the '<table>.<column>' field.", matching Access). Access
  > **applies the default**, **enforces Required**, and **enforces the CHECK** on its own inserts —
  > including on a LibRed-created table (verified: ACE rejects an insert omitting a LibRed `NOT NULL` column). `LvProp` is stored
  > on a **single LVAL page** (`LongValueWriter`, descriptor flag `0x40`) — the form Access's property
  > loader requires. **Verified:** Access opens the file and **applies the default** on its own insert
  > that omits the column. (An *inline* value, flag `0x80`, is written and read fine by LibRed but is
  > **not** recognised by Access's property loader — established by dumping the raw descriptors; nothing
  > else differs, only `MSysObjects`+`MSysACEs` are touched.)
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
  > LibRed allow it. Round-trip verified: ACE reads the promoted counter and assigns next id = seed. Guards:
  > Jet permits only one AutoNumber per table (a second is rejected), and a column in a relationship is
  > rejected (matching ACE).
  >
  > **Demoting a counter to a plain int** (`ALTER COLUMN <counter> LONG`) is the reverse in-place edit — clear
  > the `0x04` flag and reset the header to a non-AutoNumber table's state (`0x14` = 0, `0x18` = 1); values are
  > kept and the column stops auto-assigning. Unlike promotion this is **not** a divergence: ACE allows it too
  > (matching the Access UI's AutoNumber→Number change). Round-trip verified: ACE reads the demoted column as a
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
  > Increment-vs-Random distinction lives entirely in this default expression. Verified against a modern
  > Office-365-authored file (`Table1(ID AutoNumber, New Values=Random)`): the ID descriptor and TDEF header are
  > **byte-identical** to an increment counter, and LibRed already surfaces it (`ColumnDef.DefaultValue` =
  > `"GenUniqueID()"`) via the ordinary DefaultValue read path — no special handling needed to detect it. A
  > Random AutoNumber **can** be created in pure SQL (not UI/DAO-only): `CREATE TABLE T (Id COUNTER DEFAULT
  > GenUniqueID(), ...)` — also `AUTOINCREMENT`/`COUNTER PRIMARY KEY` forms — is accepted by ACE and yields
  > genuinely random signed-Long IDs on insert (verified: `-1637443712, 1680187777, 83315118`), reading back
  > byte-identical to the UI-authored column. `GenUniqueID()` **is a real ACE default-expression**, not a marker:
  > `SELECT GenUniqueID()`
  > errors ("Undefined function"), yet an **unquoted** `col LONG DEFAULT GenUniqueID()` **is** accepted and
  > generates a **random signed Long per row** (verified: `117617513`, `904519542`, `-1470084161`). It is
  > accepted **only on a `LONG` (Int32) column** — the same width a `COUNTER` stores; **every other type is
  > rejected** ("Cannot place this validation expression on this field"), verified across BYTE/SHORT/SINGLE/
  > DOUBLE/CURRENCY/DECIMAL/GUID/DATETIME/BIT/TEXT. Quoting it —
  > `DEFAULT 'GenUniqueID()'` — makes it a plain literal string stored verbatim. So a Random AutoNumber is
  > effectively an AutoNumber column carrying the unquoted `GenUniqueID()` default. **LibRed now creates and
  > inserts these**: `CREATE TABLE ( Id COUNTER DEFAULT GenUniqueID(), … )` persists the `GenUniqueID()` default
  > to the column's LvProp (byte-identical to a UI/ACE-authored one, so ACE reads it as a Random AutoNumber), and
  > on insert LibRed assigns a random non-zero Int32 per row instead of the sequential counter, leaving the TDEF
  > high-water (`0x14`) unadvanced (as ACE does). `ColumnDef.IsRandomAutoNumber` gates this off the default text.
  > A **plain (non-AutoNumber) `LONG DEFAULT GenUniqueID()`** column works too: `GenUniqueID()` is a real
  > evaluable function in LibRed's expression evaluator (a random non-zero Int32), so an omitted value defaults to
  > a random Long while a supplied value is kept — matching ACE, which reads and applies a LibRed-written one.
  > LibRed also **enforces the LONG-only restriction at CREATE/ADD-COLUMN time** (`GenUniqueID()` on any other
  > type raises "Cannot place this validation expression on this field"), matching ACE.

- **LVAL (long-value) page** — a data page (type `0x01`) whose owner field (`0x04`) is the ASCII marker
  `"LVAL"` instead of a TDEF page number. A single-page long value stores the whole payload as row 0; the
  in-row reference descriptor is `[length:3][flags:1][row:1][page:3][4 reserved]` with flag `0x40` = single
  page (`0x80` = inline, payload follows the descriptor; `0x00` = chained across pages). LibRed writes the
  single-page form (`LongValueWriter`); chained pages for payloads larger than one page are not written yet.

  > With those fields set, Access **enumerates** a LibRed-created table (it appears in the
  > schema/Tables rowset) — verified via OLE DB. Maintaining MSysObjects' indexes (the composite
  > `ParentId+Name` and `Id` indexes) then lets Access **resolve the table by name** and attempt
  > to open it. Opening it then requires the table's own structures to be byte-valid to Access
  > (see §3.7).

- **Views / queries** are `MSysObjects` rows of **Type 5** with a **negative synthetic `Id`** (queries
  increment from `0x80000000`), `ParentId 0x0F000001`, `Flags 0x10000000`, `LvProp` null.

  > **MSysQueries columns (8, verified vs Northwind).** The table has exactly: `ObjectId` (Int32, the
  > query object's `Id`), `Attribute` (Byte, the row kind — see below), `Flag` (Int16, attribute-specific),
  > `Name1` and `Name2` (Text, attribute-specific names), `Expression` (Memo, attribute-specific text —
  > SQL fragments), `Order` (Binary, a 4-byte big-endian per-attribute sequence counter), and `LvExtra`
  > (Int32) — a long-value/overflow field that is **null in every Northwind query row** and that LibRed
  > leaves null (not needed for the queries it writes). Only index = composite PK `(ObjectId, Attribute,
  > Order)`.

  The query itself is stored in **MSysQueries**, decomposed into rows keyed by `ObjectId`, each with an `Attribute`
  byte (Jackcess "query rows", verified vs ACE for the "simple SELECT" a view may contain): `0x00` =
  query type (`Flag 1` = SELECT), `0x02` = a **declared parameter** (`Name1`=parameter name, `Flag`=Jet
  type code — same codes as on-disk column types, e.g. `8`=DateTime; one row per parameter, `Order`
  1-based), `0x03` = flags (`Flag 2` = DISTINCT; **`Flag 0x10` = TOP**, with `Name1` = the count as text,
  e.g. `Name1=10`), `0x05` = FROM source, `0x06` =
  output column (`Expression`=verbatim text; **`Name1`=the column's output alias** when it has one, e.g.
  `Expression=Customers.CompanyName`, `Name1=CustomerName`; a computed column stores its whole verbatim
  expression, `Expression=(FirstName + ' ' + LastName)`, `Name1=Salesperson`), `0x07` = join
  (`Expression`=condition, `Flag`=kind, and **`Name1`/`Name2`=the two tables named in the condition** —
  `Customers.CustomerID = Orders.CustomerID` → `Name1=Customers`, `Name2=Orders`), `0x08` = WHERE
  (`Expression`), `0x09` = a **GROUP BY** column (`Expression`; one row per group column, in order —
  their presence makes it a "totals" query, and the aggregate output columns are ordinary `0x06` rows,
  e.g. `Expression=Sum(...)`), `0x0B` = an **ORDER BY** key (`Expression`=the sort column, `Name1`=`"d"`
  for **descending**, absent for ascending; one row per key, `Order` 1-based — verified against Northwind's
  "Ten Most Expensive Products", `SELECT TOP 10 … ORDER BY Products.UnitPrice DESC`), `0xFF` = end. A **FROM source** (`0x05`) is either a **named table**
  (`Name1`=table, `Name2`=alias) or a **derived table / subquery** (`Expression`=the verbatim inner
  subquery SQL — outer parens and `AS alias` stripped, whitespace preserved — `Name2`=alias, **no `Name1`**;
  verified against Northwind's "Customer and Suppliers by City"). **Nested / parenthesised joins are stored
  flat** — one `0x05` per base table and one `0x07` per join condition, no grouping — so Access re-derives
  the join tree from the conditions (verified against "Invoices": 6 tables, 5 flat joins). `Order` is a 4-byte **big-endian**
  per-attribute counter (stored in the Binary `Order` column). MSysQueries' only index is the composite PK
  `(ObjectId Int32, Attribute Byte, Order Binary)`; its Binary key encodes as `0x7F` + the raw bytes +
  `00 00 00 00` + a length byte.

  > **Row order matters.** Access writes the rows in the order **type, end, parameters (`0x02`), distinct/top,
  > tables (`0x05`), columns (`0x06`), joins (`0x07`), where (`0x08`), group-by (`0x09`), order-by (`0x0B`)** — *tables before columns* (verified across five
  > Northwind views). Access tolerates the wrong order for a **named** table, but a **derived** table
  > defines an alias the column expressions reference, so its `0x05` row must precede the `0x06` rows or
  > Access opens the database yet **fails to run the view**.
  >
  > **Long `Expression` lives on an LVAL page.** `Expression` is a Memo, so a subquery longer than the
  > 64-byte inline limit is written to an LVAL page (§8) — required for Access to *run* the view (an
  > inlined long value opens but won't execute). Verified: a LibRed derived-table UNION view returns the
  > same rows in Access as the equivalent Northwind view.
  >
  > **CREATE PROCEDURE** is stored identically to a view (Type-5 `MSysObjects` row + `MSysQueries` rows) —
  > a stored query is a stored query — with one `0x02` parameter row per declared parameter. The Access
  > syntax accepts the parameter list either bare or **parenthesised**, and a parameter may be written
  > `@name`; Access stores the **bare** name (the `@` is stripped — `@Beginning_Date` → `Name1=Beginning_Date`)
  > while the body keeps the `@` reference verbatim: `CREATE PROCEDURE name (p1 datatype, p2 datatype) AS
  > select` or `CREATE PROCEDURE name p1 datatype AS select`. Verified: a LibRed-written parameterized query
  > runs in Access and honours supplied parameter values. **Read-back:** LibRed reconstructs a parameterized query with a leading `PARAMETERS
  > name Type, …;` clause (the `0x02` rows) and lowers body references to a declared name into engine
  > parameters, so LibRed's own engine executes the stored procedure when values are supplied.
  >
  > **Action-query procedure bodies** (a CREATE PROCEDURE body that is not a SELECT) are stored with a
  > different MSysObjects `Flags` and an `Attribute=0x01` row (verified vs ACE):
  > - **Data-definition** (CREATE TABLE / DROP TABLE): MSysObjects `Flags=0x10000060`; one `0x01` row with
  >   `Flag 7` and `Expression` = the **whole DDL statement** verbatim (ACE prepends a single space).
  > - **Append** (INSERT): MSysObjects `Flags=0x10000040`; a `0x01` row with `Flag 3` and `Name1` = the
  >   target table, then one `0x06` column row per appended column — `Name2` = target column, `Expression`
  >   = the value; `Flag 0x8000` marks an INSERT … **VALUES** append (an INSERT … **SELECT** instead uses
  >   `Flag 0` on the `0x06` rows plus the usual `0x05` table / `0x08` where rows).
  >
  > (A plain view/SELECT query uses `Flags=0x10000000` and no `0x01` row.) LibRed writes CREATE TABLE and
  > INSERT … VALUES bodies; INSERT … SELECT and UPDATE/DELETE are not written yet. **Read-back:** LibRed
  > reconstructs a stored action query from these rows (DDL → the verbatim SQL; INSERT … VALUES → a rebuilt
  > `INSERT INTO t (cols) VALUES (…)`) and executes it by name; kinds it can't run (INSERT … SELECT, etc.)
  > read back with an "unsupported" reason and throw when executed.

- **MSysRelationships** defines foreign keys (one row per relationship column): `szRelationship`
  (name), `szObject` (child/referencing table), `szColumn` (child column), `szReferencedObject`
  (parent table), `szReferencedColumn`, `icolumn` (0-based column order within the key),
  `ccolumn` (total column count of the key, repeated on every row), `grbit` (flags: `0x02`
  don't-enforce, `0x100` cascade-update, `0x1000` cascade-delete, `0x2000` delete-set-null). Verified against Northwind: an
  enforced, no-cascade single-column FK stores `ccolumn = 1`, `icolumn = 0`, `grbit = 0`; the
  cascade nav-pane relationships store `grbit = 0x1100` (update+delete cascade).

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
  > name**, a rename has to repoint them — and ACE does. Measured (Jet suite's `RenameFanOutProbeTest`,
  > against a real ACE engine via the DAO/ADOX rename path): renaming a table rewrites `szObject` /
  > `szReferencedObject`, renaming a column rewrites `szColumn` / `szReferencedColumn`, and in both cases the
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
per-column backing table, wired up through hidden system tables. Decoded from the dev-edition Northwind
fixture; LibRed **reads every piece as an ordinary table but does not yet auto-resolve** a `0x12` column to
its backing rows (a `SELECT` of it returns the raw 4-byte id as `byte[]`).

Three layers, all ordinary hidden/system tables:

1. **The user table** holds the `Complex` column; each row's value is the 4-byte complex id.
2. **`MSysComplexColumns`** maps each complex column to its backing table:
   `(ColumnName, ComplexID, ComplexTypeObjectID, ConceptualTableID, FlatTableID)`. E.g.
   `Attachments | 3 | 39 | 79 | 150` = the `Attachments` column of table `79`, element type **39**
   (`MSysComplexType_Attachment`), backed by flat table **150** = `f_<GUID>_Attachments`.
3. **`MSysComplexType_*`** — nine schema templates, one per element subtype, matching the DAO `dbComplex*`
   codes: `UnsignedByte, Short, Long, IEEESingle, IEEEDouble, GUID, Decimal, Text, Attachment`.
   `MSysComplexType_Attachment` = `FileData:Ole, FileFlags:Int32, FileName:Text, FileTimeStamp:DateTime,
   FileType:Text, FileURL:Memo`.
4. **`f_<GUID>_<Column>`** — the actual data, one row per value/attachment: `_<Column>:Int32` (PK),
   `<ConceptualTable>_<Column>:Int32` (FK back to the owning row), then the subtype's value columns. For an
   Attachments column the rows are the real files (`FileData` = OLE `byte[]`, `FileName`, `FileType`);
   a multi-value scalar column is the same shape with just the value column + FK.

**To materialize** (if ever needed): read the row's `0x12` id → look up its column in `MSysComplexColumns` →
open `FlatTableID`'s `f_` table → select rows whose FK equals that id. All readable today by hand.
