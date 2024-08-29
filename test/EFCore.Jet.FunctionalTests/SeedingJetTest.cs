// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Data;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class SeedingJetTest : SeedingTestBase
    {
        protected override SeedingContext CreateContextWithEmptyDatabase(string testId)
        {
            var context = new SeedingJetContext(testId);

            context.Database.EnsureClean();

            return context;
        }

        protected override TestStore TestStore => JetTestStore.Create("SeedingTest");

        protected class SeedingJetContext(string testId) : SeedingContext(testId)
        {
            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder.UseJet(JetTestStore.CreateConnectionString($"Seeds{TestId}"), TestEnvironment.DataAccessProviderFactory);
        }
    }
}
