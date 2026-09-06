using System.Data.OleDb;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// An index spans at most 10 columns, and the incremental CREATE INDEX path has to say so.
//
// The index-data block carries exactly IndexBlockFormat.MaxColumns slots, with no count field and no
// continuation, so an eleventh cannot be represented. TdefBuilder rejects that when a table is created with
// its indexes; TableCreator.InsertIndex is the other way in - CREATE INDEX and ADD FOREIGN KEY on an
// existing table - and used to check nothing. BuildIndexDataBlock filled ten slots and marked the rest
// unused, so LibRed accepted an 11-column index and stored a 10-column one.
//
// That is a nastier failure than the other limit overruns found alongside it. Exceeding the 32-index cap
// produced a file Access could not open, and exceeding the record size produced a row it could not read -
// both loud. This one produces a file ACE reads perfectly happily, containing an index that quietly covers
// different columns from the ones requested. It was also internally inconsistent: EnsureNoDuplicateKeys
// validated using all eleven requested columns while BackfillIndex populated the index from the ten the
// TDEF actually recorded.
public class IndexColumnCountAccessTests : TempDatabaseTest
{
    private const int Columns = 12;

    private static List<ColumnSpec> Specs()
    {
        var specs = new List<ColumnSpec> { new("Id", JetDataType.Int32, 4, IsFixedLength: true) };
        for (int i = 0; i < Columns; i++)
            specs.Add(new ColumnSpec($"C{i}", JetDataType.Int32, 4, IsFixedLength: true));
        return specs;
    }

    [Fact]
    public void Ace_refuses_an_index_over_more_than_ten_columns()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "idxcols-ace-");
        using OleDbConnection connection = AceTestDatabase.Open(path);
        using (OleDbCommand ddl = connection.CreateCommand())
        {
            ddl.CommandText = "CREATE TABLE IdxProbe (Id LONG PRIMARY KEY, "
                + string.Join(", ", Enumerable.Range(0, Columns).Select(i => $"C{i} LONG")) + ")";
            ddl.ExecuteNonQuery();
        }

        using OleDbCommand ten = connection.CreateCommand();
        ten.CommandText = "CREATE INDEX IxTen ON IdxProbe ("
            + string.Join(", ", Enumerable.Range(0, 10).Select(i => $"C{i}")) + ")";
        ten.ExecuteNonQuery();

        using OleDbCommand eleven = connection.CreateCommand();
        eleven.CommandText = "CREATE INDEX IxEleven ON IdxProbe ("
            + string.Join(", ", Enumerable.Range(0, 11).Select(i => $"C{i}")) + ")";
        var thrown = Assert.Throws<OleDbException>(() => eleven.ExecuteNonQuery());
        Assert.Contains("more than 10 fields", thrown.Message);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void LibRed_matches_that_on_the_incremental_path(int indexed, bool accepted)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "idxcols-libred-");
        using var database = JetDatabase.Open(path, readOnly: false);
        database.CreateTable("IdxProbe", Specs(), primaryKey: ["Id"]);

        var columns = Enumerable.Range(0, indexed).Select(i => ($"C{i}", false)).ToList();
        if (!accepted)
        {
            var thrown = Assert.Throws<NotSupportedException>(
                () => database.CreateIndex("IdxProbe", "IxWide", columns));
            Assert.Contains("at most 10 fields", thrown.Message);
            // The refusal happens before the TDEF is touched, so no half-built index is left behind.
            Assert.DoesNotContain(database.Catalog.FindTable("IdxProbe")!.Indexes, i => i.Name == "IxWide");
            return;
        }

        database.CreateIndex("IdxProbe", "IxWide", columns);
        Assert.Equal(indexed, database.Catalog.FindTable("IdxProbe")!.Indexes
            .Single(i => i.Name == "IxWide").Columns.Count);
    }
}
