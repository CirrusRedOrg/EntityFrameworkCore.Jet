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
| `0x08` | DateTime | 8-byte IEEE double, OLE-automation epoch (1899-12-30) |
| `0x09` | Binary | raw bytes |
| `0x0A` | Text | UTF-16LE, or compressed Unicode (§7); inline ≤ 255 chars |
| `0x0B` | OLE | long value (§8) |
| `0x0C` | Memo | long value (§8); text once resolved |
| `0x0F` | GUID | 16 raw bytes. Stored as a *variable*-length column when declared through SQL (see below) |
| `0x10` | FixedPoint (Numeric/Decimal) | 17 bytes: sign byte (`0x80` = negative) + 128-bit magnitude (four 32-bit little-endian words, low word last); value = magnitude / 10^scale. Precision/scale from the column descriptor (§3.4) |
| `0x12` | Complex (multi-value / attachment) | descriptor parsed; contents not materialized (out of scope for SQL/EF) |
| `0x13` | Int64 — **BIGINT** (ACE 16 / Access 2016) | 8-byte little-endian signed integer. Stored as a *variable*-length column (see below) |
| `0x14` | DateTimeExtended — **DATETIME2** (ACE 17 / Access 2019+) | fixed 42-byte ASCII `<day>:<time>:<precision>` (see below) |

LibRed's scalar reader requires the exact fixed widths listed above before invoking the numeric,
GUID, date, or decimal codec. Text, Binary, Memo/OLE descriptors, and Complex values remain
variable-length. A width mismatch is treated as row corruption (`InvalidDataException`) rather
than being allowed to fail incidentally inside a primitive decoder.

**`BIGINT` is variable-length despite being a fixed 8 bytes.** ACE puts it behind the row's variable offset
table rather than in the fixed region — a descriptor carrying length 8 with the fixed flag clear (verified: a
column ACE created reads back `length=8 fixed=False`, and the row lays the value out at a variable-column
start offset). Its *index* key is unaffected — that dispatches on the column's type, not on where the row
keeps the bytes — and is the same sign-bit-flipped big-endian int64 as Currency (§10.4).

> **The reason to match is faithfulness, not readability — ACE honours the descriptor's fixed flag.** It has
> to: its own `MSysComplexType_GUID` declares `Value` *fixed* while every GUID column its DDL creates is
> *variable*, so one engine reads both layouts routinely. Measured directly rather than argued: a **fixed**
> BIGINT, Currency and DateTime, and a **variable** Int32, Double, Currency and DateTime, all read back
> correctly through ACE — each with a variable column before the target and a fixed one after, so a value
> landing in the wrong region would have shifted its neighbours. (An earlier revision of this file claimed a
> fixed BIGINT would put the value "somewhere ACE does not look for it"; that was inferred from what ACE
> writes, never measured, and it is wrong.) `FixedFlagHonouredAccessTests`.

**`GUID` is variable-length too — but only where ACE's DDL made it.** Every GUID column ACE's SQL creates
carries length 16 with the fixed flag clear, at one column or at 252 (it is not a fallback for wide tables),
and `SELECT … INTO` produces the same. ACE's **own system tables are the exception**: `MSysComplexType_GUID`
`Value` is *fixed*, verified across eight ACE-created fixtures, and `DatabaseCreator` reproduces that — so
"GUID is variable" is a rule about declarations, not about GUID storage everywhere.

Unlike BIGINT this is not a wrong-value hazard: ACE reads a value back correctly from either layout
(verified with a variable column before the GUID and a fixed one after, so a misplaced value would have
shifted its neighbours). What it costs is record budget — 16 *fixed* bytes per column that ACE does not
spend, enough that a 250-column GUID table ACE creates without complaint exceeded the declared-record limit
in §3.4. `AccessTypeMapper` and `StatementExecutor.ColumnSpecFor` both declare it variable;
`GuidColumnStorageAccessTests` (in the Core and Engine suites) holds the measurements.

