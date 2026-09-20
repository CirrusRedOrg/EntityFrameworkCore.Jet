using System.Data;
using System.Data.Common;
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

    /// <summary>A query whose four columns are each a different kind: a stored key column, an aliased stored
    /// column, one reached through a join, and one the query computes.</summary>
    private const string MixedProvenance =
        "SELECT o.OrderID, o.CustomerID AS Cust, c.CompanyName, o.Freight * 2 AS Doubled " +
        "FROM Orders AS o INNER JOIN Customers AS c ON o.CustomerID = c.CustomerID";

    [Fact]
    public void GetColumnSchema_traces_each_column_back_to_its_stored_column()
    {
        using var connection = new LibRedConnection($"Data Source={Northwind}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = MixedProvenance;

        using DbDataReader reader = command.ExecuteReader();
        Assert.True(reader.CanGetColumnSchema()); // the reader implements IDbColumnSchemaGenerator
        var columns = reader.GetColumnSchema();

        DbColumn key = columns[0];
        Assert.Equal("Orders", key.BaseTableName);
        Assert.Equal("OrderID", key.BaseColumnName);
        Assert.Equal(typeof(int), key.DataType);
        Assert.Equal("Long", key.DataTypeName);
        Assert.True(key.IsKey);
        Assert.True(key.IsAutoIncrement);
        Assert.True(key.IsIdentity);
        Assert.False(key.AllowDBNull);
        Assert.False(key.IsAliased);

        DbColumn aliased = columns[1];
        Assert.Equal("Cust", aliased.ColumnName);
        Assert.Equal("CustomerID", aliased.BaseColumnName); // the alias does not change the stored name
        Assert.True(aliased.IsAliased);
        Assert.Equal(5, aliased.ColumnSize);
        Assert.False(aliased.IsExpression);

        DbColumn joined = columns[2];
        Assert.Equal("Customers", joined.BaseTableName); // the other side of the join
        Assert.Equal(40, joined.ColumnSize);

        DbColumn computed = columns[3];
        Assert.Equal("Doubled", computed.ColumnName);
        Assert.True(computed.IsExpression);
        Assert.True(computed.IsReadOnly);
        Assert.Null(computed.BaseTableName); // nothing stored stands behind it
        Assert.Null(computed.BaseColumnName);

        // Not applicable to a file rather than unknown.
        Assert.All(columns, c => Assert.Null(c.BaseServerName));
        Assert.All(columns, c => Assert.Null(c.BaseSchemaName));
        Assert.All(columns, c => Assert.False(c.IsHidden));
    }

    [Fact]
    public void GetSchemaTable_reports_the_same_facts_in_the_DataTable_form()
    {
        using var connection = new LibRedConnection($"Data Source={Northwind}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = MixedProvenance;

        using DbDataReader reader = command.ExecuteReader();
        DataTable schema = reader.GetSchemaTable()!;

        Assert.Equal(4, schema.Rows.Count);
        Assert.Contains(SchemaTableColumn.ProviderType, schema.Columns.Cast<DataColumn>().Select(c => c.ColumnName));

        DataRow key = schema.Rows[0];
        Assert.Equal("OrderID", key[SchemaTableColumn.ColumnName]);
        Assert.Equal(0, key[SchemaTableColumn.ColumnOrdinal]);
        Assert.Equal(3, key[SchemaTableColumn.ProviderType]);   // OLE DB DBTYPE_I4, as the Columns collection reports it
        Assert.Equal(typeof(int), key[SchemaTableColumn.DataType]);
        Assert.Equal("Long", key["DataTypeName"]);
        Assert.True((bool)key[SchemaTableColumn.IsKey]);
        Assert.True((bool)key[SchemaTableOptionalColumn.IsAutoIncrement]);
        Assert.False((bool)key[SchemaTableColumn.AllowDBNull]);

        // A Jet file has no server, catalog or schema, no row versions, and hides no column.
        Assert.Equal(DBNull.Value, key[SchemaTableOptionalColumn.BaseServerName]);
        Assert.Equal(DBNull.Value, key[SchemaTableOptionalColumn.BaseCatalogName]);
        Assert.Equal(DBNull.Value, key[SchemaTableColumn.BaseSchemaName]);
        Assert.False((bool)key[SchemaTableOptionalColumn.IsRowVersion]);
        Assert.False((bool)key[SchemaTableOptionalColumn.IsHidden]);

        DataRow computed = schema.Rows[3];
        Assert.True((bool)computed[SchemaTableColumn.IsExpression]);
        Assert.Equal(DBNull.Value, computed[SchemaTableColumn.BaseTableName]);
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
