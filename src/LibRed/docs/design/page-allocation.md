# Page allocation: the measurement record

*Working note, not spec. The allocation rules themselves live in `docs/format/page-05-usage-maps.md` §9.1
("Page allocation works through the free-pages map"); this is the record of the probes, fixtures and trials
they were measured from, moved out of the spec intact so none of it is lost.*

**Status:** the index's two-group rule and its order against a live shared reservation are settled and in the
allocator (the last two bullets of the continuous-session notes). With them, the four controlled appends and all
12 alignment starts place every page as ACE does, every explicit continuous-session prefix of statement 118
(payloads 80, 120, 150 and 200, 430 to 1,000 rows) leaves a byte-identical file, the step-by-step corpus stays at
479 of 479, and the whole continuous run rises from 263 to 285 identical pages of 2,016. Older paragraphs below
record states of the investigation that later measurements superseded; they are kept as written.

**Free-page selection is stateful, not simply lowest-first.** The explicit
`PageGroupProbeTest.Statement_size_reservations` probe measures the same ordered 1,000-row Bulk workload
as the whole-file parity probe's known bulk-insert difference, on a smaller independent schema. It varies
statement size and whether the connection closes between statements, and reads a copy after the final
statement's flush while the session remains open, then reads the file after close. The reservation
classification here is limited to the fixture's inline 512-page map: a clear bit with an unwritten page,
including pages beyond EOF, identifies a reservation; it is not a general classifier for recycled pages.

Measured on 2026-10-15 with the installed 64-bit ACE OLE DB provider:

| Rows inserted, one session | 1 or 50 rows per statement | 250 or 1,000 rows per statement |
| --- | --- | --- |
| 240 | 89 file pages; reserved 89–119; free hole 81–87 | same |
| 480 | 120 file pages; no reservation; free hole 82–87 | same |
| 610 | 132 file pages; reserved 132–151; no free hole | 137 file pages; reserved 132–135 and 144–159; free hole 127 |
| 1,000 | 187 file pages; free hole 165–167 | 194 file pages; free holes 141–143 and 177–183 |

The one-session measurements reproduced in a second run. They describe resulting placement, not
an index-split rule: an operation requests new pages, and the allocator chooses their placement and
reservation. Closing returns unused reservations to the free map. The isolated measurements below
separate those allocator decisions from the bulk workload.

Closing and reopening between batches changes the result further:

| Rows per statement, reopen between statements | File pages at 240 / 480 / 610 / 1,000 rows | Free holes at 1,000 rows |
| --- | --- | --- |
| 50 | 89 / 121 / 134 / 184 | none |
| 250 | 89 / 117 / 137 / 192 | 143 and 169–175 |

For example, after reopening between 50-row batches, pages 102–103 are reserved at 610 rows and
returned at close; at 1,000 rows they hold data. This demonstrates reuse of an internal group separately
from growth at EOF. Session close is therefore another allocation input, independent of statement size.

**Isolated allocator measurements (2026-10-15).**
`PageGroupProbeTest.Controlled_free_page_selection` starts with an ACE-created heap, already holding one
row, then materializes a tail through page 255 and sets only selected unused tail pages free. Pages below
the original EOF, including all live metadata, are untouched; non-offered tail pages are synthetic
occupied placeholders. Pages 256–511 remain the normal free growth region. Nine fixed `BINARY(255)`
columns make each additional row need exactly one data page (the probe checks the owned-page count).
There are no indexes in the heap cases. Separate cases create a new small index root, without splitting.
For each prefix, the probe compares free bits before, after a 3-second flush while open, and after close:
`taken = beforeFree − openFree`, `used = beforeFree − closedFree`,
`returned = closedFree − openFree`. This does not infer reservations from page types.

