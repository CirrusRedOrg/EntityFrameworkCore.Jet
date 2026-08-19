// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;
using Xunit;

#if FIXED_TEST_ORDER

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: TestCollectionOrderer(typeof(AscendingTestCollectionOrderer))]
[assembly: TestCaseOrderer(typeof(AscendingTestCaseOrderer))]

#endif