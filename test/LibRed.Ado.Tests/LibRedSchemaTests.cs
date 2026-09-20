using System.Data;
using LibRed.Data;
using Xunit;

namespace LibRed.Ado.Tests;

/// <summary>
/// The metadata collections <c>GetSchema</c> serves. The expected values are Northwind's, and the shapes are
/// ACE's: where ACE serves the same collection, these rows match it column for column (verified against the
/// ACE 12 OLE DB provider over this same file).
/// </summary>
public class LibRedSchemaTests
{
    private static readonly string Northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");

    private static LibRedConnection OpenConnection()
    {
        var connection = new LibRedConnection($"Data Source={Northwind}");
        connection.Open();
        return connection;
    }

    private static DataTable Schema(string collection, params string?[] restrictions)
    {
        using LibRedConnection connection = OpenConnection();
        return restrictions.Length == 0
            ? connection.GetSchema(collection)
            : connection.GetSchema(collection, restrictions);
    }

    [Fact]
    public void MetaDataCollections_lists_the_framework_collections_first()
    {
        DataTable collections = Schema("MetaDataCollections");

        var names = collections.Rows.Cast<DataRow>().Select(r => (string)r["CollectionName"]).ToList();
        Assert.Equal(
            ["MetaDataCollections", "DataSourceInformation", "DataTypes", "Restrictions", "ReservedWords"],
            names.Take(5));
        Assert.Equal(20, names.Count);
        Assert.Contains("ViewColumns", names);
    }

    [Theory]
    [InlineData("MetaDataCollections", 20)]
    [InlineData("DataSourceInformation", 1)]
    [InlineData("DataTypes", 19)]
    [InlineData("Restrictions", 67)]
    [InlineData("ReservedWords", 122)]
    [InlineData("Tables", 41)]
    [InlineData("Columns", 228)]   // every table's columns and every view's output columns
    [InlineData("Indexes", 69)]
    [InlineData("Views", 17)]
    [InlineData("Procedures", 6)]  // the stored queries that declare parameters
    [InlineData("ForeignKeys", 15)]
    [InlineData("PrimaryKeys", 21)]
    [InlineData("TableConstraints", 36)]
    [InlineData("KeyColumnUsage", 41)]
    [InlineData("ConstraintColumnUsage", 41)]
    [InlineData("ReferentialConstraints", 15)]
    [InlineData("CheckConstraints", 0)]   // Jet has no CHECK constraints
    [InlineData("Statistics", 24)]
    [InlineData("ProcedureParameters", 9)]
    [InlineData("ViewColumns", 90)]
    public void Collection_has_the_rows_Northwind_holds(string collection, int expected) =>
        Assert.Equal(expected, Schema(collection).Rows.Count);

    [Fact]
    public void Tables_separates_the_four_kinds_of_object()
    {
        // TABLE_TYPE comes from MSysObjects.Flags: Access's own nav-pane tables are ACCESS TABLE, the MSys*
        // catalog is SYSTEM TABLE, and a stored SELECT is a VIEW rather than a table of its own.
        var byType = Schema("Tables").Rows.Cast<DataRow>()
            .ToLookup(r => (string)r["TABLE_TYPE"], r => (string)r["TABLE_NAME"]);

        Assert.Equal(14, byType["TABLE"].Count());
        Assert.Equal(17, byType["VIEW"].Count());
        Assert.Equal(5, byType["SYSTEM TABLE"].Count());
        Assert.Equal(5, byType["ACCESS TABLE"].Count());

        Assert.Contains("Orders", byType["TABLE"]);
        Assert.Contains("Invoices", byType["VIEW"]);
        Assert.Contains("MSysObjects", byType["SYSTEM TABLE"]);
    }

