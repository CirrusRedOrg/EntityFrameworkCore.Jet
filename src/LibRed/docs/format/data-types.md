# Data types and text encoding

> Part of the [LibRed Jet / ACE file-format reference](README.md). Cross-references use the original **§-numbers**; the [section map](README.md#section-map) says which file each lives in.

## 6. Data types

| Code | Type | Storage / decode |
| --- | --- | --- |
| `0x01` | Boolean | A bit in the null bitmap (no data) |
| `0x02` | Byte | 1 byte |
| `0x03` | Int16 | 2 bytes LE |
| `0x04` | Int32 | 4 bytes LE |
| `0x05` | Currency | int64 LE, scaled: value / 10000 |
| `0x06` | Single | 4-byte IEEE |
| `0x07` | Double | 8-byte IEEE |
| `0x08` | DateTime | 8-byte IEEE double, OLE-automation epoch (1899-12-30); nothing before 0100-01-01 (serial -657434) — ACE refuses an earlier date or serial |
| `0x09` | Binary | raw bytes |
| `0x0A` | Text | UTF-16LE, or compressed Unicode (§7); inline ≤ 255 chars |
| `0x0B` | OLE | long value (§8) |
| `0x0C` | Memo | long value (§8); text once resolved |
| `0x0F` | GUID | 16 raw bytes. Stored as a *variable*-length column when declared through SQL (see below) |
| `0x10` | FixedPoint (Numeric/Decimal) | 17 bytes: sign byte (`0x80` = negative) + 128-bit magnitude (four 32-bit little-endian words, low word last); value = magnitude / 10^scale. Precision/scale from the column descriptor (§3.4) |
| `0x11` | BigBinary — **BIGBINARY(n)** | raw bytes, inline, up to 4000 (see below) |
| `0x12` | Complex (multi-value / attachment) | descriptor parsed; contents not materialized (out of scope for SQL/EF) |
| `0x13` | Int64 — **BIGINT** (ACE 16 / Access 2016) | 8-byte little-endian signed integer. Stored as a *variable*-length column (see below) |
| `0x14` | DateTimeExtended — **DATETIME2** (ACE 17 / Access 2019+) | fixed 42-byte ASCII `<day>:<time>:<precision>` (see below) |

> **Unmodelled codes are read, not refused.** `0x0D` and `0x0E` are held open in `JetDataType` as
> `Unknown*` placeholders: a TDEF carrying one still parses, the column decodes to its **raw bytes**, and only
> *writing* such a column is refused. This is not tidiness — the catalog reads **every** table's definition,
> so a reader that refuses one unrecognised column cannot open the database at all. Neither has ever been
> observed.

**`0x11` is BigBinary — a binary value bounded by a single data page.** It holds up to 4000 bytes, which sits
just inside the 4060-byte record cap ([page-01 §5](page-01-data-and-rows.md)), and the value is always stored
**inline** in the row, never as a long value. So it is the largest binary a row can carry on one page without
the long-value machinery — and a full-size one leaves the row's other columns almost nothing.

ACE's DDL declares it as `BIGBINARY(n)`, and a bare `BIGBINARY` takes the maximum, 4000; `BIGBINARY(4001)` is
refused with "Size of field is too long", as `VARBINARY(511)` is. The descriptor is byte-for-byte the one ACE
writes for the same `VARBINARY(n)` except for the type byte — variable, same flags, length in bytes — and DDL
has no fixed-length form. It needs no format raise: ACE creates one in an ACE 12 file without touching the
version byte, and Jet 4 `.mdb` files carry it.

The value lives in the row's variable section exactly as a `VARBINARY` value does, at every size, so it counts
against the record cap: two BigBinary columns take 2000 + 2000 bytes in one row, and 4000 + 4000 is refused
with "Record is too large." — where two `LONGBINARY` columns take 4000 + 4000, since only their 12-byte
descriptors are in the row. ACE reports the column as binary in all its metadata — OLE DB `DATA_TYPE` 128 with
the declared length and no fixed or long flag, ADOX `adVarBinary` with its `DefinedSize`, DAO `Type` 17 — and
names it `BigBinary` only in its DataTypes list, where it is the binary type above 510 bytes.

**In a query it is an OLE Object, not a binary.** Stored inline or not, ACE puts it under every restriction a
long value has, with the same messages it gives `LONGBINARY` (verified, statement for statement, against both):

| Use | BigBinary and OLE alike |
| --- | --- |
| `=`, `<`, column-to-column comparison, `LIKE` in `WHERE` | accepted |
| `LEN`, `LENB`, `MID`, `&` | accepted |
| `ORDER BY` | "Cannot sort on Memo, OLE, or Hyperlink Object" |
| `GROUP BY` | "Cannot group on Memo, OLE, or Hyperlink Object" |
| `DISTINCT` | "Cannot include Memo, OLE, or Hyperlink Object when you select unique values" |
| `MAX` (an aggregate argument) | "Cannot have Memo, OLE, or Hyperlink Object fields in aggregate argument" |
| `JOIN … ON` it | "Cannot join on Memo, OLE, or Hyperlink Object" |
| selected by a `UNION` | "Cannot use Memo, OLE, or Hyperlink Object field … in the SELECT clause of a union query" |
| `IN (SELECT` it `…)` | "Invalid Memo, OLE, or Hyperlink Object in subquery" |

**It cannot be indexed** either. ACE refuses it on every route an OLE column is refused on, with the same
message — `CREATE INDEX`, `PRIMARY KEY`, `UNIQUE`, a foreign key, and `ALTER COLUMN` of an indexed column to
`BIGBINARY` — see [§10.4](page-03-04-index-btree.md).

So against `LONGBINARY` it gives up size (4000 bytes against about 1 GB) and row budget, and gains nothing a
query can use; what it saves is the long-value page a `LONGBINARY` value past the inline size needs.

> **Access's legacy object store is a BigBinary column.** `MSysAccessObjects.Data` is `0x11`, *fixed*, 3992
> bytes, holding chunks of an **OLE Compound File** (signature `D0 CF 11 E0 A1 B1 1A E1`) — Access's own
> object storage, the VBA project and its type-library references (one chunk reads
> `ado\msado21.tlb#Microsoft…`). A single stream sliced across rows; LibRed hands the bytes back and does not
> interpret the container. ACE still reports that fixed column as variable, the same collapse it makes for
> `BINARY(n)`.
>
> Access later replaced `MSysAccessObjects` with `MSysAccessStorage`, which uses no BigBinary column, and a
> file has one or the other. The discriminator is **not** the format version — Jet 4 `.mdb` files with page-0
> version byte `0x01` split either way. It is the generation Access chose **when it created the database**,
> recorded as the `AccessVersion` property on the `MSysDb` object: `08.50` (Access 2000) uses the legacy
> store, `09.50` (Access 2002+) does not. A file created by anything else (DAO, LibRed) gets
> `MSysAccessStorage` when Access first opens it.

LibRed's scalar reader requires the exact fixed widths listed above before invoking the numeric,
GUID, date, or decimal codec. Text, Binary, Memo/OLE descriptors, and Complex values remain
variable-length. A width mismatch is treated as row corruption (`InvalidDataException`) rather
than being allowed to fail incidentally inside a primitive decoder.

**A `NUMERIC`/`DECIMAL` column's declared precision is a contract the payload cannot express, and ACE enforces
it on write.** The stored form is the same 17 bytes — a sign byte plus a 128-bit magnitude — whatever the column
declares, so a 20-digit value occupies a `DECIMAL(18,4)` exactly as comfortably as a 2-digit one and nothing on
disk records that it is out of contract. ACE therefore checks the value against the declaration instead, and
refuses what will not fit with *"The decimal field's precision is too small to accept the numeric you attempted
to add."* The permitted magnitude follows the standard rule: `p` total digits with `s` after the point, so at
most `p − s` before it, and `DECIMAL(18,4)` accepts up to `99999999999999.9999` and refuses `10^14`.

The enforcement covers **every path that can put a value in the column** — `INSERT`, `UPDATE`,
`INSERT … SELECT`, and an `ALTER COLUMN` that *narrows* or retypes the declaration over rows already stored. That last one
is what makes it an invariant over the whole column rather than a filter on one statement: ACE will not shrink
a declaration to something its existing data would violate.

> Scale is treated differently and is **not** refused: excess decimals are coerced, and **ACE truncates toward
> zero**. `1.23456` into a `DECIMAL(18,4)` stores `1.2345`, `1.99999` stores `1.9999`, `-1.23455` stores
> `-1.2345` — truncation in every case, over both midpoint parities and both signs.
>
> LibRed matches it in `JetTypeCodec.EncodeNumeric`, and `IndexKeyEncoder.EncodeFixedPoint` **must** quantise
> identically: the key is the same unscaled integer the row stores, so quantising one differently files a value
> under a number its row does not contain. Rounding (`decimal.Round(…, 0)`, `ToEven`) is the obvious
> implementation and is wrong: it stores `1.2346` and `2.0000` for ACE's `1.2345` and `1.9999`, and a row and
> index that round consistently with each other give no internal sign of it.

**`BIGINT` is variable-length despite being a fixed 8 bytes.** ACE puts it behind the row's variable offset
table rather than in the fixed region — a descriptor carrying length 8 with the fixed flag clear (verified: a
column ACE creates has `length=8 fixed=False`, and its row holds the value at a variable-column start
offset). Its *index* key is unaffected — key encoding dispatches on the column's type, not on where
the row keeps the bytes; the encoding itself is [§10.4](page-03-04-index-btree.md).

> **The reason to match is faithfulness, not readability — ACE honours the descriptor's fixed flag.** It has
> to: its own `MSysComplexType_GUID` declares `Value` *fixed* while every GUID column its DDL creates is
> *variable*, so one engine reads both layouts routinely. Verified: a **fixed** BIGINT, Currency and
> DateTime, and a **variable** Int32, Double, Currency and DateTime, all read back correctly through ACE. A
> fixed BIGINT is therefore *not* misread — the layout ACE writes is not the only one it looks for.

**`GUID` is variable-length too — but only where ACE's DDL made it.** Every GUID column ACE's SQL creates
carries length 16 with the fixed flag clear, whatever the table's width (it is not a fallback for wide
tables), and `SELECT … INTO` produces the same. ACE's **own system tables are the exception**:
`MSysComplexType_GUID` `Value` is *fixed* (verified), and `DatabaseCreator` reproduces that — so "GUID is
variable" is a rule about declarations, not about GUID storage everywhere.

