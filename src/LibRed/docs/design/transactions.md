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
> whole transaction. The undo log described below is gone. Everything else here — the lock-manager layering
> (L0), the ACE co-residency constraint (§2), commit-byte / cross-process protocol, cascade worklist — still
> stands as the roadmap. See `TransactionIsolationTests` and [[libred-parallel-dirty-read-flakiness]].

This is the ground-up design for LibRed's transaction support and the concurrency
infrastructure it sits on. It replaces the ad-hoc page-level undo log currently in
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
- **Cascade correctness.** Cascade delete/update runs as a bounded worklist inside the
  transaction — no unbounded recursion, no double-mutate on diamond graphs.
- **Concurrency infrastructure, built in from day one.** Every read/write flows through
  explicit lock-acquisition seams and per-connection transaction context, so the
  real lock manager is a *fill-in*, not a later rewrite.

**Non-goals (deferred, but not precluded)**
- **Byte-exact ACE lock interop.** The initial lock manager is **self-consistent**
  (LibRed↔LibRed): a correct page-locking protocol using our own offsets. Reproducing
  Jet's exact `LockFileEx` offset bands, the `.laccdb` per-user records, and the
  page-0 commit-byte polling — so a live `MSACCESS.EXE` can share the file — is a later
  swap of *values and detection*, not of *structure*. See [[jet-locking-user-registry]].
- **Crash durability beyond Jet's.** We match Jet's model: detect-and-repair, not a
  write-ahead log. A WAL is actively incompatible with ACE co-residency (§5).
- **Record-level locking.** Page-level only. ACCDB per-page encryption already forces
  whole-page granularity — you cannot sub-page lock what you decrypt as a unit.
- **Multi-writer performance.** Writers serialize for now (priority order:
  correctness → speed → concurrency, [[libred-priority-order]]).

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
**in-process** (while the process lives) via an undo log. Cross-process consistency
comes from the lock protocol; crash consistency is Jet's (weak-by-design) detect-and-repair.
We may be *more disciplined* about flush ordering within that protocol, but we add no
structure to the file that Access doesn't understand.

## 3. Layered architecture

```
 L4  ADO surface          LibRedTransaction / LibRedCommand enforce against L2
 L3  Statement layer      QueryEngine: implicit per-statement txn; cascade worklist
 L2  Transaction manager  per-connection Transaction: begin/commit/rollback + savepoints
 L1  PageChannel          write choke point: lock seams + in-process undo log
 L0  Lock manager         page locks, commit-byte map, .ldb — self-consistent now, Jet later
```

The invariant that makes L0 a later fill-in: **L1 already calls
`AcquireShared(page)` / `AcquireExclusive(page)` around every read/write and threads a
per-connection transaction context.** Today those calls resolve to a no-op (or a
process-local monitor) coordinator; the Jet coordinator is dropped in behind the same
interface.

### L0 — Lock manager (`ILockManager`)

Owns cross-connection/cross-process coordination. Interface (stable; implementations vary):

```
interface ILockManager : IDisposable
{
    IDisposable AcquireShared(int page);      // read lock; multiple readers
    IDisposable AcquireExclusive(int page);   // write lock; single writer, excludes readers
    void MarkCommitPending();                 // set our commit-byte slot -> mid-write
    void MarkCommitDone();                    // clear -> idle
    int RegisterUser();                       // claim a slot; returns user index
    void ReleaseUser(int index);
}
```

- **`SelfConsistentLockManager` (initial):** page locks via `FileStream.Lock` on the
  main handle at *our own* deterministic offset band (`page → base + page*stride`);
  a simple in-file or side-file presence map. Correct LibRed↔LibRed, Windows-and-Unix
  where `FileStream.Lock` is supported; a `MonitorLockManager` (process-local
  `ReaderWriterLockSlim` per page) covers single-process / cross-platform.
- **`JetLockManager` (later):** the exact Jet 4/ACE offsets (~10M/20M bands on the
  `.laccdb` handle), the page-0 commit-byte table registration/polling, the `.laccdb`
  32+32-byte identity records. Windows-only. This is a values+detection swap; the L1
  call sites and L2 semantics do not change. Source facts: [[jet-locking-user-registry]].

### L1 — PageChannel (write choke point)

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

`Transaction` is **per connection** (EF holds several connections on one shared
`PageChannel`; a single global undo log is the current bug). Contents:

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
- **Cascade as a worklist:** delete/update collects affected child rows into a queue with
  a `visited` (in-progress) set; cycles terminate, diamonds mutate once. All mutations
  are in the one transaction, so a mid-cascade failure rolls the whole thing back.

