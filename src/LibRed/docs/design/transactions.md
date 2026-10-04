# LibRed transaction & concurrency design

Status: **draft / accepted direction** · Date: 2026-07-18

> **Implementation note (2026-07-24) — isolation is a deferred-write overlay, not undo + strict 2PL.**
> Isolation shipped by a simpler route than the write-through-undo-under-held-locks sketch in §3 L1 / §4.
> `PageChannel` now buffers a transaction's writes into a **private per-connection overlay**
> (`page → uncommitted bytes`) instead of writing them through to disk and the shared page cache. Reads on the
> owning channel see the overlay (read-your-writes); every other channel on the file sees only committed
> state — so a concurrent reader never observes an uncommitted page. **Commit** replays the overlay to
> disk + shared cache in ascending page order; **rollback** simply discards it (nothing to restore, truncate,
> or flush); **savepoints** snapshot the prior overlay state per frame (`Transaction`). This gives
> **read-committed** isolation **without holding exclusive locks for the transaction's lifetime** — so the
> writer-serialization / reader-blocking of strict 2PL (§4) is not needed for isolation, and EF's parallel
> shared-store tests (each mutating inside a rolled-back transaction) no longer leak into concurrent readers.
> The exclusive page lock is now held only for the duration of an individual committed page write, not the
> whole transaction. Before publishing, commit compares every page's committed plaintext image with the image
> from which that overlay page was first derived, under a per-file publish gate. Writers touching disjoint pages
> may both commit; if another connection changed an overlapping data/index/TDEF/usage-map page, the stale commit
> fails with a write conflict and remains open for rollback. This prevents silent lost updates without strict
> two-phase locking. If publication itself fails after writing some pages, that prefix is restored from the
> validated baselines (and appended tail pages are truncated), leaving the transaction open and rollbackable.
> **A page check cannot see what a transaction only read**, which is where write skew gets in: an
> `INSERT` checks its foreign key's parent row and writes no page for it, so a concurrent connection may delete
> that parent, and each transaction's precondition holds at the moment it is checked while the pair of them
> leaves a child referencing nothing. So a transaction also carries a **read set** of the conditions its writes
> depend on (`PageChannel.DependOn`), each re-evaluated under the publication gate before anything is
> published; a commit whose condition has stopped holding is refused like a write conflict. The conditions are
> semantic rather than physical — the FK's parent is sought again, not its page compared — so an unrelated row
> on the same page cannot produce a false conflict. A missing parent only refuses the commit while it is still
> needed: while the relationship survives and a child row still holds the key, so a transaction that later
> deleted or moved its child, or dropped the relationship, commits. The relationship is identified by the
> definition pages of its tables and the ids of its columns, so renames inside the transaction do not lose it,
> and one dropped and added again under the same name over other columns is a different relationship. The same
> hole exists from the parent's end: a `DELETE` of a parent row, or a change to its key, that found no child
> (or cascaded or nulled the ones it found) writes nothing another connection's child insert would conflict
> with, and that insert finds the parent it has not seen deleted. So the parent side registers the same
> condition for the key it removed — no child row may hold it without a parent row holding it too. It is one
> condition from either end, so a condition registered under a key the transaction already holds (the same
> relationship and key) is held once. `ALTER TABLE … ADD FOREIGN KEY` reads the same way, checking every existing child row against its
> parent, so it registers one condition for the relationship: while it survives, every child row still has its
> parent. ACE gets the same guarantee by holding both tables exclusively until the transaction ends. A savepoint rollback drops the conditions recorded after it, and their keys, along with the writes that
> needed them.
> Schema-changing commits also advance a shared per-file catalog generation; other open
> connections invalidate their parsed table/relationship/view caches on the next catalog access, while ordinary
> DML does not force a catalog reload. The undo log described below is gone, and with it the before-image
> `SavepointStack`: savepoint frames live in `Transaction` and snapshot overlay state. Everything else here — the
> lock-manager layering (L0), the ACE co-residency constraint (§2), commit-byte / cross-process protocol — still
> stands as the roadmap. Of the cascade design in §3 L3, the **delete** cascade is the bounded worklist; an
> **ON UPDATE** cascade recurses one call per level instead (a rewritten child applies its own relationships), and
> terminates because a row already holding the new key is not rewritten again. See `TransactionIsolationTests`.

