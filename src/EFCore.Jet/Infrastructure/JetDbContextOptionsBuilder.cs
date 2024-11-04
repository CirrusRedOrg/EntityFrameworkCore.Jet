// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.Infrastructure.Internal;

namespace EntityFrameworkCore.Jet.Infrastructure
{
    /// <summary>
    /// Provides extension methods on <see cref="DbContextOptionsBuilder"/> and <see cref="DbContextOptionsBuilder{T}"/>
    /// to configure a <see cref="DbContext"/> to use with Jet/Access and EntityFrameworkCore.Jet.
    /// </summary>
    /// <remarks>
    ///     Initializes a new instance of the <see cref="JetDbContextOptionsBuilder" /> class.
    /// </remarks>
    /// <param name="optionsBuilder"> The options builder. </param>
    public class JetDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
                : RelationalDbContextOptionsBuilder<JetDbContextOptionsBuilder, JetOptionsExtension>(optionsBuilder)
    {

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
        ///     Configures the context support milliseconds in `DateTime`, `DateTimeOffset` and `TimeSpan` values when
        ///     accessing Jet databases. Jet has no native support for milliseconds, therefore this feature is opt-in.
        /// </summary>
        public virtual JetDbContextOptionsBuilder EnableMillisecondsSupport(bool enabled = true)
            => WithOption(e => e.WithEnableMillisecondsSupport(enabled));


        /// <summary>
        ///     Set this to enabled to map the System.String CLR type to the Jet `Short Text` data type instead of the
        ///     Long Text data type. This will limit the maximum length of strings to 255 characters.
        ///     As System.String does not have a size it is normally mapped to 'lonchar' or 'memo' (SQL Server is 'nvarchar(max)'
        ///     Jet/Ace has limitations when using memo for strings:
        ///     - Joins based on the memo column are not supported
        ///     - Ordering the column (specially the implicit ordering) can be a bit different to expected behaviour 
        /// </summary>
        public virtual JetDbContextOptionsBuilder UseShortTextForSystemString(bool enabled = true)
            => WithOption(e => e.WithUseShortTextForSystemString(enabled));

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
            ICollection<int> errorNumbersToAdd)
            => ExecutionStrategy(c => new JetRetryingExecutionStrategy(c, maxRetryCount, maxRetryDelay, errorNumbersToAdd));
    }
}