using LibRed.Data;
using Xunit;

namespace LibRed.Ado.Tests;

public class LibRedDataReaderMetadataTests
{
    private static readonly string Northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");

    [Fact]
    public void Empty_result_preserves_declared_column_types()
    {
        using var connection = new LibRedConnection($"Data Source={Northwind}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProductID, ProductName FROM Products WHERE ProductID < 0";

        using var reader = command.ExecuteReader();

        Assert.False(reader.HasRows);
        Assert.Equal(typeof(int), reader.GetFieldType(0));
        Assert.Equal(typeof(string), reader.GetFieldType(1));
        Assert.Equal(nameof(Int32), reader.GetDataTypeName(0));
        Assert.Equal(nameof(String), reader.GetDataTypeName(1));
    }

    [Fact]
    public void First_null_result_preserves_the_declared_column_type()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-metadata-{Guid.NewGuid():N}.accdb");
        File.Copy(Northwind, path);
        try
        {
            using var connection = new LibRedConnection($"Data Source={path}");
            connection.Open();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE TABLE T (Id LONG PRIMARY KEY, V TEXT(20))";
                command.ExecuteNonQuery();
                command.CommandText = "INSERT INTO T (Id, V) VALUES (1, NULL)";
                command.ExecuteNonQuery();
                command.CommandText = "INSERT INTO T (Id, V) VALUES (2, 'later')";
                command.ExecuteNonQuery();
                command.CommandText = "SELECT V FROM T ORDER BY Id";

                using var reader = command.ExecuteReader();
                Assert.True(reader.HasRows);
                Assert.Equal(typeof(string), reader.GetFieldType(0));
                Assert.Equal(nameof(String), reader.GetDataTypeName(0));
                Assert.True(reader.Read());
                Assert.True(reader.IsDBNull(0));
                Assert.True(reader.Read());
                Assert.Equal("later", reader.GetString(0));
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Empty_computed_projection_reports_known_expression_types()
    {
        using var connection = new LibRedConnection($"Data Source={Northwind}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProductID + 1 AS NextId, ProductName & '!' AS Label, " +
            "ProductID > 0 AS Positive FROM Products WHERE ProductID < 0";

        using var reader = command.ExecuteReader();

        Assert.False(reader.HasRows);
        Assert.Equal(typeof(int), reader.GetFieldType(0));
        Assert.Equal(typeof(string), reader.GetFieldType(1));
        Assert.Equal(typeof(bool), reader.GetFieldType(2));
    }

    [Fact]
    public void Null_aggregate_result_preserves_its_argument_type()
    {
        using var connection = new LibRedConnection($"Data Source={Northwind}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) AS C, SUM(ProductID) AS S FROM Products WHERE ProductID < 0";

        using var reader = command.ExecuteReader();

        Assert.Equal(typeof(int), reader.GetFieldType(0));
        Assert.Equal(typeof(int), reader.GetFieldType(1));
        Assert.True(reader.Read());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.True(reader.IsDBNull(1));
    }
}
