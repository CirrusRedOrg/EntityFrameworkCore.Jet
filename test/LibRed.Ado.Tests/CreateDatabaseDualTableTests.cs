using LibRed.Data;
using Xunit;

namespace LibRed.Ado.Tests;

/// <summary>
/// A LibRed-created database must contain the single-row <c>#Dual</c> helper table. EFCore.Jet's query generator
/// renders FROM-less scalar queries (All/Any/Count/constant projections) as <c>FROM (SELECT COUNT(*) FROM `#Dual`)</c>,
/// so without it those queries fail to bind. The DAO/ADOX creation path created it via EnsureDualTable; native
/// creation (DatabaseCreator.CreateEmpty) must do the same — this guards that it does.
/// </summary>
public class CreateDatabaseDualTableTests
{
    [Fact]
    public void Native_create_produces_a_queryable_single_row_dual_table()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred_dual_{Guid.NewGuid():N}.accdb");
        string cs = $"Data Source={path}";
        try
        {
            LibRedConnection.CreateDatabase(cs);

            using var conn = new LibRedConnection(cs);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM `#Dual`";
            Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Dual_table_is_hidden_from_user_tables()
    {
        // The leading '#' keeps #Dual out of the user-table catalog, so EnsureCreated's HasTables() check
        // doesn't treat a fresh (schema-less) database as already populated.
        string path = Path.Combine(Path.GetTempPath(), $"libred_dual_{Guid.NewGuid():N}.accdb");
        string cs = $"Data Source={path}";
        try
        {
            LibRedConnection.CreateDatabase(cs);
            using var conn = new LibRedConnection(cs);
            conn.Open();
            Assert.False(((LibRedConnection)conn).HasUserTables());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
