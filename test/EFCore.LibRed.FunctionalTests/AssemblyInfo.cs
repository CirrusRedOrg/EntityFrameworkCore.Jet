using Xunit;

// Runs in parallel (MaxParallelThreads = 0). Unlike EFCore.Jet.FunctionalTests — which must serialise because
// the ACE OLE DB provider crashes the host with 0xC0000005 under concurrent connections — LibRed uses no ACE
// and now coordinates shared-file access itself: page-level reader/writer locking (MonitorLockManager) plus a
// copy-on-write page cache, so the many xUnit collections EF's shared-store fixtures place on one physical
// <name>.accdb run concurrently without the read corruption an earlier, uncoordinated trial hit.
//
// Requires an x64 test host. testhost.x86.exe is not LargeAddressAware (2 GB cap), and LibRed — being an
// in-process, fully-managed engine — puts its whole working set (page caches, catalogs, query buffers) in the
// same managed address space as EF and the test platform, unlike out-of-process/native providers (SQL Server,
// ACE). Under this concurrency that overflows 32-bit; on x64 the full ~38k-test suite runs green in parallel.
[assembly: CollectionBehavior(
    DisableTestParallelization = false,
    MaxParallelThreads = 0)]