| Free pages offered below EOF | First allocation uses | Bits cleared while open | Returned at close |
| --- | --- | --- | --- |
| 64–255 | 64 | 64–95 | 65–95 |
| 65–95 | 65 | 65–95 | 66–95 |
| 65, 80–111 | 65 | 65, 80–95 | 80–95 |
| 65, 67, 70, 73, 75, 78, 96–111 | 65 | 65, 67, 70, 73, 75, 78 | all except 65 |
| 67–69, 80–87 | 67 | 67–69, 80–87 | all except 67 |
| none (growth begins at 256) | 256 | 256–287 | 257–287 |
| 72–167 | 72 | 72–103 | 73–103 |
| 80–175 | 80 | 80–111 | 81–111 |
| 93–188 | 93 | 93–119 | 94–119 |
| 97–192 | 97 | 97–127 | 98–127 |

These support the shared-reservation rule: find the lowest free page `p`, reserve **every currently free
bit in the 32-page window `[p & ~7, (p & ~7) + 31]`**, and serve requests from that reservation in ascending
page order. The reservation is a bitmap window, not a contiguous run or necessarily 32 available pages:
occupied gaps are skipped, and a longer run is cut at the window boundary. Creating a fresh index root
instead of a heap page gave the same result for the contiguous, singleton-before-run, fragmented, and
growth layouts, so this rule is not specific to a data page or an index split.
The shifted starts distinguish **8-page alignment** from 32-page alignment; the window still spans
32 pages (four groups), and is not expanded to collect 32 free bits. It is not an unconditional
first-allocation-per-session rule: the heap's existing owned-page count can select another path below.

On exhaustion, heap allocations continue one page at a time: with 64–255 free, prefixes of 32, 33, 64, 65,
and 97 requests used exactly 64–95, 64–96, 64–127, 64–128, and 64–160 respectively, with no unused
reservation. Starting at EOF 256 gave the same pattern through page 352. With six scattered initial free
pages in 64–95, the seventh request reserved all sixteen free bits at 96–111; this case therefore differs
from exhausting a full 32-page window and must not be collapsed into an unconditional single-page rule.

The reservation is shared: after one heap allocation at 64, creating an index root or growing a second
heap uses 65 out of the same reservation. After 32 heap allocations consume 64–95, either of those
consumers uses 96 and reserves 96–127. If the first heap has already continued singly onto 96, the other
consumer uses 97 and reserves the free portion 97–127. Conversely, creating an index root at 64 first,
then inserting 32 heap rows, uses 65–96 without an extra reservation. Thus the distinction is not
"data pages versus index pages": continuing a heap and asking for another consumer's page can enter
different allocation paths on the same connection.

An independent check, `Growth_alignment_follows_the_session`, starts with EOF 121 and grows a heap to
pages 121–123: the remaining reservation is 124–151, exactly the free portion of the four-group window
120–151. Two later index-page requests take 124–125 from that same reservation.

**The heap switches at eight owned data pages (measured).** With a fresh session and free pages 128–511,
preloads of 1, 2, 4, or 7 data pages reserve 128–159 on the next page request. Preloads of 8, 16, 24, 31,
32, 33, or 64 data pages instead take only page 128, leaving 129–159 free even while the session is open.
Changing to five fixed binary columns puts three rows per page: 21 rows on seven pages still reserve
128–159, whereas 24 rows on eight pages take only 128. Thus the threshold tracks owned pages, not rows.

The group path first reuses free pages in the heap's current 8-page group: with data pages 44–51 and
free pages 52–55, 129, 136–167, the next two allocations take 52 and 53 singly. Otherwise it prefers a
**fully free aligned 8-page group**, skipping partially occupied groups:

| State before request | Seven-page heap | Eight-page heap |
| --- | --- | --- |
| Free 129 and 136–167 | uses 129; reserves 129 and 136–159 | uses 136; only bit 136 cleared |
| Free 129, 137, 145 and growth region 256–511 | uses 129; reserves 129, 137, 145 | uses 256; only bit 256 cleared |
| No free pages inside file; EOF 257 | uses 257; reserves 257–287 | uses 264; only bit 264 cleared |

So a large heap can grow the file despite lower free pages existing: those pages lie in partial groups.
For an existing session whose heap consumed 64–95, free page 97 competes with full group 104–111:
the heap uses 104, then 105, leaving 97 free. Creating an index root in that same state uses 97 and
reserves the free bits through 127. The seven/eight-page distinction and consumer difference reproduce
the page-choice effect without relying on an index split.

