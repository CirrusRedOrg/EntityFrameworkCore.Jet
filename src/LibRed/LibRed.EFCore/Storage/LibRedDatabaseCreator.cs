using EntityFrameworkCore.Jet.Storage.Internal;
using LibRed.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace LibRed.EntityFrameworkCore.Storage;

/// <summary>
/// Database creator that answers the existence and "has tables" checks from LibRed's own
/// catalog instead of EFCore.Jet's INFORMATION_SCHEMA query (which is served by DAO/ADOX).
/// This lets EnsureCreated run against LibRed-managed files cross-platform without Access.
/// Physical database creation (<see cref="JetDatabaseCreator.Create"/>) is still inherited and
/// not yet implemented for LibRed.
/// </summary>
public sealed class LibRedDatabaseCreator(
    RelationalDatabaseCreatorDependencies dependencies,
    IJetRelationalConnection relationalConnection,
    IRawSqlCommandBuilder rawSqlCommandBuilder)
    : JetDatabaseCreator(dependencies, relationalConnection, rawSqlCommandBuilder)
{
    private readonly IJetRelationalConnection _connection = relationalConnection;

    public override bool Exists()
        => LibRedConnection.DatabaseExists(_connection.DbConnection.ConnectionString);

    public override bool HasTables()
    {
        _connection.Open();
        try
        {
            return ((LibRedConnection)_connection.DbConnection).HasUserTables();
        }
        finally
        {
            _connection.Close();
        }
    }

    public override Task<bool> HasTablesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(HasTables());
}
