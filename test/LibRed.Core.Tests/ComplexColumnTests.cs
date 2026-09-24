using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Resolving a complex (multi-value / attachment) column to the values it stands for. Northwind carries one:
/// <c>MSysResources.Data</c>, the attachment column holding Access's Office theme — so this needs no ACE and
/// runs everywhere.
/// </summary>
public class ComplexColumnTests
{
    private static JetDatabase Northwind() => TemporaryDatabase.OpenTracked(
        TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "complex-"), readOnly: true);

    private static ComplexColumn TheAttachmentColumn(JetDatabase db)
    {
        ComplexColumn? column = db.Catalog.FindComplexColumn("MSysResources", "Data");
        Assert.NotNull(column);
        return column;
    }

    [Fact]
    public void The_catalog_resolves_the_complex_column_to_its_flat_table()
    {
        using JetDatabase db = Northwind();
        ComplexColumn column = TheAttachmentColumn(db);

        Assert.Equal("Data", column.ColumnName);
        Assert.Equal("MSysResources", column.OwnerTable.Name);
        Assert.StartsWith("f_", column.FlatTable.Name, StringComparison.Ordinal);
        Assert.Equal("MSysComplexType_Attachment", column.ElementTypeName);
        Assert.True(column.IsAttachment);

        // The two bookkeeping columns are found by index shape, and are not each other.
        Assert.NotEqual(column.OwnerLink.Name, column.ValueId.Name);
        Assert.DoesNotContain(column.OwnerLink, column.ValueColumns);
        Assert.DoesNotContain(column.ValueId, column.ValueColumns);
        // What is left is the attachment template's own six columns.
        Assert.Equal(
            ["FileData", "FileFlags", "FileName", "FileTimeStamp", "FileType", "FileURL"],
            column.ValueColumns.Select(c => c.Name).Order(StringComparer.Ordinal));
    }

    // The value-id column carries the primary index and is an ordinary AutoNumber; the owner link is the
    // non-unique one. Getting these the wrong way round returns one value per record and silently loses
    // every multi-valued case, so it is worth asserting directly.
    [Fact]
    public void The_value_id_is_the_primary_autonumber_and_the_owner_link_is_not()
    {
        using JetDatabase db = Northwind();
        ComplexColumn column = TheAttachmentColumn(db);

        Assert.True(column.ValueId.IsAutoNumber);
        Assert.False(column.OwnerLink.IsAutoNumber);
        Assert.Contains(column.FlatTable.Indexes,
            i => i.IsPrimaryKey && i.Columns.Count == 1 && i.Columns[0].Column.Name == column.ValueId.Name);
    }

    // The in-row value is the record's Int32 complex id — not a long-value descriptor, which is what it used
    // to decode as.
    [Fact]
    public void The_in_row_value_is_an_int32_id()
    {
        using JetDatabase db = Northwind();
        ComplexColumn column = TheAttachmentColumn(db);
        ColumnDef inRow = column.OwnerTable.FindColumn("Data")!;

        object?[] row = db.OpenTable("MSysResources").Rows().First();
        Assert.IsType<int>(row[inRow.Index]);
    }

    [Fact]
    public void Reading_a_records_values_gives_the_attachment()
    {
        using JetDatabase db = Northwind();
        ComplexColumn column = TheAttachmentColumn(db);
        ColumnDef inRow = column.OwnerTable.FindColumn("Data")!;

        object?[] record = db.OpenTable("MSysResources").Rows().First();
        int complexId = (int)record[inRow.Index]!;

        IReadOnlyList<object?[]> values = db.ReadComplexValues(column, complexId);
        object?[] only = Assert.Single(values);

        int fileName = column.ValueColumns.ToList().FindIndex(c => c.Name == "FileName");
        int fileData = column.ValueColumns.ToList().FindIndex(c => c.Name == "FileData");
        Assert.EndsWith(".thmx", (string)only[fileName]!, StringComparison.OrdinalIgnoreCase);

        // The payload unwraps to the real file: a .thmx is a zip, so it opens "PK".
        ComplexAttachment attachment = ComplexAttachment.Unwrap((byte[])only[fileData]!);
        Assert.Equal("thmx", attachment.Extension);
        Assert.Equal([(byte)'P', (byte)'K'], attachment.Content.AsSpan(0, 2).ToArray());
    }

    // An id that names no values is the ordinary answer, not an error: the id is allocated when the row is
    // created, whether or not a value ever follows.
    [Fact]
    public void A_record_with_no_values_reads_as_empty()
    {
        using JetDatabase db = Northwind();
        Assert.Empty(db.ReadComplexValues("MSysResources", "Data", complexId: 999_999));
    }

    // A complex column carries the descriptor's 0x04 AutoNumber flag, so a table can have several columns
    // reading IsAutoNumber at once — MSysResources has two, and complex1.accdb's Table1 has five. Only the
    // non-complex one draws on the TDEF header's seed/increment pair (0x14/0x18); a complex column is
    // allocated from 0x1C, so it must not report the other counter's configuration as its own.
    [Fact]
    public void A_complex_column_is_flagged_autonumber_but_does_not_claim_the_header_counter()
    {
        using JetDatabase db = Northwind();
        TableDef table = db.Catalog.FindTable("MSysResources")!;
        ColumnDef data = table.FindColumn("Data")!;
        ColumnDef id = table.FindColumn("Id")!;

        Assert.Equal(JetDataType.Complex, data.Type);
        Assert.True(data.IsAutoNumber);
        Assert.True(id.IsAutoNumber);

        // The ordinary counter reports the header pair; the complex column reports neither.
        Assert.Equal(2, id.Seed);
        Assert.Equal(1, data.Seed);
        Assert.Equal(1, data.Increment);

        // Its high-water lives on the table instead.
        Assert.Equal(1, table.ComplexAutoNumber);
    }

    // The drop-and-recreate rebuild is refused on a table that owns a complex column, because it cannot carry
    // the column's links across: the rebuilt descriptor writes the collation LANGID over the 0x0B that holds
    // the MSysComplexColumns key, and the table lands on a new TDEF page while its catalog row still names the
    // old one. Measured on this very table before the refusal went in — MSysResources.Data resolved before the
    // ALTER and no complex column resolved at all after it — and on complex1.accdb's four attachment columns.
    // ACE performs the same ALTER with every link intact (ComplexWriteAceReadbackTests), so this is a gap.
    //
    // It used to assert the rebuild SUCCEEDED here, which is how the orphaning went unnoticed: it checked the
    // AutoNumber flag and the 0x1C counter, which do survive, rather than whether the column still resolved.
    [Fact]
    public void A_table_with_a_complex_column_refuses_the_rebuild()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "complex-alter-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                // Text -> Memo is a storage-type change, which takes the full logical rebuild.
                var refused = Assert.Throws<NotSupportedException>(() => db.AlterColumn("MSysResources", "Name",
                    new ColumnSpec("Name", JetDataType.Memo, 0, IsFixedLength: false)));
                Assert.Contains("multi-value or attachment", refused.Message, StringComparison.Ordinal);
            }

            using var reopened = JetDatabase.Open(path);
            TableDef table = reopened.Catalog.FindTable("MSysResources")!;
            Assert.Equal(JetDataType.Text, table.FindColumn("Name")!.Type);   // nothing was changed
            Assert.Equal(1, reopened.Catalog.ComplexColumns.Count(c => c.OwnerTable.Name == "MSysResources"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_column_that_is_not_complex_is_rejected()
    {
        using JetDatabase db = Northwind();
        Assert.Null(db.Catalog.FindComplexColumn("Customers", "CustomerID"));
        Assert.Throws<InvalidOperationException>(() => db.ReadComplexValues("Customers", "CustomerID", 1));
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 })]                                    // shorter than the outer header
    [InlineData(new byte[] { 9, 0, 0, 0, 4, 0, 0, 0, 1, 2, 3, 4 })]        // unknown compression flag
    [InlineData(new byte[] { 0, 0, 0, 0, 99, 0, 0, 0, 1, 2, 3, 4 })]       // body shorter than declared
    public void Malformed_attachment_data_is_rejected(byte[] blob) =>
        Assert.Throws<InvalidDataException>(() => ComplexAttachment.Unwrap(blob));
}
