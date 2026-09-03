// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.Xunit;
using Xunit;

// Set assembly wide conditions to control tests in accordance with the environment they are run in.
[assembly: LibRedConfiguredCondition]
[assembly: TestConditions]

// Reports queries the provider has declared it cannot translate as skipped rather than failed.
[assembly: TestFramework(typeof(UnsupportedExpressionTestFramework))]

#if FIXED_TEST_ORDER

[assembly: TestCollectionOrderer(typeof(AscendingTestCollectionOrderer))]
[assembly: TestCaseOrderer(typeof(AscendingTestCaseOrderer))]

#endif

// Records the in-flight test so a runner-killing crash can be skipped next run.
[assembly: TestRunnerCrashDetection]
