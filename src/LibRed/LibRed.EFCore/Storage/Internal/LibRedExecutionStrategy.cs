using EntityFrameworkCore.Jet.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.LibRed.Storage.Internal;

/// <summary>
///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
///     the same compatibility standards as public APIs. It may be changed or removed without notice in
///     any release. You should only use it directly in your code with extreme caution and knowing that
///     doing so can result in application failures when updating to a new Entity Framework Core release.
/// </summary>
/// <remarks>
///     The default, non-retrying strategy; transient-failure detection is LibRed-native
///     (<see cref="LibRedTransientExceptionDetector" />).
/// </remarks>
public class LibRedExecutionStrategy(ExecutionStrategyDependencies dependencies) : IExecutionStrategy
{
    private ExecutionStrategyDependencies Dependencies { get; } = dependencies;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual bool RetriesOnFailure => false;

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual TResult Execute<TState, TResult>(
        TState state,
        Func<DbContext, TState, TResult> operation,
        Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded)
    {
        try
        {
            return operation(Dependencies.CurrentContext.Context, state);
        }
        catch (Exception ex) when (ExecutionStrategy.CallOnWrappedException(ex, LibRedTransientExceptionDetector.ShouldRetryOn))
        {
            throw new InvalidOperationException(JetStrings.TransientExceptionDetected, ex);
        }
    }

    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public virtual async Task<TResult> ExecuteAsync<TState, TResult>(
        TState state,
        Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
        Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation(Dependencies.CurrentContext.Context, state, cancellationToken);
        }
        catch (Exception ex) when (ExecutionStrategy.CallOnWrappedException(ex, LibRedTransientExceptionDetector.ShouldRetryOn))
        {
            throw new InvalidOperationException(JetStrings.TransientExceptionDetected, ex);
        }
    }
}
