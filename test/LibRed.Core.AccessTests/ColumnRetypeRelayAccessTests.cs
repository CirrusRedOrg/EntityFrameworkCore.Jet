using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using LibRed.Tests.Shared;
using Xunit;

namespace LibRed.Core.Tests;

// ACE's in-place ALTER COLUMN: the descriptor is edited and every row re-laid where it stands, rather than the
// table being rebuilt. The re-lay has to read each row exactly as it is on disk, and a table's rows are not
// uniform — ADD COLUMN is metadata-only, so rows written at different times carry different widths, different
// variable-slot counts, and sometimes no variable trailer at all. Each test below is one of those shapes, with
// ACE asked to read the result, because a mis-laid row is a file ACE rejects rather than one LibRed misreads.
[Collection(AceCollection.Name)]
public class ColumnRetypeRelayAccessTests(ITestOutputHelper output)
{
    // BuildRelaidRecord sized the variable section from the ROW's stored numVar while the retyped column's
    // descriptor takes its index from the TDEF's 0x2B. ADD COLUMN is metadata-only, so every pre-existing row
    // carries fewer slots than 0x2B — and the retyped column then landed on the wrong slot, which is the file
    // ACE rejects with "A column Id is incorrect".
    [Fact]
    public void A_retype_after_adding_a_variable_column_keeps_rows_readable()
    {
        string path = TemporaryDatabase.CreatePath("relay-addcol-");
        try
        {
            CreateWithAce(path, ["CREATE TABLE T (K LONG, A LONG, B TEXT(30))"]);
            using (var connection = AceTestDatabase.Open(path))
                Exec(connection, "INSERT INTO T (K, A, B) VALUES (1, 42, 'b1')");

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.True(db.AddColumn("T", new ColumnSpec("C", JetDataType.Text, 30, IsFixedLength: false)));
                db.AlterColumnTypeInPlace("T", "A", new ColumnSpec("A", JetDataType.Text, 20, IsFixedLength: false));
            }

            using var ace = AceTestDatabase.Open(path);
            using var read = ace.CreateCommand();
            read.CommandText = "SELECT A, B FROM T WHERE K = 1";
            using OleDbDataReader reader = read.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("42", reader.GetString(0));
            Assert.Equal("b1", reader.GetString(1));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The same function derived hasVar from the schema alone, dropping the "ColumnId < storedCount" qualifier
    // its two siblings carry. A row written before the table's first variable ADD has no variable trailer at
    // all, so parsing it as though it had one reads fixed bytes as an offset table.
    [Fact]
    public void A_retype_reads_rows_written_before_the_first_variable_column()
    {
        string path = TemporaryDatabase.CreatePath("relay-hasvar-");
        try
        {
            CreateWithAce(path, ["CREATE TABLE T (K LONG, A LONG, N LONG)"]);   // all fixed: no trailer
            using (var connection = AceTestDatabase.Open(path))
                Exec(connection, "INSERT INTO T (K, A, N) VALUES (1, 42, 7)");

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.True(db.AddColumn("T", new ColumnSpec("M", JetDataType.Text, 30, IsFixedLength: false)));
                db.AlterColumnTypeInPlace("T", "A", new ColumnSpec("A", JetDataType.Double, 8, IsFixedLength: true));
            }

            using var ace = AceTestDatabase.Open(path);
            using var read = ace.CreateCommand();
            read.CommandText = "SELECT A, N FROM T WHERE K = 1";
            using OleDbDataReader reader = read.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(42d, reader.GetDouble(0));
            Assert.Equal(7, reader.GetInt32(1));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The re-lay pinned the fixed-region length to rows[0] and applied it to every row. ADD COLUMN of a FIXED
    // column is metadata-only, so a table legitimately holds short rows written before it alongside full-width
    // ones — and one length for both truncates the long rows or drags the short rows' variable data upward.
    [Fact]
    public void A_retype_handles_rows_of_two_different_fixed_widths()
    {
        string path = TemporaryDatabase.CreatePath("relay-mixed-width-");
        try
        {
            CreateWithAce(path, ["CREATE TABLE T (K LONG, A LONG, B TEXT(30))"]);
            using (var connection = AceTestDatabase.Open(path))
                Exec(connection, "INSERT INTO T (K, A, B) VALUES (1, 11, 'first')");

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.True(db.AddColumn("T", new ColumnSpec("X", JetDataType.Int32, 4, IsFixedLength: true)));
                Insert(db, "T", ("K", 2), ("A", 22), ("B", "second"), ("X", 99));   // now a WIDER row
                db.AlterColumnTypeInPlace("T", "A", new ColumnSpec("A", JetDataType.Double, 8, IsFixedLength: true));
            }

            using var ace = AceTestDatabase.Open(path);
            using var read = ace.CreateCommand();
            read.CommandText = "SELECT K, A, B, X FROM T ORDER BY K";
            using OleDbDataReader reader = read.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(11d, reader.GetDouble(1));
            Assert.Equal("first", reader.GetString(2));
            Assert.True(reader.IsDBNull(3));                 // written before X existed
            Assert.True(reader.Read());
            Assert.Equal(22d, reader.GetDouble(1));
            Assert.Equal("second", reader.GetString(2));
            Assert.Equal(99, reader.GetInt32(3));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The re-lay reaches RowCodec.AssembleRow but never RowCodec.Encode, where the declared-width check
    // used to live — so a narrowing retype wrote values wider than the column declares.
    [Fact]
    public void A_retype_that_would_overflow_the_new_width_is_refused()
    {
        string path = TemporaryDatabase.CreatePath("relay-width-");
        try
        {
            CreateWithAce(path, ["CREATE TABLE T (K LONG, N LONG)"]);
            using (var connection = AceTestDatabase.Open(path))
                Exec(connection, "INSERT INTO T (K, N) VALUES (1, 1234567890)");

            using var db = JetDatabase.Open(path, readOnly: false);
            var error = Assert.Throws<InvalidOperationException>(() => db.AlterColumnTypeInPlace(
                "T", "N", new ColumnSpec("N", JetDataType.Text, 4, IsFixedLength: false)));   // 2 characters
            output.WriteLine(error.Message);
            Assert.Contains("too small to accept", error.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Narrowing is a retype like any other, so the re-lay's width check refuses a value wider than the new
    // declaration, with ACE's message — rather than leaving exactly what the insert-time check exists to prevent.
    [Fact]
    public void Narrowing_a_column_below_its_stored_values_is_refused()
    {
        string path = TemporaryDatabase.CreatePath("narrow-");
        try
        {
            CreateWithAce(path, ["CREATE TABLE T (K LONG, V TEXT(100))"]);
            using (var connection = AceTestDatabase.Open(path))
                Exec(connection, "INSERT INTO T (K, V) VALUES (1, 'a value far longer than five')");

            using var db = JetDatabase.Open(path, readOnly: false);
            var error = Assert.Throws<InvalidOperationException>(() => db.AlterColumn(
                "T", "V", new ColumnSpec("V", JetDataType.Text, 10, IsFixedLength: false)));
            output.WriteLine(error.Message);
            Assert.Contains("too small to accept", error.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Retyping AWAY from DECIMAL left precision and scale sitting in the descriptor's LANGID bytes, because
    // only the decimal arm of that type-keyed union was ever written.
    [Fact]
    public void Retyping_away_from_decimal_restores_the_collation_bytes()
    {
        string path = TemporaryDatabase.CreatePath("decimal-union-");
        try
        {
            using var db = JetDatabase.Open(CreateWithAce(path,
                ["CREATE TABLE T (K LONG, D DECIMAL(12,3))"]), readOnly: false);

            db.AlterColumnTypeInPlace("T", "D", new ColumnSpec("D", JetDataType.Text, 50, IsFixedLength: false));
            db.Catalog.Invalidate();

            ColumnDef retyped = db.Catalog.FindTable("T")!.FindColumn("D")!;
            output.WriteLine($"collation after retype: {retyped.Collation}");
            Assert.Equal(db.Collation, retyped.Collation);   // NOT 0x030C, the old precision/scale pair
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string CreateWithAce(string path, string[] ddl)
    {
        JetDatabase.Create(path);
        using var connection = AceTestDatabase.Open(path);
        foreach (string sql in ddl) Exec(connection, sql);
        return path;
    }

    private static void Insert(JetDatabase db, string table, params (string Column, object Value)[] cells)
    {
        Table t = db.OpenTable(table);
        var values = new object?[t.Definition.Columns.Count];
        foreach ((string column, object value) in cells)
            values[t.Definition.FindColumn(column)!.Index] = value;
        t.Insert(values);
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}