Unlike BIGINT this is not a wrong-value hazard: ACE reads a value back correctly from either layout
(verified). What it costs is record budget — 16 *fixed* bytes per column that ACE does not spend, enough to
push a 250-column GUID table that ACE creates without complaint past the declared-record limit in §3.4.
`AccessTypeMapper` and `StatementExecutor.ColumnSpecFor` both declare it variable.

> **Writing one through ACE's OLE DB provider: not `DBTYPE_I8`.** An `OleDbType.BigInt` (20) parameter carries
> **no** value into a Large Number column — every value fails with "data value could not be converted", zero
> included — so the one type named for the job is the only one that cannot do it. Use **`Numeric` (131)**;
> `Decimal` (14) and `Variant` (12) also round-trip the full range exactly. `VarNumeric` (139) is rejected
> outright ("Type name is invalid"), and `Double` (5) is the trap: it succeeds quietly for small values and
> overflows near ±2⁶³. EFCore.Jet's `JetLongTypeMapping.ConfigureParameter` forces OLE DB 131 / ODBC 7 for its
> own `long` parameters for the same defect, though that mapping targets a `decimal(20,0)` column rather than a
> real `0x13` one.

**New-type format versions — the two are NOT the same version**, which is the natural assumption and is
wrong. **`BIGINT` (Large Number)** requires the **ACE 16 / Access 2016** format (`0x05`); **`DATETIME2`
(Date/Time Extended)** requires the **ACE 17 / Access 2019+** format (`0x06`) — it arrived later (Access for
Microsoft 365). LibRed gates each accordingly (`AccessTypeMapper`). Both thresholds are verified — see
[page-00 §2](page-00-database.md), which owns the version byte. `DATETIME2` is a fixed 42-byte ASCII string
of three colon-separated fields: the .NET **day number**, the count of **100-ns ticks within the
day**, and the fractional **precision** (e.g. `7`). The first two are zero-padded to 19 digits so
that byte order equals chronological order (an order-preserving inline encoding). The value is
`new DateTime(day * TicksPerDay + time)`; e.g. `…693593:…0:7` is the 1899-12-30 epoch and
`…737590:…495300000000:7` is 2020-06-15 13:45:30. Sub-second precision (to 100 ns) is preserved.

