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
> 4-byte trailer *after reconstruction* (§10.3), and requires the compressed prefix to fit the first
> entry. Node child/tail, leaf
> previous/next, and indexed-row page pointers are checked against the file's page range; optional leaf
> links must be zero or name an in-file page. Point/range seeks track every
> descent and leaf-chain page and reject repeats; the full index cursor uses an iterative ordered walk
> with the same repeated-page check, avoiding recursive stack exhaustion. A followed leaf link must
> resolve to another leaf of the same owner. Insert, delete, split propagation, and leaf-link mutation
> revalidate the target page at the final read-modify-write boundary, including its expected leaf/node
> role; this avoids relying on an earlier descent check if the file changed between reads. Violations
> are reported as `InvalidDataException`.

### 10.3 Prefix compression

Entries on a page share a leading prefix of `compressedByteCount` (`0x18`) bytes. The **first** entry is
stored in full; its first `compressedByteCount` bytes are the shared prefix, which every subsequent entry
omits. Reconstruct: `fullEntry = prefix ++ stored`.

> **The prefix covers the entry whole — it can reach into the trailer.** An earlier revision of this section
> claimed "the trailing pointer is never compressed, so reading row pointers needs none of this". That is
> **wrong**, and it made LibRed reject pages ACE had written. When many rows share a key they are also
> consecutive on one data page, so the trailer's leading bytes are common too and ACE compresses them away.
> A leaf holding 500 rows all keyed `"same"`:
>
> ```
> compressedByteCount = 9
> entry 0 (11 bytes)   7F 6B 4A 60 51 01 00 | 00 01 62 00     key "same", then row (page 354, row 0)
> entry 1  (2 bytes)                     62 01               → prefix ++ 62 01 = … 00 01 62 01, row 1
> entry 2  (2 bytes)                     62 02               → row 2
> ```
>
> The prefix `7F 6B 4A 60 51 01 00 00 01` is the seven-byte key **plus the first two bytes of the trailer**,
> leaving two stored bytes per entry. So **size limits apply to the reconstructed entry, never to what is
> stored** — a stored entry may be shorter than the 4-byte trailer, and the key may be empty. Take both the
> key and the trailer from the reconstruction. Likewise `compressedByteCount` is bounded by the first
> entry's **whole** length, not by its key. (`DuplicateIndexKeyProbeTest`; the old reading refused any index
> with ~500+ equal keys, which is ordinary for a non-unique index.)

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

