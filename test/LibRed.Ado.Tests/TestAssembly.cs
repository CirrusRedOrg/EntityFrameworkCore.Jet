using Xunit;

// Several tests copy the shared Northwind fixture while other tests open it read/write. File.Copy's source
// handle does not share writes, so cross-class parallelism makes otherwise unrelated tests race at startup.
// Explicit multi-connection behavior is covered by dedicated tests; keep this fixture-based project ordered.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
