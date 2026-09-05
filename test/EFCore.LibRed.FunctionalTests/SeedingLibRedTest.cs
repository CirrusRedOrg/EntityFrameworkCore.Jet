// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.LibRed.FunctionalTests
{
    public class SeedingLibRedTest : SeedingTestBase
    {
        protected override SeedingContext CreateContextWithEmptyDatabase(string testId)
        {
            var context = new SeedingLibRedContext(testId);

            context.Database.EnsureClean();

            return context;
        }

        protected override TestStore TestStore => LibRedTestStore.Create("SeedingTest");

        protected class SeedingLibRedContext(string testId) : SeedingContext(testId)
        {
            protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
                => optionsBuilder.UseLibRed(LibRedTestStore.CreateConnectionString($"Seeds{TestId}"), b => b.UseSqlMode());
        }
    }
}