    [Fact]
    public void Columns_describes_an_AutoNumber_primary_key()
    {
        DataRow column = Assert.Single(Schema("Columns", null, null, "Orders", "OrderID").Rows.Cast<DataRow>());

        Assert.Equal(1L, column["ORDINAL_POSITION"]);
        Assert.Equal(3, column["DATA_TYPE"]);            // OLE DB DBTYPE_I4
        Assert.Equal("Long", column["TYPE_NAME"]);
        Assert.Equal(10, column["NUMERIC_PRECISION"]);
        Assert.False((bool)column["IS_NULLABLE"]);
        Assert.False((bool)column["IS_COMPUTED"]);
        Assert.True((bool)column["IS_AUTOINCREMENT"]);
        Assert.Equal(1, Convert.ToInt32(column["INCREMENT"]));
        // MAYDEFER | WRITEUNKNOWN | ISFIXEDLENGTH | MAYBENULL — the flags ACE reports for a stored column.
        Assert.Equal(0x02L | 0x08L | 0x10L | 0x40L, column["COLUMN_FLAGS"]);
    }

    [Fact]
    public void Columns_reports_a_views_output_columns()
    {
        // A view's columns are the shape its query produces, which only planning the query can say.
        var invoices = Schema("Columns", null, null, "Invoices", null).Rows.Cast<DataRow>()
            .Select(r => (string)r["COLUMN_NAME"]).ToList();

        Assert.Equal(26, invoices.Count);
        Assert.Contains("ExtendedPrice", invoices);   // computed by the view
        Assert.Contains("Salesperson", invoices);     // an expression over two stored columns
    }

    [Fact]
    public void ViewColumns_names_the_stored_column_behind_each_one()
    {
        // This is OLE DB's view-column *usage*: which stored column a view's column reads, so an alias reports
        // the column's own name and a computed column has no row at all.
        var usage = Schema("ViewColumns", null, null, "Invoices", null).Rows.Cast<DataRow>()
            .Select(r => ((string)r["TABLE_NAME"], (string)r["COLUMN_NAME"])).ToList();

        Assert.Equal(24, usage.Count);
        Assert.Contains(("Orders", "OrderID"), usage);
        // CustomerName and ShipperName are both aliases of a CompanyName column, from different tables.
        Assert.Contains(("Customers", "CompanyName"), usage);
        Assert.Contains(("Shippers", "CompanyName"), usage);
        Assert.DoesNotContain(usage, u => u.Item2 is "ExtendedPrice" or "Salesperson");
    }

    [Fact]
    public void Indexes_describes_a_primary_key_index()
    {
        DataRow index = Assert.Single(Schema("Indexes", null, null, null, null, "Shippers").Rows.Cast<DataRow>());

        Assert.Equal("PK_Shippers", index["INDEX_NAME"]);
        Assert.True((bool)index["PRIMARY_KEY"]);
        Assert.True((bool)index["UNIQUE"]);
        Assert.False((bool)index["CLUSTERED"]);
        Assert.Equal("ShipperID", index["COLUMN_NAME"]);
        Assert.Equal(1L, index["ORDINAL_POSITION"]);
        Assert.Equal(3L, Convert.ToInt64(index["CARDINALITY"]));
    }

    [Fact]
    public void PrimaryKeys_and_ForeignKeys_report_a_relationship_from_both_ends()
    {
        DataRow key = Assert.Single(Schema("PrimaryKeys", null, null, "Orders").Rows.Cast<DataRow>());
        Assert.Equal("OrderID", key["COLUMN_NAME"]);
        Assert.Equal("PK_Orders", key["PK_NAME"]);

        DataRow relation = Assert.Single(
            Schema("ForeignKeys", null, null, "Customers", null, null, "Orders").Rows.Cast<DataRow>());
        Assert.Equal("CustomerID", relation["PK_COLUMN_NAME"]);
        Assert.Equal("CustomerID", relation["FK_COLUMN_NAME"]);
        Assert.Equal("FK_Orders_Customers", relation["FK_NAME"]);
        // Jet enforces the rules itself rather than declaring them, so an unenforced rule reads as NO ACTION.
        Assert.Equal("NO ACTION", relation["UPDATE_RULE"]);
        Assert.Equal("NO ACTION", relation["DELETE_RULE"]);
    }

    [Fact]
    public void TableConstraints_names_the_primary_key_constraint()
    {
        DataRow constraint = Assert.Single(
            Schema("TableConstraints", null, null, null, null, null, "Shippers", null).Rows.Cast<DataRow>());

        Assert.Equal("PK_Shippers", constraint["CONSTRAINT_NAME"]);
        Assert.Equal("PRIMARY KEY", constraint["CONSTRAINT_TYPE"]);
        Assert.False((bool)constraint["IS_DEFERRABLE"]);
    }

