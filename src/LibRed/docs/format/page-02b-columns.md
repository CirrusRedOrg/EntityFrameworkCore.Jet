# TDEF: columns and column maintenance

> Part of the [LibRed Jet / ACE file-format reference](README.md). Cross-references use the original **§-numbers**; the [section map](README.md#section-map) says which file each lives in.

### 3.4 Column descriptor (25 bytes)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Data type (see §6) |
| `0x01` | 2 | Record marker `0x0659` (see §3.1 note); ignored |
| `0x03` | 2 | Unknown (zero observed) |
| `0x05` | 2 | Column id |
| `0x07` | 2 | Variable-table index. For a **fixed** column it is the running count of variable columns with a smaller id (**not** `0`), dropped ones included — ACE's own `ADD COLUMN` writes `2` for a LONG added to `(K LONG, A TEXT, B TEXT)`, and still `2` when `B` was dropped first: an added column's count is the `0x2B` high-water. For a **variable** column it is that column's own slot index, which is the `0x2B` **high-water** and *not* the count of live variable columns: after a variable column is dropped the next one goes above the abandoned slot, so the two part company. Verified byte-for-byte against DAO-written system tables and against ACE performing the same DDL. |
| `0x09` | 2 | Ordinal position — DAO's `Field.OrdinalPosition`. The engine presents columns in descriptor order, not by this; DAO keeps the two in step by moving the descriptor when it sets it. At creation a second copy of the column id `0x05` on a **user** table, but **zero** on the tables the engine writes for itself (see the note below). It **diverges** after an `ALTER COLUMN` type change, which burns a new id into `0x05` yet leaves `0x09` alone (§3.8), after a `DROP COLUMN`, whose gap the next `ADD COLUMN` closes by ranking the values, and whenever DAO sets it |
| `0x0B` | 1 | Numeric **precision** (Decimal/Numeric columns); on a **Complex** column the `MSysComplexColumns.ComplexID` (see below); otherwise the low byte of the collation's LANGID (the database default, e.g. `0x09` for en-US) |
| `0x0C` | 1 | Numeric **scale** (Decimal/Numeric columns); `0` on a **Complex** column; otherwise the high byte of the LANGID (`0x04` for en-US) |
| `0x0D` | 1 | Collation **sort id** — the LCID's high word; `0` except for an alternate sort order (see the note below) |
| `0x0E` | 1 | Collation **sort-order version**: `0` = General Legacy (Access 2000–2007), `1` = the "General" order Access 2010+ made default (a different key encoding, §10.4) |
| `0x0F` | 1 | Flags (see below) |
| `0x10` | 1 | Extended flags: `0x01` compressed-Unicode capable, `0x10` attachment value column, `0xC0` calculated column; a complex column's flat table also sets `0x04` and `0x08` (see below) |
| `0x11` | 4 | Unknown (zero observed) |
| `0x15` | 2 | Fixed-data offset within the row's fixed region |
| `0x17` | 2 | Length (bytes) |

**Flags (`0x0F`):** `0x01` fixed-length, `0x02` updatable, `0x04` auto-number,
`0x10` system-catalog column, `0x20` security-identifier column, `0x40` auto-number GUID, `0x80` hyperlink
(on a Memo column). `0x08` is set on no column seen.

> **`0x10` and `0x20` mark the engine's own catalog (verified).** `0x10` is set on every column of
> `MSysObjects`, `MSysACEs`, `MSysQueries`, `MSysRelationships` and `MSysComplexColumns`, and on no other —
> not on the other `MSys*` tables Access creates (`MSysAccessStorage`, `MSysResources`, `MSysNavPane*`,
> `MSysNameMap`, the `MSysComplexType_*` templates) and not on any user column. `0x20` is set on exactly the
> two columns that hold a Windows SID, `MSysObjects.Owner` and `MSysACEs.SID`, always together with `0x10`.
> mdbtools also gives `0x10` to replication columns (`s_…`, `Gen_…`), which no file seen has (unverified).

> **Extended flags beyond `0x01`/`0xC0` belong to the complex columns (verified where set).** `0x10` is set on
> exactly the six attachment value columns — `FileData`, `FileFlags`, `FileName`, `FileTimeStamp`, `FileType`,
> `FileURL` — in the `MSysComplexType_Attachment` template and in every attachment flat table, and on no other
> column. A flat table (`f_<GUID>_…`) also sets `0x08` on its `_<column>` column and `0x04` on its
> `<table>_<column>` column. mdbtools reads `0x08` as the flat table's foreign key to the complex id, which
> Access refuses to open a flat table without; what `0x04` means is unknown (both unverified). mdbtools also
> gives `0x20` to a version-history column, which no file seen has.

> **A `Complex` column (type `0x12`) is an AutoNumber, and its `0x0B` names its `MSysComplexColumns` row.**
> Verified on all six complex columns across two files: `0x0F` = `0x07` on every one — fixed `0x01`,
> updatable `0x02` and **auto-number `0x04`** — so the 4-byte in-row complex id is engine-generated per row,
> exactly like an ordinary counter. Its high-water is **not** `0x14`, which belongs to the table's own
> AutoNumber; it is [`0x1C`](page-02a-tdef.md), which is why mdbtools calls that field `ct_autonum`. **A
> table can therefore carry two independent AutoNumber counters** — `LIBRARY.accdb`'s `Book` has `BK_ID` at
> `0x14` = 8 and `BK_category` at `0x1C` = 10.
>
> Two counters, but **any number of flagged columns**: every complex column on the table sets `0x04` while
> sharing the single `0x1C`, so counting flagged columns does not count counters. `MSysResources` has two
> flagged (`Id`, `Data`) and `complex1.accdb`'s `Table1` five (`ID` plus four attachment columns). Two
> consequences for a reader/writer: the `0x14`/`0x18` seed and increment describe only the non-complex one and
> must not be reported for the others, and a "one AutoNumber per table" rule must be applied over the
> non-complex columns alone or it rejects a table Access created quite normally.
>
> `0x0B` carries the column's `ComplexID`, the key of its `MSysComplexColumns` row, where an ordinary column
> would hold a precision or the LANGID low byte — and `0x0C` is `0`, where an ordinary column holds the
> LANGID high byte (`04` for en-US on every plain column beside these). So the descriptor points at the
> catalog entry directly; a reader need not match on column name.
>
> | column | `0x0B` | `MSysComplexColumns.ComplexID` |
> | --- | --- | --- |
> | `complex1` `MSysResources.Data` | 1 | 1 |
> | `complex1` `Table1.Attachment` | 2 | 2 |
> | `LIBRARY` `MSysResources.Data` | 1 | 1 |
> | `LIBRARY` `Book.BK_category` | 2 | 2 |
> | `LIBRARY` `Borrow.BRW_book` | 3 | 3 |
> | `LIBRARY` `Borrow.BRW_mmberINFO` | 4 | 4 |
>
> *Unverified:* whether the id is one byte at `0x0B` or a 16-bit value spanning `0x0B`–`0x0C`. Every observed
> id is 1–4, so a zero at `0x0C` fits both readings.

> **`0x09` is written by everything that creates a user table, and only by those.** Every genuine user
> table carries the id on every column, and every zero belongs to a table the engine made for itself —
> `MSysObjects`, `MSysACEs`, `MSysQueries`, `MSysRelationships`, `MSysComplexColumns`,
> `MSysComplexType_*`, and the `f_<GUID>_Data` complex-column backing tables. A database with no user
> tables is therefore *all* zeros. (`MSysAccessStorage` goes each way depending on the file, so it is not
> part of the bootstrap set.)
>
> ACE's SQL DDL, DAO's object model (`CreateTableDef`/`CreateField`/`Append`, the path Access's UI uses)
> and DAO-executed SQL all write the id. **Compacting a database preserves the field exactly** — before and
> after are byte-identical. A writer that generalises from the system tables and writes zero everywhere is
> wrong; LibRed writes the id except on a system column.
>
> **More exactly, `0x09` is DAO's `Field.OrdinalPosition`** — a presentation value that DAO reports, not the
> order the engine presents in. At creation it counts 0, 1, 2 … and so equals the id. Verified against ACE
> and DAO:
>
> - **The engine presents columns in descriptor order and ignores `0x09`.** With the two made to disagree by
>   patching `0x09` alone, ACE's `SELECT *`, the OLE DB schema's `ORDINAL_POSITION` (1-based) and the order
>   of DAO's `Fields` collection all follow the descriptors; only `Field.OrdinalPosition` reports `0x09`,
>   verbatim.
> - **Setting `OrdinalPosition` through DAO writes the value and moves the descriptor**, which is what keeps
>   the two in step in any file DAO or Access wrote. Ties and gaps are allowed: on `(A, B, C, D, E)`, setting
>   `E` to 0 and then `B` to 7 leaves descriptors in the order `A E C D B` with `0x09` = `0 0 2 3 7`. The moved
>   descriptor lands before the first one with a larger `0x09` (after the last when there is none) — all
>   three measured moves fit that; with a tie it went after the column already there. Only the definition page
>   changes — the value is not a property in the LvProp blob.
> - **`DROP COLUMN` leaves the others alone**, so a gap opens; so do `ALTER COLUMN` (a retype keeps `0x09`
>   while `0x05` takes a new id, §3.8) and `CREATE INDEX`.
> - **`ADD COLUMN` compacts it**: every distinct value is replaced by its rank, so tied columns stay tied,
>   and the new column takes the next rank — `0 0 3 7` after dropping `C` becomes `0 0 1 2`, the added
>   column `3`. After SQL DDL alone there are no ties, and this is simply each column's position:
>   `(A, B, C, D, E)`, drop `B`, add `F` → `0x05` ids `0 2 3 4 5`, `0x09` `0 1 2 3 4`.
>
> So **descriptor order is display order**, and a reader needs nothing else — LibRed reads column order from
> descriptor order and never reads `0x09`, and matches ACE even in a file where the two were made to disagree.
> A reader that assumed descriptor order is id order would be wrong after a reorder: `E` (id 4) precedes `B`
> (id 1). Row decoding is unaffected, since it goes by id (`0x05`), variable slot (`0x07`) and fixed offset,
> never by descriptor position.

> **Date/Time Extended carries only the primary language id.** For a `DATETIME2` column ACE writes the
> **low byte** of the database's LANGID at `0x0B`/`0x0C` — the primary language with the sublanguage half
> cleared — and zero at `0x0D`/`0x0E`, where every other type carries the whole LANGID. The value is 42
> bytes of ASCII (§6), so there is nothing to collate.
>
> Verified across collating orders, with a `TEXT` column in the same table for comparison:
>
> | database LANGID | `TEXT` `0x0B`/`0x0C` | `DATETIME2` `0x0B`/`0x0C` |
> | --- | --- | --- |
> | `0x0409` en-US | `09 04` | `09 00` |
> | `0x0809` en-GB | `09 08` | `09 00` |
> | `0x0407` German | `07 04` | `07 00` |
> | `0x040E` Hungarian | `0E 04` | `0E 00` |
> | `0x041D` Swedish | `1D 04` | `1D 00` |
>
> On an en-US database alone this reads as the constant `0x0009`, but it is not a constant: en-GB gives the
> same value only because it shares en-US's primary id.
> Every sort order Access offers has a primary id below `0xFF`, so "low byte" and Windows' `PRIMARYLANGID`
> (mask `0x3FF`) cannot be told apart here.

> **Every flag a user column carries is modelled; the rest ride through raw.** LibRed reads the user-column
> bits of `0x0F` and `0x10` into `ColumnDef` (`IsUpdatable`/`IsGuidAutoNumber`/`IsHyperlink`,
> `SupportsCompressedUnicode`/`IsCalculated`) and composes them back on write, so they round-trip explicitly.
> It writes the catalog bits `0x10`/`0x20` on the system tables it creates. Everything else — the reserved
> words at `0x03` and `0x11`, the catalog bits of an existing column, and the `0x10` bits it does not model,
> which the complex columns' flat tables set — survives because every ALTER edits the existing descriptor in
> place, as ACE does, rather than re-emitting it.

> **Nullability, defaults and checks are *not* in the descriptor.** The column's *Required* (NOT NULL)
> property is **not** encoded anywhere in the 25-byte descriptor — verified: a nullable column and a
> non-null column of the same type have **byte-identical** descriptors, and no flag in `0x0F` or `0x10`
> means nullability. `Required`,
> `DefaultValue`, and `CheckConstraints` instead live in the table's **column-properties blob** (Jet's
> per-object extended properties, a.k.a. `LvProp`, stored in the `MSysObjects` row). LibRed reads and
> writes all three, byte-for-byte vs ACE — the **on-disk `LvProp` format** is documented in
> [system-catalog.md](system-catalog.md), and the **`DEFAULT` expression semantics** (what a default may
> contain, how it is evaluated) in [page-02c-default-values.md](page-02c-default-values.md).

### 3.4a Calculated columns

A calculated column (extended flag `0xC0` at `0x10`, ACE 14+) does not store a bare value. It is **always
variable-length**, and its slot holds a fixed envelope around the result ACE last computed:

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 4 | **VBA error number**, little-endian — zero when the expression produced a value (or Null) |
| `0x04` | 12 | Reserved; zero in every row measured |
| `0x10` | 4 | Payload length, little-endian |
| `0x14` | *n* | Payload — the value in its ordinary on-disk encoding for its type |
| `0x14`+*n* | 3 | Padding; zero in every row measured |

> **An error is stored, and it is not the same as Null.** The expression service raises rather than
> propagating in some cases — notably the conversions, which is why `CDbl(Null)` is an error and not a Null
> result — and ACE caches the failure instead of a value. Measured on a `Double`-result column:
>
> | expression, over the bad input | envelope |
> | --- | --- |
> | `[Qty]/3` over Null (propagates) | all 23 bytes zero — a zero-length payload, i.e. Null |
> | `CDbl([Qty])/3` over Null | starts `5E 00 00 00`, 38 bytes — **94**, VBA "Invalid use of Null" |
> | `CDbl([A])` over `'hello'` | starts `0D 00 00 00`, 38 bytes — **13**, VBA "Type mismatch" |
>
> So the leading field is the VBA runtime error number, and an error envelope is 38 bytes against 23 for a
> Null. Reading such a column through OLE DB fails with *"Multiple-step OLE DB operation generated errors"* —
> ACE will not hand back a row whose calculated column is in an error state. Anything decoding the payload
> alone reads it as Null, which is what LibRed does today and is a silent disagreement, not a Null.

so a stored value occupies *n* + 23 bytes. The payload is encoded as the same value would be in an ordinary
column, with one measured exception: **text compression follows the declared type, not the payload.** A
calculated **Text** payload takes the usual compressed (`FF FE` prefix) form, and takes it even though the
column's extended flags carry `0xC0` and not `0x01` — so the compressed-capable flag does not gate it here,
the same exemption inline long values get. A calculated **Memo** payload is **never** compressed, at any
length. Measured: `"hello-x"` in a Text column stores as 9 compressed bytes, `"hello-memo"` in a Memo column
as 20 bytes of raw UTF-16LE.

> **The descriptor's type is a *storage* type, not the result type.** ACE widens the declared type to the
> next one in its family and stores the payload at the result's natural width, so reading `0x17` bytes for
> the declared type reads the wrong number. The payload length is what says how to decode it:
>
> | Descriptor type (`0x00`) | Payload length | Value is |
> | --- | --- | --- |
> | `0x03` Int16 | 1 | Boolean |
> | `0x04` Int32 | 1 | Byte |
> | `0x04` Int32 | 2 | Int16 |
> | `0x04` Int32 | 4 | Int32 |
> | `0x07` Double | 4 | Single |
> | `0x07` Double | 8 | Double |
> | `0x05` Currency | 8 | Currency |
> | `0x08` DateTime | 8 | DateTime |
> | `0x0A` Text | *n* | Text |
> | any | 0 | **Null** |
>
> The pair is unambiguous: Boolean and Byte both store one byte but promote to *different* declared types.
> A Null result is a zero-length payload — the null-bitmap bit stays **set**, so the bit means "present"
> here and nothing more. That also makes a calculated **Boolean** a trap: an ordinary Boolean *is* its
> bitmap bit, but a calculated one carries a real payload (`FF`/`00`) and its bit is set even when the
> value is False.
>
> **The declared length at `0x17` is a constant per result-type family, not a payload size.** Across
> every type DAO will create: `0` for a Memo result, `509` for Text at *any* requested size (10, 60 and 255
> all give 509), `510` for Binary, and `39` for everything else including GUID — which, like Memo and Binary,
> takes descriptor type `0x0A` Text, so only the length tells the three apart. Nothing moves these: not the
> size requested, not the length of the expression. Because the requested size is discarded, ACE does **not**
> truncate — a `Text(5)` calculated column stores and returns a much longer result in full.
>
> **A calculated Memo arrives through the long-value machinery.** ACE declares it `0x0A` Text with a
> declared length of `0`, gives it a **long-value map entry**, and stores a long-value descriptor in the
> slot; the envelope above is what that descriptor resolves to. So a long-value map may name a column whose
> declared type is not Memo/OLE — a reader that rejects such a TDEF makes the whole database unopenable,
> because the catalog loads every TDEF.
>
> That descriptor obeys the ordinary long-value rules: the value inlines while it is at most 64 bytes and
> otherwise takes its own LVAL page. What is measured against that limit is the **whole envelope**, 23 bytes
> of frame plus the uncompressed payload, so a calculated Memo crosses it sooner than the text alone
> suggests — `"hello-memo"` inlines at 43 bytes, while a 36-character result is 95 and spills.
>
> Verified against ACE-authored columns of every type DAO will create. LibRed reads **and writes** these
> values: it evaluates the expression itself and refreshes the cache on exactly the writes ACE would — when
> an UPDATE touches a column the expression reads, and not otherwise. It also **creates** them, and ACE
> accepts, evaluates and recomputes the result for every result type. Three things must agree or Access
> reads the payload at the wrong width: the descriptor's promoted type, the constant declared length, and `ResultType` in the
> blob. A Memo result additionally needs long-value maps, which its *declared* type does not indicate.

> `0x0B`–`0x0C` is a union keyed by type: for a Decimal/Numeric column (type `0x10`) it holds
> the **precision** (`0x0B`) and **scale** (`0x0C`) — verified with a `DECIMAL(12,3)` column,
> which reads precision = 12, scale = 3; for every other type it holds the collation's LANGID (below) —
> the database default, so `0x0409` only on an en-US database.
>
> **`0x0B`–`0x0E` is a 32-bit Windows LCID with the sort-order version in its unused top byte.** For
> non-numeric columns the four bytes are:
>
> | offset | size | meaning |
> |---|---|---|
> | `0x0B` | 2 | **LANGID**, little-endian (`0x0409` = 1033 en-US) — the low word of the LCID |
> | `0x0D` | 1 | **Sort id** — the LCID's high word |
> | `0x0E` | 1 | **Sort-order version**: `0` = the legacy compacted table, `1` = the Access-2010 NLS order |
>
> so the full LCID is `(0x0D << 16) | LANGID`, and Jet reuses the LCID's otherwise-unused top byte for the
> version. Windows does not define that byte, which is what makes the reuse safe.
>
> **The sort id is what separates an alternate sort order from its base locale.** `German Phone Book` is
> `0x00010407` against German's `0x00000407`; `Hungarian Technical` is `0x0001040E` against Hungarian's
> `0x0000040E`; `Georgian Modern` is `0x00010437`. All share their LANGID with the base locale and differ in
> **nothing else** — verified against Access-authored databases, reading the whole four-byte field raw. A
> reader that takes only the LANGID reads Hungarian Technical as plain Hungarian.
>
> **Verified** against databases authored with each order: a v1 text column has `0x0E = 01` and produces
> index keys unlike the v0 encoder. The version is the byte at `0x0E`, not `0x0D` — `0x0D` is `0` in both
> General orders, so reading it reports every database as v0.
>
> **The collation is stored in two places, and they agree.** The `(LCID, version)` sort order lives
> *both* per column (here: locale at `0x0B`–`0x0C`, version at `0x0E`) *and* database-wide in the
> obfuscated page-0 header (LCID at `0x6E`, version byte at `0x71`; see
> [page-00-database.md §2.1](page-00-database.md#21-the-obfuscated-header-0x180x98)). For a given
> database the per-column `0x0E` equals the page-0 `0x71` (verified). Changing the
> default **language** moves the LCID in both places; flipping **General vs General Legacy** moves the
> version byte in both places.
>
> (In a raw v0/v1 diff the still-obfuscated `0x71` looks like creation-date noise; de-obfuscate the header
> with the fixed mask before reading it.)

> **The format allows a per-column collation; the engine never writes one.** Each descriptor carries its
> own four bytes, so a file *could* hold a column sorting differently from its database — and one can be
> made, by editing the bytes. Nothing Microsoft ships will make it: DAO documents `Field.CollatingOrder` as
> **read-only** (and "not supported" on `Index` and `Relation`), states that its value "corresponds to the
> locale argument of the **CreateDatabase** method … or the **CompactDatabase** method", and says outright
> that *"you can't set a collating order for an individual index — you can only set it for an entire
> table"*. ACE's SQL has no syntax for it and Access's UI offers only the database-wide setting. Hence the
> per-column `0x0E` equalling page-0 `0x71`: not a coincidence, an absence of any way to make it otherwise.
> The room is reserved in the format if a future version wants it.
>
> ACE does honour a **stamped** column: with the database created as General v1 and a target LCID written
> onto one column's descriptor, ACE indexes that column with it. For a version-1 declaration whose LCID has
> no version-1 table, ACE falls back — region-specific LCIDs resolve to plain General v0, neutral ones to
> their own language's v0 order (measured across all known LCIDs). LibRed does **not** implement that
> fallback: it is real behaviour, but on input that no supported tool can produce.
>
> **A query compares text in the DATABASE's collation, never the column's.** The column's collation goes
> into its index keys and nowhere else. Verified against ACE with Croatian v1 against General v1, stamped on
> one side at a time: a column stamped Croatian in a General database sorts, groups, compares against a
> literal either way round, and yields `MIN`/`MAX` in **General** order, and a General column in a database
> stamped Croatian does all of those in **Croatian** order — as do a comparison of two literals and an
> expression over the column (`S & ''`, `UCASE(S)`). So the order every query-time text comparison uses is the
> page-0 one (`0x6E`/`0x70`/`0x71`), whatever the operands are.
>
> **Format-version coupling.** Access sets the file format to the lowest version that supports the features
> used, so choosing General Legacy in the UI *downgrades the file to the 2007 format*, while General (v1)
> forces 2010+. But the format byte is a **ceiling**, not a fingerprint — a 2016/2019 file (bumped by BigInt
> or datetime2) can still be v0 — so the collation must be read from `0x0D`-`0x0E`, never inferred from the
> format version.
>
> **LibRed model.** The triple is a `Collation` value — `Order` (the LANGID, as a `CollatingOrder`),
> `Version` (`0x0E`), `SortId` (`0x0D`) — with `Collation.Lcid` assembling the 32-bit LCID. It is **read**
> per column into `ColumnDef.Collation` (numeric columns, whose `0x0B/0x0C` are precision/scale, carry none)
> and **written** from `JetDatabase.Collation` (the database default), all three bytes explicitly. The write
> is byte-identical for General legacy (verified). Which of these collations LibRed can **encode index keys
> for** is not this section's subject and is answered in [§10.4](page-03-04-index-btree.md): the gate is
> `Collation.IsIndexKeyEncodable`, it is deliberately default-closed, and both sort-order versions plus
> several hundred locales pass it.

Variable-length columns carry a *variable index* — their slot in the row's variable-offset table
([§5](page-01-data-and-rows.md)), stored in the descriptor at `0x07`. For an untouched table it equals the
column's rank among the variable columns ordered by ascending id.

> **Always read `0x07`; never re-derive it by ranking.** The two agree only until a `DROP COLUMN` leaves a
> gap in the index space, and a survivor then keeps an index that ranking would shift down into the hole —
> so deriving decodes the wrong slot (verified: after dropping a middle Text column, the next Text column
> read the dropped column's value). Why the gap is left, and what it means for writing a row rather than
> reading one, are [§3.1](page-02a-tdef.md#31-header) and [§5](page-01-data-and-rows.md) respectively.

#### Declared width limits

Two limits bind a declaration, both enforced by ACE when it **opens the file**, so writing past either
damages the database rather than just the table. Verified against ACE 16 (OLE DB); LibRed applies both in
`Catalog/RowCodec.cs`, on create and on every incremental path.

**Per field: 510 bytes** — 255 Text characters, or 510 bytes of Binary, fixed or variable alike. ACE
refuses a wider column through its own DDL identically on `CREATE TABLE`, `ALTER COLUMN` and `ADD COLUMN`
(*"Size of field is too long"*: `TEXT(255)` and `BINARY(510)` are accepted, `TEXT(256)` and `BINARY(511)`
are not). A file written with a wider column **opens**, but the table cannot be queried — `SELECT` fails
with *"The size of a field is too long"*. Memo and OLE are exempt: their data lives on long-value pages.

**Per declaration: the widest possible record must fit the 4060-byte cap** (§5), which is far tighter than
the TDEF's own 2-byte offset fields:

```
2 + fixedBytes + varOverhead + ceil(columnIdHighWater / 8)  <=  4060
varOverhead = 4 when the table has no variable columns, else (numVar + 1) * 2 + 2
```

Note what is **not** in the sum: a variable column costs only its 2 bytes of offset table, never its
declared width — which is why an all-Text table is unconstrained while a wide fixed region is not. The
4-byte allowance for a table with no variable columns at all is ACE's; LibRed's own encoder omits the
variable section entirely in that case (§5), so its rows come in 4 bytes under what ACE reserves.

Verified on both sides of the boundary; the null-bitmap term moves the boundary byte for byte:

| shape | fixed bytes | widest record | ACE |
| --- | ---: | ---: | --- |
| 8 fixed columns (1 bitmap byte) | 4053 | 4060 | opens |
| 8 fixed columns | 4054 | 4061 | *"Unrecognized database format"* |
| 252 fixed columns (32 bitmap bytes) | 4022 | 4060 | opens |
| 252 fixed columns | 4023 | 4061 | *"Unrecognized database format"* |
| 4018 fixed bytes + 2 variable | 4018 | 4060 | opens |
| 4018 fixed bytes + 3 variable | 4018 | 4062 | *"Unrecognized database format"* |

ACE's own SQL DDL cannot easily reach this: it declares `GUID` columns **variable** (see
[data-types.md](data-types.md)), so a table wide enough to trip the limit takes deliberately wide fixed
columns.


### 3.8 In-place column type/length change (`ALTER COLUMN`) — verified byte-for-byte

Changing a column's **type or length** does **not** edit that column in place. Access **makes a brand-new
column that keeps the old one's ordinal position but takes a fresh id**, copies + converts the data into
it, and **leaves the old column's storage as dead space** (it is *not* compacted away). Verified by
whole-file diff against ACE; LibRed's in-place path reproduces every byte across fixed, variable,
fixed↔variable, PK, indexed, multi-page and decimal shapes, including repeated non-sequential modifies.
This is **the same mechanism for every type/length change** — including a *widening* `TEXT(n)→TEXT(m)`;
there is no cheap "just bump the length" path, ACE burns the id there too.

The same holds for a Memo/OLE source or target (below); the id high-water limit is [§3.1](page-02a-tdef.md#31-header).

> **Relationship columns cannot be altered.** ACE rejects a type or length change when the target is either
> a referencing FK column or its referenced parent column: *"Cannot change field 'X'. It is part of one or
> more relationships."* This is verified for both sides. LibRed performs this check before the edit, so no
> descriptor, row, or index page is changed on rejection.

**TDEF header:** the max-column-id high-water (`0x29`, §3.1) bumps **+1** (this is the burned id). For a
change **to a variable type**, the variable-column count (`0x2B`) also bumps **+1**. Every field burn is
permanent: repeated modifies keep consuming ids from `0x29`, which is why a heavily-altered table can hit
"Too many fields defined" with far fewer than 255 *live* columns (only a compact renumbers).

Example: a 4-column table `A,B,C,D` (ids 0,1,2,3); `ALTER COLUMN B …` → B stays at index 1 with
**id 4**; a later `ALTER COLUMN C …` → C stays at index 2 with **id 5**; every *other* column keeps its id.
So after modifies the ids are non-contiguous while the physical order is unchanged — which is why the row's
null bitmap is keyed by **id** and not by position (§5).

> **An identity ALTER is not always free, and nullability never enters into it.** An identical
> `SHORT`/`LONG` declaration consumes no id, even at 255; an identical Memo/OLE declaration *does* consume
> one and is rejected at 255. ACE accepts the identity ALTER at 255 for a `NOT NULL` column as readily as a
> nullable one, so
> LibRed must not compare nullability when deciding an ALTER is a no-op — and no ALTER path carries it
> anyway, since `Required` is applied separately and the in-place edit keeps the column's own.
>
> Because the edit is in place, everything the ALTER does not touch stays where it was — every other column's
> descriptor and id, and every index not over the column, the primary key included, in its place among the
> logical and index-data blocks (verified). The indexes over the column keep the slots they held between them,
> but not necessarily their own (below).

**Target column descriptor (§3.4)** — the *only* descriptor that changes; all others stay byte-identical:

| Offset | New value |
| --- | --- |
| `0x00` | new data type |
| `0x05` | **burned id** = the old `0x29` high-water (so the id ≥ every existing id; position is unchanged) |
| `0x07` | variable-table index = the **old** variable-column count (the next free var slot) — set for **both** a fixed and a variable retype |
| `0x09` | **left unchanged** — it is the column's ordinal position (§3.4), which a retype does not move; it only looked like the *old* id because the two coincide until a column is dropped or moved |
| `0x0F` | fixed-length bit (`0x01`) set/cleared for the new type; auto-number bit likewise |
| `0x0B`/`0x0C` | precision/scale for a `DECIMAL`/`NUMERIC` (`FixedPoint`) target |
| `0x15` | fixed-data offset = **end of the current fixed region** (appended) for a fixed target, or `0` for a variable target. The old slot is left where it was as dead bytes. |
| `0x17` | new length |

The "end of the current fixed region" must be measured from an **existing row** (its variable-data start),
**not** from the live column descriptors — after a prior retype left a dead fixed slot, the descriptors
under-count the true fixed width. An all-fixed table has no var-data pointer, so its fixed length comes
from the schema (§5).

**Row re-lay** — every row is rewritten (in place, landing at the offset ACE's repack-from-end produces):
the **old fixed region and old variable chunks are kept verbatim** (the dead old-target slot / chunk keeps
its stale bytes), and the converted target is **appended** — a new fixed slot at the offset above, or a new
variable chunk at variable-index = the old var count. The leading count, variable-offset table + `numVar`,
and null bitmap are then rebuilt per §5 (count and bitmap width = max id + 1; each dead id's bit as the old
row had it — so the retyped column's old id is present where it held a value and clear where it was NULL).
A fixed target whose converted value is NULL gets no bytes written: its new slot holds whatever the old record
had at those offsets (the start of its variable data, on a row with some), not zeros.

**A Memo/OLE source or target** takes the same edit and re-lay, plus the long-value side of ADD and DROP
COLUMN (verified, both directions and a Memo re-declared as Memo). A column becoming Memo/OLE gets its §3.3.2
entry, and its owned and free map records appended to the table's usage-map page; each converted value is then
stored as an insert stores it — inline (a memo compressed) up to 64 bytes, else on an LVAL page — and the row
carries its descriptor as the appended chunk. Those two map records go onto the usage-map page ahead of the
records an index rebuild appends there, when the column is indexed. A column ceasing to be one has its entry removed and its map
records retired as a dropped column's are; its LVAL pages go back to the global free map with their bytes
untouched, and each row keeps the old descriptor as its dead chunk.

**Indexed target — full index rebuild.** When the modified column is in an index, ACE reconstructs that
index (its keys change type). Verified:

- Allocate a **fresh empty root leaf** (an appended page); the old root is freed **last** (so the new root
  gets the appended page, not the recycled old one).
- **Re-point the index-data block** (§3.5) in the TDEF: the target's **burned id** replaces the old id in
  its column slot (`0x04` array), the **new root** at `0x26`, and the **new usage-map row** at `0x22`. The
  index's **stats block** (§3.3.1) is set from the rebuilt index: total = its entries, unique = its distinct
  keys among the rows present.
- **Recycle the owned-pages usage-map row** the way ACE does — the append/move/tombstone dance, and the
  stale bytes it deliberately leaves behind, are [page-05 §9](page-05-usage-maps.md).
- **Back-fill** the new B-tree with new-type keys (one `AddEntry` per row).
- **Several indexes over the column are rebuilt in logical-block (name) order** — the primary key like any
  other — each taking its fresh root in that order, and they are handed back the real-index slots they held
  between them in that order: the lowest slot to the first rebuilt, and so on, stats and data block together,
  every logical block's data ordinal (`+0x08`) following its index. The logical blocks over those indexes hand
  their numbers (`+0x04`, a numbering of the logical blocks, distinct from the data ordinal once two share a real
  index) round the same way: pooled, sorted, and given back in logical-block order. An index not over the column
  keeps its slot and its number even when it sits between them. With `PK_I`, `IX_Z(B)`, `IX_E(E)`, `IX_A(B, ID)`
  in slots 0–3, a retype of `B` leaves `PK_I`, `IX_A`, `IX_E`, `IX_Z`, and a retype of `ID` leaves `IX_A`,
  `IX_Z`, `IX_E`, `PK_I`. With `IX_Y(B)` sharing `IX_Z`'s real index as well, numbered `IX_Z` 1, `IX_Y` 2,
  `IX_E` 3, `IX_A` 4, the retype of `B` numbers them `IX_A` 1, `IX_Y` 2, `IX_E` 3, `IX_Z` 4. With one index over
  the column nothing moves.

The descriptor edit and the index-block re-point are applied to **one** parsed TDEF and written **once**.

**Unmatchable environmental diffs** (not faithfulness gaps): the **page-0 modification counter** and
**MSysObjects.DateUpdate** (the table's last-modified wall-clock timestamp) — no writer can match a clock.