> **The text weights below are one specific collation: General, version 0.** A text column's collation is
> the `(LANGID, sort id, version)` triple in its descriptor at `0x0B`–`0x0E` (§3.4) — here
> (1033, 0, **0**), the Access 2000–2007 order Access later renamed "General legacy". The other orders use
> the *same framing* and different weights: see **Two General orders** and **Locale-specific orders** below.
> Encoding must **gate on the whole triple** rather than assume, which is what `IndexKeyEncoder` does — it
> throws on anything it has no table for instead of emitting General bytes. That matters more than it
> sounds: a wrong key does not fail, it silently disagrees with ACE's.

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
> | inline position | counts primary **weights** | the same (so `O'Brien` is `0x0B` in both, though v1 has emitted twice as many bytes) |
> | soft hyphen | inline record, code `0x83` | wholly ignorable, no record |
>
> **v0 is the NT4-era NLS order, renumbered into one byte.** v1 could be *identified* because its primaries
> are the NLS `(SM, AW)` pair verbatim; v0's are a Jet-specific compaction, which is why its table had to be
> measured character by character instead. But the compaction turns out to be **order-preserving**, so the
> table is explained rather than merely recorded. Sorting every character by the primary in the
> **Windows NT 4.0 – Server 2003** table (the generation contemporary with Jet 3.5/Access 97 and Jet 4/Access
> 2000) and checking v0's bytes come out non-decreasing gives **507 of 510** strictly-ordered pairs kept, and
> **947 of 955** NT4 ties still tying (`SortOrderProvenanceProbeTest`, needs `LIBRED_NT4_TABLE`). By block:
>
> | | agreement |
> |---|---|
> | Cyrillic, Greek, Hebrew, both Latin extensions, punctuation, currency, letterlike, number forms, spacing modifiers, fullwidth | **100%** — every block, every pair |
> | Latin-1 + ASCII | 51/52; the single exception is `U+0651`, whose v0 primary is the anomalous `FF FF` |
> | Arabic | 149/151 — the only script where Jet genuinely renumbered against NLS |
>
> So the `+2` stride, the gaps that became language-letter insertion slots, and the `0x79` page for
> non-Latin scripts are all one decision: **compact the NT4 primary order into a byte, leaving room**.
> Jet also *narrowed* it — of the 552 characters v0 treats as ignorable, 464 (84%) are unweighted in the NT4
> table too, but the remaining 88 are weighted by NLS and dropped by Jet, which is an editorial choice of its
> own and not something a published table would have told us.
>
> v1's table is **very nearly** the Windows Server 2008 sorting weight table, frozen — identified by
> reconstructing measured ACE v1 keys from every published Windows table (Server 2008 scores 25/25;
> Win7/2008R2 24/25, Vista 23/25, Win8+ 22/25, NT4-2003 18/25, the discriminators being `1` = `13 25` vs
> `13 26`, its DW `2` vs `3`, and `½` = `13 24 214` vs `13 17 2`). Access 2010 shipped with the then-current
> weights and froze them when Windows 7/8 moved them — the "major NLS version, re-index everything" event
> described in [MS-UCODEREF] and *Handling Sorting in Your Applications*.
>
> **"Very nearly" is load-bearing.** Those 25 discriminators were all Latin and symbols, and a full-BMP sweep
> shows the published file is not what ACE carries everywhere: it is right about 57,793 characters and wrong
> about 501, plus 5,082 that ACE treats as wholly ignorable and the published file has no entry for at all.
> The disagreements are concentrated in scripts added or reweighted after Server 2008 — ACE gives Balinese
> and Canadian syllabics *Latin* weights — and in the Arabic harakat and several ligature blocks. Rather
> than guess at which NLS revision ACE really carries, the differences are **measured and embedded**
> (`SortKeyTableV1Overrides.bin`, 2.0 KB, written by `SortKeyTableV1OverrideGeneratorTest` with
> `LIBRED_GENERATE_V1=1`) — the same answer v0 needed, at 3% of the size, because v1 is right about the rest.
>
> An override records the primary and secondary bytes **raw**, not as `(SM, AW, DW)` weights, because that
> reading assumes every primary is a two-byte pair carrying one secondary and ACE breaks it both ways: the
> Arabic harakat have a secondary and *no primary* (`U+064C` is `7F 01 56 00`), and the Lao vowel signs take
> a **one-byte** primary (`U+0EB0` is `7F 41 01 0A 00`). A primary byte can even *be* `0x01`: `U+0385`,
> `U+1B3B` and `U+FC25` weigh `07 53 01`, and `U+FC33` and `U+FCC2` weigh `29 0B 01`, so the section
> delimiter is the **last** `0x01` in a key, not the first. Splitting at the first made those five look like
> a key with an extra section bolted on; measuring them in combination (`aX`, `Xa`, `XaX`) showed they are
> ordinary two-weight expansions.
>
> This also explains the framing generally: **script member 6 is the word-sort class**, and the apostrophe's
> `0x80` and hyphen's `0x82` inline codes are simply their Alphabetic Weights — so the inline record is
> `<position:16> <SM> <AW>`, not a bespoke Access encoding. And because the NLS **Case Weight** is the
> tertiary section this format truncates, case *and* character width fold for free (`Ａ` U+FF21 and `A` share
> the primary `0E02` and differ only in that discarded weight).
>
> **The position is a 16-bit field, big-endian, with bit 15 set** — `0x80` is not a marker byte.
> [MS-UCODEREF]'s *GetWindowsSortKey* pseudocode states it: `SpecialWeightType` is `(Position: 16 bit
> integer, ScriptMember, PrimaryWeight)`, emitted as `Byte1 = Position >> 8`, `Byte2 = Position & 0xff`, then
> the two weights.
>
> The two readings agree below `0x100` and diverge above it, and the offset `0x07 + 4 x position` passes
> `0xFF` at position 62. So a hyphen at character 63 is `81 03`, at 200 `83 27`, at 250 `83 EF` — measured
> against ACE across positions 10 to 250 under both orders.
>
> Worth stating loudly, because it is invisible to the obvious tests. Every single character encodes
> correctly, every short string encodes correctly, and the field only overflows past character 62 — so
> reading `0x80` as a marker and truncating the position looked right everywhere anyone had looked, and
> silently produced a wrong key for any longer value containing an apostrophe or hyphen. A hyphenated name in
> a 255-character column is enough. The lesson is to measure COMBINATIONS and not only characters: a
> per-character sweep can be exhaustive — all 63,422 of them — and still miss a whole class of bug.
>
> **French is the diacritic section written BACKWARDS — no tailored letter at all.** The same pseudocode has
> an `IsReverseDW` flag whose rule is: drop the run of default diacritics from the **left** rather than the
> right, and write what remains **right to left**. Verified against ACE byte for byte:
>
> | | diacritics | trimmed | stored |
> |---|---|---|---|
> | `coté` | `02 02 02 0E` | `0E` | `01 0E 00` |
> | `côte` | `02 12 02 02` | `12 02 02` | `01 02 02 12 00` |
> | `côté` | `02 12 02 0E` | `12 02 0E` | `01 0E 02 12 00` |
>
> So French orders by the LAST accent — `cote < côte < coté < côté`, where General gives
> `cote < coté < côte < côté`. LibRed matches ACE across all of Latin-1 and Latin Extended-A with accents
> doubled and tripled per string, 1,289 values, zero differences.
>
> It sat in the "unclassified, secondary-section tailoring" bucket for a long time, and the reason is worth
> keeping: a word with ONE accent encodes identically under both orders, and the sample set that measured
> every locale against General contained no two-accent word. The rule was invisible to the measurement, not
> absent from it — the same shape of blind spot as the inline position field above.
>
> **And the `01 01 01` before a word-sort record is three SECTION SEPARATORS, not an introducer.** The same
> pseudocode gives the full frame as
>
> ```
> primaries  01  diacritics  01  case-weights  01  extra-weights  01  special-weights  00
> ```
>
> Access emits that frame with the **case-weight section empty**, which is the mechanism behind something
> long known here empirically: case and character width fold because width lives in bit 0 of the Case Weight,
> and Access simply never writes that section. So the run of three is end-of-diacritics, an empty case
> section, an empty extra section — and it shortens to `FF 01` when a kana section fills the extra slot.
> `MIN_DW = 2` in the same source is the `0x02` default secondary whose trailing run gets trimmed.
>
> Three more things that source settles, or usefully fails to:
>
> - **The contraction limit corroborates v0's provenance independently.** It supports only 2- and
>   3-character contractions on NT4 through Server 2003, and 4- to 8-character ones from Vista. Every v0
>   tailoring here tops out at three (Hungarian `ggy`) — arrived at by measurement, and matching the
>   generation the weight-table comparison already identified. Two unrelated routes to the same date.
> - **The `FD FF` Han primary is NOT the Windows 7 three-byte weight.** That feature emits `SM PW DW` —
>   *three* bytes, with the diacritic moved into the primary and omitted from its own section — and arrived
>   in Windows 7 / Server 2008 R2, *after* the table Access froze. Ours is four bytes and is the older
>   extension-marker shape, alongside `SCRIPT_MEMBER_EXT_A` / `PRIMARY_WEIGHT_EXT_A`. Consistent with the
>   freeze; the measured bytes stand.
> - **Access PACKS the East Asia extra weights where Windows does not.** The specification gives one byte per
>   character per group (`W6`, `W7`, trailing `0xE4` trimmed, `0xFF` between). Access instead packs the kana
>   flags three to a byte — measured across all thirty combinations up to four kana. So the kana section is a
>   compacted variant of the documented structure rather than the structure itself.
>
> **Nothing in that source covers the 510-byte cap, truncation or the checksum below.** A useful negative:
> those are Jet/ACE inventions with no Windows counterpart, which is why they had to be measured.
>
> LibRed encodes both: `JetTextCollation` (v0, a measured table) and `JetTextCollationV1` (v1, the published
> table plus the measured overrides — see `tools/sortkey-table/generate.ps1`), sharing `JetKanaSection`.
> **Both now cover the whole Basic Multilingual Plane**: 63,422 characters each, every key byte-for-byte
> what ACE stores, nothing refused and nothing left unhandled (`Probe_full_bmp_coverage`, needs
> `LIBRED_FULL_BMP`). Other locales are still refused under v1.
>
> **Above the BMP** the two orders disagree completely, measured over all of planes 1 and 2 and sampled across
> all sixteen. **v0 ignores astral characters entirely** — every one gets the empty key `7F 01 00`, so under
> General Legacy an astral character is invisible to the index. **v1 weighs both surrogate halves**, each
> looked up in the table like any other character: `U+10000` is `7F B002 B4F8 01 3F 3F 00`, the high surrogate
> `D800` weighing `B002` and the low `DC00` weighing `B4F8`.
>
> Only the high surrogates up to `U+D87F` carry weights. From **plane 3 upward the high half is ignorable**
> and the low one stands alone — `U+30000` is `7F B4F8 01 3F 00`, and `U+31000`, `U+34000` and `U+40000` give
> the same. So planes 1 and 2 are fully distinguished, while planes 3 to 16 collapse onto **1,024 keys** and
> any two code points there congruent mod `0x400` share one.
>
> The only change v1 needed was to treat an **unweighted surrogate as ignorable rather than an error**. The
> tempting reading of the plane-3 samples — "the high surrogate contributes nothing" — is wrong, and skipping
> every high surrogate breaks all 131,068 characters of planes 1 and 2. `AstralCollationProbeTest`, needs
> `LIBRED_ASTRAL=1` (or `LIBRED_ASTRAL_FULL=1` for a whole plane).

### 10.5 The 510-byte index entry limit

**ACE stores an index entry of at most 510 bytes as built.** At exactly 510 it comes back byte-for-byte; a
value that would need 511 comes back as 510: the first **508** bytes kept, and the rest replaced by a
two-byte **checksum over the bytes that were dropped**. That is why two long values sharing a 508-byte
prefix still sort apart instead of colliding.

#### The checksum

Recovered by measurement. Three tails differing in one byte show the function is **affine over GF(2)** —
`L(0xA3) = CA03`, `L(0x13) = 6980`, `L(0xB0) = A383`, and `CA03 ^ 6980 = A383` exactly — and it is
**shift-invariant** across 173 observations, so a byte at distance *d* from the end contributes `S^(d-1)` of
itself whatever the message length. Sweeping all 65,536 polynomials in five framings found nothing, because
the framing is the unusual part: the standard reflected update is `crc = (crc >> 8) ^ T[(crc ^ b) & 0xFF]`,
passing the byte **through** the table, while ACE computes

```
crc = 0
for each dropped byte b, except the terminator:
    crc = (crc >> 8) ^ T[crc & 0xFF] ^ b        // b injected RAW, not through T