> **Deferred (2026-09-24) — the commit-byte protocol and the Jet lock manager (§3 L0, phases 7–8).** The two
> are one job, not two: a writer's commit slot is `0xE00 + 2n` for *its own* user number *n*, and that number
> comes from the position it takes in the `.laccdb` — so there is no writing the commit table without first
> doing the lock-file registration. What is missing is measured behaviour: the values ACE writes into its slot
> as it begins and ends a batch (the order and 2-byte extent are known, the values are not — page-00 §2.2),
> and whether page 0 is itself under a byte-range lock meanwhile. Guessing writes a signal Access reads as
> corruption, so this waits on a ProcMon/Frida characterisation of a live `MSACCESS.EXE`. Until then LibRed is
> **single-writer**, as `src/LibRed/README.md` states, and every mention of commit bytes, `.laccdb` records and
> cross-process recovery below is **roadmap, not code**. What ships today is the deferred-write overlay, the
> write-conflict check on commit, and process-local page locks.

This is the ground-up design for LibRed's transaction support and the concurrency
infrastructure it sits on. It replaced the ad-hoc page-level undo log that used to be in
`PageChannel`, and is written so the localized atomicity gaps the production audit
found (`LIBRED_AUDIT_REPORT.md`, the P0s) are closed by *one* model rather than
patched site by site.

## 1. Goals and non-goals

**Goals**
- **Statement atomicity.** Every statement is all-or-nothing, even with no explicit
  user transaction. A late constraint/I/O failure never leaves a half-written row,
  index, usage map, LVAL chain, TDEF, or catalog entry.
- **Explicit transactions.** `BEGIN`/`COMMIT`/`ROLLBACK` (and the ADO
  `LibRedTransaction`) group many statements atomically, with **savepoints** (nesting).
- **Cascade correctness.** Cascade delete runs as a bounded worklist inside the
  transaction — no unbounded recursion, no double-mutate on diamond graphs. (Cascade
  update recurses per level; see the implementation note above.)
- **Concurrency infrastructure, built in from day one.** Every read/write flows through
  explicit lock-acquisition seams and per-connection transaction context, so the
  real lock manager is a *fill-in*, not a later rewrite.

**Non-goals (deferred, but not precluded)**
- **Byte-exact ACE lock interop.** The initial lock manager is **self-consistent**
  (LibRed↔LibRed): a correct page-locking protocol using our own offsets. Reproducing
  Jet's exact `LockFileEx` offset bands, the `.laccdb` per-user records, and the
  page-0 commit-byte polling — so a live `MSACCESS.EXE` can share the file — is a later
  swap of *values and detection*, not of *structure*.
- **Crash durability beyond Jet's.** We match Jet's model: detect-and-repair, not a
  write-ahead log. A WAL is actively incompatible with ACE co-residency (§5).
- **Record-level locking.** Page-level only. ACCDB per-page encryption already forces
  whole-page granularity — you cannot sub-page lock what you decrypt as a unit.
- **Multi-writer performance.** Writers serialize for now (priority order:
  correctness → speed → concurrency).

## 2. Why not a WAL / journal (the ACE-co-residency constraint)

The hard requirement is that Access and LibRed can hold the **same file open at the
same time**. That rules out any LibRed-private durability side-channel:

- Access writes pages under *its* protocol and never touches a LibRed journal, so our
  recovery would roll its committed work back — corruption, not safety.
- Jet itself has no WAL. Durability = the page-0 **commit-byte table** (`0xE00`–`0xFFF`,
  256×2 bytes: idle `00 01`, **mid-write `00 00`**, corrupt `01 00`) plus the OS
  byte-range locks. A `00 00` with no matching `.ldb` lock after a crash ⇒ Jet marks the
  DB suspect and repairs.

So LibRed **matches Jet's on-file commit protocol** and provides atomicity/rollback
**in-process** (while the process lives) via the deferred-write overlay (originally an undo log). Cross-process consistency
comes from the lock protocol; crash consistency is Jet's (weak-by-design) detect-and-repair.
We may be *more disciplined* about flush ordering within that protocol, but we add no
structure to the file that Access doesn't understand.

## 3. Layered architecture

```
 L4  ADO surface          LibRedTransaction / LibRedCommand enforce against L2
 L3  Statement layer      QueryEngine: implicit per-statement txn; cascade worklist
 L2  Transaction manager  per-connection Transaction: begin/commit/rollback + savepoints
 L1  PageChannel          write choke point: lock seams + per-transaction write overlay
 L0  Lock manager         page locks, commit-byte map, .ldb — self-consistent now, Jet later
```

