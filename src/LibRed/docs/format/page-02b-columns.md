# TDEF: columns and column maintenance

> Part of the [LibRed Jet / ACE file-format reference](README.md). Cross-references use the original **§-numbers**; the [section map](README.md#section-map) says which file each lives in.

### 3.4 Column descriptor (25 bytes)

| Offset | Size | Meaning |
| --- | --- | --- |
| `0x00` | 1 | Data type (see §6) |
| `0x01` | 2 | Record marker `0x0659` (see §3.1 note); ignored |
| `0x03` | 2 | Unknown (zero observed) |
| `0x05` | 2 | Column id |
| `0x07` | 2 | Variable-length table index — this column's position among the variable columns (0 for fixed columns) |
| `0x09` | 2 | Column number (equals the column id `0x05` in a freshly written descriptor — but **diverges after an `ALTER COLUMN` type change**, which burns a new id into `0x05` yet leaves `0x09` at the *old* id; see §3.8) |
| `0x0B` | 1 | Numeric **precision** (Decimal/Numeric columns); otherwise the low byte of the locale id, `0x09` |
| `0x0C` | 1 | Numeric **scale** (Decimal/Numeric columns); otherwise the high byte of the locale id, `0x04` |
| `0x0D` | 2 | Text sort-order **version** (mdbtools `misc_ext`; Jackcess's sort-order version) — the high half of a 4-byte sort-order descriptor whose low half is the locale at `0x0B` (`0x0409` = General, §10.4). `0` for **every** column (all types) in ACE 2007 — the General collation is version 0; see note |
| `0x0F` | 1 | Flags (see below) |
| `0x10` | 1 | Extended flags: `0x01` compressed-Unicode capable, `0xC0` calculated column |
| `0x11` | 4 | Unknown (zero observed) |
| `0x15` | 2 | Fixed-data offset within the row's fixed region |
| `0x17` | 2 | Length (bytes) |

**Flags (`0x0F`):** `0x01` fixed-length, `0x02` updatable, `0x04` auto-number,
`0x40` auto-number GUID, `0x80` hyperlink (on a Memo column).

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
> **Sort-order version (`0x0D`) — confirmed.** For non-Numeric columns, `0x0B`–`0x0E` form a 4-byte
> **sort-order descriptor**: locale `0x0409` (1033) at `0x0B`, **version** at `0x0D`. mdbtools states
> it explicitly (note after its index-record section): the encoding is the **"General" sort order in
> Access 2000–2007 = (1033, version 0)**; as of Access 2010 that is renamed **"General legacy"** and
> the new default **"General" = (1033, version 1)** is a *different* key encoding. So `0x0D` selects
> which collation the text keys use.
>
> Observed `0` on **every** column of ACE-2007 Northwind, *and* on a text column freshly created by
> the installed ACE 16 engine — because the file is ACE-2007 format (version byte `2`), the engine
> writes the version-0 "General legacy" order regardless of its own age. LibRed's `JetTextCollation`
> (§10.4) implements exactly this **version-0** order and its writer leaves `0x0D` at 0 — correct for
> ACE-2007 files. A **version-1** file (Access 2010+, version byte ≥ `3`, created with the default
> General order) would carry `0x0D = 1` and different index-key bytes that LibRed does not yet handle
> (no version-1 fixture available here to verify against).
>
> **LibRed model.** The `(locale, version)` pair is a `Collation` value (`CollatingOrder` enum = the DAO
> LCIDs). It is **read** per column into `ColumnDef.Collation` (numeric columns, whose `0x0B/0x0C` are
> precision/scale, carry none), and **written** from `JetDatabase.Collation` — the database's default,
> threaded through `TdefBuilder` instead of the former hardcoded `0x0409`/`0x00` constant. The write is
> byte-identical to the constant for General legacy (verified). `IndexKeyEncoder` **gates** on the
> column's collation: it refuses (throws) anything but General legacy rather than emit version-0 key bytes
> for a version-1 or non-English column. `JetDatabase.Collation` currently defaults to General legacy;
> populating it from the page-0 sort order needs that region's de-obfuscation (still a TODO, §2).
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

