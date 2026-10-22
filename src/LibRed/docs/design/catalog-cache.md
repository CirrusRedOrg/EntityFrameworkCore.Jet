# LibRed catalog cache

Status: **accepted direction** · Date: 2026-10-21

## Decision

`JetCatalog` caches a table's **DDL** across requests: its columns, indexes, relationships, defaults, CHECK
constraints and validation rules. Real workloads are overwhelmingly INSERT, UPDATE and DELETE. Tables, columns
and relationships change rarely, and almost never from several connections at once, so the DDL side of the
catalog is effectively constant and re-reading it per statement only costs time. (Reading it per request made a
primary-key SELECT about 4× slower and DML 2–3× slower; that is the cost this cache removes.)

## Cached vs live

The TDEF page holds both kinds of state, and only one of them is cached:

| Cached (DDL) | Read live, every time (table state) |
| --- | --- |
| Column descriptors, index definitions, logical indexes | Row count (`0x10`) |
| Long-value map pointers | AutoNumber high-water (`0x14`) |
| Defaults, `Required`, `AllowZeroLength`, CHECKs, validation rule (from `LvProp`) | Complex-id high-water (`0x1C`) |
| Relationships (`MSysRelationships`) | Each real index's total and unique entry counts |

The counters are shared state every insert advances, so a cached copy is wrong by definition rather than merely
stale. An insert reads the definition page **once**, draws its AutoNumber and complex ids from that copy, advances
the row and index counters in the same copy and writes it back (`RowInserter.Insert`). `TableDefinition.DdlMetadata`
is the decoded DDL kept apart from any table-state snapshot; a definition rebuilt from it reads the counters from
the current page.

## Invalidation

- **Row writes** (ours or ACE's) change only the counters, so they leave the cached DDL valid. A TDEF whose bytes
  differ only in the counters reuses its decoded DDL.
- **Schema changes** are rare, so they only have to be correct, not fast. LibRed's own DDL publishes a schema
  change (`JetCatalog.Invalidate` → `PageChannel.MarkSchemaChanged`). The current implementation then revalidates
  each entry against its catalog-row and TDEF page images; the accepted direction is the simpler rule of dropping
  the DDL cache wholesale on any schema change.
- **Another process's writes** are not yet seen at all while LibRed holds the file open: the page cache is
  discarded only when the last handle closes, and the DDL cache is only as fresh as the page cache. Detecting them
  belongs to the multi-user locking work — page 0's commit bytes (`0xE00`) say that another user has committed,
  which drops the affected pages and, on a schema change, the DDL cache.

## Locking (future)

Once LibRed takes ACE's locks, an insert locks the table's definition page before its single read and holds the
lock until the transaction's commit has written that page. Releasing it earlier would let the next user read the
old counters, because the deferred-write overlay keeps the new ones in memory until commit. See
[transactions.md](transactions.md).