> **Writing one through ACE's OLE DB provider: not `DBTYPE_I8`.** An `OleDbType.BigInt` (20) parameter carries
> **no** value into a Large Number column — every value fails with "data value could not be converted", zero
> included — so the one type named for the job is the only one that cannot do it. Use **`Numeric` (131)**;
> `Decimal` (14) and `Variant` (12) also round-trip the full range exactly. `VarNumeric` (139) is rejected
> outright ("Type name is invalid"), and `Double` (5) is the trap: it succeeds quietly for small values and
> overflows near ±2⁶³. Measured against both extremes. EFCore.Jet's `JetLongTypeMapping.ConfigureParameter`
> already forces OLE DB 131 / ODBC 7 for its own `long` parameters, commented *"Using BigInt doesn't always
> work … When running in x64 it fails to convert"* — the same defect, found from the other direction and years
> earlier, though that mapping targets a `decimal(20,0)` column rather than a real `0x13` one.

**New-type format versions — the two are NOT the same version** (verified against files authored with each
feature enabled: enabling BigInt made the file version byte `0x05`, enabling Date/Time Extended made it
`0x06`; and measured again from the other direction — issuing `CREATE TABLE … BIGINT` against an ACE 12 file
raises it to `0x05`, `DATETIME2` to `0x06`). **`BIGINT` (Large Number)** requires the **ACE 16 / Access 2016** format (`0x05`); **`DATETIME2`
(Date/Time Extended)** requires the **ACE 17 / Access 2019+** format (`0x06`) — it arrived later (Access for
Microsoft 365). LibRed gates each accordingly (`AccessTypeMapper`). `DATETIME2` is a fixed 42-byte ASCII string of
three colon-separated fields: the .NET **day number**, the count of **100-ns ticks within the
day**, and the fractional **precision** (e.g. `7`). The first two are zero-padded to 19 digits so
that byte order equals chronological order (an order-preserving inline encoding). The value is
`new DateTime(day * TicksPerDay + time)`; e.g. `…693593:…0:7` is the 1899-12-30 epoch and
`…737590:…495300000000:7` is 2020-06-15 13:45:30. Sub-second precision (to 100 ns) is preserved.

Those three fields occupy **41** characters (19 + 1 + 19 + 1 + 1); the 42nd byte is a **NUL (`0x00`), not a
space** — verified by reading rows ACE itself wrote (`… 3A 37 00`). The distinction is not cosmetic: the whole
42 bytes go into the index key verbatim, so a space there would put every key out of step with ACE's and make
its seeks miss those rows.

**Indexing.** ACE does permit an index on the type, and keys it through the same 8-byte chunking it uses for
`Binary` (§10.4) — start flag, 8 bytes, `0x09` while more follow, then the final chunk and its real-byte count
— rather than folding the value to a number the way `DateTime` folds to its OA double. It can do that because
the stored form is already order-preserving. Verified ascending and descending in
`DateTime2KeyEncodingTests`.