```

with no initial value and no final XOR. The step's table, solved by Gaussian elimination over the measured
contributions and predicting all 657 of them, is

```
T[1<<i] = 0580 0F80 1B80 3380 6380 C380 8381 0383      (i = 0..7)
```

The terminator is excluded: running it would advance every other byte one step further. It is `0x00` anyway,
and a linear map sends zero to zero.

LibRed reproduces this (`JetIndexKeyChecksum`), so a long value is truncated exactly as ACE truncates it
rather than refused — verified against ACE at and past the boundary for Latin, accented and Han text under
both sort orders.

**One case is still refused:** where the dropped bytes contain an inline word-sort record. It cannot be
verified even in principle, because the record sits in the part ACE discarded and what it held is
unobservable; if ACE recomputes its position when truncating, the checksum's input is not what LibRed
reconstructs. Guessing there would write a silently wrong key.

The cap is on the **whole entry, not per column**: two 200-character text columns weigh about 404 bytes of
key each, comfortably under the cap individually, and ACE stores their combined entry hashed at 510.

Because it limits **weights** rather than characters, the text it buys depends on collation and script — and
this is the practical cost of General over General Legacy, invisible in the schema:

| | bytes per character | characters indexed in full |
|---|---|---|
| v0, Latin | 1 primary | **255** — the column limit is reached first |
| v0, accented / CJK | 2 | **254** |
| v1, Latin | 2 primary | **253** |
| v1, accented | 3 | **169** |
| v1, Han | 4 (`FD FF AW DW`) | **127** | Tests: `GeneralV1CollationTests` (keys
> measured from ACE) and `GeneralV1CollationAccessTests` (live oracle, plus ACE seeking an index LibRed
> wrote in a v1 database).

- **Text:** Jet's "General" collation. The key is the start flag, then one or two
  **primary-weight** bytes per character, then a `01 00` terminator. Weights are **case-folded**
  (lowercase weighs the same as uppercase), **trailing spaces are dropped**, and an internal
  space weighs `0x07`. Most characters weigh one byte; `^ _ \` { | } ~` weigh two (sharing the
  `0x2B` page). The weight table is a fixed lookup (A=`4A`, B=`4C`, C=`4D`, …, digits step by two
  from `0x36`), **verified byte-for-byte against the ACE engine** over printable ASCII and
  implemented by `JetTextCollation` — so LibRed can now *write* ASCII text index keys (e.g. a
  string primary key). Decoding remains lossy (case is discarded — that is why a text primary key
  treats `'A'` and `'a'` as duplicates).

  **Twenty characters are "ignorable"** (so `O'Brien` sorts next to `OBrien`): they add **no
  primary weight**, but each appends an inline record to a trailing section.

  > Sixty across the BMP — twenty hand-verified below, and forty more measured into the resource (CJK and
  > fullwidth punctuation, further dashes and quotation forms).
  >
  > The hand-verified set and their codes, measured alone and inside a word so the position arithmetic is
  > confirmed rather than assumed: apostrophe `0x80`, hyphen `0x82`, soft hyphen `0x83`, `U+2010` `0x84`, `U+2011`
  > `0x85`, `U+2027` `0x86`, `U+2043` `0x87`, `U+2012` `0x88`, `U+2013` `0x89`, `U+2014` `0x8B`, `U+2015`
  > `0x8C`, and the Arabic harakat `U+064B`–`U+0650` and `U+0652` running `0xA0`–`0xA6`. **The fullwidth
  > apostrophe and hyphen share their ASCII counterparts' codes exactly** (`U+FF07` = `0x80`, `U+FF0D` =
  > `0x82`) — the one place fullwidth really does collapse onto ASCII, unlike the letters. `0x8A` is unused
  > by anything in the swept range. After the primary's
  `0x01` end marker, if any ignorable char is present the key adds `01 01 01` once, then per
  ignorable char four bytes `80 <pos> 06 <code>`, then the final `00`. `<pos> = 0x07 + 4 × (count
  of **primary weights** emitted before it)` and `<code>` is `0x80` for apostrophe / `0x82` for
  hyphen — verified against ACE (e.g. `ANNE-MARIE` → `… 80 17 06 82 …`, the hyphen at position 4;
  `Aß-B` → `7F 4A 6B 6B 4C 01 01 01 01 80 13 06 82 00`, hyphen at position **3** because ß expands to
  two weights `S`+`S`).

  > **Weights, not bytes — an earlier revision said bytes and LibRed implemented that.** The two agree for
  > everything Latin, which is why it stood so long. A two-byte weight settles it: `£-` puts the hyphen at
  > `0x0B` (`0x07 + 4×1`) although `£` is `34 A7`, and `©`, `½`, `Ω`, `б` all behave the same, while `£A-`
  > is `0x0F`. So both the secondary section and this one index by weight. Guarded by the `£-`/`Ω'A` family
  > in `LocaleCollationAccessTests` — the older samples were Latin-only and could not see it.

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

  **The long s `ſ` (U+017F) is a letter of its own**, not a fold onto `s`: it takes the two-byte primary
  `6C 06` — the S–T gap — in **every** v0 order measured, General included (`LocaleCollationAccessTests`).
  Uppercasing it invariantly gives `S`, so it has to be matched on the original character or the
  distinction is lost.

  **A few letters expand to multiple base letters** (each expanded letter weighs its normal primary,
  no accent): `ß`→`SS`, `Þ`/`þ`→`TH`, `Æ`→`AE` — verified against ACE (`ß` = `7F 6B 6B 01 00`, same as
  `SS`). Because the ignorable-position count is by primary byte, an expansion counts as its expanded
  length (above).

  > **General v0 covers the whole Basic Multilingual Plane.** Every character ACE stores a key for, LibRed
  > encodes identically.
  > The weights are in an embedded resource (`SortKeyTableV0.bin`, 74 KB): 63,105 of them, 19,186 ignorable,
  > plus 40 word-sort ignorables and 276 kana — far past anything hand-maintainable. Most of v1's table can
  > be embedded from a published Microsoft file; v0's cannot at all, since its primaries are a Jet compaction
  > rather than the NLS weights, so **ACE itself is the source**: `SortKeyTableV0GeneratorTest` inserts every
  > code point into an indexed text column, reads the stored keys back and writes the resource
  > (`LIBRED_GENERATE_V0=1`). Non-Latin scripts nearly all live on the same **two-byte `0x79` page** the
  > locale tailorings use for letters sorting after Z.
  >
  > Both generators must run with the resource they are about to replace **suppressed**
  > (`JetTextCollationV1Overrides.Suppressed`), and v1's shows why plainly: it records where the encoder
  > *disagrees* with ACE, so measuring an encoder that already consults it would find no disagreements and
  > write an empty file. Suppressing from the outset also means a generator never has to be able to *read*
  > the resource it replaces, so it bootstraps from a stale or absent one.
  >
  > Two things that only a full sweep would show. **ACE weighs every CJK ideograph and the entire private-use
  > area** — `U+5000`–`8FFF`, `B000`–`CFFF` and `E000`–`EFFF` are 4,096 for 4,096, none of it ignorable. And
  > across all 65,536 code points **ACE refused exactly one**.
  >
  > **Kana are their own mechanism.** A kana takes the two-byte primary `7F <sound>`, and the key gains a
  > section of its own — so a single kana changes the shape of the whole key:
  >
  > ```
  > U+3042 あ    7F 7F 02 01 01 01 FF 02 80 FF 80 00
  > U+30A2 ア    7F 7F 02 01 01 01 FF 02 80 FF 80 00   identical: the two scripts fold together
  > U+FF71 ｱ    7F 7F 02 01 01 01 FF 02 80 FF 80 00   as does the halfwidth form
  > U+3041 ぁ    7F 7F 02 01 01 01 A0 FF 02 80 FF 80 00   the small form adds a flag byte
  > ```
  >
  > Hiragana, katakana and halfwidth katakana share a sound, so what separates them lives in a level this
  > format truncates — the same reason case and width fold. **Voicing is an ordinary secondary**: `03`
  > dakuten, `04` handakuten, so `かが` is secondaries `02 03`.
  >
  > **The small/normal flags are bit-packed.** Trailing normal forms are dropped, then what remains goes
  > three per byte, two bits each, most significant first, under a `10` marker in the byte's top two bits —
  > `11` normal, `10` small, `00` padding. One small kana is `A0`, "normal small" is `B8`, four kana take two
  > bytes with the marker repeated. Verified over all 30 combinations up to four kana.
  >
  > Two rules that only appear in multi-character strings. The **halfwidth voicing marks `U+FF9E`/`U+FF9F`
  > are combining** — measured alone they look ignorable, but ACE folds them into the preceding kana's
  > secondary, so `ﾆﾎﾝｺﾞ` is four primaries with secondaries `02 02 02 03`. And when a kana section is
  > present the **inline word-sort section is introduced by `FF 01`** rather than `01 01 01`.
  >
  > **The prolonged sound mark lengthens the preceding kana's VOWEL**, which is exactly what the character
  > means — `がー` is `7F 0A` then `7F 02`, "ga" lengthened by "a", *not* by "ga". So the vowel is a property
  > of each kana and has to be measured per character rather than derived. `ー` also inherits the preceding
  > kana's small flag (`ぁー` packs as small+small), and marks itself in **a second packed section** using
  > the same three-per-byte scheme with its own codes — ordinary `01`, prolonged `11`:
  >
  > ```
  > あー      [01,11]        10|01 11 00 = 9C          あーー    [01,11,11]  = 9F
  > あいー    [01,01,11]     10|01 01 11 = 97          あああー  [01,01,01,11] = 95 B0
  > ```
  >
  > So the section is `01 01 <small flags> FF <prolonged flags> 02 80 FF 80`. With no kana before it, `ー` is
  > nothing special and keeps the ordinary `FF FF` primary the table holds for it.
  >
  > **Version 1 builds the kana section identically** — same sound weights, same framing, byte for byte:
  > ACE encodes `U+304C` as `7F 7F0A 01 03 0101 FF 02 80 FF 80 00` under both orders. The two versions
  > disagree about a great deal in the base table and about kana almost not at all, so `JetKanaSection` is
  > shared rather than duplicated. Two differences remain, both narrow:
  >
  > - A **compatibility form takes its base kana's sound in v1** and its own in v0. The circled katakana are
  >   the case: v1 weighs `㋐` as `ア` (`02`), where the v0 table holds `03` for it, and `46` and `2A` where
  >   v1 wants `03` and `04`. The enclosure itself rides along as the secondary, `EE`.
  > - Five kana are absent from the measured v0 table — the small hiragana ka and ke, and the katakana
  >   phonetic extensions. v1's own table classifies them under **script member 3**, whose `(AW, DW)` are the
  >   sound and voicing. The one fact it does not supply is the small flag, and that cannot be inferred from
  >   reaching that path: script member 3 also collects the circled forms, which are *not* small, and the
  >   iteration marks, the lone prolonged mark and the double hyphen, which are not kana letters at all and
  >   which ACE gives the unweighted `FF FF` primary and no kana section.
  >
  > What `02 80 FF 80` denotes is still not established; it never varies, so it is emitted as a literal.
  >
  > Three categories emerged that the Latin-1 range never showed:
  > - **Ignorable** — ACE stores *nothing at all* (key `7F 01 00`): no primary, not even a secondary slot.
  >   Romanian's comma-below `ș`/`ț` are in this class, which is why they appear to "keep General's weights":
  >   General has none for them. Ignorability held in every order measured.
  > - **Secondary-only** — a combining mark contributes a secondary and *no* primary (`7F 01 ss 00`). All of
  >   Hebrew's niqqud, the Cyrillic combining marks and three Greek ones work this way.
  > - **Locale-dependent expansion** — `Ǆ` is `D`+`Ž` (two weights, the caron on the second: `7F 4F 78 01 02
  >   14 00`), and `Ǣ` is `Æ` with a macron whose letters differ per locale — Icelandic gives it its own `Æ`
  >   at `79 04`. These are **refused** rather than approximated, since one weight where ACE uses two is
  >   silently wrong in any string with a later accent.
  >
  > **Locales share the block tables**, because measuring all 21 against General showed the departures are
  > tiny: **27 entries in total across every locale**, and most add only one or two over the entire extended
  > range (Croatian eleven, the outlier). Each is listed in that locale's tailoring, which is consulted
  > first — Lithuanian retailors fullwidth `Ｙ`, Ukrainian moves `ь`, Swedish puts wynn on `v` because it
  > makes `w` a variant of `v`. Estonian, by contrast, leaves fullwidth `Ｖ` on General's weight, so these
  > really are per-locale facts rather than a rule. `LocaleCollationAccessTests` asserts the whole range for
  > every order, so a missed departure fails the build rather than writing a silently wrong key.
  >
  > **A ligature character weighs as its decomposition**, one component at a time — there is no ligature
  > mechanism in the format at all. `Ǆ` encodes exactly as the string `DŽ` (`7F 4F 78 01 02 14 00`), `Ǉ` as
  > `LJ`, `Ǳ` as `DZ`, and `Ǣ` as `ĀĒ` — the macron landing on *both* letters, hence `01 17 17`. Upper, title
  > and lower case forms are identical, case having folded. Appending an accented letter proves the weight
  > count: `Ǆé` is `7F 4F 78 51 01 02 14 0E 00`, three primaries and three secondaries.
  >
  > Two rules make this work under a tailoring. The components are weighed **individually and never re-enter
  > the contraction matcher** — expand `Ǳ` in a Hungarian database and its `dz` digraph would fire, giving
  > `50 03` where ACE stores `4F 78`. And the decomposition sits **below** the tailoring, because some locales
  > do not decompose: Icelandic's `Ǣ` is its own `Æ` plus a secondary, and Croatian's `Ǆ` is its single-letter
  > `dž`. The components do take the locale's letters, though — Slovenian's `Ǆ` is `D` plus *Slovenian's* `ž`.
  >
  > **Coverage is complete: all 2,147 characters ACE encodes, for every one of the 23 orders, with zero
  > mismatches.** Nothing in the swept range is refused and nothing disagrees with ACE.

  **Diacritic secondary weights**, each depending only on the mark and not the base letter — derived from
  ACE by `TailoringGeneratorProbeTest`: acute `0x0E`, grave `0x0F`, **dot above `0x10`**, circumflex `0x12`,
  diaeresis `0x13`, **caron `0x14`**, **breve `0x15`**, **macron `0x17`**, tilde `0x19`, ring `0x1A`,
  **ogonek `0x1B`**, cedilla `0x1C`, **double acute `0x1D`**. Atomic letters that do not decompose carry one
  directly: `Ø`→`O`+`0x21`, `Ð`→`D`+`0x68`, **stroke** `Đ`→`D`+`0x1E`, `Ħ`→`H`+`0x1E`, `Ł`→`L`+`0x1F`,
  `Ŀ`→`L`+`0x11`, `ĸ`→`K`+`0x03`, `ŉ`→`N`+`0x48`. **Expansions**: `Æ`→`AE`, `ß`→`SS`, `Þ`→`TH`, `Ĳ`→`IJ`,
  `Œ`→`OE`. **Own primaries**: `ſ` `6C 06`, `ŋ` `63 05`, `ŧ` `6E 06`+`0x1E`, `ı` `59`+`0x03`, and
  NBSP `08 02` (against the ordinary space's `0x07`).

  **Accented Latin-1 letters** sort with their **base letter's primary weight** and record the
  accent in a **secondary section**. Each character has a secondary weight (default `0x02`); an
  accented letter carries the weight of its diacritic instead (verified against ACE, and the weight
  depends only on the accent, not the base letter): **acute `0x0E`, grave `0x0F`, circumflex `0x12`,
  diaeresis/umlaut `0x13`, tilde `0x19`, ring `0x1A`, cedilla `0x1C`**; plus atomic `Ø`→base `O`+`0x21`,
  `Ð`→base `D`+`0x68`, and the ligature `Æ`→primaries `A E` (no accent).

  > **The secondary section has one entry per primary *weight*, not per primary *byte*.** A weight may be
  > one byte or two, and a two-byte weight still takes a single slot — Norwegian `ö` is
  > `7F 79 06 01 13 00`: two primary bytes, one secondary. This only becomes visible once two-byte primaries
  > and accents appear together, which is why it surfaced with the locale tailorings
  > (`Ångström` in Norwegian, where `å` and `ö` are both two-byte). The **inline** apostrophe/hyphen section
  > below counts weights as well, so both sections index the same way. An expansion is several *weights*
  > (`ß`→`SS` is two one-byte weights), so it takes two slots.

  The section is emitted only when some character is accented: after the primary's `0x01` end marker it
  lists the secondary weight of **every weight from the first up to and including the last accented one**,
  e.g. `México D.F.` →
  `7F 60 51 75 59 4D 64 07 4F 1C 53 1C 01 02 0E 00` (é = primary `0x51` = E, secondary `0x0E`), and
  `Montréal` (é at position 5) → `… 01 02 02 02 02 02 0E 00`. LibRed decomposes via Unicode NFD (base
  letter + combining mark) plus the small atomic table above; `JetTextCollation` reproduces these keys
  **byte-for-byte vs ACE** (México/Montréal/München/São Paulo/Résumé and single accents).

  **Descending** text keys are the **bitwise inverse of the ascending key, with a `0x00`
  appended** — verified against ACE (e.g. ascending `A` = `7F 4A 01 00` → descending
  `80 B5 FE FF 00`). The inverted start flag is `~0x7F = 0x80`, matching the descending flag of
  the fixed-type keys.

  **Locale-specific orders.** A database can be created with a sort order other than General; Access exposes
  them as the "New Database Sort Order" list. Verified against **29 Access-authored fixtures — every non-CJK
  entry in that list**, in `Data/`, each diffed against General v0 by having ACE encode the same 193 samples
  and reading the stored keys back (`LocaleFixtureCollationProbeTest`, plus `DaoLocaleCollationProbeTest` for
  orders only DAO can name):

  - The stored value is a **true LCID**, not a small enum — Spanish Traditional is `1034` (`0x040A`) and
    Spanish **Modern** is `3082` (`0x0C0A`). DAO's `CollatingOrderEnum` lists only `dbSortSpanish = 1034`;
    the Modern order postdates it and has no DAO name. Both files are **sort-order version `0`**, so the
    version is **orthogonal to the locale** — though in practice few locales have both generations. Access's
    "New Database Sort Order" list names a legacy order separately (`General - Legacy`, `Romanian - Legacy`,
    `Croatian - Legacy`, `Japanese - Legacy`), and **neither Spanish order has a `- Legacy` twin**: a second
    generation exists only where the Windows tailoring actually changed.

  - **Version 1 is not a General-only thing.** Five of the fixtures stamp version `1` — Bosnian, Croatian,
    Indic, Romanian, Serbian — and all encode with **2-byte NLS primaries** exactly as General v1 does
    (`a` = `7F 0E 02 01 00`, `c` = `7F 0E 0A 01 00`, `d` = `7F 0E 1A 01 00`). Every one of 193 samples differs
    from General v0, because the whole key shape changes rather than individual letters moving. Croatian and
    Romanian are the ones Access offers in both generations, and their `- Legacy` twins are ordinary v0 files
    with the same LANGID; Bosnian, Indic and Serbian have no legacy twin at all.

  - **The whole four-byte field is one 32-bit LCID** (§3.4). Several entries in Access's list are Windows
    *alternate sort orders*, which live in the LCID's high word and share their LANGID with the base locale:

    | fixture | raw `0x6E`..`0x71` | LANGID | sort id | version | LCID |
    |---|---|---|---|---|---|
    | `German Phone Book` | `07 04 01 00` | 1031 | `01` | 0 | `0x00010407` |
    | `Hungarian Technical` | `0E 04 01 00` | 1038 | `01` | 0 | `0x0001040E` |
    | `Hungarian` | `0E 04 00 00` | 1038 | `00` | 0 | `0x0000040E` |
    | `Georgian Modern` | `37 04 01 00` | 1079 | `01` | 0 | `0x00010437` |
    | `Croatian` / `Croatian - Legacy` | `1A 04 00 01` / `1A 04 00 00` | 1050 | `00` | 1 / 0 | `0x0000041A` |

    Hungarian and Hungarian Technical differ **only** in the sort id, so an implementation that reads the
    LANGID alone cannot tell them apart — LibRed could not, until these fixtures.

  - **German Phone Book is an expansion, not an insertion**: `ä` = `7F 4A 51 01 00`, i.e. primaries `a` + `e`
    (General has `7F 4A 01 13 00`, `a` + umlaut secondary); likewise `ö` → `o`+`e` and `ü` → `u`+`e`. It uses
    the same primitive as `ß`→`SS` above, so it needs no new machinery.
  - The **framing is unchanged**: start flag, primaries, `0x01`, secondaries, `0x00`. Only the weights move.

  - **A language letter takes a two-byte primary — a free value from the letter table, plus a sub-position.**
    The General letter table steps by +2 almost everywhere (only `B→C`, `Q→R` and `X→Y` are consecutive), and
    tailorings land in those gaps, always in the linguistically correct place:

    | locale | letter | key | slot sits between |
    |---|---|---|---|
    | Spanish Traditional | `ch` | `7F 4E 04 01 00` | C `4D`, D `4F` |
    | Spanish Traditional | `ll` | `7F 5F 04 01 00` | L `5E`, M `60` |
    | Spanish (both) | `ñ` | `7F 63 04 01 00` | N `62`, O `64` — General has `7F 62 01 19 00`, `n` + tilde |
    | Czech / Slovak | `ch` | `7F 58 03 01 00` | H `57`, I `59` — Czech sorts `ch` after `h`, not after `c` |
    | Turkish | `ı` | `7F 58 06 01 00` | H `57`, I `59` — dotless `ı` before `i` |
    | Lithuanian | `y` | `7F 5A 02 01 00` | I `59`, J `5B` — Lithuanian `y` follows `į` |
    | Estonian | `z` | `7F 6C 07 01 00` | S `6B`, T `6D` — Estonian `z` sits between `s` and `t` |
    | Croatian Legacy | `lj` / `nj` | `7F 5F 03 01 00` / `7F 63 04 01 00` | L–M, N–O |

    **The second byte orders letters that share a slot.** That is settled by the locales which put several
    into one: Hungarian Technical fits five in the A–B gap `0x4B` — `á` `02`, `â` `03`, `ä` `04`, `ă` `05`,
    `ą` `06` — and three in `0x4E` (`ç` `02`, `ć` `03`, `č` `04`). Estonian's `0x6C` holds `š` `06`, `z` `07`,
    `ž` `08`, in exactly Estonian alphabet order. Swedish/Finnish and Norwegian/Danish both stack their three
    extra vowels after Z: `å` `05` / `ä` `07` / `ö` `08` for Swedish, `æ` `04` / `ø` `06` / `å` `09` for
    Norwegian — each language's own order. (An earlier revision here said cross-locale disagreement ruled a
    sub-position out. It does not: it only ruled out a *fixed marker*. The values are per-locale ordinals.)
    How a specific value is chosen is still unknown — they are ordered but not dense, and Latvian uses `0x12`
    for `ķ` and `0x0C` for `ņ`.

  - **The after-Z letters use the `0x79` page**, which is where General already keeps Greek and Cyrillic:
    Czech `ž` = `79 05`, Polish `ż` = `79 04`, Icelandic `þ` `03` / `æ` `04` / `ö` `05`. Same two-byte
    primary + sub-position shape.

  - **Tailoring is not only insertion.** Five other devices appear, all within the existing framing:

    - **Contraction** — two characters, one primary. The Spanish and Croatian digraphs above; Hungarian's
      full set (`cs` `4E 05`, `gy` `56 03`, `ny` `63 06`, `sz` `6C 08`, `zs` `79 09`, `ty` `6E 06`).

      Matching is **greedy longest-first**, left to right, and does not backtrack: Hungarian `dzs` is the
      three-character letter `50 05`, not `dz`+`s`; Spanish `lll` is `ll`+`l` (`5F 04 5E`) and `llll` is
      `ll`+`ll`. **Only Hungarian doubles** — a doubled digraph is written by doubling its first letter, so
      `ggy` is `gy`+`gy` (`56 03 56 03`) and `ssz` is `sz`+`sz`, while Czech `cch` is plainly `c`+`ch`
      (`4D 58 03`) and Spanish `cch` is `c`+`ch`. Doubling has to be tested *before* the plain longest match,
      or `ggy` degrades to `g`+`gy`. A contraction can carry a secondary of its own: Croatian `dž` is
      `50 04` with secondary `04`, Danish `aa` is `å`'s primary with secondary `03`.
    - **Expansion** — one character, several primaries. German Phone Book: `ä` = `7F 4A 51 01 00`, primaries
      `a`+`e` (General has `a` + umlaut secondary); likewise `ö`→`o`+`e`, `ü`→`u`+`e`. Same primitive as
      `ß`→`SS` above, so it needs no new machinery.
    - **Secondary retune** — a letter stays on a base primary but changes its *secondary*. Swedish/Finnish
      makes `w` a variant of `v` (`7F 71 01 03 00`, `v`'s primary plus secondary `03`) and `ü` a variant of
      `y`; Estonian does the same for `w`. Danish folds `aa` onto `å` (`79 09 01 03`). Lithuanian leaves its
      ogonek letters as secondaries but *changes the weight* from `0x1B` to `0x0F`. Slovak and Croatian Legacy
      go the other way and **demote** letters General gives distinct diacritics.
    - **Remapping the base table.** Estonian is the extreme: `v` moves to `70 03`, and `õ` and `ö` take over
      the *bare* one-byte primaries `0x71` and `0x73` that General uses for `v` and `w`. So a locale can
      rewrite the base alphabet, not merely extend it.
    - **Reordering.** Thai is the only order here that changes a *sequence* rather than weights: `เ` and `ก`
      each match General on their own, but the pair `เก` is `7F 7C 99 01 03 00` against General's
      `7F 7C 93 7C 98 01 03 03 00` — the leading vowel, written before the consonant it follows
      phonetically, is folded with it.

  - Promotion is selective, and non-promoted letters keep the ordinary base-plus-secondary form: `ż` is a
    letter in Polish (`7F 79 04 01 00`) but merely `z` + accent in Czech (`7F 78 01 04 00`). **Turkish**
    resolves case in the tailoring rather than by folding: `ı` takes its own slot, and `İ` collapses onto
    plain `i` (`7F 59 01 00`, no secondary at all) where General gives it a secondary `0x10`.

  - **Every order is General plus a small tailoring** — including the version-1 ones. Compared against the
    General order of **its own version** (the v1 baseline is a database LibRed creates with
    `Collation.General`, which ACE then encodes into), no order departs in more than 47 of 193 samples, and
    a version-1 order is *not* a wholesale reweighting — it only looked like one against a v0 baseline,
    because the key shape changes. `LocaleFixtureCollationProbeTest` reports both.

    | departure | orders |
    |---|---|
    | 47 | Hungarian Technical |
    | 14–16 | Bosnian, Croatian, Croatian Legacy, Estonian, Serbian (16), Slovak (15), Czech, Hungarian (14) |
    | 6–11 | Icelandic (11), Lithuanian, Norwegian/Danish, Polish, Vietnamese (9), Latvian, Slovenian, Swedish/Finnish (8), Romanian (7), Turkish (6) |
    | 1–4 | Romanian Legacy (4), German Phone Book, Spanish Traditional (3), Macedonian (2), French, Spanish Modern, Thai, Ukrainian (1) |
    | **0 — indistinguishable from General** | **Georgian Modern**, **Indic** |

    Croatian, Bosnian and Serbian depart in the same 16 as Croatian Legacy — the same letter set tailored in
    both generations, so a locale's *character list* is version-independent even though its weights are not.

    Two caveats on reading this as effort. 193 samples are a sample, not an alphabet: `0 differ` means
    *indistinguishable over these*, and a real implementation needs a fuller sweep per locale. And **French
    is under-measured** — its one difference is in the *secondary section*, consistent with French ordering
    accents from the end of the word, which single-character samples cannot exercise.

  - **Some orders are recorded but unimplemented — including one Access itself lists.** `Arabic` (1025),
    `Greek` (1032), `Hebrew` (1037), `Dutch` (1043) and `Cyrillic` (1049) are created happily by DAO, land on
    page 0 with the right LCID, get stamped onto the columns ACE itself creates, and ACE opens and runs DDL
    against them — yet the keys are **byte-identical to General across 57 samples**, chosen to include what a
    tailoring would actually move (Greek tonos and final sigma, Cyrillic `ё`/`й`/`ь`/`ъ`, Hebrew final forms,
    Arabic hamza forms, the `ĳ` ligature). Access's list offers none of those five. But `Georgian Modern`
    **is** in the list, carries sort id `0x01`, and is likewise indistinguishable from General over 193
    samples — so appearing in the UI does not imply an implementation, and the sort id can be recorded for an
    order that does nothing.

  - **DAO can author a locale order**, even though it cannot author a sort-order *version*
    (`DaoDatabaseCreationProbeTest`). A DAO-created `LANGID=0x040A` database reproduces the Access-authored
    `SpanishTraditional.accdb` keys byte-for-byte, so locale fixtures need no manual Access step.
  - `ñ` is a **letter in both Spanish orders** and an accented `n` in General — so **Modern = General plus
    that one letter**, and **Traditional = Modern plus the two digraphs**. Every other sample encodes
    byte-identically across all three orders.
  - Traditional's digraphs are a **contraction**: two characters producing one primary, the inverse of the
    expansions above. `chico` is `7F 4E 04 59 4D 64 01 00` — five characters, four primaries. Case folds as
    usual, so `ch`, `Ch` and `CH` share a key.

  **LibRed implements the tailorings whose every difference is a single character** — `JetLocaleTailoring`,
  a per-locale `char` → primaries override consulted ahead of the General tables, looked up by the *original*
  character before the uppercased one (which is what lets Turkish disagree with invariant casing, where `I`
  is the dotless letter). Implemented and asserted byte-for-byte against ACE over 345 values — the whole of
  printable ASCII, Latin-1 and Latin Extended-A, plus words (`LocaleCollationAccessTests`):

  | order | tailoring |
  |---|---|
  | Spanish Modern | `ñ` |
  | Spanish Traditional | `ñ`, and the digraphs `ch` `ll` |
  | German Phone Book | `ä ö ü` as expansions |
  | Romanian Legacy | `ă î ş ţ` |
  | Turkish | `ç ğ ö ş ü`, `ı`/`I` dotless and `İ`/`i` dotted, and the `ĳ` ligature following that casing |
  | Polish | `ą ć ę ł ń ó ś ź ż` |
  | Czech | the digraph `ch`, four letters, and twelve accent retunes (the diaeresis moves `0x13`→`0x05`) |
  | Slovak | Czech's `ch`, plus `ä ô` of its own |
  | Croatian Legacy | the digraphs `lj nj dž`, five letters, twelve accent retunes |
  | Slovenian | Croatian's letters at different sub-positions |
  | Norwegian/Danish | `æ ø å`, the contraction `aa`→`å`, and `ä ö ü ő ű` riding on them |
  | Swedish/Finnish | `å ä ö`, `w` as a variant of `v`, `ü` on `y` |
  | Icelandic | ten letters; `þ æ ö` close the alphabet after `z` |
  | Estonian | rewrites the base alphabet — see above |
  | Latvian | seven letters, the widest sub-positions seen (`ķ` at `0x12`) |
  | Lithuanian | `y` after `i`, and the ogonek retuned `0x1B`→`0x0F` |
  | Vietnamese | nine digraphs, and `p r` shifted to make room |
  | Hungarian | the nine digraphs, doubling, and `ö ü ő ű` |
  | Hungarian Technical | 46 individual letters and **no digraphs at all** |
  | Ukrainian | `ь` — one entry |
  | Macedonian | `ѓ ќ` |
  | Georgian Modern, Indic | *empty* — measured to be indistinguishable from General |

  An **empty** tailoring is meaningful and different from none: it says the order was measured to need no
  change, so the order can be encoded rather than refused.

  > **"Technical" is not a variant of the digraph order.** Hungarian Technical tailors plain `g` to `56 03`,
  > so its `gy` is that tailored `g` followed by an ordinary `y` — not a contraction. It is the largest
  > single-character tailoring measured and contains no multi-character entry.

  > **A single-character sweep cannot find a digraph.** Vietnamese looked like a single-character order until
  > `Ångström` came out three weights short: `ng` and `tr` each weigh as one letter. Its set is
  > `ch gi kh ng nh ph qu th tr` — and note `gh` and `ngh` are *not* letters, they fall out of greedy
  > matching as `g`+`h` and `ng`+`h`, which is exactly what ACE stores.

  Everything else stays refused — `Collation.IsIndexKeyEncodable` gates on it, because a wrong key is silent.
  What remains: **Thai** needs reordering, and **Bosnian, Croatian and Serbian at version 1** need the v1
  encoder to grow a tailoring hook (its primaries are 2-byte NLS values, a different shape). **French** is
  unclassified, its tailoring being in the secondary section where single-character samples do not exercise
  it — its one measured difference is on `Ǆ`, which is refused anyway.

  *Not yet handled:* characters outside ASCII + the accented Latin-1 set above (and a key mixing an
  accent with an ignorable apostrophe/hyphen is untested); every locale other than General (above).
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
- **ACE additionally splits at the RIGHT EDGE, and LibRed does not.** When the incoming key is the highest
  on the page, ACE leaves that page full and starts a new one with the new entry alone, instead of halving
  it. Nothing sorts below a maximum key, so a middle split there strands half a page for ever. Measured on
  1500 rows through both engines (leaf free space, sorted):

  | inserted | ACE | LibRed |
  | --- | --- | --- |
  | ascending | 3 leaves — `1, 1, 952` | 4 leaves — `31, 1807, 1807, 1807` |
  | descending | 4 — `31, 1807, 1807, 1807` | 4 — `49, 1801, 1801, 1801` |
  | random | 4 — `1267, 1369, 1405, 1411` | 4 — `1291, 1357, 1387, 1417` |
  | gapped, then backfilled ascending (3000 rows) | 5 — `1, 1, 1, 7, 55` | 6 — `1, 1, 7, 73, 1789, 1807` |

  Three things follow. The optimisation is **right-edge only** — descending inserts get an ordinary middle
  split from ACE too, and the two engines then agree. On random keys both settle near two-thirds full, the
  classic B-tree equilibrium, so **LibRed's middle split matches ACE's**; the gap is exclusively the missing
  special case. And it appears to cost nothing: the obvious objection — that a page packed to capacity must
  split as soon as anything lands in its range — did not show up, because an ascending backfill keeps
  meeting the right edge of a subtree. (A *random* backfill into pre-packed pages has not been measured.)

  Ascending keys are the ordinary case, since AutoNumber and identity keys are ascending by construction, so
  LibRed spends roughly 1.8x the index pages on the commonest shape. Correct either way — ACE seeks through
  LibRed's tree — and `IndexSplitPackingAccessTests` holds the assertion plus the ACE-reads-it check.
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


> **Indexable types — coverage vs ACE (§10.4).** `IndexKeyEncoder` now encodes **every type ACE lets you
> index**, all byte-verified: Boolean, Byte, Int16, Int32, Currency, Single, Double, DateTime, Text, GUID,
> Binary, FixedPoint, Memo (its first 255 chars), **`Int64`/BIGINT** (`0x13`) and
> **`DateTimeExtended`/DATETIME2** (`0x14`). ACE correctly **refuses** to index `OLE` (`0x0B`) and `Complex`
> (`0x12`).
>
> `Int64`/BIGINT keys exactly as Currency does — an int64, sign bit flipped, big-endian — which had long been
> the guess on record and is now measured across `0`, `±1`, `±42` and both extremes, ascending and descending
> (`BigIntKeyEncodingTests`). Note its **variable-length storage does not change this**: the key dispatch is on
> the column's type, not on where the row keeps the bytes. `IndexKeyDecoder` decodes it too, unlike DATETIME2 —
> it is a plain fixed-width numeric key.
>
> `DateTimeExtended` is **not** a fixed-width numeric key. ACE runs its whole 42-byte stored value through the
> Binary chunking above — start flag, 8 bytes, `0x09` while more follow, then the final chunk and its
> real-byte count — instead of folding it to a number the way `DateTime` folds to its OA double. That works
> because the stored encoding is already order-preserving (both fields zero-padded to 19 digits), and it means
> the value's trailing NUL is part of the key ([data-types](data-types.md)). Descending inverts every byte
> except the `0x09` markers, exactly as for Binary. Verified both directions in `DateTime2KeyEncodingTests`.
> `IndexKeyDecoder` does not decode it, for the same reason it does not decode Binary or Text: the chunked
> form stops the in-place walk, and the caller falls back to reading the row.