    [Fact]
    public void Statistics_reports_a_tables_row_count()
    {
        DataRow statistic = Assert.Single(Schema("Statistics", null, null, "Orders").Rows.Cast<DataRow>());
        Assert.Equal(830m, statistic["CARDINALITY"]);
    }

    [Fact]
    public void ProcedureParameters_describes_a_stored_querys_declared_parameter()
    {
        DataRow parameter = Assert.Single(
            Schema("ProcedureParameters", null, null, "CustOrdersOrders", null).Rows.Cast<DataRow>());

        Assert.Equal("CustomerID", parameter["PARAMETER_NAME"]);
        Assert.Equal(1, parameter["ORDINAL_POSITION"]);
        Assert.Equal(1, parameter["PARAMETER_TYPE"]);     // input
        Assert.Equal(130, parameter["DATA_TYPE"]);        // DBTYPE_WSTR
        Assert.Equal("VarChar", parameter["TYPE_NAME"]);
    }

    [Fact]
    public void Procedures_carry_the_parameters_clause_ahead_of_their_statement()
    {
        DataRow procedure = Assert.Single(
            Schema("Procedures", null, null, "CustOrdersOrders", null).Rows.Cast<DataRow>());

        string definition = (string)procedure["PROCEDURE_DEFINITION"];
        Assert.StartsWith("PARAMETERS [CustomerID] TEXT;", definition);
        Assert.Contains("FROM [Orders]", definition);
    }

    [Fact]
    public void DataTypes_names_every_type_the_file_can_hold()
    {
        var types = Schema("DataTypes").Rows.Cast<DataRow>()
            .ToDictionary(r => (string)r["TypeName"], r => (int)r["ProviderDbType"]);

        Assert.Equal(19, types.Count);
        Assert.Equal(3, types["Long"]);
        Assert.Equal(6, types["Currency"]);
        // Fixed-length text and binary are types of their own, told apart from the variable-length forms.
        Assert.Equal(130, types["Char"]);
        Assert.Equal(128, types["Binary"]);
        // ACE 12 and up only: the engine has these two even though its own OLE DB and ODBC drivers cannot
        // materialise them.
        Assert.Equal(20, types["BigInt"]);
        Assert.Equal(135, types["DateTime2"]);
    }

    [Fact]
    public void Restrictions_are_declared_for_the_collections_that_take_them()
    {
        var restrictions = Schema("Restrictions").Rows.Cast<DataRow>()
            .ToLookup(r => (string)r["CollectionName"], r => (string)r["RestrictionName"]);

        Assert.Equal(["TABLE_CATALOG", "TABLE_SCHEMA", "TABLE_NAME", "COLUMN_NAME"], restrictions["Columns"]);
        // Indexes takes the table name LAST, after the index name and type, as OLE DB orders it.
        Assert.Equal("TABLE_NAME", restrictions["Indexes"].Last());
    }

    [Fact]
    public void A_restriction_filters_the_rows()
    {
        var columns = Schema("Columns", null, null, "Shippers", null).Rows.Cast<DataRow>().ToList();

        Assert.Equal(3, columns.Count);
        Assert.All(columns, c => Assert.Equal("Shippers", c["TABLE_NAME"]));
    }

    [Fact]
    public void A_catalog_or_schema_restriction_matches_everything()
    {
        // A Jet file holds one nameless catalog and no schemas, so filtering on the null every row carries
        // would return nothing at all.
        Assert.Equal(3, Schema("Columns", "anything", "anything", "Shippers", null).Rows.Count);
    }

    [Fact]
    public void An_unknown_collection_or_too_many_restrictions_is_rejected()
    {
        using LibRedConnection connection = OpenConnection();

        Assert.Throws<ArgumentException>(() => connection.GetSchema("NoSuchCollection"));
        // Tables takes four restrictions.
        Assert.Throws<ArgumentException>(() => connection.GetSchema("Tables", [null, null, null, null, null]));
    }

    [Fact]
    public void Schema_needs_an_open_connection()
    {
        using var connection = new LibRedConnection($"Data Source={Northwind}");
        Assert.Throws<InvalidOperationException>(() => connection.GetSchema("Tables"));
    }
}
