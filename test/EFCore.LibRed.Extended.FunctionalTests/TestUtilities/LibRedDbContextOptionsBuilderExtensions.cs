// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.LibRed.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.LibRed.Extended.FunctionalTests.TestUtilities
{
    public static class LibRedDbContextOptionsBuilderExtensions
    {
        public static LibRedDbContextOptionsBuilder ApplyConfiguration(this LibRedDbContextOptionsBuilder optionsBuilder)
        {
            var maxBatch = TestEnvironment.GetInt(nameof(LibRedDbContextOptionsBuilder.MaxBatchSize));
            if (maxBatch.HasValue)
            {
                optionsBuilder.MaxBatchSize(maxBatch.Value);
            }

            optionsBuilder.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);

            optionsBuilder.ExecutionStrategy(d => new TestLibRedRetryingExecutionStrategy(d));

            optionsBuilder.CommandTimeout(LibRedTestStore.CommandTimeout);

            return optionsBuilder;
        }
    }
}