The invariant that makes L0 a later fill-in: **L1 already calls
`EnterShared(page)` / `EnterExclusive(page)` (and the matching `Exit…`) around every read/write and threads a
per-connection transaction context.** Today those calls resolve to the process-local `MonitorLockManager`; the
Jet coordinator is dropped in behind the same interface.

### L0 — Lock manager (`ILockManager`)

Owns cross-connection/cross-process coordination. The interface as it ships:

```
public interface ILockManager
{
    void EnterShared(int page);      // read lock; multiple readers
    void ExitShared(int page);
    void EnterExclusive(int page);   // write lock; single writer, excludes readers
    void ExitExclusive(int page);
}
```

The commit-byte and user-registry members the Jet coordinator will need — mark our slot mid-write and idle, claim
and release a user index — are **roadmap**: they arrive with the lock-file registration (see the deferral note
above), not before.

- **`SelfConsistentLockManager` (initial):** page locks via `FileStream.Lock` on the
  main handle at *our own* deterministic offset band (`page → base + page*stride`);
  a simple in-file or side-file presence map. Correct LibRed↔LibRed, Windows-and-Unix
  where `FileStream.Lock` is supported; a `MonitorLockManager` (process-local
  `ReaderWriterLockSlim` per page) covers single-process / cross-platform.
- **`JetLockManager` (later):** the exact Jet 4/ACE offsets (~10M/20M bands on the
  `.laccdb` handle), the page-0 commit-byte table registration/polling, the `.laccdb`
  32+32-byte identity records. Windows-only. This is a values+detection swap; the L1
  call sites and L2 semantics do not change.

### L1 — PageChannel (write choke point)

> **Superseded — kept as the original sketch.** The before-image undo and held-to-commit exclusive locks below
> were replaced by the deferred-write overlay (see the implementation note at the top): writes buffer per
> transaction, and the exclusive page lock is held only while a committed page is written.

- `ReadPage(p)`: `using (locks.AcquireShared(p))` → decode/return (existing parsed-page
  cache unchanged).
- `WritePage(p, bytes)` under a transaction:
  1. `AcquireExclusive(p)`,
  2. snapshot the **before-image** into the active transaction's undo set (once per page
     per savepoint frame),
  3. `MarkCommitPending()` on first dirty page of the txn,
  4. write the page (write-through, as today),
  5. keep the exclusive lock until commit/rollback (strict two-phase, §4).
- The undo store moves **off** `PageChannel` (no more single global `_undo`) into the
  per-connection `Transaction` (§4). `PageChannel` becomes stateless w.r.t. transactions
  beyond holding the file/lock handles.

### L2 — Transaction manager

> **Superseded in part.** `Transaction` is per connection as designed, but it holds overlay state rather than
> before-images: a savepoint frame snapshots the overlay, rollback discards it, and there is no truncation or
> lock release to do. The frame stack below is the original sketch.

`Transaction` is **per connection** (EF holds several connections on one shared
`PageChannel`; a single global undo log was the original bug). Contents:

- `SavepointStack` — each frame holds its own `Dictionary<int, byte[]>` of before-images
  and the set of exclusive locks first taken in that frame. `Begin` pushes; `Release`
  merges a frame down; `RollbackTo` restores that frame's before-images in reverse and
  releases its locks.
- **Commit:** flush (§5 ordering), `MarkCommitDone()`, release all locks, discard undo.
- **Rollback:** restore before-images newest→oldest, truncate to the transaction's
  original page count (pages allocated in the txn are discarded), `MarkCommitDone()`,
  release locks.
- **Writer serialization:** a transaction that takes its first exclusive lock is the
  writer; others block (or fail-fast per isolation policy) until it ends. Readers proceed
  under shared locks.

### L3 — Statement + cascade layer

- **Implicit transaction:** `QueryEngine.Route` runs each statement inside
  `txn ??= connection.BeginImplicit()`; on success it commits the implicit txn, on any
  throw it rolls back. If an explicit user transaction is open, statements are savepoints
  within it instead. This single change closes the statement-atomicity P0 for *all*
  writers (row insert, DDL, view create, LVAL) without per-site edits.
- **Cascade as a worklist:** delete collects affected child rows into a stack with
  a `visited` (in-progress) set; cycles terminate, diamonds mutate once. An update cascade
  rewrites each child through the same code as a directly updated row, which applies that
  child's own relationships in turn. All mutations are in the one transaction, so a
  mid-cascade failure rolls the whole thing back.

### L4 — ADO surface

- `LibRedConnection.BeginTransaction()` → creates the connection's explicit `Transaction`;
  `LibRedTransaction.Commit/Rollback` drive L2.
