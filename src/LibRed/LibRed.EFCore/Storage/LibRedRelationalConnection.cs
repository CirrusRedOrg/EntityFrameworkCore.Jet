using System.Data.Common;
using EntityFrameworkCore.Jet.Storage.Internal;
using LibRed.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace LibRed.EntityFrameworkCore.Storage;

/// <summary>
/// Relational connection that creates a native <see cref="LibRedConnection"/> instead of the
/// ODBC/OLE DB <c>JetConnection</c>. This is the single service LibRed must override to sit
/// the EFCore.Jet provider on its own managed engine.
/// </summary>
public sealed class LibRedRelationalConnection(RelationalConnectionDependencies dependencies)
    : JetRelationalConnection(dependencies)
{
    protected override DbConnection CreateDbConnection()
        => new LibRedConnection { ConnectionString = ConnectionString };

    public override IJetRelationalConnection CreateEmptyConnection()
        => throw new NotSupportedException(
            "LibRed does not support masterless (empty) connections yet — database creation/drop is not implemented.");
}
