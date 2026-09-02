using EntityFrameworkCore.Jet.Storage.Internal;
using LibRed.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace EntityFrameworkCore.LibRed.Storage.Internal;

/// <summary>
/// Database creator that answers the existence and "has tables" checks from LibRed's own
/// catalog instead of EFCore.Jet's INFORMATION_SCHEMA query (which is served by DAO/ADOX).
/// This lets EnsureCreated run against LibRed-managed files cross-platform without Access.
/// </summary>
/// <remarks>
/// Not sealed: mirrors EFCore.Jet's <see cref="JetDatabaseCreator"/>, which is also open for
/// test/derived doubles (see <c>JetDatabaseCreatorTest.TestDatabaseCreator</c>).
/// </remarks>
public class LibRedDatabaseCreator(
    RelationalDatabaseCreatorDependencies dependencies,
    IRelationalConnection relationalConnection)
    : RelationalDatabaseCreator(dependencies)
{
    // Jet's Create/Delete go through relationalConnection.CreateEmptyConnection() - a "masterless"
    // connection used to run a CREATE/DROP DATABASE migration command. LibRedRelationalConnection
    // doesn't support that (there's nothing to connect to before the file exists), so these bypass
    // it entirely and go straight through LibRedConnection's own bootstrap (see its CreateDatabase
    // remarks: DatabaseCreator.CreateEmpty synthesises the file from scratch - no DAO/ADOX).
    public override void Create()
        => LibRedConnection.CreateDatabase(relationalConnection.DbConnection.ConnectionString);

    public override void Delete()
    {
        relationalConnection.Close();
        LibRedConnection.DropDatabase(relationalConnection.DbConnection.ConnectionString);
    }

    public override bool Exists()
        => LibRedConnection.DatabaseExists(relationalConnection.DbConnection.ConnectionString);

    public override bool HasTables()
    {
        relationalConnection.Open();
        try
        {
            return ((LibRedConnection)relationalConnection.DbConnection).HasUserTables();
        }
        finally
        {
            relationalConnection.Close();
        }
    }

    public override Task<bool> HasTablesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(HasTables());
    }
}
