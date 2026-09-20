# Page type `0x08` — a released table-definition page

A TDEF page that `DROP TABLE` has given back. Access marks it by **setting this one byte and changing
nothing else**: the dropped table's definition — header, column descriptors, names, index blocks — stays on
the page exactly as it was until a Compact reclaims it.

> Measured across an ACE `DROP TABLE`: **exactly one byte of the TDEF page's 4,096 changes**, offset `0x000`,
> `0x02` → `0x08`.

That is what the `0x08` pages in real-world files are, and it is why they read as *structured* rather than
blank — a reader walking raw pages finds a whole table definition sitting behind a type byte that says the
page is no longer in use. It is also the reason a released TDEF cannot be mistaken for a live one by type
alone.

The other pages a drop frees — data pages, long-value pages, usage-map holders — **keep their original type
bytes**; only the definition page is marked. A definition that runs onto continuation pages has only its first
page marked: the continuations are freed with every byte, their `0x02` type included, left as it was. (A long-value page released for a different reason does get its
own marker, [`0x09`](page-09-released-long-value.md), but not as part of a drop.)

## Reading

**Nothing needs to handle it.** Allocation selects on the global free map, not on this byte, so a released
TDEF is handed out and overwritten like any other free page. LibRed names it
`PageType.ReleasedTableDefinition` so that a page walk can report it, and treats it no further.

## Writing

LibRed sets the same marker in `TableCreator.DropTable`, as the last step of giving the table's pages back
(the owned/free maps, the per-column long-value maps, and the map-holder rows are covered in
[page-05 §9](page-05-usage-maps.md)).

> An ACE drop and a LibRed drop of the same table leave the file **byte-identical** except for the three
> catalog **index root pages** — identical entries in identical order, but LibRed compacts a leaf harder than
> ACE does after removing entries ([page-03-04 §10.4a](page-03-04-index-btree.md)) — and page 0, by the one
> byte of the opening user's commit slot at `0xE02` ([page-00 §2.2](page-00-database.md)), which moves for any
> write at all.

## Related

- [page-02a](page-02a-tdef.md) — what the page held while it was live.
- [page-05 §9](page-05-usage-maps.md) — the rest of what a drop gives back.
- [page-09](page-09-released-long-value.md) — the other released-page marker, from a different mechanism.
