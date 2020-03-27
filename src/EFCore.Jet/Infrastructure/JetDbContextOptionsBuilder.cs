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
    ///     <para>
    ///         Allows Jet specific configuration to be performed on <see cref="DbContextOptions" />.
    ///     </para>
    ///     <para>
    ///         Instances of this class are returned from a call to
    ///         <see
    ///             cref="JetDbContextOptionsExtensions.UseJet(DbContextOptionsBuilder, JetConfiguration.DefaultProviderFactory,string,System.Action{JetDbContextOptionsBuilder})" />
    ///         and it is not designed to be directly constructed in your application code.
    ///     </para>
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