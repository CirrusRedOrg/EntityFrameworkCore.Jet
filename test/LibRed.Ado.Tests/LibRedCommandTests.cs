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
    public void TimeSpan_parameter_round_trips_through_a_datetime_column()
    {
        // Jet has no TimeSpan type; EF stores it in a datetime column as an offset from 1899-12-30.
        // The command must accept a TimeSpan value and the reader must give it back as a TimeSpan.
        string path = Path.Combine(Path.GetTempPath(), $"libred-ts-{Guid.NewGuid():N}.accdb");
        File.Copy(Northwind, path);
        try
        {
            using var conn = new LibRedConnection($"Data Source={path}");
            conn.Open();

            using (var create = conn.CreateCommand())
            {
                create.CommandText = "CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY, `Dur` DATETIME)";
                create.ExecuteNonQuery();
            }
            using (var insert = conn.CreateCommand())
            {
                insert.CommandText = "INSERT INTO `T` (`Id`, `Dur`) VALUES (1, @d)";
                var p = insert.CreateParameter();
                p.ParameterName = "@d";
                p.Value = TimeSpan.FromHours(5).Add(TimeSpan.FromMinutes(30));
                insert.Parameters.Add(p);
                Assert.Equal(1, insert.ExecuteNonQuery());
            }
            using (var read = conn.CreateCommand())
            {
                read.CommandText = "SELECT `Dur` FROM `T` WHERE `Id` = 1";
                using var reader = read.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal(new TimeSpan(5, 30, 0), reader.GetFieldValue<TimeSpan>(0));
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Table_created_on_one_connection_is_visible_to_a_new_connection()
    {
        // EF creates the schema and then (after closing) inserts on a fresh connection. A table
        // created + committed via one connection must be visible when another opens the same file.
        string path = Path.Combine(Path.GetTempPath(), $"libred-xconn-{Guid.NewGuid():N}.accdb");
        File.Copy(Northwind, path);
        try
        {
            using (var c1 = new LibRedConnection($"Data Source={path}"))
            {
                c1.Open();
                using var create = c1.CreateCommand();
                create.CommandText = "CREATE TABLE `Foo` (`Id` INTEGER PRIMARY KEY, `N` TEXT(10))";
                create.ExecuteNonQuery();
            } // c1 disposed → must flush

            using (var c2 = new LibRedConnection($"Data Source={path}"))
            {
                c2.Open();
                using var insert = c2.CreateCommand();
                insert.CommandText = "INSERT INTO `Foo` (`Id`, `N`) VALUES (1, 'x')";
                Assert.Equal(1, insert.ExecuteNonQuery()); // throws "table not found" if not visible
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void ExecuteReader_runs_an_insert_and_reports_records_affected()
    {
        // EF Core executes inserts through ExecuteReader and inspects RecordsAffected — so the reader
        // path must handle DML, not just queries.
        string path = Path.Combine(Path.GetTempPath(), $"libred-ado-{Guid.NewGuid():N}.accdb");
        File.Copy(Northwind, path);
        try
        {
            using (var conn = new LibRedConnection($"Data Source={path}"))
            {
                conn.Open();

                using (var create = conn.CreateCommand())
                {
                    create.CommandText = "CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY, `N` TEXT(10))";
                    using var cr = create.ExecuteReader();      // DDL via the reader path
                    Assert.Equal(0, cr.RecordsAffected);
                }
                using (var insert = conn.CreateCommand())
                {
                    insert.CommandText = "INSERT INTO `T` (`Id`, `N`) VALUES (@id, @n)";
                    var pid = insert.CreateParameter(); pid.ParameterName = "@id"; pid.Value = 1; insert.Parameters.Add(pid);
                    var pn = insert.CreateParameter(); pn.ParameterName = "@n"; pn.Value = "x"; insert.Parameters.Add(pn);

                    using var ir = insert.ExecuteReader();       // the EF path
                    Assert.Equal(1, ir.RecordsAffected);
                    Assert.False(ir.Read());                     // an insert yields no rows
                }
                using (var sel = conn.CreateCommand())
                {
                    sel.CommandText = "SELECT `N` FROM `T` WHERE `Id` = 1";
                    Assert.Equal("x", sel.ExecuteScalar());
                }
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Getordinal_getvalues_and_typed_accessors_work_after_read()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ProductID, ProductName, UnitPrice FROM Products WHERE ProductID = @id";
        var p = cmd.CreateParameter();
        p.ParameterName = "@id";
        p.Value = 1;
        cmd.Parameters.Add(p);

        using var reader = cmd.ExecuteReader();
        Assert.True(reader.HasRows);
        Assert.True(reader.Read());

        Assert.Equal(0, reader.GetOrdinal("productid")); // case-insensitive lookup
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal("Chai", reader.GetString(reader.GetOrdinal("ProductName")));
        Assert.Equal(18.0m, reader.GetDecimal(2));

        object[] values = new object[5];
        Assert.Equal(3, reader.GetValues(values));
        Assert.Equal(1, values[0]);
        Assert.Equal("Chai", values[1]);
        Assert.Equal(18.0m, Convert.ToDecimal(values[2]));
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