The bytes on disk are correct, but **ACE's own OLE DB provider cannot read this type back** — see the
[footnote](#footnote--reading-datetime2-through-aces-own-drivers) at the bottom of this page.


## 7. Compressed Unicode

A text value that begins with the 2-byte marker `FF FE` is **compressed**: the following bytes
are one per character (ASCII range), not UTF-16. Otherwise the value is UTF-16LE. Applies to
both `Text` and resolved `Memo`.

Compression is opt-in per column, via the descriptor's `0x10` extended flag `0x01`. ACE sets it only when the
column is declared `WITH COMPRESSION` (or `WITH COMP`) — a plain `TEXT`/`MEMO` column created through SQL DDL
leaves it **clear** and stores UTF-16 whatever the content.

**When a capable column actually compresses a value** (measured in `CompressedTextAccessTests` and
`LongTextStorageAccessTests`, and reproduced by LibRed byte-for-byte):

- **Every character must fit one byte** (`<= 0xFF`, so Latin1, not just ASCII — `café` compresses, `一` does
  not). One non-Latin1 character leaves the whole value UTF-16; LibRed does not split runs, and neither does
  ACE here.
- **It must save space.** The marker costs 2 bytes, so 1- and 2-character values stay UTF-16 (2 + N < 2N only
  from N = 3). Verified at each of 1, 2 and 3 characters.
- **A chained long value is never compressed.** Compression is decided *after* the storage form, and the
  form — inline, single page or chained — is chosen on the **uncompressed** UTF-16 length. So an inline or
  single-page Memo compresses and a chained one does not, and the compressed size never approaches any limit.
  Microsoft's "only instances that, when compressed, will fit within 4096 bytes" describes the wrong
  quantity; see [long-values.md](long-values.md) for the measured boundary.

> **The mixed form is unreproduced.** The format allows a value to toggle between 1-byte and 2-byte runs
> mid-string — an embedded `0x00` after the marker switches mode — and mdbtools decodes it. LibRed reads only
> the all-compressed case, and writes either the whole value compressed or the whole value UTF-16.
>
> That gap is unreachable from anything ACE writes: **one incompressible character forfeits compression for
> the entire value**, position irrelevant, even when that throws away a ~1,000-byte saving
> (`MixedCompressionAccessTests` — 1,000 ASCII compresses, 1,000 ASCII + one CJK does not, whether the CJK
> sits first, last or in the middle). Checked by hand on ACE 12.0 and 16.0, which agree byte for byte; the
> test can only assert whichever is installed. A producer plausibly exists — the scheme dates from Jet 4.0,
> and Jackcess has a bug report about Access 2000 files — so treat this as *technically possible, not
> reproducible here*, and revisit if a real mixed-form file turns up.


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
- `DECIMAL(p,s)` / `NUMERIC(p,s)` use precision `1..28` and scale `0..p`; LibRed rejects dimensions outside
  those ACE/.NET decimal bounds before allocating or writing a table definition.
- The grammar parses **two-word** type names (`CHARACTER VARYING`, `BIT VARYING`); three-word
  (`NATIONAL CHARACTER VARYING`) is not parsed yet. `HYPERLINK`/`XML`/`SQL_VARIANT`/`VARIANT`/`COMP` have no
  mapping (rejected, as ACE also rejects them).


---

## Footnote — reading `DATETIME2` through ACE's own drivers

*Driver behaviour, not file format. Recorded here because it is the reason LibRed cannot cross-check this one
type against ACE the way it does every other type, and because it silently corrupts data in the wider repo.*

**The bytes on disk are correct; ACE's OLE DB read path is broken in three independent ways.** Verified
2026-08-26 against **Access / Microsoft 365 version 2608 (build 20326.20100 Click-to-Run, Current Channel,
x64)** — i.e. the then-current shipping build, not an old redistributable. Values were inserted through ACE and
then read back three ways — ACE OLE DB, ACE ODBC, and LibRed reading the file directly:

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

Measured with a consumer calling the OLE DB COM vtables directly — `CoCreateInstance` → `IDataInitialize` →
`IDBInitialize` → `IDBCreateSession` → `ICommandText` → `IColumnsInfo`/`IAccessor`/`IRowset`, every buffer
natively allocated. **No ADO, no `System.Data.OleDb`, no ODBC in the path**, so everything below is the
provider's own behaviour with nothing in between.

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

This is a genuine consumer buffer overrun, and it explains the `0xC0000374` / `0xC0000409` process crashes seen
under OLE DB reader churn: `System.Data.OleDb` places `obValue` at 16 in a 32-byte row buffer, so ACE writes 26
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
goes through the broken conversion. And it is live in the real provider stack, not just in a probe:
`AdHocMiscellaneousQueryJetTest` seeds **nine** `datetime2` columns (precisions 0–7), materialises all of them
in `Where_not_equals_DateTime_Now`, and is green — only because every seeded date is in **September**, which
merely shifts to August, and because the test asserts `Assert.Single` rather than any value. Changing one seeded
date to January makes it fail immediately with the `ArgumentOutOfRangeException` above (verified by doing it,
then reverting). So: predicates are right, corruption is silent outside January, Access itself never reads
through OLE DB, and the suites that do exercise the type check row counts rather than values.

Test: `AceDateTime2UpgradeTests.LibRed_decodes_datetime2_values_that_ace_reads_back_wrongly`.
