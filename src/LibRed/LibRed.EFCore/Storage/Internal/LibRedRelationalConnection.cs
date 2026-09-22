using LibRed.Data;

namespace EntityFrameworkCore.LibRed.Storage.Internal;

/// <summary>
/// Relational connection that creates a native <see cref="LibRedConnection"/> instead of the
/// ODBC/OLE DB <c>JetConnection</c>. This is the single service LibRed must override to sit
/// the EFCore.Jet provider on its own managed engine.
/// </summary>
public class LibRedRelationalConnection(RelationalConnectionDependencies dependencies)
    : RelationalConnection(dependencies), ILibRedRelationalConnection
{
    protected override DbConnection CreateDbConnection()
        => new LibRedConnection { ConnectionString = ConnectionString };
}