An important distinction: these heap snapshots are taken **after statement commit**. Unused members
of the owner's eight-page group have already returned to the global free map at that point. The
four-group shared reservation still clears its unused bits, and close returns them. Calling both an
"8-page allocation batch" would lose this observable distinction.

**The same boundary applies to an index's own B-tree page count, including its root.** The `tree7`,
`tree8`, and `tree9` cases build indexes through ACE until their owned maps name exactly 7, 8, and 9
pages, respectively (96, 115, and 134 text-key rows in this fixture). Each is then given the same free
space, 128–511, with incidental old free holes removed. At a prefix of 18 more rows, only two data
pages have been allocated, at 128–129. The 19th row requests one new index page, with no new data page:

| Index pages before request | New index page | Bits cleared while open after this request | Returned at close |
| --- | --- | --- | --- |
| 7 | 130 | 128–159 | 131–159 |
| 8 | 136 | 128–129 and 136 | none |
| 9 | 136 | 128–129 and 136 | none |

This isolates the choice made **for** a new index page. Splitting is only the stimulus that requests it;
the allocation policy is keyed to how many pages that owner already has. A table's data map and each
index's own map therefore cross the boundary independently. Seven-page indexes still use the shared
reservation even while their table already has 10 data pages and uses group selection.

**An existing shared reservation takes precedence.** The `tree7-prime` and `tree8-prime` cases first
make a one-page heap allocation at 128, reserving 128–159. The other table's data pages then take
129–130, and either the seven-page or eight-page index takes 131. Both leave 132–159 reserved until
close. Thus the eight-page boundary selects the group path only when the shared reservation cannot
satisfy the request; it does not force the caller to abandon an existing reservation for a full group.

**Group ordering before and after the owner (measured).** The `order-*` fixtures use an ACE-written
heap of eight pages at 172–179, with current group 176–183. A dropped padding table supplies real free
pages below it; the probe then limits the free map to the selected groups and the growth region.
With groups 64–71, 160–167, 184–191 and 200–207 free, the allocations choose 64–71, then 160–167,
then 184–191. With only earlier groups 64–71 and 160–167, both are used before growing at 256.
With nearer earlier group 160–167 and farther later group 208–215, 160 is chosen first. This is
**lowest eligible group**, not nearest or forward-from-owner order, within the inline map.
Free pages 180–183 in the owner's current group take priority over 64–71; once those four are used,
the scan returns to 64. This also distinguishes current-group reuse from the general group search.

**Long values use shared reservations, not the eight-page owner switch (measured).** Separate cases
preload a column with exactly 7, 8, or 64 LVAL pages. Packed binary values of 3,000 bytes, chained binary
values of 9,000 bytes, and 1,500-character memo values all continue through the shared reservation.
With free 128–511 and 64 LVAL pages already owned, packed values use 128–159 for the first 32 new
pages; the 33rd reserves 160–191, and the 65th reserves 192–223. A chain needing three pages crosses
the same boundary: eleven 9,000-byte values use 128–160 and leave 161–191 reserved until close.
The row records stay on one data page, so no new table data page accounts for these reservations.
Prebuilding the column's owned and free maps in reference form does not change this long-value policy.

**Reference-form owner maps and global maps are separate decisions.** `owner-ref` retains an inline
global free map and a heap owning only page 44, but gives that heap a prebuilt reference owned map.
With page 65 and run 80–111 free, ACE chooses 80 singly, leaving 65 free. Thus the group path is also
selected by a reference-form owned map; "eight pages" alone is not the complete condition.

For the global reference map, `ref-*` fixtures cover two 32,736-page bitmap ranges. Synthetic bitmap
pages are placed on verified unused pages, and the relevant owner maps' bitmap pages are prebuilt,
so their promotion does not add unrelated allocations. The `GlobalFree` observer now uses Core's
existing inline/reference decoder rather than silently treating a reference map as empty.