Those three fields occupy **41** characters (19 + 1 + 19 + 1 + 1); the 42nd byte is a **NUL (`0x00`), not a
space** — verified against rows ACE wrote (`… 3A 37 00`). The distinction is not cosmetic: the whole
42 bytes go into the index key verbatim, so a space there would put every key out of step with ACE's and make
its seeks miss those rows.

**Indexing.** ACE does permit an index on the type, and keys the 42 bytes verbatim through its `Binary`
chunking rather than folding the value to a number — which it can do precisely because the stored form
above is already order-preserving. The encoding itself is
[§10.4](page-03-04-index-btree.md).

The bytes on disk are correct, but **ACE's own OLE DB provider cannot read this type back** — see the
[footnote](#footnote--reading-datetime2-through-aces-own-drivers) at the bottom of this page.


## 7. Compressed Unicode

A text value that begins with the 2-byte marker `FF FE` is **compressed**: the following bytes
are one per character (ASCII range), not UTF-16. Otherwise the value is UTF-16LE. Applies to
both `Text` and resolved `Memo`.

> **A leading U+FEFF is indistinguishable from the marker, and the format does not resolve it.** U+FEFF in
> UTF-16LE *is* `FF FE`, so a value whose first character is one cannot be told from a compressed value by its
> bytes. Such a value is stored as plain UTF-16 — the collision is made, not avoided — and reads back wrongly:
> `U+FEFF`+`abc` as `abc`, a lone `U+FEFF` as the empty string, two of them as `U+00FF U+00FE`. That is what
> Access itself returns for its own value (verified), so the character is not recoverable at all. Only the
> first position is ambiguous: `a`+U+FEFF+`b` round-trips intact.

The descriptor's `0x10` extended flag `0x01` records the column as compression-*capable*. ACE sets it only
when the column is declared `WITH COMPRESSION` (or `WITH COMP`); a plain `TEXT`/`MEMO` column created through
SQL DDL leaves it **clear**.

> **The flag does not gate everything, and for a long value it gates the *page* case only.** A plain
> `LONGTEXT` column — capable flag clear — still stores an **inline** value compressed. What the declaration
> buys is compression of a value that landed on a single LVAL page. Verified against ACE, ASCII throughout:
>
> | column | characters | form | on-disk length |
> | --- | ---: | --- | ---: |
> | `LONGTEXT` | 30 | inline | 32 — compressed |
> | `LONGTEXT` | 32 | inline | 34 — compressed |
> | `LONGTEXT` | 33 | page | 66 — **not** compressed |
> | `LONGTEXT` | 40 | page | 80 — not compressed |
> | `LONGTEXT WITH COMPRESSION` | 40 | page | 42 — compressed |
> | `LONGTEXT WITH COMPRESSION` | 100 | page | 102 — compressed |
>
> The 32/33 boundary also shows the form being chosen on the **uncompressed** length: 33 characters are 66
> bytes and go to a page, though they would compress to 35 and fit inline. An ordinary `Text` column does
> honour the flag, so this is specific to long values. A writer that requires the flag everywhere stores
> every short ASCII memo at twice ACE's size; LibRed follows ACE in `JetTypeCodec`.

**When a capable column actually compresses a value** (verified against ACE, and reproduced by LibRed
byte-for-byte):

- **A run of characters that fit one byte** (`<= 0xFF`, so Latin1, not just ASCII) is stored one byte per
  character. Runs are **split**, not all-or-nothing: a non-Latin1 character switches the value into 2-byte
  mode rather than forfeiting compression for the whole of it — see the mixed-form note below. The exception
  is a 2-byte character with a `0x00` low byte (`一` = `00 4E`), which is indistinguishable from the mode
  switch and does forfeit the whole value.
- **It must save space.** The marker costs 2 bytes, so 1- and 2-character values stay UTF-16 (2 + N < 2N only
  from N = 3).
- **A chained long value is never compressed.** Compression is decided *after* the storage form, and the
  form — inline, single page or chained — is chosen on the **uncompressed** UTF-16 length. So an inline Memo
  compresses (whatever the capable flag says), a single-page one compresses only on a `WITH COMPRESSION`
  column, and a chained one never does; the compressed size never approaches any limit.
  Microsoft's "only instances that, when compressed, will fit within 4096 bytes" describes the wrong
  quantity; see [long-values.md](long-values.md) for the measured boundary. The uncompressed length also picks
  the *page* for a compressed single-page value, and its uncompressed bytes are left in that page's free space
  ([long-values.md](long-values.md#writing-long-values)).

> **The mixed form is real, and ACE writes it readily.** A value can toggle between 1-byte and 2-byte runs
> mid-string: after the `FF FE` marker the value starts in 1-byte mode and every `0x00` byte at a character
> boundary switches mode. `café中` is stored as `FF FE 63 61 66 E9 00 2D 4E` — "café", a switch, then 中.
>
> ACE emits it under two conditions:
>
> - **It must strictly save space.** `abc中` is 8 bytes either way, so it stays UTF-16; `café中` saves one
>   byte and is mixed.
> - **No character in a 2-byte run may have a `0x00` low byte**, since that is indistinguishable from the
>   switch. U+4E00 is `00 4E` and U+0100 is `00 01`, so `aaaaa一` and `aaaaaĀ` are stored as plain UTF-16
>   while `aaaaa中` is mixed. One such character forfeits the form for the whole value.
>
> It is **not** limited to inline values: 1,000 ASCII characters plus one `中` on an LVAL page store as
> 1,005 bytes (`FF FE` + 1,000 + switch + 2) where the same shape with `一` stores 2,002 (plain UTF-16, the
> saving thrown away). So "one incompressible character forfeits the whole value" holds only for the
> *ambiguous* ones. The character's position is irrelevant in both cases; ACE 12.0 and 16.0 agree byte for
> byte.
>
> The two conditions above are what make toggling on `0x00` unambiguous for a reader. **A reader that decodes
> the whole payload as a single Latin1 run** silently returns `café\0-N` for a value Access wrote — wrong data, no
> error, and reachable by ordinary mixed-script text since Access's UI defaults Unicode Compression to Yes.
> `JetTypeCodec.DecodeText` honours the switches, and `EncodeCompressed` emits them.
>
> **The two paths break an exact tie differently.** A long value takes the compressed form only when it is
> strictly smaller; an ordinary `Text` column takes it when it is no larger. `ab中cd` is 10 bytes either way
> and comes back UTF-16 from a Memo but mixed from a `WITH COMPRESSION` Text column. Under three characters
> nothing is compressed on either path, whatever the arithmetic says, which is what settles the all-Latin1
> ties (`ab` is 4 bytes either way and stays UTF-16).


---

## SQL type-name aliases (CREATE TABLE)

The on-disk **type codes** above are what a column descriptor stores; Access SQL accepts many *keyword*
spellings that all map onto them. LibRed's `AccessTypeMapper` implements the mapping; the canonical list is
[MS Learn — Equivalent ANSI SQL data types](https://learn.microsoft.com/office/client-developer/access/desktop-database-reference/equivalent-ansi-sql-data-types).
Points verified against ACE that aren't obvious from that page:

- **Boolean** aliases: `bit` / `logical` / `logical1` / `yesno` / `boolean` (ACE's own DDL rejects
  `BOOLEAN`; LibRed accepts it → still an ACE-readable Boolean).
- **Width-suffixed integers**: `integer1` = Byte, `integer2` = Int16, `integer4` = Int32 (Long).
- **`SMALLDATETIME` → DateTime** and **`SMALLMONEY` → Currency** — ACE folds these SQL-Server names onto its
  single 8-byte date / currency type (no narrower storage, no version-byte upgrade).
- **Size-less `char`/`varchar` default to 255**, size-less `binary`/`varbinary` to **510** (ACE's schema
  `CHARACTER_MAXIMUM_LENGTH`), **not** 1.
- **Bare `TEXT` → Memo** (long text); `TEXT(n)` → `varchar(n)` (a Jet quirk, ACE-verified).
- Sized Text/Binary dimensions must be positive: Text is `1..255` characters and Binary is `1..510` bytes.
- **A declaration wider than that is refused at DDL time — it is not promoted to Memo and not clamped.**
  Verified: `VARCHAR(255)` creates a 255-character column, `VARCHAR(256)` and everything above it
  (`VARCHAR(1000)`, `TEXT(1000)`, `CHAR(1000)`, `VARCHAR(65535)`) fail the whole `CREATE TABLE` with
  ACE's *"Size of field 'c' is too long."* LibRed refuses the same declarations at the same threshold and
  opens its message with ACE's wording, so a caller matching on it behaves the same against either engine.
- **`CHAR(n)` / `BINARY(n)` are FIXED-length columns; `TEXT(n)` / `VARBINARY(n)` are variable** — ACE's own DDL
  produces both forms, so the fixed form is not a LibRed-only construct.
- **An over-long value is refused on both forms, with one message**: *"The field is too small to accept the
  amount of data you attempted to add."* The fixed form is the one worth recording: because ACE stores fixed
  text space-padded to the full width, a writer that pads is one line away from silently *truncating* the
  over-long case instead of refusing it. The width check therefore belongs on the encode path for fixed
  columns (`JetTypeCodec.EnsureFitsFixedWidth`, before padding) and on the shared row-assembly path for variable ones (`RowEncoder.AssembleRow`, so the ALTER re-lay passes it too).
- **Narrowing an existing column is checked against its rows.** `ALTER TABLE … ALTER COLUMN c TEXT(5)` on a
  column holding wider values is refused rather than leaving rows that violate the declaration — and so is a
  retype whose converted values are too wide: a Memo or a `LONG` to `TEXT(1)`, a `LONG` to `BINARY(1)`. ACE
  refuses every one with *"The field is too small to accept the amount of data you attempted to add."*
- `DECIMAL(p,s)` / `NUMERIC(p,s)` use precision `1..28` and scale `0..p`, and these are **ACE's own bounds,
  refused at DDL with two distinct messages**: *"Invalid precision for decimal data type."* for `(0)`, `(0,0)`
  and `(29)`, *"Invalid scale for decimal data type."* for `(5,7)`. `(1,0)` and `(28,28)` are both accepted, so
  scale may equal precision — and `(28,28)` is usable rather than merely declarable: with no digits left in
  front of the point it holds values below 1, keeping all 28 decimals, and both engines refuse `1`.
  **Size-less `DECIMAL` and `NUMERIC` default to precision 18, scale 0** — the same kind of default as the
  255/510 above — while an explicit `(p)`/`(p,s)` is stamped exactly as written; the column is 17 bytes and
  fixed-length either way. **A precision of 0 cannot be declared at all**, which fits ACE's own OLE DB reader
  being unable to materialise such a column — a 0 leaves the value no declared shape to be read back into.
  LibRed rejects out-of-range dimensions in `AccessTypeMapper` and, because a direct Core caller bypasses that, in `TdefBuilder` too; an unspecified
  precision resolves to 18 on write rather than reaching the file as 0.
- The grammar parses **two-word** type names (`CHARACTER VARYING`, `BIT VARYING`); three-word
  (`NATIONAL CHARACTER VARYING`) is not parsed yet. `HYPERLINK`/`XML`/`SQL_VARIANT`/`VARIANT`/`COMP` have no
  mapping (rejected, as ACE also rejects them).


---

## Footnote — reading `DATETIME2` through ACE's own drivers

*Driver behaviour, not file format. Recorded here because ACE's own drivers cannot be used to cross-check
this one type the way every other type can, and because the OLE DB provider silently corrupts it.*

**The bytes on disk are correct; ACE's OLE DB read path is broken in three independent ways.** Verified
against **Access / Microsoft 365 version 2608 (build 20326.20100 Click-to-Run, Current Channel, x64)** — a
shipping build, not an old redistributable. Values inserted through ACE, read back three ways — ACE OLE DB,
ACE ODBC, and LibRed reading the file directly:

| literal | OLE DB (`Microsoft.ACE.OLEDB.16.0`) | ODBC (`ACEODBC.DLL`) | LibRed |
| --- | --- | --- | --- |
| `#2021-01-15 05:06:07#` | `ArgumentOutOfRangeException` | `byte[42]` `"…737804:…183670000000:7 "` | 2021-01-15 05:06:07 |
| `#2021-03-04 05:06:07#` | 2021-**02**-04 05:06:07 | `byte[42]` `"…737852:…183670000000:7 "` | 2021-03-04 05:06:07 |
| `#2021-12-25 13:14:15#` | 2021-**11**-25 13:14:15 | `byte[42]` `"…738148:…476550000000:7 "` | 2021-12-25 13:14:15 |
| `#2020-02-29 00:00:00#` | 2020-**01**-29 00:00:00 | `byte[42]` `"…737483:…0:7 "` | 2020-02-29 00:00:00 |

**ODBC** does not convert at all — it hands back the raw 42 bytes, exactly as stored, so it is *uncorrupted* but
must be decoded by the caller (the same parse LibRed does). Note `OdbcConnection.ServerVersion` reports the
**file's** engine level, not the driver's: `12.00.0000` for an ACE 12 file, `16.00.12600` for a `0x06` one.

> **Neither driver's name tells you its age.** The `Microsoft.ACE.OLEDB.16.0` ProgID and `ACEODBC.DLL` have been
> stable since Office 2016; the binaries behind them ship with Office and follow its update channel. A `16.0` in
> the connection string is *not* evidence of an out-of-date component.

### What the provider actually does

Observed through the OLE DB COM interfaces called directly (`IDataInitialize` → `IDBInitialize` →
`IDBCreateSession` → `ICommandText` → `IColumnsInfo`/`IAccessor`/`IRowset`, natively allocated buffers).
**No ADO, no `System.Data.OleDb`, no ODBC in the path**, so everything below is the provider's own behaviour.

The column's `DBCOLUMNINFO`:

```
wType        = 135 (0x0087) = DBTYPE_DBTIMESTAMP     <- a 16-byte struct
ulColumnSize = 42                                    <- the on-disk ASCII length
bPrecision   = 255, bScale = 255
```

**That contradiction is the root defect** — the provider declares a 16-byte type for a 42-byte value. (Control:
a plain `DATETIME` column in the same table reports `wType = 7 DBTYPE_DATE, ulColumnSize = 8`.)

**1 — It overruns the consumer's buffer.** Given a `DBTIMESTAMP` binding with `cbMaxLen = 16`, in a buffer
pre-filled with `0xCD` sentinels:

```
+2048  E5 07 02 00 04 00 05 00 06 00 07 00 00 00 00 00   <- the 16 bytes it was allowed
+2064  38 35 32 3A 30 30 30 30 30 30 30 31 38 33 36 37   "852:000000018367"
+2080  30 30 30 30 30 30 30 3A 37 00 CD CD CD CD CD CD   "0000000:7" NUL
```

**26 bytes past the slot**, and they are exactly characters 16–40 of the 42-byte on-disk string. The provider
copies the whole 42-byte ASCII value to `obValue`, NUL-terminates at offset 41, overwrites the first 16 bytes
with the converted struct, then reports `cbLength = 16, DBSTATUS_S_OK`. It never consults `cbMaxLen` — a
512-byte slot produces the identical 42-byte footprint. Bindings for narrower types fare worse still: `DBDATE`
(6 bytes) and `DBTYPE_R8` (8 bytes) each get a 16-byte struct splatted at `obValue` regardless, then return
`E_DATAOVERFLOW` — or, for `R8`, the flatly wrong `DBSTATUS_S_ISNULL`.

This is a genuine consumer buffer overrun, and it explains `0xC0000374` / `0xC0000409` process crashes under
OLE DB reader churn: `System.Data.OleDb` places `obValue` at 16 in a 32-byte row buffer, so ACE writes 26
bytes off the end of a managed allocation.

**2 — Its `DBTIMESTAMP` conversion is one month short.** The 16 bytes it wrote for `2021-03-04 05:06:07`:

```
E5 07 | 02 00 | 04 00 | 05 00 | 06 00 | 07 00 | 00 00 00 00
year    month   day     hour    minute  second  fraction
2021      2       4       5       6       7        0
```

The month field literally holds `2`; every other field is right. It looks like a 0-based `tm_mon` copied into
the 1-based `DBTIMESTAMP.month` without the `+1`. It reproduces with a 512-byte slot, so it is independent of
the overrun, and the provider demonstrably knows better: `SELECT Month(E) FROM X` through the same raw rowset
returns **3**, and a plain `DATETIME` column holding the same instant decodes as **3**. `System.Data.OleDb` is
faithful here — it builds a `DateTime` straight from the struct (`ColumnBinding.Value_DBTIMESTAMP`), so January
throws (month `0` is not representable) while every other month corrupts **silently**: 2020-02-29 becomes a
perfectly valid 2020-01-29.

**3 — The string conversions are garbage.** `DBTYPE_STR` and `DBTYPE_WSTR` both return
`"12336-12336-12336 12336:12336:12336.926103344"`. `12336 = 0x3030 = "00"` — the string path reinterprets the
42-byte ASCII payload *as* a `DBTIMESTAMP` struct and formats the result. `DBTYPE_BYTES` and `DBTYPE_VARIANT`
are refused outright at `CreateAccessor` (`DB_E_ERRORSOCCURRED`, `DBBINDSTATUS_UNSUPPORTEDCONVERSION`), so
there is no binding that returns the raw value either.

**There is no binding through which the OLE DB provider returns this column correctly.** Reading the file
directly — what LibRed does — is not merely an alternative; it is the only correct path.

### Why this has gone unnoticed

Server-side comparison is unaffected — a `WHERE dt2 = #…#` matches correctly, because only *materialisation*
goes through the broken conversion. It is live in the ordinary `System.Data.OleDb` stack, not only in a raw
COM consumer: a materialised value from any month but January shifts back a month to a valid date, and only a
January value throws the `ArgumentOutOfRangeException` above. So: predicates are right, corruption is silent
outside January, and Access itself never reads through OLE DB.

## Footnote — what the drivers know about `BIGINT` and `DATETIME2`

*Driver behaviour, not file format. Both types postdate the drivers, and each layer was left at a different
moment, so what a caller is told depends entirely on which one it asks.* Measured against ACE 16 on a file the
engine raised to `0x06` when the columns were created:

| | schema metadata | a value in a query |
| --- | --- | --- |
| **`BIGINT`**, OLE DB | `DATA_TYPE` **20** (`DBTYPE_I8`), but `COLUMN_FLAGS` **106** — variable length, no precision | correct: `Int64`, `DBTYPE_I8` |
| **`BIGINT`**, ODBC | `DATA_TYPE` **-8** (`SQL_WCHAR`), `TYPE_NAME` **CHAR**, size **4** | the 8 stored bytes as 4 UTF-16 characters — `9223372036854775807` reads back as `"￿￿￿翿"` |
| **`DATETIME2`**, OLE DB | `DATA_TYPE` **135**, `COLUMN_FLAGS` **106**, no precision | corrupt (above) |
| **`DATETIME2`**, ODBC | `DATA_TYPE` **-3** (`SQL_VARBINARY`), size **42** | the raw 42 bytes, uncorrupted |
| **both**, DAO | — | correct: `BIGINT` as `Int64` (field type **16**), `DATETIME2` as a full-precision string (field type **26**) |

Two rules fall out. **ODBC describes what it does not know by its stored width**, through whichever generic
type fits — 8 bytes as 4 wide characters, 42 bytes as binary — and hands back exactly those bytes; it never
converts, so nothing is corrupted but nothing is decoded either.

**Only materialisation is broken, and only in the driver.** Through ODBC, an `INSERT` or `UPDATE` of either
type stores the right value (read back from the file directly to confirm), `WHERE Big = 4294967297` and
`WHERE Stamp = #…#` both match, and anything the engine computes comes back correctly — `CStr(Big)` returns
`"4294967297"`, `Year(Stamp)` returns `2021`. It is only selecting the column itself, `MAX(Big)` included, that
yields the undecoded bytes. So a caller stuck on ODBC can use either type by never selecting it bare; a tool
that issues `SELECT *` gets nonsense with no error raised. **OLE DB knows both type codes** and reads a
`BIGINT` correctly, yet describes both columns with the generic "variable length, no precision" it gives a type
it has no entry for. Its `DataTypes` list never learned either type: it still reports the same 15 as it did for
Jet 4, so neither `BigInt` nor `DateTime2` appears in the types the provider claims to support.

**DAO is the exception on both counts**: it has type codes for the two types and returns their values
correctly, and it is the only surface that knows a column is calculated — `Field.Properties("Expression")`
hands back the expression, which OLE DB, ODBC and ADOX all withhold while reporting the expression's result
type as if it were a stored one. The engine and DAO moved together; the interop layers did not, which is
consistent with Access never reading its own files through them.
