// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using EntityFrameworkCore.Jet.Infrastructure.Internal;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.Jet.Infrastructure
{
    /// <summary>
    /// Provides extension methods on <see cref="DbContextOptionsBuilder"/> and <see cref="DbContextOptionsBuilder{T}"/>
    /// to configure a <see cref="DbContext"/> to use with Jet/Access and EntityFrameworkCore.Jet.
    /// </summary>
    public class JetDbContextOptionsBuilder
        : RelationalDbContextOptionsBuilder<JetDbContextOptionsBuilder, JetOptionsExtension>
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="JetDbContextOptionsBuilder" /> class.
        /// </summary>
        /// <param name="optionsBuilder"> The options builder. </param>
        public JetDbContextOptionsBuilder([NotNull] DbContextOptionsBuilder optionsBuilder)
            : base(optionsBuilder)
        {
        }

        /// <summary>
        ///     Use a ROW_NUMBER() in queries instead of OFFSET/FETCH. This method is backwards-compatible to Jet 2005.
        /// </summary>
        // [Obsolete("Row-number paging is no longer supported. See https://aka.ms/AA6h122 for more information.")]
        // public virtual JetDbContextOptionsBuilder UseRowNumberForPaging(bool useRowNumberForPaging = true)
        //    => WithOption(e => e.WithRowNumberPaging(useRowNumberForPaging));

        /// <summary>
        ///     Jet/ACE doesn't natively support row skipping. When this option is enabled, row skipping will be
        ///     emulated in the most outer SELECT statement, by letting the JetDataReader ignore as many returned rows
        ///     as should have been skipped by the database.
        ///     This will only work when `JetCommand.ExecuteDataReader()` is beeing used to execute the `JetCommand`.
        ///     It is recommanded to not use this option, but to switch to client evaluation instead, by inserting a
        ///     call to either `AsEnumerable()`, `AsAsyncEnumerable()`, `ToList()`, or `ToListAsync()` and only then
        ///     to use `Skip()`. This will work in all cases and independent of the specific `JetCommand.Execute()`
        ///     method called. 
        /// </summary>
        [Obsolete("This method exists for backward compatibility reasons only. Switch to client evaluation instead.")]
        public virtual JetDbContextOptionsBuilder UseOuterSelectSkipEmulationViaDataReader(bool enabled = true)
            => WithOption(e => e.WithUseOuterSelectSkipEmulationViaDataReader(enabled));

        /// <summary>
        ///     Configures the context to use the default retrying <see cref="IExecutionStrategy" />.
        /// </summary>
        public virtual JetDbContextOptionsBuilder EnableRetryOnFailure()
            => ExecutionStrategy(c => new JetRetryingExecutionStrategy(c));

        /// <summary>
        ///     Configures the context to use the default retrying <see cref="IExecutionStrategy" />.
        /// </summary>
        public virtual JetDbContextOptionsBuilder EnableRetryOnFailure(int maxRetryCount)
            => ExecutionStrategy(c => new JetRetryingExecutionStrategy(c, maxRetryCount));

        /// <summary>
        ///     Configures the context to use the default retrying <see cref="IExecutionStrategy" />.
        /// </summary>
        /// <param name="maxRetryCount"> The maximum number of retry attempts. </param>
        /// <param name="maxRetryDelay"> The maximum delay between retries. </param>
        /// <param name="errorNumbersToAdd"> Additional SQL error numbers that should be considered transient. </param>
        public virtual JetDbContextOptionsBuilder EnableRetryOnFailure(
            int maxRetryCount,
            TimeSpan maxRetryDelay,
            [CanBeNull] ICollection<int> errorNumbersToAdd)
            => ExecutionStrategy(c => new JetRetryingExecutionStrategy(c, maxRetryCount, maxRetryDelay, errorNumbersToAdd));
    }
}