Shared reservations **stop at the current bitmap's coverage boundary**: with 32729–32767 free, a new
index root uses 32729 and reserves only 32729–32735, not through 32759. Packed and chained long values
do the same, then refill from 32736–32767. A chained value can cross that boundary: nine 9,000-byte
values use 32729–32755 and return 32756–32767 at close. With only 32729 free in the first range,
the first packed value uses it without a remainder; the second reserves 32736–32767.

The selected bitmap is session state: with singleton 32729 in range 0 and a full group at 32736 in
range 1, a group allocation takes 32736. A subsequent new index root on that connection uses 32737
and reserves through 32767, leaving 32729 free. Closing and reopening between the heap allocation
and index creation resets the selection: the index then uses 32729. This rules out an unconditional
scan from bitmap slot zero for every request.

An empty earlier slot is skipped: with slot 0 absent (pointer zero) and 32736–32767 free in slot 1,
an index root uses 32736, reserves through 32767, and returns 32737–32767 at close.

The shared-reservation scan **does not wrap backward** when its selected bitmap is exhausted.
`ref-exhaust` materializes both bitmap ranges through page 65471, offering only singleton 32729 and
group 32736–32743. Eight group allocations consume 32736–32743 and select bitmap 1. A new index root
then grows at 65473 (65472 is the new global bitmap page), reserving the free portion through 65503
and returning 65474–65503 at close. Singleton 32729 remains free. Thus the forward scan can grow
despite a reusable page existing in an earlier bitmap; reopening resets the scan and makes it reusable.

The **full-group search does reconsider earlier bitmaps**, even without reopening. `lv8-release`
first fills group 32736–32743, then updates the long-value column to NULL. A 32,000-byte chain makes
group 56–63 free immediately. After the UPDATE's incidental shared reservation is exhausted (heap
pages 32745–32767), the next group allocation takes 56. Reopening before continuing also finds 56.
The snapshots explicitly confirm 56–63 are free while the original session remains open. The older
packed LVAL pages 47 and 49–55 in this same UPDATE return at close instead; empty packed-page
release and chain-page reuse must not be conflated.

The naturally ACE-grown `ref-native-order` fixture confirms cross-bitmap group ordering on an owner
near the boundary. ACE itself grows a padding table until the global free map is reference form;
after that table is dropped, Bulk owns 32757–32764, in current group 32760–32767. Offered groups
64–71 and 160–167 (bitmap 0) are selected before 32768–32775 and 32784–32791 (bitmap 1), then growth
at 32800. Bitmap-holder placements are controlled only after this ACE-authored transition and are
checked against genuinely free pages before being overwritten.

The measured model is therefore:

1. Serve a request from the session's existing shared reservation when available.
2. Without one, a data/B-tree owner with an inline map and fewer than eight pages, or a long-value
   allocation regardless of the measured owned-map form/count, uses a shared reservation. Reserve
   free bits in the four-group window starting at the selected page's 8-page boundary, clipped to the
   selected global bitmap's end. Search that bitmap, then later bitmap slots; do not wrap backward.
   A data map and each B-tree map are separate owners for this decision.
3. A data/B-tree owner already at eight pages, or with a reference owned map, can continue in its
   current group, then search from the beginning for the lowest eligible fully free aligned 8-page
   group across the global map, reserving its free members privately for that owner. Lower partial groups can be skipped, including
   the unfinished group at EOF; this explains alignment gaps and growth despite free holes. A B-tree owner
   works two groups instead of one, by the rule measured under the continuous-session notes below.
4. Unused shared-reservation bits return at close. Unused owner-private group members return at
   statement commit, so post-statement snapshots show them free even while the connection stays open.

The ordered Bulk insert (statement 118 in the 336-statement parity corpus) distinguishes these
lifetimes. Data reserves 104–111 and has used only 104–108 when the index needs a page. ACE skips
109–111 and selects 112, starting shared reservation 112–143. Data consumes that shared reservation
first, then resumes its private 109–111. LibRed reproduces the complete resulting 215-page file
byte for byte with the existing parity comparison masks; no allocation-difference exemption is used.