### L4 — ADO surface

- `LibRedConnection.BeginTransaction()` → creates the connection's explicit `Transaction`;
  `LibRedTransaction.Commit/Rollback` drive L2.
- `LibRedCommand` executes inside its connection's active transaction (explicit or the
  per-statement implicit one). The "stored but unenforced transaction" gap is closed:
  a command with a foreign/stale transaction is rejected.

## 4. Transaction semantics

- **Atomicity unit:** the statement (implicit) or the explicit `BEGIN…COMMIT` span.
- **Isolation (initial):** single-writer / many-readers via strict two-phase page
  locking — exclusive locks held to commit. This yields serializable behavior for the
  single-writer case. MVCC/snapshot is a later concurrency-phase option and is not
  designed in here beyond "don't preclude" (before-images already exist).
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
- **Durability:** commit flushes dirty pages then clears the commit-byte; a crash before
  the clear leaves the Jet "suspect" signal (later, with `JetLockManager`) → repair path.
  With the self-consistent manager, recovery is process-local (no cross-process crash
  interop claimed yet).

## 5. Flush / commit ordering (matching Jet, staying ACE-safe)

On commit, in order: (1) write all dirty data/index/LVAL/usage-map pages; (2) fsync;
(3) write the page-0 header/commit-state update; (4) fsync; (5) clear commit-byte /
release locks. Never leave a header pointing at pages that aren't durable. No structure
is written that Access cannot parse — the commit-byte table and lock offsets are the only
concurrency-visible state, exactly as Jet uses them.

## 6. Phased implementation plan

Build correctness first with lock seams stubbed; drop the Jet lock manager in last.

1. **L2 core.** ✅ done. `Transaction` + `SavepointStack` with before-image undo, moved out of
   `PageChannel`. `PageChannel.WritePage` records into the active transaction. Unit tests
   for commit/rollback/nested rollback, allocate-then-rollback truncation.
2. **L3 statement atomicity.** ✅ done. Wrap every `QueryEngine` statement in an implicit txn;
   convert the audit's non-atomic writers (`RowInserter`, `TableCreator`, `ViewCreator`,
   usage-map/LVAL) to rely on it. Regression: inject a late failure mid-statement, assert
   no partial state.
3. **Cascade worklist.** ✅ done. Replace recursive cascade with the queue+visited worklist
   inside the txn. Tests: cyclic FK, diamond FK, deep chain (former stack-overflow).
4. **L1 lock seams + `MonitorLockManager`.** ✅ done. Introduce `ILockManager`, route
   read/write through it (cache-hit reads stay lock-free via copy-on-write `Store`), ship
   the process-local monitor implementation.
5. **L4 ADO enforcement.** ✅ done. Wire `LibRedTransaction`/`LibRedCommand` to L2; reject
   stale/foreign transactions; EF savepoint support (`SupportsSavepoints`).
6. **SQL transaction-control statements (with nesting).** Add engine-native `BEGIN`/`COMMIT`/
   `ROLLBACK [TRANSACTION|WORK]` (and Access's `BEGIN TRANS`), plus named `SAVE`/`ROLLBACK
   TRANSACTION <name>`. Parse to AST → a new `QueryEngine.Route` branch that drives a
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
   unknowns from [[jet-locking-user-registry]] (own-slot writes, poll interval, Jet-4
   `.laccdb` record shape) first.

Steps 1–3 close every transaction-related P0 in the audit (✅). Step 5 completes the
in-process transaction contract (✅), and step 6 adds the SQL front door onto it. Steps 7–8
are the cross-process concurrency ladder and land independently — the seams from step 4
don't move.

## 7. Open questions

- **Reader isolation while a writer commits:** do readers under shared locks see the
  pre-commit page (blocked until release) or is a dirty-read window acceptable initially?
  Proposed: block (strict 2PL) — simplest correct default.
- **Implicit-txn cost:** per-statement begin/commit must be cheap for read-only statements
  (no dirty pages ⇒ commit is just lock release). Ensure a read-only statement never
  touches the commit-byte.
- **Deadlock policy** once multiple pages lock in different orders: initial mitigation is
  a global writer lock (one writer at a time), so no page-order deadlock exists yet;
  revisit when finer locking arrives.
- **`.ldb`/`.laccdb` lifecycle** (create on first open, delete on last close) — belongs to
  step 6/7; not needed for in-process correctness.
