using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.Engine;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

[Collection(AceCollection.Name)]
public class LongValueColumnLayoutAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Compare_ACE_and_LibRed_repeated_long_value_alter(bool binary, bool drop)
    {
        string acePath = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-lv-layout-");
        string type = binary ? "LONGBINARY" : "LONGCHAR";
        object payload = binary ? Enumerable.Range(0, 8000).Select(i => (byte)i).ToArray() : new string('\u4E00', 4000);
        using (var connection = AceTestDatabase.Open(acePath))
        {
            Execute(connection, $"CREATE TABLE LayoutProbe (Id LONG PRIMARY KEY, Gone TEXT(10), Payload {type}, Tail LONG, Flag BIT)");
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO LayoutProbe (Id, Gone, Payload, Tail, Flag) VALUES (1, 'old', ?, 73, TRUE)";
            insert.Parameters.Add("p", binary ? OleDbType.LongVarBinary : OleDbType.LongVarWChar, binary ? 8000 : 4000).Value = payload;
            insert.ExecuteNonQuery();
            Execute(connection, "INSERT INTO LayoutProbe (Id, Tail, Flag) VALUES (2, 91, FALSE)");
            if (drop) Execute(connection, "ALTER TABLE LayoutProbe DROP COLUMN Gone");
        }
        string libredPath = TemporaryDatabase.CopyPath(acePath, "libred-lv-layout-");
        Dump(acePath, "ACE initial");
        for (int step = 1; step <= 2; step++)
        {
            using (var connection = AceTestDatabase.Open(acePath))
                Execute(connection, $"ALTER TABLE LayoutProbe ALTER COLUMN Payload {type}");
            using (var db = JetDatabase.Open(libredPath, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery($"ALTER TABLE LayoutProbe ALTER COLUMN Payload {type}");
            Dump(acePath, $"ACE step {step}");
            Dump(libredPath, $"LibRed step {step}");
            Verify(acePath, payload);
            Verify(libredPath, payload);
        }
        // Continue writing with ACE after LibRed's rebuild to expose latent descriptor/row faults.
        using (var connection = AceTestDatabase.Open(libredPath))
        {
            Execute(connection, "INSERT INTO LayoutProbe (Id, Tail, Flag) VALUES (3, 117, TRUE)");
            Execute(connection, "UPDATE LayoutProbe SET Tail = 74 WHERE Id = 1");
        }
        using var reopened = AceTestDatabase.Open(libredPath);
        using var read = reopened.CreateCommand();
        read.CommandText = "SELECT SUM(Tail) FROM LayoutProbe";
        Assert.Equal(282, Convert.ToInt32(read.ExecuteScalar()));
    }

    private static void Execute(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Verify(string path, object expected)
    {
        using var connection = AceTestDatabase.Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Payload, Tail, Flag FROM LayoutProbe ORDER BY Id";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        if (expected is string text) Assert.Equal(text, reader.GetString(1));
        else Assert.Equal((byte[])expected, Assert.IsType<byte[]>(reader.GetValue(1)));
        Assert.Equal(73, reader.GetInt32(2));
        Assert.True(reader.GetBoolean(3));
        Assert.True(reader.Read());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.True(reader.IsDBNull(1));
        Assert.Equal(91, reader.GetInt32(2));
        Assert.False(reader.GetBoolean(3));
        Assert.False(reader.Read());
    }

    private void Dump(string path, string stage)
    {
        using var channel = PageChannel.Open(path, readOnly: true);
        var table = new JetCatalog(channel).FindTable("LayoutProbe")!;
        var header = channel.ReadPage(table.DefinitionPage);
        output.WriteLine($"{stage}: highWater={BinaryPrimitives.ReadUInt16LittleEndian(header.Span.Slice(channel.Format.TdefMaxColumnsOffset, 2))}, varCount={BinaryPrimitives.ReadUInt16LittleEndian(header.Span.Slice(channel.Format.TdefVariableColumnsOffset, 2))}");
        foreach (var c in table.Columns)
            output.WriteLine($"{c.Name}: id={c.ColumnId}, ordinal={c.Index}, var={c.VariableIndex}, fixedOffset={c.FixedOffset}, descriptor={Convert.ToHexString(c.RawDescriptor!)}");
        foreach (int number in new UsageMap(channel, table).DataPages())
        {
            var page = new DataPage();
            page.Read(channel.ReadPage(number), channel.Format);
            for (int row = 0; row < page.RowCount; row++)
                if (!page.Rows[row].IsDeleted) output.WriteLine($"row {number}:{row}: {Convert.ToHexString(page.GetRow(row))}");
        }
    }
}