**Continuous-session limitation (measured):** that result starts the statement on ACE's prior file
with a fresh session. Keeping the entire workload in one session matches through statement 117
(81 pages), but diverges at 118: ACE ends at 226 pages and LibRed at 218. The placement trace shows
LibRed using index leaf 149 where ACE leaves 149–151 free and later puts that leaf at 208; ACE also
leaves 209–215 free before continuing data at 216. LibRed instead continues data through 200–217.
Thus session-history allocation policy is not completely reproduced yet. By statement 140 LibRed
has 248 pages to ACE's 240, before the known isolated-statement differences starting at 151. The
gap falls to five pages by 168 and stays five through 336 (368 versus 373). These are closed-session
prefix snapshots, not live reservation counts. The explicit prefixes of
`WholeFileParityProbeTest.The_whole_run_leaves_libred_and_ace_with_the_same_file` reproduce the trace.

Further controls narrow the unresolved state (all measured, not an implemented replacement policy):

- Splitting the bulk insert into two statements at row 900, on the same open connection, produces an
  identical 218-page file. The single-statement version produces 226 ACE pages versus 218 LibRed pages.
- A separate `Indexed(K TEXT(200))` fixture starts with eight index pages and twelve data pages. Its
  single-statement ascending append uses index pages 136, 144, 137, 145, 138 in that order, with data
  taking 128–135 and then 152 onward. Individual autocommit inserts instead fill index 136–143 before
  selecting 160. An explicit transaction around the individual inserts gives data two groups
  (128 and 136) and index one (144). DAO transaction depths one, two and three give the same result;
  nesting depth alone is therefore not the selector. A persistent DAO recordset reproduces the
  single-statement append pattern, with or without moving to its `LastModified` bookmark.
- An Access-hosted VBA callback, with a small buffer to expose uncommitted global-map writes, shows
  unused bits returning **during the statement** when its index group changes. In the equivalent
  fixture offered free pages from 512, index group 520–527 is reserved first. At the next split,
  unused 521–527 become free and 528–535 become reserved. The following split returns unused
  529–535 and reserves the remaining free portion of 520–527 again. The callback, no-callback,
  flush and no-flush controls have the same final allocation pattern. Owned-map pages remain pinned
  and do not expose their in-progress changes in these snapshots; the evidence is the global map.

- The divergence comes down to one decision at one row. With 120-character payloads the files are identical
  through 590 rows of the bulk insert and differ at 591; with 150 characters, identical through 900 and
  different by 910. At row 591 only the label index needs a page: it holds 144–145 of group 144–151 (146–151
  free), and the table's data has since taken 152–158. ACE puts the leaf at **160**, a fresh group, leaving
  146–151 and 159 unused; LibRed continues in the index's group and takes 146. In the 150-character run the same
  choice leaves 149–151 unused and sends the index's last leaf to 208.
- "One owner group at a time" — handing back every other owner's group remainder when an owner takes a new one,
  and dropping the displaced owner's current group — reproduces row 591 but not the rest: every prefix past 430
  rows grows LibRed's file well beyond ACE's (281 pages to 226 at 1,000 rows). ACE keeps the data's and the
  index's groups side by side most of the time, so what ends the index's group at row 591 is something narrower.
- The prefixes give the order. While the data fills 140–143 the index takes 144 and then 145 from its group
  144–151; from row 530 the data needs a new group and, 144–151 being the index's, takes 152–159; at row 591 the
  data takes 158 from its group and the index, splitting, takes 160 rather than 146. So the index's group stops
  being its current one once the data has taken a group; whether the data's group likewise survives the index
  taking one is not shown (the closed files do not order the index's 144 against the data's last pages in 140–143).
  A stale-group trial (another owner's group stays reserved but unusable until
  commit, its owner left with no current group) gives the same bloat as above, page for page, so the
  displacement is one-directional or depends on something the closed files do not show.
