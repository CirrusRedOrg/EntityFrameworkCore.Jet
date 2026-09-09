using Xunit.Sdk;
using Xunit.v3;

[assembly: Parallelization(
    Mode = ParallelMode.Collections,
    MaxThreads = 1)]