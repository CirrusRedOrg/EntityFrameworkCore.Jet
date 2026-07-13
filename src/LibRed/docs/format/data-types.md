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
| `0x0F` | GUID | 16 raw bytes |
| `0x10` | FixedPoint (Numeric/Decimal) | 17 bytes: sign byte (`0x80` = negative) + 128-bit magnitude (four 32-bit little-endian words, low word last); value = magnitude / 10^scale. Precision/scale from the column descriptor (§3.4) |
| `0x12` | Complex (multi-value / attachment) | descriptor parsed; contents not materialized (out of scope for SQL/EF) |
| `0x13` | Int64 — **BIGINT** (ACE 16) | 8-byte little-endian signed integer. Stored as a *variable*-length column |
| `0x14` | DateTimeExtended — **DATETIME2** (ACE 16) | fixed 42-byte ASCII `<day>:<time>:<precision>` (see below) |

**ACE 16 types.** Office 2016 added `BIGINT` and `DATETIME2`. `DATETIME2` is a fixed 42-byte ASCII string of
three colon-separated fields: the .NET **day number**, the count of **100-ns ticks within the
day**, and the fractional **precision** (e.g. `7`). The first two are zero-padded to 19 digits so
that byte order equals chronological order (an order-preserving inline encoding). The value is
`new DateTime(day * TicksPerDay + time)`; e.g. `…693593:…0:7` is the 1899-12-30 epoch and
`…737590:…495300000000:7` is 2020-06-15 13:45:30. Sub-second precision (to 100 ns) is preserved.


## 7. Compressed Unicode

A text value that begins with the 2-byte marker `FF FE` is **compressed**: the following bytes
are one per character (ASCII range), not UTF-16. Otherwise the value is UTF-16LE. Applies to
both `Text` and resolved `Memo`.

> Not yet handled: the full format can toggle between 1-byte and 2-byte runs mid-string for
> mixed scripts. LibRed handles the common all-compressed case.


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
- The grammar parses **two-word** type names (`CHARACTER VARYING`, `BIT VARYING`); three-word
  (`NATIONAL CHARACTER VARYING`) is not parsed yet. `HYPERLINK`/`XML`/`SQL_VARIANT`/`VARIANT`/`COMP` have no
  mapping (rejected, as ACE also rejects them).
