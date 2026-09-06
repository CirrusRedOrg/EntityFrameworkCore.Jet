using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// ACE stores at most 4060 bytes per record, excluding anything on LVAL pages, and LibRed now enforces the
// same (JetFormatBase.MaxRecordSize).
//
// Enforcing it is not politeness. The page could hold 4080 - 4096 less the 14-byte header and a 2-byte slot
// - and LibRed used to write into that band happily, producing a row ACE cannot read: it fails while
// materialising the row with "you and another user are attempting to change the same data at the same
// time", a concurrency message with nothing to do with the cause. Above 4080 it did not even get that far,
// throwing ArgumentOutOfRangeException out of the offset arithmetic.
//
// The 20-byte reserve between 4060 and what the page holds is measured, not explained.
public class RecordSizeAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    /// <summary>Row overhead for a table of <paramref name="columns"/> TEXT columns plus a LONG key: the
    /// 2-byte count, the fixed key, the variable-offset table, numVar, and the null bitmap.</summary>
    private static int Overhead(int columns) => 2 + 4 + (columns + 1) * 2 + 2 + (columns + 1 + 7) / 8;

    private static List<ColumnSpec> Specs(int columns)
    {
        var specs = new List<ColumnSpec> { new("Id", JetDataType.Int32, 4, IsFixedLength: true) };
        for (int i = 0; i < columns; i++)
            specs.Add(new ColumnSpec($"C{i}", JetDataType.Text, 510, IsFixedLength: false));
        return specs;
    }

    private static string Ddl(int columns) =>
        "CREATE TABLE WideRow (Id LONG PRIMARY KEY, "
        + string.Join(", ", Enumerable.Range(0, columns).Select(i => $"C{i} TEXT(255)")) + ")";

    // The cap is flat: these three shapes differ by 23 bytes of overhead, and the record total lands on the
    // same value each time. That is what rules out a limit derived from the row's layout, and so what makes
    // a single constant the right fix.
    [Theory]
    [InlineData(9)]
    [InlineData(12)]
    [InlineData(20)]
    public void Ace_stores_at_most_the_documented_record_size(int columns)
    {
        const int full = 7;
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "recsize-ace-");
        using OleDbConnection connection = AceTestDatabase.Open(path);
        using (OleDbCommand ddl = connection.CreateCommand())
        {
            ddl.CommandText = Ddl(columns);
            ddl.ExecuteNonQuery();
        }

        int id = 0, accepted = 0, rejected = 0;
        for (int tail = 1; tail <= 255 && rejected == 0; tail++)
        {
            string names = string.Join(", ", Enumerable.Range(0, full + 1).Select(i => $"C{i}"));
            string slots = string.Join(", ", Enumerable.Range(0, full + 1).Select(_ => "?"));
            using OleDbCommand insert = connection.CreateCommand();
            insert.CommandText = $"INSERT INTO WideRow (Id, {names}) VALUES ({++id}, {slots})";
            for (int i = 0; i < full; i++)
                insert.Parameters.Add($"c{i}", OleDbType.VarWChar, 255).Value = new string('a', 255);
            insert.Parameters.Add("t", OleDbType.VarWChar, 255).Value = new string('a', tail);

            int record = full * 510 + tail * 2 + Overhead(columns);
            try { insert.ExecuteNonQuery(); accepted = record; }
            catch (OleDbException) { rejected = record; }
        }

        int cap = new Jet4Format().MaxRecordSize;
        output.WriteLine($"{columns} columns: accepted up to {accepted}, refused {rejected} (cap {cap})");

        Assert.True(rejected > cap, $"ACE accepted a {rejected}-byte record, above the {cap} cap.");
        // Text steps in 2-byte units, so the last accepted record lands on the cap or one below it.
        Assert.InRange(accepted, cap - 1, cap);
    }

    [Fact]
    public void LibRed_refuses_a_record_ace_would_refuse_and_leaves_the_file_alone()
    {
        const int columns = 12;
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "recsize-refuse-");
        using var database = JetDatabase.Open(path, readOnly: false);
        database.CreateTable("WideRow", Specs(columns), primaryKey: ["Id"]);
        Table table = database.OpenTable("WideRow");

        // One character past the cap: 7 full columns plus 228 characters is 4062 bytes.
        var values = new object?[columns + 1];
        values[0] = 1;
        for (int i = 0; i < 7; i++) values[i + 1] = new string('a', 255);
        values[8] = new string('a', 228);

        var thrown = Assert.Throws<InvalidOperationException>(() => table.Insert(values));
        Assert.Contains("Record is too large", thrown.Message);
        Assert.Empty(table.Rows());
    }

    // The 20-byte reserve is unexplained, and the error ACE gives for a record inside it names row locking,
    // which is suggestive: row-level locking arrived in Jet 4, the same generation as this reserve.
    //
    // This does not test that theory - if the engine always reserves the space so a file stays portable
    // between clients, both modes behave identically and the theory predicts the null result. What it does
    // test is that assumption of portability, which is what licenses LibRed enforcing one constant for
    // everybody: LibRed has no locking mode at all, so a limit that moved with the mode would mean LibRed
    // had silently picked a side.
    [Theory]
    [InlineData(0)]   // page-level locking
    [InlineData(1)]   // row-level locking
    public void The_record_limit_does_not_move_with_the_locking_mode(int lockingMode)
    {
        const int columns = 12;
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, $"recsize-lock{lockingMode}-");
        using var connection = new OleDbConnection(
            $"Provider=Microsoft.ACE.OLEDB.16.0;Data Source={path};"
            + $"Jet OLEDB:Database Locking Mode={lockingMode};OLE DB Services=-4;");
        connection.Open();

        using (OleDbCommand ddl = connection.CreateCommand())
        {
            ddl.CommandText = Ddl(columns);
            ddl.ExecuteNonQuery();
        }

        int cap = new Jet4Format().MaxRecordSize;
        foreach (int tail in new[] { 227, 228 })
        {
            string names = string.Join(", ", Enumerable.Range(0, 8).Select(i => $"C{i}"));
            string slots = string.Join(", ", Enumerable.Range(0, 8).Select(_ => "?"));
            using OleDbCommand insert = connection.CreateCommand();
            insert.CommandText = $"INSERT INTO WideRow (Id, {names}) VALUES ({tail}, {slots})";
            for (int i = 0; i < 7; i++)
                insert.Parameters.Add($"c{i}", OleDbType.VarWChar, 255).Value = new string('a', 255);
            insert.Parameters.Add("t", OleDbType.VarWChar, 255).Value = new string('a', tail);

            int record = 7 * 510 + tail * 2 + Overhead(columns);
            Exception? failure = Record.Exception(() => insert.ExecuteNonQuery());
            output.WriteLine($"mode={lockingMode} record={record}: {(failure is null ? "accepted" : "refused")}");
            Assert.Equal(record > cap, failure is not null);
        }
    }

    [Fact]
    public void Ace_reads_the_largest_record_libred_will_write()
    {
        const int columns = 12;
        string full = new('a', 255);
        string tail = new('b', 227);   // 7 * 510 + 227 * 2 + 36 = 4060, exactly the cap
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "recsize-max-");

        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            database.CreateTable("WideRow", Specs(columns), primaryKey: ["Id"]);
            var values = new object?[columns + 1];
            values[0] = 1;
            for (int i = 0; i < 7; i++) values[i + 1] = full;
            values[8] = tail;
            database.OpenTable("WideRow").Insert(values);
        }

        using var connection = AceTestDatabase.Open(path);
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT C0, C6, C7 FROM WideRow";
        using OleDbDataReader rows = read.ExecuteReader();
        Assert.True(rows.Read());
        Assert.Equal(full, rows.GetString(0));
        Assert.Equal(full, rows.GetString(1));
        Assert.Equal(tail, rows.GetString(2));
        Assert.False(rows.Read());
    }
}