- `LibRedCommand` executes inside its connection's active transaction (explicit or the
  per-statement implicit one). The "stored but unenforced transaction" gap is closed:
  a command with a foreign/stale transaction is rejected.

## 4. Transaction semantics

- **Atomicity unit:** the statement (implicit) or the explicit `BEGIN…COMMIT` span.
- **Isolation:** read-committed, from the deferred-write overlay — see the implementation
  note at the top. (The original plan was strict two-phase page locking with exclusive
  locks held to commit; the overlay made that unnecessary.) MVCC/snapshot is a later
  concurrency-phase option and is not designed in here beyond "don't preclude".
- **Savepoints:** nesting via the frame stack; an inner statement inside an explicit txn
  is a frame, so its failure rolls back just that statement, not the user's transaction.
- **Nested transactions** (SQL `BEGIN`/`COMMIT`/`ROLLBACK`, and any caller that nests) map
  onto the one savepoint stack — there is a single physical transaction, never truly nested
  ones. A per-connection **transaction controller** holds a depth counter shared by *both*
  front doors (the ADO API and SQL statements), so they can't open parallel transactions:
  - **BEGIN** at depth 0 opens the real L2 transaction; at depth ≥ 1 it pushes a savepoint.
    Depth increments.
  - **COMMIT** at depth 1 commits the real transaction; at depth ≥ 2 it *releases* the
    innermost savepoint (merges its work into the enclosing level). Depth decrements.
  - **ROLLBACK** at depth 1 rolls the transaction back and closes it; at depth ≥ 2 it rolls
    back to the innermost savepoint (undoing just that level). Depth decrements.
  - This follows **Jet/DAO nested semantics** — commit/rollback act on the *innermost* level
    — which is our compatibility target, *not* SQL Server's "unqualified ROLLBACK unwinds
    all levels". A named `SAVE`/`ROLLBACK TRANSACTION <name>` addresses a specific frame.
  No new mechanism is needed: nesting is the Phase-1 savepoint stack, driven by the controller.
- **Durability:** commit writes dirty pages out (to the OS, as ACE does — §5) then clears
  the commit-byte; a crash before
  the clear leaves the Jet "suspect" signal (later, with `JetLockManager`) → repair path.
  With the self-consistent manager, recovery is process-local (no cross-process crash
  interop claimed yet).

## 5. Flush / commit ordering (matching Jet, staying ACE-safe)

**What ACE does (measured: ACE 16 over OLE DB, Process Monitor).** ACE never forces the OS
cache to disk — no `FlushFileBuffers` on a statement, on an explicit `COMMIT`, or at close —
and it opens the file without write-through. Its durability is the OS file cache plus the
commit-byte protocol, nothing more. What its settings change is only *when* the write
reaches the OS:

| Setting (registry default) | Effect |
| --- | --- |
| `ImplicitCommitSync` = **no** | A lone statement's writes are issued *after* the statement returns — deferred, and flushed to the OS when the next statement starts or the timeout fires |
| `UserCommitSync` = **yes** | An explicit `BEGIN…COMMIT` writes its pages inside `COMMIT`, before it returns |
| `FlushTransactionTimeout` = **500 ms** | How long a deferred write waits; overrides `SharedAsyncDelay` (50) and `ExclusiveAsyncDelay` (2000) |
| `PageTimeout` = **5000 ms** | How long another user's cached pages may stay stale |

Two orderings hold in every mode, and they are the ones that matter for concurrency:

- **Pages reach the OS before the lock is released.** A page's `.laccdb` lock is released
  only after that page's `WriteFile`, never before. Cross-connection visibility therefore
  rests on the write reaching the OS — which every handle on the machine reads through —
  not on it reaching the disk.
- **The commit-slot write brackets the batch**: the connection's own slot at `0xE02` is
  written immediately before the first page and again after the last
  ([page-00 §2.2](../format/page-00-database.md)). An explicit transaction writes nothing
  at all until its commit.
- File growth is a 1-byte write at the last byte of the new page, then the page itself.

**What LibRed does.** The same, deliberately: a commit publishes every overlay page to the
OS under the publish lock, and neither a commit nor a close forces them to disk. An
autocommit statement writes its pages immediately rather than deferring them — stricter
than ACE's default, and the deferral is what the engine-settings work would add. Never
leave a header pointing at pages the OS has not been given. No structure is written that
Access cannot parse — the commit-byte table and lock offsets are the only
concurrency-visible state, exactly as Jet uses them.

## 6. Phased implementation plan