- A per-row timeline (`WholeFileParityProbeTest.Bulk_insert_page_timeline`: the bulk insert cut to every row count
  1–1,000 on fresh copies of ACE's file after statement 117, the pages each owner gains per row, and each new
  index page's siblings, which name the leaf it split from) refutes "data displaces the index": with 150-character
  payloads ACE's label index splits 128 at row 370 into a fresh group, **136**, though the data has taken no new
  group since the index took 128 at row 325 and 129–135 are free. It then works two groups in turn, a split's new
  page going to the group other than the split page's while both have room — 128→136, 136→129, 129→137,
  130→138 — and stays in 136–143 (139, 140, 141) once the primary key's first split (131–132) and the data
  (133–135) have used up 128–135. At row 770, splitting 141, it opens a third group, 184, with 142–143 still
  free, then takes 142 and 143 and returns to 184–191 (185, 186). LibRed always continues from the owner's last
  page. Not yet a rule: the 770 step is not explained.
  With 120 characters the first difference is row 415 (split 128 → ACE 136, LibRed 129), and the index's pages
  then run 129, 130, **131** (split 130, the same group), 142, 132, **133** (split 132, the same group), 176
  (fresh, 134–135 free), 134, 177, 135, 178. Group 136–143 is not the index's alone: the primary key's first split
  takes 137–138 and the data 139–141 and 143. With 200 characters the two engines agree until row 905, where ACE
  splits 171 into a fresh group (232) with 172–175 free and takes 172 at the next split. None of "always the other
  group", "lowest free page in the index's groups but the split page's", or alternation by allocation count fits
  all three runs.
- Copies taken with the session open (`Bulk_insert_held_pages`, around each split at 120 characters) show only
  one hold surviving a statement: the primary key's first split at row 603 takes 137–138 and leaves 139–143
  reserved, which the data (139–141) and the index (142) then draw on. Owner groups go back at statement commit,
  so these copies cannot show them.
- The closest fit so far: the index keeps a **primary** group (its first private group, 128–135) and a
  **secondary** one; if the table's data has taken a new group since the index's last page, the index continues
  in its primary group, otherwise it takes from its secondary, opening a fresh one when that is full. That gives
  every index decision from row 325 to 591 at 150 characters and from 370 to 591 at 120, but not row 770 at 150
  (a fresh group with the secondary's 142–143 free) nor where the primary key's split lands at 120 (137–138, not
  the lower 132).
- Tried in the allocator, for index pages only (applied to every owner it wrecks even the 430-row prefix): every
  continuous-session prefix of statement 118 then matches ACE byte for byte — 1,000 rows at 150 characters
  included (226 pages, was 218 to 226) — except 1,000 and 610 rows at 120 characters (214 to 213 and 170 to 161
  pages), and the whole continuous run rises from 263 to 285 identical pages of 2,016. But statement 118 run on its
  own, in a fresh session on ACE's file after 117 — which the old rule matches exactly — then differs on 32 pages.
  Reverted.
- What decides it is the file, not the session. The timeline already runs statement 118 in a fresh session; run on
  the file statements 1–117 leave when applied one session each (77 pages, no free page below the end), ACE and
  LibRed agree on every allocation of all 1,000 rows. Run on the file they leave applied in one session (81 pages,
  with 56, 57, 58 and 68 free below the end, handed back at that session's close), they part at row 370
  (`Bulk_insert_starting_files` lists both files).
- The holes are not the cause (`Bulk_insert_holes_pair`): a twin of the 81-page file with only those four free
  bits cleared diverges too (ACE 226 pages, LibRed 219), and on both twins the session's shared reservation and the
  data's first pages are identical in the two engines at every row sampled from 1 to 38. What differs between the
  77- and 81-page files is where everything sits — four pages later, so the 8-page groups fall differently against
  the rhythm of the data's and the index's pages. The divergence depends on that alignment, as the 80- and
  200-character variants matching and the 120-character one not already suggested.
- The alignment repeats every 8 pages (`Bulk_insert_alignment_map`: the 77-page start shifted by a filler table of
  0–10 one-page rows): starts of 77, 79 and 84–87 pages agree for all 1,000 rows; 80 and 88 part at row 905; 81–83
  and 89 at row 591 — each time ACE's index taking a page further on (168, 208) where LibRed continues in its group
  (161, 155). At the 81-page start, open-session copies show no shared reservation at rows 546 or 591: the index
  opens 160–167 at row 546, opens 168 at row 591, and by row 603 161–167 are free again (the primary key's split
  takes 161–162). So the index handed back its group when it opened the next one, as the VBA observation above
  records. At the 77-page start the index opens 152–159 at row 546, the data opens 160 at row 577, and the index
  continues to 153 at row 591; at the 81-page start the data opens no group between the index's two pages, and the
  index moves on. The one-session 150-character run does not follow that trigger (129, 137, 130, 138 alternate
  regardless of the data's groups at rows 397, 469 and 541), and the primary/secondary trial scored 2 of the 12
  starts against the current allocator's 6. Reverted.
- Watching inside the statement (`WholeFileParityProbeTest.Bulk_insert_observed_inside_Access`, a VBA callback
  copying the file per row, the rows fed in Id order from a keyed table) reproduces the divergence, but even with
  `dbMaxBufferSize` at 64 the pages reach the file well behind their allocation (rows from 550 first appear in the
  copy taken before row 591), so it cannot time a single decision.
- A controlled append (`WholeFileParityProbeTest.Indexed_append_timeline`: one statement appending 1,000 rows in
  key order to `Indexed (K TEXT(200), P TEXT(n))` with an index on K, so every split is at the rightmost leaf and
  the index's entries are the same for every padding n) gives the index's rule (measured, n = 0, 50, 100, 200).
  An index keeps a **current** group, its free remainder held, and an **other** group, the one it left, whose
  remainder is back in the free map. For a new **leaf**, after the shared reservation:
  - if the table's data is in a different 8-page group from where it was at the index's previous page, the leaf
    takes the lowest free page of the two groups — the current group's held pages and the other's free ones;
  - otherwise it takes the other group's lowest free page, or opens a fresh group (the lowest fully free aligned
    8-page group) when the other has none.

  Leaving a group returns its held remainder to the free map, and the left group becomes the other one. A **node**
  page (a root or node split) takes the current group's held pages first, then the other group's lowest free page,
  and only then a fresh group. The padding changes only when the data moves, and the rule takes every placement
  of all four runs. In the allocator, together with the node rule, it makes all four runs identical to
  ACE's row for row; with the reservation order below, all 12 starts of `Bulk_insert_alignment_map` (the allocator
  before it: 6).
- With a shared reservation live, two signals about the data decide the order (measured, the 81- and 83-page
  starts, rows 591–905). On both, the primary key, not yet in group mode, takes 161 at row 603 and opens a
  reservation over 160–191; the data draws on it (163–166) and at row 635 the index's leaf finds the data's last
  page in a new group (160, from 152). On the 81-page start the data still holds its own group 152–159: the
  reservation goes first, the index takes 167 from it and keeps its held 169–175, which it uses at row 770 after
  the reservation (176–191) is spent. On the 83-page start the data took a fresh group of its own, 176, at row
  595, after the index's previous page: the leaf then decides ahead of the reservation, counts the reserved 167 as
  not in use, takes it as the lowest page of its two groups and leaves group 168 — and its held 169–175 join the
  live reservation, whose window covers them, ahead of 184, so the next pages come 169–175 (data, index 174,
  data), 184–191, then the data's own 177–183. A fresh group, when the leaf needs one, still comes after the
  reservation.

These observations rule out a single permanently reserved group per index for the entire statement.
They do **not** yet establish how ACE selects the current group/cursor on every request. A trial with
two alternating per-index hints matched the original 150-character-payload case but introduced a
difference in a previously matching 200-character variant, so it was removed. Payloads of 80 and 200
characters match with the existing allocator; a 120-character variant also exposes a pre-existing
allocation difference. `Controlled_free_page_selection`'s `tree8-bulk`, `tree8-tx`, `tree8-dao*` and
`tree8-rs*` cases, and `Index_groups_observed_inside_Access_statement`, reproduce these controls.

This is measured allocation policy, not an on-disk-format requirement. The inline map is one bitmap;
a global reference map additionally keeps a selected bitmap across requests and resets it at reopen.
Do not collapse the distinction into a blanket "always take the lowest free page" rule.
