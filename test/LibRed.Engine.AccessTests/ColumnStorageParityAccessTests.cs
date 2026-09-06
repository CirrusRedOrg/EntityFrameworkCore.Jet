using System.Data.OleDb;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Engine.Tests;

// For every type, LibRed must put the column in the same region ACE does — fixed-data or behind the
// variable offset table — with the same type code and declared length.
//
// This exists because the type table got it wrong twice. BIGINT was caught first; GUID was still declared
// fixed where ACE declares it variable, which cost record budget ACE does not spend and made a 252-column
// GUID table ACE creates happily impossible for LibRed to write (GuidColumnStorageAccessTests).
//
// Neither mistake was findable by reasoning about it. The obvious theory - that ACE moves wide types out
// of the fixed region to save declaration budget - is false: DATETIME2 is 42 bytes and fixed, DECIMAL is
// 17 and fixed, while GUID at 16 and BIGINT at 8 are variable. So is "later additions went variable":
// DECIMAL is a Jet 4 addition and stayed fixed. Whatever ACE's rule is, it is not one we can derive, which
// is exactly why the whole table is measured rather than argued.
//
// The assertion compares the two engines directly rather than hardcoding expectations, so it keeps working
// as the type surface grows — a new type simply needs a row here.
[Collection(AceCollection.Name)]
public class ColumnStorageParityAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    public static TheoryData<string> Types =>
    [
        "BIT", "BYTE", "SMALLINT", "INTEGER", "REAL", "FLOAT", "CURRENCY", "DATETIME",
        "GUID", "DECIMAL(18,4)", "NUMERIC(10,2)", "CHAR(50)", "VARCHAR(50)", "TEXT(50)",
        "BINARY(50)", "VARBINARY(50)", "LONGTEXT", "LONGBINARY",
        "BIGINT",       // ACE 16+
        "DATETIME2",    // ACE 17+
    ];

    [Theory]
    [MemberData(nameof(Types))]
    public void Libred_declares_a_column_the_way_ace_declares_it(string declaration)
    {
        string sql = $"CREATE TABLE W (Id LONG PRIMARY KEY, V {declaration})";

        string ace = Describe(Copy("parity-ace-"), path =>
        {
            using OleDbConnection connection = AceTestDatabase.Open(path);
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        });

        // A type this engine is too old for says nothing about LibRed's mapping — BIGINT needs ACE 16 and
        // DATETIME2 needs ACE 17, and the matrix runs older legs.
        Assert.SkipWhen(ace.StartsWith("refused"), $"This ACE build does not accept {declaration}: {ace}");

        string libred = Describe(Copy("parity-libred-"), path =>
        {
            using var database = JetDatabase.Open(path, readOnly: false);
            new QueryEngine(database).ExecuteNonQuery(sql);
        });

        output.WriteLine($"{declaration}: ACE {ace} / LibRed {libred}");
        Assert.Equal(ace, libred);
    }

    private static string Copy(string prefix) => TemporaryDatabase.CopyPath(
        Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), prefix);

    private static string Describe(string path, Action<string> create)
    {
        try
        {
            create(path);
        }
        catch (Exception ex)
        {
            return $"refused ({ex.GetType().Name})";
        }

        using var database = JetDatabase.Open(path, readOnly: true);
        ColumnDef? column = database.Catalog.FindTable("W")?.FindColumn("V");
        return column is null
            ? "no column"
            : $"{column.Type} len {column.Length} {(column.IsFixedLength ? "FIXED" : "VARIABLE")}";
    }
}
