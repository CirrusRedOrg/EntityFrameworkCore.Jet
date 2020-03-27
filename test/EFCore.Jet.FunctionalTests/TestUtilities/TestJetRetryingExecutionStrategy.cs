// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Data.Jet;

using System.Data.OleDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.FunctionalTests.TestUtilities
{
    public class TestJetRetryingExecutionStrategy : JetRetryingExecutionStrategy
    {
        private const bool ErrorNumberDebugMode = false;

        // CHECK: ACE/Jet error codes using ODBC and OLE DB.
        private static readonly int[] _additionalErrorNumbers =
        {
            -1, // Physical connection is not usable
            -2, // Timeout
            1807, // Could not obtain exclusive lock on database 'model'
            42008, // Mirroring (Only when a database is deleted and another one is created in fast succession)
            42019 // CREATE DATABASE operation failed
        };

        public TestJetRetryingExecutionStrategy()
            : base(
                new DbContext(
                    new DbContextOptionsBuilder()
                        .EnableServiceProviderCaching(false)
                        .UseJet(TestEnvironment.DefaultConnection, JetConfiguration.DefaultProviderFactory).Options),
                DefaultMaxRetryCount, DefaultMaxDelay, _additionalErrorNumbers)
        {
        }

        public TestJetRetryingExecutionStrategy(DbContext context)
            : base(context, DefaultMaxRetryCount, DefaultMaxDelay, _additionalErrorNumbers)
        {
        }

        public TestJetRetryingExecutionStrategy(DbContext context, TimeSpan maxDelay)
            : base(context, DefaultMaxRetryCount, maxDelay, _additionalErrorNumbers)
        {
        }

        public TestJetRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
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
                && exception is OleDbException oleDbException)
            {
                var message = "Didn't retry on";
                foreach (OleDbError err in oleDbException.Errors)
                {
                    message += " " + err.NativeError;
                }

                message += Environment.NewLine;
                throw new InvalidOperationException(message + exception, exception);
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