Build correctness first with lock seams stubbed; drop the Jet lock manager in last.

1. **L2 core.** ✅ done, then superseded by the overlay (see the implementation note): the
   before-image undo and `SavepointStack` shipped first and were replaced — a transaction's
   writes now buffer in the overlay, savepoint frames live in `Transaction`, and rollback
   discards pages rather than restoring and truncating them.
2. **L3 statement atomicity.** ✅ done. Wrap every `QueryEngine` statement in an implicit txn;
   convert the audit's non-atomic writers (`RowInserter`, `SchemaEditor`, `JetCatalog`,
   usage-map/LVAL) to rely on it. Regression: inject a late failure mid-statement, assert
   no partial state.
3. **Cascade worklist.** ✅ done for DELETE. Replace recursive cascade delete with the
   stack+visited worklist inside the txn. Tests: cyclic FK, diamond FK, deep chain (former
   stack-overflow). An update cascade recurses per level.
4. **L1 lock seams + `MonitorLockManager`.** ✅ done. Introduce `ILockManager`, route
   read/write through it (cache-hit reads stay lock-free via copy-on-write `Store`), ship
   the process-local monitor implementation.
5. **L4 ADO enforcement.** ✅ done. Wire `LibRedTransaction`/`LibRedCommand` to L2; reject
   stale/foreign transactions; EF savepoint support (`SupportsSavepoints`).
6. **SQL transaction-control statements (with nesting).** ✅ done. Engine-native `BEGIN`/`COMMIT`/
   `ROLLBACK [TRANSACTION|WORK]`. Parse to AST → a new `QueryEngine.Route` branch that drives a
   per-connection **transaction controller** (the §4 depth counter) on the *same* L2 as the
   ADO front door — so a SQL `BEGIN` and an ADO `BeginTransaction` can't open parallel
   transactions, and the controller is the single source of `InTransaction`. Two must-haves:
   (a) transaction-control statements are **exempt from the Phase-2 implicit wrap** — they
   manage the transaction rather than run inside one; (b) nesting reuses the Phase-1 savepoint
   stack (BEGIN→savepoint at depth ≥ 1; COMMIT→release; ROLLBACK→rollback-to). This is the
   audit's deferred "ownership/liveness" item — the controller is where ADO and SQL reconcile.
   Relevant to executing generated migration scripts and raw `BEGIN…COMMIT` batches; the EF
   runtime path keeps using the ADO API. Tests: nested BEGIN/COMMIT/ROLLBACK depth behaviour,
   SQL-then-ADO consistency, control statements not self-wrapped.
7. **L0 `SelfConsistentLockManager`.** Real byte-range page locks (our offsets) + presence
   map; multi-*process* single-writer LibRed↔LibRed.
8. **(Concurrency phase) `JetLockManager`.** Jet-exact offsets, commit-byte polling,
   `.laccdb` records → live co-residency with `MSACCESS.EXE`. Characterize the remaining
   unknowns (own-slot writes, poll interval, Jet-4 `.laccdb` record shape) first.

Steps 1–3 close every transaction-related P0 in the audit (✅). Step 5 completes the
in-process transaction contract (✅), and step 6 adds the SQL front door onto it. Steps 7–8
are the cross-process concurrency ladder and land independently — the seams from step 4
don't move.

## 7. Open questions

- **Reader isolation while a writer commits:** ✅ every statement holds the shared cache's publication gate for
  its complete duration, so a reader sees either the complete pre-commit or complete post-commit page set, never
  a torn mixture while an overlay is being published. The gate is a **reader/writer** lock, not a mutex: a
  statement that cannot write (`SELECT`, a set operation, a system-variable select) takes it **shared**, so
  concurrent readers on one file still run together; everything else takes it **exclusive** for the whole
  statement, which is what makes a multi-page write atomic to readers. Anything not provably read-only takes the
  exclusive scope — the shared scope cannot be upgraded and says so rather than deadlocking. Parsing happens
  before the scope is taken (it touches no pages). See `ReaderWriterIsolationTests`.
- **Implicit-txn cost:** per-statement begin/commit must be cheap for read-only statements
  (no dirty pages ⇒ commit is just lock release). Ensure a read-only statement never
  touches the commit-byte.
- **Deadlock policy** once multiple pages lock in different orders: initial mitigation is
  a global writer lock (one writer at a time), so no page-order deadlock exists yet;
  revisit when finer locking arrives.
- **`.ldb`/`.laccdb` lifecycle** (create on first open, delete on last close) — belongs to
  steps 7–8; not needed for in-process correctness.
