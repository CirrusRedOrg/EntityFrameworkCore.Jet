# TDEF: columns and column maintenance

> Part of the [LibRed Jet / ACE file-format reference](README.md). Cross-references use the original **§-numbers**; the [section map](README.md#section-map) says which file each lives in.

### 3.4 Column descriptor (25 bytes)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Data type (see §6) |
| `0x01` | 2 | Record marker `0x0659` (see §3.1 note); ignored |
| `0x03` | 2 | Unknown (zero observed) |
| `0x05` | 2 | Column id |
| `0x07` | 2 | Variable-table index — the count of variable-length columns whose column id is smaller than this one's. For a **variable** column this equals its own position in the variable-offset table; for a **fixed** column it is the running count of preceding variable columns (**not** `0`). Access stores it on every column and its strict reader relies on it — writing `0` on fixed columns yields a file Access rejects with *"record(s) cannot be read"* even though LibRed/OLE DB (which recompute the index) tolerate it. Verified byte-for-byte against `MSysObjects`/`MSysACEs` in a real DAO file. |
| `0x09` | 2 | Column number (equals the column id `0x05` in a freshly written descriptor — but **diverges after an `ALTER COLUMN` type change**, which burns a new id into `0x05` yet leaves `0x09` at the *old* id; see §3.8) |
| `0x0B` | 1 | Numeric **precision** (Decimal/Numeric columns); otherwise the low byte of the locale id, `0x09` |
| `0x0C` | 1 | Numeric **scale** (Decimal/Numeric columns); otherwise the high byte of the locale id, `0x04` |
| `0x0D` | 2 | Text sort-order **version** — a 2-byte field (the high half of a 4-byte sort-order descriptor whose low half is the locale at `0x0B`, `0x0409` = General, §10.4). The version *number* is the **high byte at `0x0E`**: `0` = General Legacy (Access 2000–2007), `1` = the "General" order Access 2010+ made default (a different key encoding). The **low byte `0x0D` is `0` in every file observed** and isn't modelled — but the field is nominally 2 bytes, so keep an eye on it (see note). |
| `0x0F` | 1 | Flags (see below) |
| `0x10` | 1 | Extended flags: `0x01` compressed-Unicode capable, `0xC0` calculated column |
| `0x11` | 4 | Unknown (zero observed) |
| `0x15` | 2 | Fixed-data offset within the row's fixed region |
| `0x17` | 2 | Length (bytes) |

**Flags (`0x0F`):** `0x01` fixed-length, `0x02` updatable, `0x04` auto-number,
`0x40` auto-number GUID, `0x80` hyperlink (on a Memo column).

> **Every documented flag is modelled — nothing rides through raw except the reserved/unknown.** LibRed reads
> each `0x0F` bit and the whole `0x10` byte into `ColumnDef` (`IsUpdatable`/`IsGuidAutoNumber`/`IsHyperlink`,
> `SupportsCompressedUnicode`/`IsCalculated`) and composes them back on write, so they round-trip explicitly.
> The only bytes preserved verbatim through `ColumnDef.RawDescriptor` are the genuinely reserved/unknown ones:
> the reserved words at `0x03` and `0x11`, and any *undocumented* bits of `0x0F`/`0x10` (zero in every file
> observed). `ColumnDescriptorFlagTests`.

> **Nullability, defaults and checks are *not* in the descriptor.** The column's *Required* (NOT NULL)
> property is **not** encoded anywhere in the 25-byte descriptor — verified against Northwind: a nullable
> column (`Orders.ShippedDate`) and a non-null column of the same type (`Orders.OrderDate`) have
> **byte-identical** descriptors; the flag byte `0x0F` only ever distinguishes fixed-length
> (`0x01`), the always-set updatable bit (`0x02`), and auto-number (`0x04`), while the extended
> flags (`0x10`) and reserved bytes (`0x03`, `0x0D`) are zero for every column. `Required`,
> `DefaultValue`, and `CheckConstraints` instead live in the table's **column-properties blob** (Jet's
> per-object extended properties, a.k.a. `LvProp`, stored in the `MSysObjects` row). LibRed reads and
> writes all three, byte-for-byte vs ACE — the **on-disk `LvProp` format** is documented in
> [system-catalog.md](system-catalog.md), and the **`DEFAULT` expression semantics** (what a default may
> contain, how it is evaluated) in [page-02c-default-values.md](page-02c-default-values.md).

