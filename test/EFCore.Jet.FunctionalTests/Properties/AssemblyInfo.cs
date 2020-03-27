// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Xunit;

// Skip the entire assembly if not on Windows and no external SQL Server is configured
[assembly: JetConfiguredCondition]

// [assembly: TestCaseOrderer("EntityFrameworkCore.Jet.FunctionalTests.TestUtilities.AscendingTestCaseOrderer", "EntityFrameworkCore.Jet.FunctionalTests")]