// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.LibRed.Storage.Internal;
using Microsoft.EntityFrameworkCore.Storage;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>
///     An <see cref="IExecutionStrategy" /> implementation for retrying failed executions
///     on LibRed.
/// </summary>
/// <remarks>
///     Same shape as EFCore.Jet's retrying strategy, but transient-failure detection is LibRed-native
///     (<see cref="LibRedTransientExceptionDetector" />): LibRed doesn't raise
///     <c>OleDbException</c>/<c>OdbcException</c>, so this retries on <see cref="TimeoutException" /> plus
///     any <see cref="LibRed.Data.LibRedException" /> whose error number the caller passes to
///     <c>errorNumbersToAdd</c>.
/// </remarks>
public class LibRedRetryingExecutionStrategy : ExecutionStrategy
{
    private readonly HashSet<int>? _additionalErrorNumbers;

    /// <summary>
    ///     Creates a new instance of <see cref="LibRedRetryingExecutionStrategy" />.
    /// </summary>
    /// <param name="context"> The context on which the operations will be invoked. </param>
    /// <remarks>
    ///     The default retry limit is 6, which means that the total amount of time spent before failing is about a minute.
    /// </remarks>
    public LibRedRetryingExecutionStrategy(
        DbContext context)
        : this(context, DefaultMaxRetryCount)
    {
    }

    /// <summary>
    ///     Creates a new instance of <see cref="LibRedRetryingExecutionStrategy" />.
    /// </summary>
    /// <param name="dependencies"> Parameter object containing service dependencies. </param>
    public LibRedRetryingExecutionStrategy(
        ExecutionStrategyDependencies dependencies)
        : this(dependencies, DefaultMaxRetryCount)
    {
    }

    /// <summary>
    ///     Creates a new instance of <see cref="LibRedRetryingExecutionStrategy" />.
    /// </summary>
    /// <param name="context"> The context on which the operations will be invoked. </param>
    /// <param name="maxRetryCount"> The maximum number of retry attempts. </param>
    public LibRedRetryingExecutionStrategy(
        DbContext context,
        int maxRetryCount)
        : this(context, maxRetryCount, DefaultMaxDelay, errorNumbersToAdd: null)
    {
    }

    /// <summary>
    ///     Creates a new instance of <see cref="LibRedRetryingExecutionStrategy" />.
    /// </summary>
    /// <param name="dependencies"> Parameter object containing service dependencies. </param>
    /// <param name="maxRetryCount"> The maximum number of retry attempts. </param>
    public LibRedRetryingExecutionStrategy(
        ExecutionStrategyDependencies dependencies,
        int maxRetryCount)
        : this(dependencies, maxRetryCount, DefaultMaxDelay, errorNumbersToAdd: null)
    {
    }

    /// <summary>
    ///     Creates a new instance of <see cref="LibRedRetryingExecutionStrategy" />.
    /// </summary>
    /// <param name="context"> The context on which the operations will be invoked. </param>
    /// <param name="maxRetryCount"> The maximum number of retry attempts. </param>
    /// <param name="maxRetryDelay"> The maximum delay between retries. </param>
    /// <param name="errorNumbersToAdd"> Additional error numbers that should be considered transient. </param>
    public LibRedRetryingExecutionStrategy(
        DbContext context,
        int maxRetryCount,
        TimeSpan maxRetryDelay,
        IEnumerable<int>? errorNumbersToAdd)
        : base(
            context,
            maxRetryCount,
            maxRetryDelay)
    {
        _additionalErrorNumbers = errorNumbersToAdd?.ToHashSet();
    }

    /// <summary>
    ///     Creates a new instance of <see cref="LibRedRetryingExecutionStrategy" />.
    /// </summary>
    /// <param name="dependencies"> Parameter object containing service dependencies. </param>
    /// <param name="maxRetryCount"> The maximum number of retry attempts. </param>
    /// <param name="maxRetryDelay"> The maximum delay between retries. </param>
    /// <param name="errorNumbersToAdd"> Additional error numbers that should be considered transient. </param>
    public LibRedRetryingExecutionStrategy(
        ExecutionStrategyDependencies dependencies,
        int maxRetryCount,
        TimeSpan maxRetryDelay,
        IEnumerable<int>? errorNumbersToAdd)
        : base(dependencies, maxRetryCount, maxRetryDelay)
    {
        _additionalErrorNumbers = errorNumbersToAdd?.ToHashSet();
    }

    /// <summary>
    ///     Determines whether the specified exception represents a transient failure that can be
    ///     compensated by a retry. Additional exceptions to retry on can be passed to the constructor.
    /// </summary>
    /// <param name="exception"> The exception object to be verified. </param>
    /// <returns>
    ///     <c>true</c> if the specified exception is considered as transient, otherwise <c>false</c>.
    /// </returns>
    protected override bool ShouldRetryOn(Exception exception)
        => LibRedTransientExceptionDetector.ShouldRetryOn(exception, _additionalErrorNumbers);

    /// <summary>
    ///     Determines whether the operation should be retried and the delay before the next attempt.
    /// </summary>
    /// <param name="lastException"> The exception thrown during the last execution attempt. </param>
    /// <returns>
    ///     Returns the delay indicating how long to wait for before the next execution attempt if the operation should be retried;
    ///     <c>null</c> otherwise
    /// </returns>
    protected override TimeSpan? GetNextDelay(Exception lastException)
    {
        var baseDelay = base.GetNextDelay(lastException);
        if (baseDelay == null)
        {
            return null;
        }
        return baseDelay;
    }
}
