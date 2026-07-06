using System.Data.Common;

namespace LibRed.Data;

/// <summary>
/// The exception raised for errors originating in the LibRed provider. It carries a
/// <see cref="Number"/> so the retrying execution strategy can decide whether a failure is
/// transient without depending on the ODBC/OLE DB exception types — LibRed is a native managed
/// engine and does not surface <c>OleDbException</c>/<c>OdbcException</c>.
/// </summary>
public sealed class LibRedException : DbException
{
    public LibRedException(string message, int number)
        : base(message)
    {
        Number = number;
        HResult = number;
    }

    public LibRedException(string message, int number, Exception? innerException)
        : base(message, innerException)
    {
        Number = number;
        HResult = number;
    }

    /// <summary>The LibRed error number, used to classify transient failures.</summary>
    public int Number { get; }
}
