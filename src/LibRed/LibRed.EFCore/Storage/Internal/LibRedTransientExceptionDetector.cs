using LibRed.Data;

namespace EntityFrameworkCore.LibRed.Storage.Internal;

/// <summary>
///     Detects the exceptions caused by LibRed transient failures. Unlike EFCore.Jet's detector, this
///     does not look at <c>OleDbException</c>/<c>OdbcException</c> — LibRed is a native managed engine and
///     surfaces its own <see cref="LibRedException" />. A <see cref="TimeoutException" /> is always
///     transient; a <see cref="LibRedException" /> is transient when its <see cref="LibRedException.Number" />
///     is in the caller-supplied set of transient error numbers (LibRed has no built-in transient codes yet).
/// </summary>
public static class LibRedTransientExceptionDetector
{
    public static bool ShouldRetryOn(Exception ex)
        => ShouldRetryOn(ex, additionalErrorNumbers: null);

    public static bool ShouldRetryOn(Exception ex, ISet<int>? additionalErrorNumbers)
    {
        if (ex is TimeoutException)
        {
            return true;
        }

        if (ex is LibRedException libRedException
            && additionalErrorNumbers is not null
            && additionalErrorNumbers.Contains(libRedException.Number))
        {
            return true;
        }

        return false;
    }
}
