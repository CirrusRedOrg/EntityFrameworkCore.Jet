using System.Data;
using LibRed.Data;
using Xunit;

namespace LibRed.Ado.Tests;

public class LibRedCommandTests
{
    private static readonly string Northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");

    private static LibRedConnection OpenConnection()
    {
        var conn = new LibRedConnection($"Data Source={Northwind}");
        conn.Open();
        return conn;
    }

    [Fact]
    public void ExecuteReader_projects_rows_and_columns()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CustomerID, City FROM Customers WHERE City = 'Berlin'";

        using var reader = cmd.ExecuteReader();
        Assert.Equal(2, reader.FieldCount);
        Assert.Equal("CustomerID", reader.GetName(0));

        Assert.True(reader.Read());
        Assert.Equal("ALFKI", reader.GetString(0));
        Assert.Equal("Berlin", reader["City"]);
        Assert.False(reader.Read()); // only one Berlin customer
    }

    [Fact]
    public void Named_parameter_flows_from_command_to_engine()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CustomerID FROM Customers WHERE City = @city";
        var p = cmd.CreateParameter();
        p.ParameterName = "@city";
        p.Value = "London";
        cmd.Parameters.Add(p);

        int count = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) count++;
        Assert.Equal(6, count);
    }

    [Fact]
    public void Null_parameter_value_maps_to_sql_null()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CustomerID FROM Customers WHERE City = @city";
        var p = cmd.CreateParameter();
        p.ParameterName = "@city";
        p.Value = DBNull.Value;
        cmd.Parameters.Add(p);

        using var reader = cmd.ExecuteReader();
        Assert.False(reader.Read()); // City = NULL matches nothing
    }

    [Fact]
    public void ExecuteScalar_returns_first_column_of_first_row()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Orders";
        Assert.Equal(830L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    [Fact]
    public void Create_table_and_insert_via_ado_commands()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-ado-ddl-{Guid.NewGuid():N}.accdb");
        File.Copy(Northwind, path);
        try
        {
            using var conn = new LibRedConnection($"Data Source={path}");
            conn.Open();

            using (var ddl = conn.CreateCommand())
            {
                ddl.CommandText = "CREATE TABLE `Thing` (`Id` INTEGER PRIMARY KEY, `Label` TEXT(50))";
                ddl.ExecuteNonQuery();
            }
            using (var insert = conn.CreateCommand())
            {
                insert.CommandText = "INSERT INTO `Thing` (`Id`, `Label`) VALUES (@id, @label)";
                Add(insert, "@id", 99);
                Add(insert, "@label", "widget");
                Assert.Equal(1, insert.ExecuteNonQuery());
            }
            using (var select = conn.CreateCommand())
            {
                select.CommandText = "SELECT `Label` FROM `Thing` WHERE `Id` = 99";
                Assert.Equal("widget", select.ExecuteScalar());
            }
        }
        finally { File.Delete(path); }

        static void Add(System.Data.Common.DbCommand cmd, string name, object value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
        }
    }

    [Fact]
    public void Reader_reports_dbnull_for_missing_values()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        // FISSA has no orders; Region is null for plenty of customers.
        cmd.CommandText = "SELECT CustomerID, Region FROM Customers WHERE CustomerID = 'ALFKI'";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(1)); // ALFKI has no Region
        Assert.Equal(DBNull.Value, reader.GetValue(1));
    }
}
