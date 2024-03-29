// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;
using Xunit;

// Set assembly wide conditions to control tests in accordance with the environment they are run in.
[assembly: JetConfiguredCondition]

#if FIXED_TEST_ORDER

[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly, DisableTestParallelization = true, MaxParallelThreads = 1)]
[assembly: TestCollectionOrderer("EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit." + nameof(AscendingTestCollectionOrderer), "EntityFrameworkCore.Jet.FunctionalTests")]
[assembly: TestCaseOrderer("EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit." + nameof(AscendingTestCaseOrderer), "EntityFrameworkCore.Jet.FunctionalTests")]

#endif

[assembly: TestFramework("EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit." + nameof(JetXunitTestFramework), "EntityFrameworkCore.Jet.FunctionalTests")]