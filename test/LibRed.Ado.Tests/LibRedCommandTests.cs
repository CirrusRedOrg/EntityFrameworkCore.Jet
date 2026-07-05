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
    public void Column_metadata_is_available_before_the_first_read()
    {
        // EF Core's BufferedDataReader reads GetFieldType/GetDataTypeName before any Read().
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ProductID, ProductName FROM Products";

        using var reader = cmd.ExecuteReader();
        Assert.True(reader.HasRows);
        Assert.Equal(2, reader.FieldCount);
        Assert.Equal("ProductID", reader.GetName(0));
        Assert.Equal(typeof(int), reader.GetFieldType(0));         // must not throw before Read()
        Assert.NotEmpty(reader.GetDataTypeName(1));
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
    public void Insert_then_select_batch_reads_the_generated_key_back()
    {
        // The exact shape EF Core emits to fetch a store-generated key: an INSERT followed, in the SAME
        // command, by a guarded SELECT that reads the AutoNumber back via @@ROWCOUNT / @@IDENTITY. The ADO
        // layer splits the batch and runs both through one engine so the session variables carry across.
        string path = Path.Combine(Path.GetTempPath(), $"libred-ident-{Guid.NewGuid():N}.accdb");
        File.Copy(Northwind, path);
        try
        {
            using var conn = new LibRedConnection($"Data Source={path}");
            conn.Open();

            using (var ddl = conn.CreateCommand())
            {
                ddl.CommandText = "CREATE TABLE `Person` (`Id` counter PRIMARY KEY, `Name` text(50))";
                ddl.ExecuteNonQuery();
            }

            long FirstInsert()
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "INSERT INTO `Person` (`Name`)\nVALUES (@p0);\n" +
                    "SELECT `Id`\nFROM `Person`\nWHERE @@ROWCOUNT = 1 AND `Id` = @@identity;";
                var p = cmd.CreateParameter(); p.ParameterName = "@p0"; p.Value = "Ann"; cmd.Parameters.Add(p);

                using var reader = cmd.ExecuteReader();
                Assert.True(reader.Read());
                long id = Convert.ToInt64(reader.GetValue(0));
                Assert.False(reader.Read()); // exactly one row (Id = @@identity)
                return id;
            }

            long first = FirstInsert();
            long second = FirstInsert();
            Assert.Equal(1, first);        // first AutoNumber
            Assert.Equal(2, second);       // @@identity advanced with the next insert
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Identity_is_connection_scoped_and_survives_an_insert_into_a_keyless_table()
    {
        // Unlike SQL Server's table-scoped @@IDENTITY, Jet's is connection-scoped and only changes when an
        // insert actually generates an AutoNumber. An intervening insert into a table WITHOUT one must leave
        // @@IDENTITY untouched — so the guarded read-back still sees the earlier key.
        string path = Path.Combine(Path.GetTempPath(), $"libred-ident2-{Guid.NewGuid():N}.accdb");
        File.Copy(Northwind, path);
        try
        {
            using var conn = new LibRedConnection($"Data Source={path}");
            conn.Open();

            Exec(conn, "CREATE TABLE `P` (`Id` counter PRIMARY KEY, `Name` text(50))");
            Exec(conn, "CREATE TABLE `Log` (`K` INTEGER PRIMARY KEY, `Msg` text(20))");
            Exec(conn, "INSERT INTO `P` (`Name`) VALUES ('first')"); // @@IDENTITY -> 1
            Exec(conn, "INSERT INTO `Log` (`K`, `Msg`) VALUES (5, 'note')"); // no AutoNumber -> unchanged

            using var q = conn.CreateCommand();
            q.CommandText = "SELECT @@identity"; // bare, FROM-less — still the P insert's id (1)
            Assert.Equal(1, Convert.ToInt32(q.ExecuteScalar()));

            static void Exec(LibRedConnection c, string sql)
            { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Bare_system_variable_select_needs_no_from_clause()
    {
        // ACE allows `SELECT @@IDENTITY` / `SELECT @@ROWCOUNT` with no FROM; LibRed supports the same
        // narrow form (system variables only) without loosening the FROM requirement for ordinary SELECTs.
        string path = Path.Combine(Path.GetTempPath(), $"libred-sv-{Guid.NewGuid():N}.accdb");
        File.Copy(Northwind, path);
        try
        {
            using var conn = new LibRedConnection($"Data Source={path}");
            conn.Open();

            using (var ddl = conn.CreateCommand())
            { ddl.CommandText = "CREATE TABLE `Q` (`Id` counter PRIMARY KEY, `V` INTEGER)"; ddl.ExecuteNonQuery(); }
            using (var ins = conn.CreateCommand())
            { ins.CommandText = "INSERT INTO `Q` (`V`) VALUES (7)"; Assert.Equal(1, ins.ExecuteNonQuery()); }

            using (var q = conn.CreateCommand())
            { q.CommandText = "SELECT @@IDENTITY"; Assert.Equal(1, Convert.ToInt32(q.ExecuteScalar())); }
            using (var q = conn.CreateCommand())
            { q.CommandText = "SELECT @@ROWCOUNT"; Assert.Equal(1, Convert.ToInt32(q.ExecuteScalar())); }

            // A comma list of both, with aliases, yields one row of two columns.
            using (var q = conn.CreateCommand())
            {
                q.CommandText = "SELECT @@IDENTITY AS `Id`, @@ROWCOUNT AS `Rows`";
                using var reader = q.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal("Id", reader.GetName(0));
                Assert.Equal("Rows", reader.GetName(1));
                Assert.Equal(1, Convert.ToInt32(reader.GetValue(0)));
                Assert.Equal(1, Convert.ToInt32(reader.GetValue(1)));
                Assert.False(reader.Read());
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Theory]
    [InlineData("SELECT 1", 1)]
    [InlineData("SELECT 1;", 1)]
    [InlineData("INSERT INTO T VALUES (1); SELECT Id FROM T", 2)]
    [InlineData("SELECT ';'", 1)]                                  // semicolon inside a string literal
    [InlineData("SELECT `a;b` FROM T; SELECT 2", 2)]              // semicolon inside a backtick identifier
    [InlineData("SELECT [a;b] FROM T", 1)]                        // semicolon inside a bracket identifier
    public void SplitStatements_splits_only_on_top_level_semicolons(string sql, int expected) =>
        Assert.Equal(expected, LibRedCommand.SplitStatements(sql).Count());

    [Fact]
    public void Rolled_back_transaction_undoes_its_writes()
    {
        // This is what gives EF Core's shared-database tests their isolation: each test runs inside a
        // transaction that is rolled back, so its inserts/updates/deletes must vanish. A no-op rollback
        // (the old behaviour) leaked rows between tests → "Sequence contains more than one element".
        string path = Path.Combine(Path.GetTempPath(), $"libred-txn-{Guid.NewGuid():N}.accdb");
        File.Copy(Northwind, path);
        try
        {
            using var conn = new LibRedConnection($"Data Source={path}");
            conn.Open();

            using (var ddl = conn.CreateCommand())
            { ddl.CommandText = "CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY, `N` TEXT(10))"; ddl.ExecuteNonQuery(); }

            using (var seed = conn.CreateCommand())
            { seed.CommandText = "INSERT INTO `T` (`Id`, `N`) VALUES (1, 'keep')"; seed.ExecuteNonQuery(); }

            using (var txn = conn.BeginTransaction())
            {
                using (var ins = conn.CreateCommand())
                {
                    ins.Transaction = txn;
                    ins.CommandText = "INSERT INTO `T` (`Id`, `N`) VALUES (2, 'gone')";
                    Assert.Equal(1, ins.ExecuteNonQuery());
                }
                Assert.Equal(2L, Count(conn)); // visible inside the transaction (read-your-writes)
                txn.Rollback();
            }

            Assert.Equal(1L, Count(conn)); // the uncommitted insert is gone
        }
        finally { try { File.Delete(path); } catch (IOException) { } }

        static long Count(LibRedConnection c)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM `T`";
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
    }

    [Fact]
    public void Committed_transaction_keeps_its_writes()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-txn-commit-{Guid.NewGuid():N}.accdb");
        File.Copy(Northwind, path);
        try
        {
            using var conn = new LibRedConnection($"Data Source={path}");
            conn.Open();

            using (var ddl = conn.CreateCommand())
            { ddl.CommandText = "CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY)"; ddl.ExecuteNonQuery(); }

            using (var txn = conn.BeginTransaction())
            {
                using (var ins = conn.CreateCommand())
                { ins.Transaction = txn; ins.CommandText = "INSERT INTO `T` (`Id`) VALUES (1)"; ins.ExecuteNonQuery(); }
                txn.Commit();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM `T`";
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar())); // survived the commit
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Disposing_an_uncommitted_transaction_rolls_it_back()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-txn-dispose-{Guid.NewGuid():N}.accdb");
        File.Copy(Northwind, path);
        try
        {
            using var conn = new LibRedConnection($"Data Source={path}");
            conn.Open();

            using (var ddl = conn.CreateCommand())
            { ddl.CommandText = "CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY)"; ddl.ExecuteNonQuery(); }

            using (var txn = conn.BeginTransaction()) // no Commit — leaving the block disposes it
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = txn;
                ins.CommandText = "INSERT INTO `T` (`Id`) VALUES (1)";
                ins.ExecuteNonQuery();
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM `T`";
            Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar())); // implicit rollback on dispose
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
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
