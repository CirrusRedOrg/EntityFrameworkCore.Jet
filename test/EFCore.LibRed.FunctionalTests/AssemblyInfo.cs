using Xunit;

// Kept sequential (MaxParallelThreads = 1) — but for a DIFFERENT reason than EFCore.Jet.FunctionalTests.
// Jet must serialise because the ACE OLE DB provider crashes the host with 0xC0000005 under concurrent
// connections. LibRed uses no ACE, so that does not apply; however LibRed still cannot run these tests in
// parallel because EF's shared-store fixtures put many different test classes (separate xUnit collections,
// which xUnit parallelises) on the SAME physical <name>.accdb file, and LibRed has no multi-connection
// concurrency control yet (concurrency is deliberately the last priority). A trial with MaxParallelThreads = 0
// corrupted reads en masse ("Nullable object must have a value") from parallel connections racing on the shared
// Northwind file + page cache. Re-enable only once LibRed has shared-file concurrency control (locking /
// per-connection isolation).
[assembly: CollectionBehavior(
    DisableTestParallelization = false,
    MaxParallelThreads = 1)]
