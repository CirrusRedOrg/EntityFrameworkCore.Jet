// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using LibRed.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.LibRed.FunctionalTests.TestUtilities
{
    public class TestLibRedRetryingExecutionStrategy : LibRedRetryingExecutionStrategy
    {
        private const bool ErrorNumberDebugMode = false;

        // LibRed error numbers treated as transient by the tests (carried on LibRedException.Number).
        private static readonly int[] _additionalErrorNumbers =
        [
            -1, // Physical connection is not usable
            -2, // Timeout
            1807, // Could not obtain exclusive lock on database 'model'
            42008, // Mirroring (Only when a database is deleted and another one is created in fast succession)
            42019 // CREATE DATABASE operation failed
        ];

        public TestLibRedRetryingExecutionStrategy()
            : base(
                new DbContext(
                    new DbContextOptionsBuilder()
                        .EnableServiceProviderCaching(false)
                        .UseLibRed(TestEnvironment.DefaultConnection, b => b.UseSqlMode()).Options),
                DefaultMaxRetryCount, DefaultMaxDelay, _additionalErrorNumbers)
        {
        }

        public TestLibRedRetryingExecutionStrategy(DbContext context)
            : base(context, DefaultMaxRetryCount, DefaultMaxDelay, _additionalErrorNumbers)
        {
        }

        public TestLibRedRetryingExecutionStrategy(DbContext context, TimeSpan maxDelay)
            : base(context, DefaultMaxRetryCount, maxDelay, _additionalErrorNumbers)
        {
        }

        public TestLibRedRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
            : base(dependencies, DefaultMaxRetryCount, DefaultMaxDelay, _additionalErrorNumbers)
        {
        }

        protected override bool ShouldRetryOn(Exception exception)
        {
            if (base.ShouldRetryOn(exception))
            {
                return true;
            }

#pragma warning disable 162
            if (ErrorNumberDebugMode
                && exception is LibRedException libRedException)
            {
                throw new InvalidOperationException(
                    $"Didn't retry on {libRedException.Number}{Environment.NewLine}{exception}", exception);
            }
#pragma warning restore 162

            return exception is InvalidOperationException invalidOperationException
                && invalidOperationException.Message == "Internal .Net Framework Data Provider error 6.";
        }

        public new virtual TimeSpan? GetNextDelay(Exception lastException)
        {
            ExceptionsEncountered.Add(lastException);
            return base.GetNextDelay(lastException);
        }
    }
}
