// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public static class JetDbContextOptionsBuilderExtensions
    {
        public static JetDbContextOptionsBuilder ApplyConfiguration(this JetDbContextOptionsBuilder optionsBuilder)
        {
            var maxBatch = TestEnvironment.GetInt(nameof(JetDbContextOptionsBuilder.MaxBatchSize));
            if (maxBatch.HasValue)
            {
                optionsBuilder.MaxBatchSize(maxBatch.Value);
            }

            optionsBuilder.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);

            optionsBuilder.ExecutionStrategy(d => new TestJetRetryingExecutionStrategy(d));

            optionsBuilder.CommandTimeout(JetTestStore.CommandTimeout);

            return optionsBuilder;
        }
    }
}
