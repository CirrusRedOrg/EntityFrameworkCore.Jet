// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.LibRed.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.LibRed.Infrastructure
{
    /// <summary>
    /// Provides extension methods on <see cref="DbContextOptionsBuilder"/> and <see cref="DbContextOptionsBuilder{T}"/>
    /// to configure a <see cref="DbContext"/> to use with Jet/Access and EntityFrameworkCore.LibRed.
    /// </summary>
    /// <remarks>
    ///     Initializes a new instance of the <see cref="LibRedDbContextOptionsBuilder" /> class.
    /// </remarks>
    /// <param name="optionsBuilder"> The options builder. </param>
    public class LibRedDbContextOptionsBuilder(DbContextOptionsBuilder optionsBuilder)
                : RelationalDbContextOptionsBuilder<LibRedDbContextOptionsBuilder, LibRedOptionsExtension>(optionsBuilder)
    {
        /// <summary>
        ///     Set this to enabled to map the System.String CLR type to the Jet `Short Text` data type instead of the
        ///     Long Text data type. This will limit the maximum length of strings to 255 characters.
        ///     As System.String does not have a size it is normally mapped to 'lonchar' or 'memo' (SQL Server is 'nvarchar(max)'
        ///     Jet/Ace has limitations when using memo for strings:
        ///     - Joins based on the memo column are not supported
        ///     - Ordering the column (specially the implicit ordering) can be a bit different to expected behaviour 
        /// </summary>
        public virtual LibRedDbContextOptionsBuilder UseShortTextForSystemString(bool enabled = true)
            => WithOption(e => e.WithUseShortTextForSystemString(enabled));

        /// <summary>
        ///     Configures the context to use the default retrying <see cref="IExecutionStrategy" />.
        /// </summary>
        public virtual LibRedDbContextOptionsBuilder EnableRetryOnFailure()
            => ExecutionStrategy(c => new LibRedRetryingExecutionStrategy(c));

        /// <summary>
        ///     Configures the context to use the default retrying <see cref="IExecutionStrategy" />.
        /// </summary>
        public virtual LibRedDbContextOptionsBuilder EnableRetryOnFailure(int maxRetryCount)
            => ExecutionStrategy(c => new LibRedRetryingExecutionStrategy(c, maxRetryCount));

        /// <summary>
        ///     Configures the context to use the default retrying <see cref="IExecutionStrategy" />.
        /// </summary>
        /// <param name="maxRetryCount"> The maximum number of retry attempts. </param>
        /// <param name="maxRetryDelay"> The maximum delay between retries. </param>
        /// <param name="errorNumbersToAdd"> Additional SQL error numbers that should be considered transient. </param>
        public virtual LibRedDbContextOptionsBuilder EnableRetryOnFailure(
            int maxRetryCount,
            TimeSpan maxRetryDelay,
            ICollection<int> errorNumbersToAdd)
            => ExecutionStrategy(c => new LibRedRetryingExecutionStrategy(c, maxRetryCount, maxRetryDelay, errorNumbersToAdd));
    }
}