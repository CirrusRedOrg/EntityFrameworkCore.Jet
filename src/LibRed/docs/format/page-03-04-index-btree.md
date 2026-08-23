# Index B-tree pages — types 0x03 / 0x04

> Part of the [LibRed Jet / ACE file-format reference](README.md). Cross-references use the original **§-numbers**; the [section map](README.md#section-map) says which file each lives in.

## 10. Index B-tree pages — types `0x03` (node) and `0x04` (leaf)

### 10.1 Header

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Page type (`0x03` node / `0x04` leaf) |
| `0x01` | 1 | Flags (observed constant `0x01` — verified) |
| `0x02` | 2 | Free space |
| `0x04` | 4 | Owning table TDEF page |
| `0x08` | 4 | **The 4-byte field Jet4 inserted** right after the owner — purpose unknown, **`0` observed** on every ACE- and LibRed-written index page (Jackcess has no constant for it either). Inserting it here is what pushes prev/next/tail/compress down by 4 vs Jet3 (see the Jet3→Jet4 note under `0x1B`). |
| `0x0C` | 4 | **Previous leaf page** (`0` on the first/leftmost leaf), little-endian. **Verified against ACE:** on an ACE-built split index the higher-key leaf's `0x0C` points back at the lower-key leaf. (An earlier draft mis-placed prev/next at `0x08`/`0x0C` — wrong by 4 bytes; the real insertion is at `0x08`.) |
| `0x10` | 4 | **Next leaf page** (`0` on the last/rightmost leaf), little-endian. **Verified against ACE — and load-bearing:** Access's full-table `COUNT(*)`/scan descends to the leftmost leaf and walks this forward chain. If it is wrong (e.g. `next` written at `0x0C`), Access stops after the first leaf and **silently sees only those rows** — a data-loss/corruption hazard, since it then treats the rest of the table's space as free. LibRed maintains `0x0C`/`0x10` across splits (§10.5). (This is **Jet3's `0x0C` next-pointer shifted +4**; the child-tail that mdbtools lists at `0x10` is the *Jet3* tail position — in Jet4 it too shifted to `0x14`.) |
| `0x14` | 4 | **Child-tail** page (node pages: the rightmost child, referenced by no entry). For Jet4/ACE this offset is **definitive — byte-for-byte verified** (the tail pointer reads correctly here and drives correct multi-level traversal). This is **Jet3's `0x10` tail shifted +4** by the `0x08` insertion, which is exactly why mdbtools (Jet3) documents the tail at `0x10`. |
| `0x18` | 2 | Compressed-byte count (shared key prefix length, §10.3). Jet3's `0x14`, shifted +4. |
| `0x1A` | 1 | The **1-byte field Jet4 inserted** just before the bitmask. ACE writes `0` on leaves and `1` on the root of a two-level split index, consistent with a **B-tree level/height** — but **only `0` and `1` have been observed** (no 3-level tree was built against ACE, so `2`+ is a guess). **Required only for leaves (verified):** writing `0x01` on a *leaf* makes ACE fail to open the whole database (`"could not find the object 'Databases'"`). **Node value is cosmetic (verified):** an isolation test — correct leaf-chain offsets but node `0x1A=0` *and* nodes prefix-compressed — still gave ACE the right `COUNT`/`SUM` at 700 and 1500 rows, so Access reads a node's tail child regardless. **Jackcess likewise has no offset constant for `0x1A`** (it tells leaf from node by the page-type byte at `0x00`). LibRed still writes the height to match ACE byte-for-byte, but the only hard requirements are the leaf-chain offsets and a *leaf's* `0x1A=0`. |
| `0x1B` | … | Entry-position bitmask. mdbtools **version-labels** this: bitmask at `0x16` (Jet3) / **`0x1B` (Jet4)** — confirming our offset. The `+5` Jet3→Jet4 shift is **fully decomposed**: a **4-byte field inserted at `0x08`** (right after the owner) plus the **1-byte B-tree level at `0x1A`** = `+5`. Everything between — prev/next leaf, child-tail, compressed count — is Jet3's field shifted by 4, with the level accounting for the final `+1`. No unexplained bytes remain in this header. **Corroborated by Jackcess**, whose `JetFormat` constants give (Jet3 → Jet4): prev `8`→`12`, next `12`→`16`, child-tail `16`→`20`, compressed-count `20`→`24`, entry-mask `22`→`27` — i.e. `0x08`/`0x0C`/`0x10`/`0x14`/`0x16` each `+4`, and the mask an extra `+1`. Two independent implementations now agree on the shift; the `0x08` insertion is the only thing that produces it. (The *positions* are ACE-verified and Jackcess-corroborated; a real Jet3 index page would still be the final confirmation that these are the exact bytes Jet3 lacked — notably Jackcess has no constant for the `0x08` field or `0x1A` either.) |
| `0x1E0` | — | Start of entry data |

### 10.2 Entries

The entry bitmask (`0x1B` up to `0x1E0`) is a bitmap whose set bits, read in order, give the
**end offsets** of successive entries within the entry-data region. Entry `n` spans
`[prevEnd, end_n)` relative to `0x1E0` (first entry starts at 0).

Each entry ends with a **4-byte big-endian** trailing pointer:
- **Leaf:** `pointer` is a row id — page = `pointer >> 8`, row = `pointer & 0xFF`.
- **Node:** the trailing 4 bytes are the **child page**; recurse into it. After all entries,
  also recurse into the header's child-tail page (`0x14`).

> **Reader/traversal guardrails.** LibRed validates every page number before I/O, requires page type
> `0x03`/`0x04` and a consistent owning TDEF, bounds every bitmask-derived entry before reading its
> 4-byte trailer, and requires the compressed prefix to fit the first key. Node child/tail, leaf
> previous/next, and indexed-row page pointers are checked against the file's page range; optional leaf
> links must be zero or name an in-file page. Point/range seeks track every
> descent and leaf-chain page and reject repeats; the full index cursor uses an iterative ordered walk
> with the same repeated-page check, avoiding recursive stack exhaustion. A followed leaf link must
> resolve to another leaf of the same owner. Insert, delete, split propagation, and leaf-link mutation
> revalidate the target page at the final read-modify-write boundary, including its expected leaf/node
> role; this avoids relying on an earlier descent check if the file changed between reads. Violations
> are reported as `InvalidDataException`.

### 10.3 Prefix compression

Entries on a page share a leading key prefix of `compressedByteCount` (`0x18`) bytes. The
**first** entry is stored in full; its first `compressedByteCount` bytes are the shared prefix,
which every subsequent entry omits. Reconstruct: `fullKey = prefix ++ storedKey`. (The trailing
pointer is never compressed, so reading row pointers needs none of this.)

> **Compression is optional on leaves.** A `compressedByteCount` of 0 (every entry stored in full)
> is a valid *leaf* that Access reads without complaint — verified by rewriting a leaf uncompressed
> and re-seeking it. **On node (`0x03`) pages, ACE writes them uncompressed (`0x18 = 0`)**, and
> LibRed matches that. *Verified cosmetic:* compressing a node does **not** break Access — an isolation test
> (correct leaf-chain offsets, but nodes compressed and `0x1A=0`) still gave the right `COUNT`/`SUM`.
> The earlier "compression breaks tail descent" idea was a misattribution to the leaf-chain bug (§10.1).
> LibRed writes nodes uncompressed only to stay byte-faithful with ACE, not because it's required.
>
> **A leaf with ≤ 1 entry writes `0x18 = 0`.** Prefix compression describes a prefix *shared across
> entries*, so with zero or one entry there is nothing to share — ACE writes `compressedByteCount = 0`,
> not the sole key's whole length. (LibRed had a bug writing the full length there via
> `CommonPrefixLength(key, key)`; now `entries.Count ≤ 1 ⇒ 0`. Verified vs ACE on an index whose fresh
> root leaf holds a single key, e.g. the rebuild in §3.8.)

### 10.4 Key encoding (order-preserving)

Each key column is encoded so that raw byte comparison equals value comparison. LibRed both
**decodes** these keys and **encodes** them (`IndexKeyEncoder`, the inverse), so it can insert
into an index. The encoder is verified **byte-for-byte against Access**: re-encoding the value
decoded from Access's own stored key reproduces the exact bytes, and after a LibRed insert
Access satisfies an indexed primary-key seek over the entry LibRed wrote.

> **Text keys are the version-0 "General legacy" collation only.** The text weights below are the
> Access 2000–2007 **General** sort order = (locale 1033, **version 0**), selected by the column
> descriptor's sort-order version (`0x0D`, §3.4). Access 2010+ introduced a *new* default **General**
> order = (1033, **version 1**) with different key bytes (the old one was renamed "General legacy").
> LibRed implements version 0 only; a version-1 column/index (a database created by Access 2010+ with
> the default order, `0x0D = 1`) would need a separate weight table for both decode and encode.
> Before writing/seeking a text index key, a version-aware implementation should check `0x0D` and
> refuse (or switch tables) on version 1 rather than emit version-0 bytes. Not yet handled — no
> version-1 fixture available to reverse-engineer against.

Non-boolean columns are prefixed by a **flag byte**:

| | Ascending | Descending |
| --- | --- | --- |
| value present | `0x7F` | `0x80` |
| null | `0x00` | `0xFF` |

Then the value, transformed:

- **Integers** (Byte/Int16/Int32) and **Currency** (int64): big-endian, with the **sign bit of
  the first byte flipped**. Descending additionally inverts all bytes. (Decode reverses this.)
- **Single / Double / DateTime** (IEEE): if non-negative, flip the first bit; if negative,
  invert all bytes (ascending). Decode: first byte's top bit set ⇒ was positive (un-flip);
  else ⇒ was negative (invert all). DateTime is the resulting double via the OLE epoch.
- **FixedPoint (Numeric/Decimal)**: a 17-byte body — a **sign byte** followed by the value's
  **16-byte big-endian unscaled magnitude** (`|value| × 10^scale`, the same integer the row codec
  stores; §5). A non-negative value uses sign `0xFF`; a **negative value is the bitwise complement
  of the whole 17-byte positive form** (sign becomes `0x00`, magnitude one's-complemented). Byte
  order therefore equals numeric order: negatives (`0x00`) precede non-negatives (`0xFF`), and
  complementing makes a larger magnitude sort earlier among the negatives. **Zero encodes as
  positive.** Descending inverts all bytes as usual. Verified byte-for-byte vs ACE, ascending and
  descending (`DecimalKeyEncodingTests`); e.g. at scale 4, `1.0` → `7F FF 00…002710` (10000) and
  `-1.0` → `7F 00 FF…FFD8EF` (`~10000`).
- **Boolean:** no flag byte — a single constant: ascending `0x00` = true, `0xFF` = false
  (true sorts first).
- **Memo (Long Text)** is **indexable** in Access (`CREATE INDEX` on a memo column succeeds — only
  `OLE Object` is rejected, *"Invalid field definition … in definition of index or relationship"*).
  Its key is the **ordinary Text collation key over the value's first 255 characters** — verified
  byte-for-byte vs ACE (`MemoKeyEncodingTests`): a 256- or 300-character memo yields exactly the key of
  its 255-character prefix, so two memos differing only past character 255 share a key (fine for a
  non-unique index). Index keys are therefore encoded from the **logical** row values, before memo/OLE
  values are materialised into their `LongValueDescriptor`s.
> **Two General orders, two weight tables.** Everything in this Text section describes **General-Legacy**
> (sort-order version `0`). The Access-2010+ **General** order (version `1`, the byte at column `0x0E` /
> page-0 `0x71`) uses the *same framing* — start flag, primary weights, `0x01`, secondary section, inline
> word-sort records, `0x00` — but different weights:
>
> | | General-Legacy (v0) | General (v1) |
> |---|---|---|
> | primary | **1 byte**, a Jet-era compaction | **2 bytes**: the Windows NLS `(Script Member, Alphabetic Weight)` verbatim |
> | secondary | the NLS Diacritic Weight | the same |
> | inline position | counts primary **bytes** | counts primary **weights** (so `O'Brien` is `0x0B` in both, though v1 has emitted twice as many bytes) |
> | soft hyphen | inline record, code `0x83` | wholly ignorable, no record |
>
> v1's table is the **Windows Server 2008** sorting weight table, frozen — identified by reconstructing
> measured ACE v1 keys from every published Windows table (Server 2008 scores 25/25; Win7/2008R2 24/25,
> Vista 23/25, Win8+ 22/25, NT4-2003 18/25, the discriminators being `1` = `13 25` vs `13 26`, its DW `2`
> vs `3`, and `½` = `13 24 214` vs `13 17 2`). Access 2010 shipped with the then-current weights and froze
> them when Windows 7/8 moved them — the "major NLS version, re-index everything" event described in
> [MS-UCODEREF] and *Handling Sorting in Your Applications*.
>
> This also explains the framing generally: **script member 6 is the word-sort class**, and the apostrophe's
> `0x80` and hyphen's `0x82` inline codes are simply their Alphabetic Weights — so the inline record is
> `80 <pos> <SM> <AW>`, not a bespoke Access encoding. And because the NLS **Case Weight** is the tertiary
> section this format truncates, case *and* character width fold for free (`Ａ` U+FF21 and `A` share the
> primary `0E02` and differ only in that discarded weight).
>
> LibRed encodes both: `JetTextCollation` (v0, hand-built tables) and `JetTextCollationV1` (v1, from an
> embedded copy of the Server 2008 table — see `tools/sortkey-table/generate.ps1`). Other locales are still
> refused. Tests: `GeneralV1CollationTests` (keys measured from ACE) and `GeneralV1CollationAccessTests`
> (live oracle, plus ACE seeking an index LibRed wrote in a v1 database).

- **Text:** Jet's "General" collation. The key is the start flag, then one or two
  **primary-weight** bytes per character, then a `01 00` terminator. Weights are **case-folded**
  (lowercase weighs the same as uppercase), **trailing spaces are dropped**, and an internal
  space weighs `0x07`. Most characters weigh one byte; `^ _ \` { | } ~` weigh two (sharing the
  `0x2B` page). The weight table is a fixed lookup (A=`4A`, B=`4C`, C=`4D`, …, digits step by two
  from `0x36`), **verified byte-for-byte against the ACE engine** over printable ASCII and
  implemented by `JetTextCollation` — so LibRed can now *write* ASCII text index keys (e.g. a
  string primary key). Decoding remains lossy (case is discarded — that is why a text primary key
  treats `'A'` and `'a'` as duplicates).

  **Apostrophe and hyphen are "ignorable"** (so `O'Brien` sorts next to `OBrien`): they add **no
  primary weight**, but each appends an inline record to a trailing section. After the primary's
  `0x01` end marker, if any ignorable char is present the key adds `01 01 01` once, then per
  ignorable char four bytes `80 <pos> 06 <code>`, then the final `00`. `<pos> = 0x07 + 4 × (count
  of **primary weight bytes** emitted before it)` and `<code>` is `0x80` for apostrophe / `0x82` for
  hyphen — verified against ACE (e.g. `ANNE-MARIE` → `… 80 17 06 82 …`, the hyphen at position 4;
  `Aß-B` → `7F 4A 6B 6B 4C 01 01 01 01 80 13 06 82 00`, hyphen at position **3** because ß expands to
  two primary bytes `SS`).

  > **Why those two characters specifically:** this is Windows' documented **word sort**, the default for
  > the NLS sorting functions — *"all punctuation marks and other nonalphanumeric characters, except for the
  > hyphen and the apostrophe, come before any alphanumeric character. The hyphen and the apostrophe are
  > treated differently … to ensure that words such as 'coop' and 'co-op' stay together in a sorted list"*
  > ([Handling Sorting in Your Applications](https://learn.microsoft.com/en-us/windows/win32/intl/handling-sorting-in-your-applications)).
  > The alternative, `SORT_STRINGSORT`, treats both as ordinary punctuation sorting before alphanumerics —
  > which is *not* what ACE's keys show. So the ignorable pair is the platform default rather than an Access
  > invention. (The soft hyphen `U+00AD`, code `0x83`, is ignorable for a different reason: it carries no
  > weight of its own. The same page notes the Arabic kashida likewise produces no sort-key value.)

  **Latin-1 punctuation and symbols** weigh two bytes, in groups that mirror the Win32 NLS primary order
  in ACE's own compacted numbering — harvested from ACE's stored keys character by character
  (`Latin1SymbolCollationAccessTests`):
  `¡ ¦ ¨ ¯ ´ ¸ ¿` = `2B 10`…`2B 16` (continuing the `^_\`{|}~` group);
  `± « » × ÷` = `33 04/05/07/09/0A`; `¢ £ ¤ ¥ § © ¬ ® ° µ ¶ ·` = `34 A6`…`34 B1`;
  `¼ ½ ¾` = `37 12/16/1A`. The **ordinal indicators** `ª`/`º` are not symbols at all: they take their base
  letter's primary with a distinguishing **secondary** `0x03` (`ª` = `7F 4A 01 03 00`), like an accent.
  The **superscript digits** `¹ ² ³` take the *same* primary as `1 2 3` with no distinguishing secondary, so
  ACE sorts and compares them **equal** to the base digit (`¹` and `1` are both `7F 38 01 00`) — which makes
  them duplicates in a unique index. The **soft hyphen** `U+00AD` is a third ignorable, code `0x83`
  (alongside apostrophe `0x80` and hyphen `0x82`).

  > **Width and case insensitivity are a consequence of the truncated key, not a normalisation pass.**
  > A Win32 NLS sort key carries case *and* width in its **tertiary** section, which this format discards:
  > `LCMapStringEx` gives `Ａ` (U+FF21) and `A` the identical primary `0E02`, differing only at the tertiary
  > weight. So full-width forms, half-width katakana, and case all collapse for free. ACE does **not**
  > pre-map with `LCMAP_HALFWIDTH`: `U+3000` (ideographic space) keeps its own key `7F 07 01 00` rather than
  > becoming a space and being dropped by the trailing-space trim, which is what a width pre-mapping would
  > produce. Ligatures need no special handling either — NLS itself expands `ﬁ` to `f` + `i`. (Probed in
  > `SortKeyComparisonProbeTest`.)

  **A few letters expand to multiple base letters** (each expanded letter weighs its normal primary,
  no accent): `ß`→`SS`, `Þ`/`þ`→`TH`, `Æ`→`AE` — verified against ACE (`ß` = `7F 6B 6B 01 00`, same as
  `SS`). Because the ignorable-position count is by primary byte, an expansion counts as its expanded
  length (above).

  **Accented Latin-1 letters** sort with their **base letter's primary weight** and record the
  accent in a **secondary section**. Each character has a secondary weight (default `0x02`); an
  accented letter carries the weight of its diacritic instead (verified against ACE, and the weight
  depends only on the accent, not the base letter): **acute `0x0E`, grave `0x0F`, circumflex `0x12`,
  diaeresis/umlaut `0x13`, tilde `0x19`, ring `0x1A`, cedilla `0x1C`**; plus atomic `Ø`→base `O`+`0x21`,
  `Ð`→base `D`+`0x68`, and the ligature `Æ`→primaries `A E` (no accent). The section is emitted only
  when some character is accented: after the primary's `0x01` end marker it lists the secondary weight
  of **every byte from the first up to and including the last accented one**, e.g. `México D.F.` →
  `7F 60 51 75 59 4D 64 07 4F 1C 53 1C 01 02 0E 00` (é = primary `0x51` = E, secondary `0x0E`), and
  `Montréal` (é at position 5) → `… 01 02 02 02 02 02 0E 00`. LibRed decomposes via Unicode NFD (base
  letter + combining mark) plus the small atomic table above; `JetTextCollation` reproduces these keys
  **byte-for-byte vs ACE** (México/Montréal/München/São Paulo/Résumé and single accents).

  **Descending** text keys are the **bitwise inverse of the ascending key, with a `0x00`
  appended** — verified against ACE (e.g. ascending `A` = `7F 4A 01 00` → descending
  `80 B5 FE FF 00`). The inverted start flag is `~0x7F = 0x80`, matching the descending flag of
  the fixed-type keys.

  *Not yet handled:* characters outside ASCII + the accented Latin-1 set above (and a key mixing an
  accent with an ignorable apostrophe/hyphen is untested).
- **GUID:** the start flag `0x7F`, then the 16 GUID bytes in **canonical string order** (i.e.
  `guid.ToString("N")` bytes — **not** the mixed-endian `.ToByteArray()` storage layout), split into two
  8-byte halves by a constant `0x09` marker, and terminated by `0x08` — a fixed **19-byte** key. Data
  bytes equal to `0x08`/`0x09` need no escaping (every field is at a fixed offset). Verified byte-for-byte
  against ACE (zeros, all-`FF`, sequential, and random GUIDs); ACE also opens a LibRed-written GUID-PK
  table and seeks a row by its key. Encoded/decoded by `IndexKeyEncoder`/`IndexKeyDecoder`. Example:
  `01020304-0506-0708-090a-0b0c0d0e0f10` → `7F 0102030405060708 09 090A0B0C0D0E0F10 08`.
  **Descending** inverts every byte of the ascending key **except the `0x09` field marker** (kept constant
  so the structure stays parseable — and it doesn't affect ordering since it's equal in every key): the
  start flag becomes `0x80` and the `0x08` terminator becomes `0xF7`, but the middle `0x09` is unchanged.
  Verified against ACE (`CREATE INDEX … (K DESC)`), e.g. `00000000-…-0` → `80 FFFFFFFFFFFFFFFF 09
  FFFFFFFFFFFFFFFF F7`. No trailing `0x00` (unlike descending text keys).
- **Binary (general):** the start flag (`0x7F` asc / `0x80` desc), then the raw bytes in **8-byte
  chunks**. A genuinely **zero-length Binary value** is the start flag alone (`7F` ascending / `80`
  descending). A non-empty value—including an all-zero value—uses normal chunks. Each chunk is
  8 bytes — real bytes left-aligned,
  **zero-padded on the right** — followed by
  a **control byte**: `0x09` when a further chunk follows (a full 8-byte chunk with more data to come),
  otherwise the **real-byte count of this final chunk** (`0x01…0x08`; `0x08` for a full final chunk).
  The count `≤ 8 < 0x09`, so control values never collide. This is exactly the
  GUID chunking generalised to any length: a 16-byte value is two chunks (`… 09 … 08`), and the old
  fixed 4-byte MSysQueries.Order case is the single-chunk form `7F <4B> 00000000 04`. The trailing
  length-terminator makes shorter values sort before longer ones that share a prefix (correct binary
  prefix order). **Descending** inverts every byte **except the `0x09` continuation markers** (mirrors
  GUID): flag → `0x80`, data bytes and the terminator inverted, markers unchanged. Verified byte-for-byte
  against ACE's `EverythingIsBytes` fixture (3/4/5/8/16-byte keys, single- and multi-chunk) by
  re-encoding each stored key's row value; descending has no ACE fixture and is extrapolated from the
  verified GUID descending (ordering-tested for internal consistency). `IndexKeyEncoder.EncodeBinaryChunked`.

### 10.5 Insertion and splitting

To insert a key, descend from the index root (§3.5 offset `0x26`) following node separators — a
separator is the **maximum key of its child subtree**, stored as a full leaf key (column key ++
4-byte row pointer), so descend into the first child whose separator `≥` the new full key, else the
child-tail (`0x14`). Slot the new entry into the target leaf in key order and rewrite the page.

When a page would overflow, **split** it (LibRed's `IndexWriter`). Verified by inserting 1500 keys —
past one leaf — and reading every one back in order through a now multi-level tree, **and against ACE**:
Access opens the file and an indexed point seek (`WHERE Id = 1234`), an indexed range (`Id BETWEEN 300
AND 309`), a full `COUNT(*)`, a non-indexed scan (`T LIKE 'r%'`) and `SUM(Id)` all return the correct
result — i.e. every row is reachable both by the tree and by the leaf-chain scan Access uses.

The split mechanics:

- **Leaf split:** partition the sorted entries in half; the lower half stays on the original page,
  the upper half goes to a newly allocated page. The doubly-linked leaf chain is maintained — the
  new right page's *prev* (`0x0C`) points at the left, its *next* (`0x10`) inherits the left's old
  next, the left's *next* becomes the right, and the old next leaf's *prev* is repointed to the
  right. **Getting these offsets right is essential** — Access's scan walks the `0x10` next-chain
  from the leftmost leaf, so a mis-placed pointer makes it lose every row past the first leaf. The
  promoted separator is the **left half's maximum full key**, which stays in the leaf (a copy is
  promoted, B+tree-style). Leaves are prefix-compressed; LibRed also writes node pages uncompressed
  with `0x1A` set to their height above the leaves to match ACE byte-for-byte, though (unlike the
  leaf-chain offsets) neither is *verified* to be required — see §10.1 `0x1A` and §10.3.
- **Node split:** partition on a **middle entry** whose key is *promoted* (removed from the node);
  its child becomes the left node's child-tail, and the old tail stays the right node's tail.
- **Propagation:** the promoted separator `[key → left page]` is inserted into the parent, whose
  pointer to the just-split page is repointed to the new right page; if the parent overflows it
  splits in turn, up to the root.
- **Root growth:** when the root itself splits, a new root node is allocated holding one entry
  `[promoted → old root]` with the new page as its child-tail, and the index-data block's root
  pointer (§3.5 `0x26`) is repointed to it. (The single-leaf → two-leaves case hits this on the
  first overflow, changing the root page's type from leaf `0x04` to node `0x03`.)

> Newly allocated split pages are taken from the global free-page map (§ page 1). Registering them
> in the *index's own* owned-pages usage map (§3.5) is **not yet done** — tolerated so far, but a
> point to revisit when validating large indexes against Access.


> **Indexable types — coverage vs ACE (§10.4).** `IndexKeyEncoder` encodes every type ACE lets you index —
> Boolean, Byte, Int16, Int32, Currency, Single, Double, DateTime, Text, GUID, Binary, FixedPoint, and Memo
> (its first 255 chars) — all byte-verified. ACE correctly **refuses** to index `OLE` (`0x0B`) and `Complex`
> (`0x12`). Two ACE-16-only types remain unencoded: **`Int64`/BIGINT** (`0x13`) — trivial (an int64 like
> Currency, sign-bit flipped) but unverified — and **`DateTimeExtended`/DATETIME2** (`0x14`), blocked on its
> missing write codec ([data-types](data-types.md)). See the worklist in `src/LibRed/README.md`.
