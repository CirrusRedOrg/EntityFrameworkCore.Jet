// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.TestCaseOrderers;
using Xunit;

// Set assembly wide conditions to control tests in accordance with the environment they are run in.
[assembly: JetConfiguredCondition]

[assembly: TestCaseOrderer("EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.TestCaseOrderers." + nameof(AscendingTestCaseOrderer), "EntityFrameworkCore.Jet.FunctionalTests")]