> `0x0B`–`0x0C` is a union keyed by type: for a Decimal/Numeric column (type `0x10`) it holds
> the **precision** (`0x0B`) and **scale** (`0x0C`) — verified with a `DECIMAL(12,3)` column,
> which reads precision = 12, scale = 3; for every other type it reads the constant `0x0409`
> (the en-US LCID / text collation).
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
> **nothing else** — verified from Access-authored fixtures in `LocaleFixtureCollationProbeTest`, where the
> whole four-byte field is printed raw and reconciled against the parse.
>
> > This byte was documented here for a long time as *"`0` in every file observed… if a database ever carries
> > a non-zero `0x0D`, we are truncating a wider value"*. That is exactly what happened, and until the
> > fixtures arrived LibRed read Hungarian Technical as plain Hungarian. Kept as a note because the warning
> > paid for itself: the same reasoning applies to any field we observe as constant-zero.
>
> **Verified** against databases authored with each order (`México`/`O'Brien`/`a`/`A` fixtures): a
> v1 text column has `0x0E = 01` and produces index keys unlike the v0 encoder. (Reading the *version* was
> once a real bug too: LibRed read the byte at `0x0D` — `0` in both General orders — and so reported every
> database as v0.)
>
> **The collation is stored in two places, and they agree.** The `(LCID, version)` sort order lives
> *both* per column (here: locale at `0x0B`–`0x0C`, version at `0x0E`) *and* database-wide in the
> obfuscated page-0 header (LCID at `0x6E`, version byte at `0x71`; see
> [page-00-database.md §2.1](page-00-database.md#21-the-obfuscated-header-0x180x98)). For a given
> database the per-column `0x0E` equals the page-0 `0x71` (verified across seven files). Changing the
> default **language** moves the LCID in both places; flipping **General vs General Legacy** moves the
> version byte in both places.
>
> (An earlier revision claimed page 0 held *no* sort-order value — that was inferred from a v0/v1 diff
> whose obfuscated `0x71` looked like creation-date noise. De-obfuscating the header with the fixed mask
> showed the version is there too.)
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
> is byte-identical for General legacy (verified). `IndexKeyEncoder` **gates** on the collation: it refuses (throws) anything but General legacy
> rather than emit v0 key bytes for a v1 or non-English column.
> LibRed reads and distinguishes v1, but does not yet **encode** its index keys (the v1 weight table is the
> remaining work, §10.4).
>
> LibRed **reads** the variable-table index from the descriptor (`0x07`) rather than deriving it by
> ranking column ids. For an untouched table the two agree, but they **diverge after a `DROP COLUMN`**:
> ACE's drop is a metadata-only TDEF edit that does **not** renumber the surviving columns or rewrite
> existing rows, so a survivor keeps its original variable index even though ranking would shift it down
> into the gap. Deriving would then decode the wrong variable slot (verified: after dropping a middle
> Text column, the next Text column read the dropped column's value); reading `0x07` decodes correctly.

Variable-length columns carry a *variable index* — their position in the row's variable-offset table
(§5), stored in the descriptor at `0x07`. For an untouched table this equals their rank among variable
columns ordered by ascending column id, but a `DROP COLUMN` can leave a gap (see the note above).


### 3.8 In-place column type/length change (`ALTER COLUMN`) — verified byte-for-byte

Changing a column's **type or length** does **not** edit that column in place. Access **makes a brand-new
column that keeps the old one's ordinal position but takes a fresh id**, copies + converts the data into
it, and **leaves the old column's storage as dead space** (it is *not* compacted away). Verified by
diffing whole files before/after `ALTER COLUMN` over the ACE OLE DB provider; LibRed reproduces every byte
(`AceModifyByteDiffProbe.Libred_in_place_modify_matches_ace_whole_file`, a theory over fixed / variable /
fixed↔variable / PK / indexed / multi-page / decimal shapes, plus a 20-column 7-step non-sequential stress
run). This is **the same mechanism for every type/length change** — including a *widening* `TEXT(n)→TEXT(m)`;
there is no cheap "just bump the length" path, ACE burns the id there too.

> **Relationship columns cannot be altered.** ACE rejects a type or length change when the target is either
> a referencing FK column or its referenced parent column: *"Cannot change field 'X'. It is part of one or
> more relationships."* This is verified for both sides. LibRed performs this check before choosing an
> in-place edit or logical rebuild, so no descriptor, row, or index page is changed on rejection.

**TDEF header:** the max-column-id high-water (`0x29`, §3.1) bumps **+1** (this is the burned id). For a
change **to a variable type**, the variable-column count (`0x2B`) also bumps **+1**. Every field burn is
permanent: repeated modifies keep consuming ids from `0x29`, which is why a heavily-altered table can hit
"Too many fields defined" with far fewer than 255 *live* columns (only a compact renumbers).

**Target column descriptor (§3.4)** — the *only* descriptor that changes; all others stay byte-identical:

| Offset | New value |
| --- | --- |
| `0x00` | new data type |
| `0x05` | **burned id** = the old `0x29` high-water (so the id ≥ every existing id; position is unchanged) |
| `0x07` | variable-table index = the **old** variable-column count (the next free var slot) — set for **both** a fixed and a variable retype |
| `0x09` | **left unchanged** — ACE does *not* update the duplicate id here (it keeps the *old* id), a deliberate quirk |
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
and null bitmap are then rebuilt per §5 (count and bitmap width = max id + 1, dead ids' bits set present).

**Indexed target — full index rebuild.** When the modified column is in an index, ACE reconstructs that
index (its keys change type). Verified reproduction:

- Allocate a **fresh empty root leaf** (an appended page); the old root is freed **last** (so the new root
  gets the appended page, not the recycled old one).
- **Re-point the index-data block** (§3.5) in the TDEF: the target's **burned id** replaces the old id in
  its column slot (`0x04` array), the **new root** at `0x26`, the **new usage-map row** at `0x22`, and the
  index **stats block** (§3.3.1) first word bumped **0→1**.
- **Recycle the owned-pages usage-map row** the way ACE does (§9): *append* a fresh row and set the new
  root's bit, then **move** that map into the old row's freed slot and soft-delete the old row as a
  0-length deleted+overflow tombstone — leaving the appended slot's bytes **stale in free space**, a
  deterministic ACE artifact reproduced byte-for-byte.
- **Back-fill** the new B-tree with new-type keys (one `AddEntry` per row).

The descriptor edit and the index-block re-point are applied to **one** parsed TDEF and written **once**.

**Unmatchable environmental diffs** (not faithfulness gaps): the **page-0 modification counter** and
**MSysObjects.DateUpdate** (the table's last-modified wall-clock timestamp) — no writer can match a clock.

