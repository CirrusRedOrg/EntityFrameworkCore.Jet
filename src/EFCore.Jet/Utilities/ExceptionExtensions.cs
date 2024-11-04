

// ReSharper disable once CheckNamespace
namespace System;

public static class ExceptionExtensions
{
    public static IEnumerable<Exception> FlattenHierarchy(this Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var innerException = ex;
        do
        {
            yield return innerException;
            innerException = innerException.InnerException;
        }
        while (innerException != null);
    }
}