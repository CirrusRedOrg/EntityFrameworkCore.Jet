using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using Xunit;

namespace LibRed.Core.Tests;

// The TDEF's variable-column count (0x2B) is a HIGH-WATER mark, not a live count: DROP COLUMN leaves it
// alone, and ADD COLUMN takes the new column's variable index from it and bumps it. So after dropping a
// variable column the stored count is deliberately larger than the number of variable columns still present.
//
// Getting that wrong is a corruption bug rather than a cosmetic one, and a subtle one: deriving the count
// from the live columns instead makes the next added column reuse a variable index that a surviving column
// already owns, so two columns read the same slot of every row's variable-length area.
//
// Checked BOTH ways, because the count alone would not catch an index collision: the header fields are
// compared against ACE performing the identical DDL, and ACE is then made to read and write the rows.
//
// The FIXED side is measured here too, deliberately, because the two halves of the same row turn out to
// follow OPPOSITE rules — see The_fixed_column_offsets_match_ace_after_a_drop_and_an_add. A variable
// column's slot index is abandoned when the column is dropped and the next one goes above it; a fixed
// column's byte offset is reused. Nothing about one half predicts the other, and having them side by side
// is the point: it is why writing the variable section by position looked reasonable.
public class VariableColumnHighWaterAccessTests(ITestOutputHelper output)
{
    // Drop a variable column from the MIDDLE. Dropping the last one would leave the high-water and the live
    // count differing in a way that a reused index could not detect, since there would be nothing above it.
    private static readonly string[] Ddl =
    [
        "CREATE TABLE V (K LONG, A TEXT(30), B TEXT(30), C TEXT(30), N LONG)",
        "ALTER TABLE V DROP COLUMN B",
        "ALTER TABLE V ADD COLUMN D TEXT(30)",
    ];

    [Fact]
    public void The_variable_column_count_matches_ace_after_a_drop_and_an_add()
    {
        string ace = Describe(AceRun);
        string libred = Describe(LibRedRun);
        output.WriteLine($"ACE    {ace}");
        output.WriteLine($"LibRed {libred}");

        Assert.Equal(ace, libred);

        // And say what the answer actually is, so the test documents the rule rather than only pinning it.
        // Five columns created, one dropped, one added, so five live — of which only THREE are variable
        // (A, C, D). 0x2B reads four because it still counts the dropped B, and D's variable index is 3
        // rather than the 2 a live count would have given it. B's index 1 is simply abandoned.
        Assert.Equal("maxCols=6 varCount=4 colCount=5 varIndexes=A:0 C:2 D:3", ace);
    }

    // The corruption shows up here rather than in the header: if D had reused B's index the two surviving
    // TEXT columns would collide in the variable-length area and ACE would read the wrong values.
    [Fact]
    public void Ace_reads_and_writes_rows_after_a_drop_and_an_add()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "varhw-");
        try
        {
            using (var database = JetDatabase.Open(path, readOnly: false))
            {
                Create(database);
                Insert(database, ("K", 1), ("A", "a1"), ("B", "b1"), ("C", "c1"), ("N", 11));
                DropAndAdd(database);
                Insert(database, ("K", 2), ("A", "a2"), ("C", "c2"), ("D", "d2"), ("N", 22));
            }

            using var connection = AceTestDatabase.Open(path);

            // The row written BEFORE the DDL: its surviving values must still read back from the right
            // slots. This is the assertion a reused variable index fails — it puts D on top of a surviving
            // column, so A or C comes back as the wrong string rather than as an error.
            // Column by column, so a failure names the column rather than the row.
            Assert.Equal("a1", Scalar(connection, "A", 1));
            Assert.Equal("c1", Scalar(connection, "C", 1));
            Assert.Equal(11, Scalar(connection, "N", 1));
            // D did not exist when row 1 was written. Asked through SQL, because the provider will answer
            // IS NULL for that case but throws if asked for the value.
            Assert.Equal(1, Scalar(connection, "COUNT(*)", 1, "AND D IS NULL"));

            // And the row written after, through every column including the new one.
            Assert.Equal("a2", Scalar(connection, "A", 2));
            Assert.Equal("c2", Scalar(connection, "C", 2));
            Assert.Equal("d2", Scalar(connection, "D", 2));
            Assert.Equal(22, Scalar(connection, "N", 2));

            // Finally ACE's own writer, which is the half LibRed cannot check for itself.
            using (var insert = connection.CreateCommand())
            {
                insert.CommandText = "INSERT INTO V (K, A, C, D, N) VALUES (3, 'a3', 'c3', 'd3', 33)";
                insert.ExecuteNonQuery();
            }
            Assert.Equal("a3", Scalar(connection, "A", 3));
            Assert.Equal("c3", Scalar(connection, "C", 3));
            Assert.Equal("d3", Scalar(connection, "D", 3));

            using var database2 = JetDatabase.Open(path);
            var table = database2.OpenTable("V");
            ColumnDef a = table.Definition.FindColumn("A")!, d = table.Definition.FindColumn("D")!;
            Assert.NotEqual(a.VariableIndex, d.VariableIndex);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>One column of one row, through ACE.</summary>
    private static object Scalar(OleDbConnection connection, string expression, int key, string extra = "")
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {expression} FROM V WHERE K = {key} {extra}";
        return command.ExecuteScalar()!;
    }

    private static void AceRun(string path)
    {
        using OleDbConnection connection = AceTestDatabase.Open(path);
        foreach (string sql in Ddl)
        {
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    // The same three operations as Ddl, through the Core API — LibRed.Core.Tests does not reference the
    // engine, so the SQL above is ACE's half only.
    private static void LibRedRun(string path)
    {
        using var database = JetDatabase.Open(path, readOnly: false);
        Create(database);
        DropAndAdd(database);
    }

    // The case the fix above leaves open. Dropping the LAST variable column takes the live high-water below
    // the TDEF's 0x2B, which never decrements — so a row written afterwards could carry either the live
    // count or the stored one, and the two differ only here. Measured rather than assumed: ACE has to read
    // rows written on both sides of the drop.
    [Fact]
    public void Ace_reads_rows_after_the_last_variable_column_is_dropped()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "varhw-tail-");
        try
        {
            using (var database = JetDatabase.Open(path, readOnly: false))
            {
                database.CreateTable("W", [Long("K"), Text("A"), Text("B"), Long("N")]);
                var table = database.OpenTable("W");
                var before = new object?[table.Definition.Columns.Count];
                foreach ((string c, object v) in new (string, object)[] { ("K", 1), ("A", "a1"), ("B", "b1"), ("N", 11) })
                    before[table.Definition.FindColumn(c)!.Index] = v;
                table.Insert(before);

                Assert.True(database.DropColumn("W", "B"));

                var after = database.OpenTable("W");
                var row = new object?[after.Definition.Columns.Count];
                foreach ((string c, object v) in new (string, object)[] { ("K", 2), ("A", "a2"), ("N", 22) })
                    row[after.Definition.FindColumn(c)!.Index] = v;
                after.Insert(row);
            }

            using var connection = AceTestDatabase.Open(path);
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT K, A, N FROM W ORDER BY K";
            using OleDbDataReader reader = command.ExecuteReader();

            Assert.True(reader.Read());
            Assert.Equal("a1", reader.GetString(1));       // written before the drop
            Assert.Equal(11, reader.GetInt32(2));
            Assert.True(reader.Read());
            Assert.Equal("a2", reader.GetString(1));       // written after it
            Assert.Equal(22, reader.GetInt32(2));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The leading count and null-bitmap width are max(column id) + 1 over the LIVE columns, in both row
    // writers. That is a third candidate for the same mistake: the TDEF's own id high-water (0x29) never
    // decrements either, so the two part company when the HIGHEST-id column is dropped. Rows written
    // afterwards then carry a shorter count and a narrower bitmap than the rows before them.
    //
    // Done on an all-fixed table, which stacks the other open case on top: with no variable columns a row
    // has no offset table to pin the fixed-region length, so RowEncoder derives it from the live columns and
    // it shrinks too. Both halves of the row get shorter at once, and ACE has to read across the change.
    [Fact]
    public void Ace_reads_rows_after_the_highest_id_column_is_dropped()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "maxid-");
        try
        {
            using (var database = JetDatabase.Open(path, readOnly: false))
            {
                database.CreateTable("H", [Long("K"), Long("A"), Long("B")]);
                Insert(database, "H", ("K", 1), ("A", 10), ("B", 100));

                Assert.True(database.DropColumn("H", "B"));    // highest id AND last fixed column
                Insert(database, "H", ("K", 2), ("A", 20));
            }

            using var connection = AceTestDatabase.Open(path);
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT K, A FROM H ORDER BY K";
            using OleDbDataReader reader = command.ExecuteReader();

            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(10, reader.GetInt32(1));     // written before the drop
            Assert.True(reader.Read());
            Assert.Equal(2, reader.GetInt32(0));
            Assert.Equal(20, reader.GetInt32(1));     // written after it
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The third writer of a row, after INSERT and UPDATE: the in-place ALTER COLUMN re-lay. It does not go
    // through RowEncoder.Encode at all — BuildRelaidRecord takes the old row's variable chunks POSITIONALLY,
    // straight off the row, and appends the retyped column's chunk on the end. That is right only while the
    // row's chunk count equals the TDEF's 0x2B, because the retyped column's declared variable index is
    // taken from 0x2B.
    //
    // Dropping the LAST variable column is exactly where those part company: 0x2B never decrements, but the
    // rows written afterwards carry only max(live index)+1 slots. So the retyped column would be declared at
    // an index past the end of the row it was just written into.
    [Fact]
    public void Ace_reads_a_retyped_column_after_the_last_variable_column_was_dropped()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "relay-");
        try
        {
            using (var database = JetDatabase.Open(path, readOnly: false))
            {
                database.CreateTable("R", [Long("K"), Text("A"), Text("Z"), Long("N")]);
                Insert(database, "R", ("K", 1), ("A", "a1"), ("Z", "z1"), ("N", 11));

                Assert.True(database.DropColumn("R", "Z"));      // the LAST variable column
                Insert(database, "R", ("K", 2), ("A", "a2"), ("N", 22));

                // Retype the fixed N to a variable type: it must land on a slot the row actually has.
                database.AlterColumnTypeInPlace("R", "N", new ColumnSpec("N", JetDataType.Text, 20, IsFixedLength: false));
            }

            using var connection = AceTestDatabase.Open(path);
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT K, A, N FROM R ORDER BY K";
            using OleDbDataReader reader = command.ExecuteReader();

            Assert.True(reader.Read());
            Assert.Equal("a1", reader.GetString(1));
            Assert.Equal("11", reader.GetString(2));
            Assert.True(reader.Read());
            Assert.Equal("a2", reader.GetString(1));
            Assert.Equal("22", reader.GetString(2));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // What ACE itself does for that sequence, which is what decides the fix: whether the retyped column's
    // variable index comes from the TDEF's 0x2B high-water or from the next slot the rows actually have.
    [Fact]
    public void The_retyped_column_index_matches_ace_after_the_last_variable_column_was_dropped()
    {
        string ace = DescribeRelay(path =>
        {
            using OleDbConnection connection = AceTestDatabase.Open(path);
            foreach (string sql in (string[])
                     [
                         "CREATE TABLE R (K LONG, A TEXT(30), Z TEXT(30), N LONG)",
                         "ALTER TABLE R DROP COLUMN Z",
                         "ALTER TABLE R ALTER COLUMN N TEXT(20)",
                     ])
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        });

        string libred = DescribeRelay(path =>
        {
            using var database = JetDatabase.Open(path, readOnly: false);
            database.CreateTable("R", [Long("K"), Text("A"), Text("Z"), Long("N")]);
            Assert.True(database.DropColumn("R", "Z"));
            database.AlterColumnTypeInPlace("R", "N", new ColumnSpec("N", JetDataType.Text, 20, IsFixedLength: false));
        });

        output.WriteLine($"ACE    {ace}");
        output.WriteLine($"LibRed {libred}");
        Assert.Equal(ace, libred);
    }

    private static string DescribeRelay(Action<string> run)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "relay-");
        try
        {
            run(path);
            int definitionPage;
            string indexes;
            using (var database = JetDatabase.Open(path))
            {
                TableDef table = database.Catalog.FindTable("R")!;
                definitionPage = table.DefinitionPage;
                indexes = string.Join(" ", table.Columns.Where(c => !c.IsFixedLength)
                    .OrderBy(c => c.VariableIndex).Select(c => $"{c.Name}:{c.VariableIndex}"));
            }
            using var channel = PageChannel.Open(path, readOnly: true);
            byte[] page = channel.ReadPage(definitionPage).Span.ToArray();
            int varCount = BinaryPrimitives.ReadUInt16LittleEndian(
                page.AsSpan(channel.Format.TdefVariableColumnsOffset, 2));
            return $"varCount={varCount} indexes={indexes}";
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The same question on the FIXED side, which is the other half of the row. A fixed column owns a byte
    // offset rather than a slot index, and AddColumn takes the new column's offset from the live maximum —
    // so dropping the LAST fixed column makes that maximum fall back, and the next added column reuses the
    // dropped column's bytes. Whether ACE does the same, or keeps a high-water and leaves the hole, decides
    // whether the two engines agree about where the column lives. Measured; nothing here assumes an answer.
    [Fact]
    public void The_fixed_column_offsets_match_ace_after_a_drop_and_an_add()
    {
        string ace = DescribeFixed(path =>
        {
            using OleDbConnection connection = AceTestDatabase.Open(path);
            foreach (string sql in (string[])
                     [
                         "CREATE TABLE F (K LONG, P LONG, Q LONG, T TEXT(30))",
                         "ALTER TABLE F DROP COLUMN Q",
                         "ALTER TABLE F ADD COLUMN R LONG",
                     ])
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        });

        string libred = DescribeFixed(path =>
        {
            using var database = JetDatabase.Open(path, readOnly: false);
            database.CreateTable("F", [Long("K"), Long("P"), Long("Q"), Text("T")]);
            Assert.True(database.DropColumn("F", "Q"));
            Assert.True(database.AddColumn("F", Long("R")));
        });

        output.WriteLine($"ACE    {ace}");
        output.WriteLine($"LibRed {libred}");
        Assert.Equal(ace, libred);
    }

    // Descriptor byte 0x07 on a FIXED column added after a variable one. The spec says it is the running count
    // of preceding variable columns and that writing 0 yields a file Access rejects; LibRed's ADD COLUMN wrote
    // 0 for years and the ACE-read tests never complained, so one of those two statements was wrong. This
    // measures what ACE itself writes, which settles it for both.
    [Fact]
    public void The_variable_table_index_of_an_added_fixed_column_matches_ace()
    {
        string ace = DescribeVarTableIndex(path =>
        {
            using OleDbConnection connection = AceTestDatabase.Open(path);
            foreach (string sql in (string[])
                     [
                         "CREATE TABLE X (K LONG, A TEXT(30), B TEXT(30))",
                         "ALTER TABLE X ADD COLUMN C LONG",
                     ])
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        });

        string libred = DescribeVarTableIndex(path =>
        {
            using var database = JetDatabase.Open(path, readOnly: false);
            database.CreateTable("X", [Long("K"), Text("A"), Text("B")]);
            Assert.True(database.AddColumn("X", Long("C")));
        });

        output.WriteLine($"ACE    {ace}");
        output.WriteLine($"LibRed {libred}");
        Assert.Equal(ace, libred);
    }

    /// <summary>Descriptor byte 0x07 for every column, read raw off the TDEF page — <see cref="ColumnDef"/>
    /// does not surface it, and the point here is the stored byte rather than the derived model.</summary>
    private static string DescribeVarTableIndex(Action<string> run)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "vartab-");
        try
        {
            run(path);
            int definitionPage;
            List<(string Name, int Index)> columns;
            using (var database = JetDatabase.Open(path))
            {
                TableDef table = database.Catalog.FindTable("X")!;
                definitionPage = table.DefinitionPage;
                columns = table.Columns.Select(c => (c.Name, c.Index)).ToList();
            }

            using var channel = PageChannel.Open(path, readOnly: true);
            var tdef = new Pages.TableDefinitionPage();
            tdef.Read(channel, definitionPage);
            byte[] page = channel.ReadPage(definitionPage).Span.ToArray();
            JetFormatBase format = channel.Format;
            int columnBlock = format.TdefRealIndexBlockOffset + tdef.IndexCount * format.RealIndexEntrySize;

            return string.Join(" ", columns.Select(c =>
            {
                int entry = columnBlock + c.Index * format.ColumnDescriptorSize;
                int stored = BinaryPrimitives.ReadUInt16LittleEndian(
                    page.AsSpan(entry + format.ColumnVariableIndexOffset, 2));
                return $"{c.Name}:{stored}";
            }));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Each surviving fixed column's byte offset, which is what a reused offset would collide on.
    /// </summary>
    private static string DescribeFixed(Action<string> run)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "fixhw-");
        try
        {
            run(path);
            using var database = JetDatabase.Open(path);
            return string.Join(" ", database.Catalog.FindTable("F")!.Columns
                .Where(c => c.IsFixedLength)
                .OrderBy(c => c.FixedOffset)
                .Select(c => $"{c.Name}@{c.FixedOffset}"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static ColumnSpec Text(string name) => new(name, JetDataType.Text, 30, IsFixedLength: false);
    private static ColumnSpec Long(string name) => new(name, JetDataType.Int32, 4, IsFixedLength: true);

    private static void Create(JetDatabase database) =>
        database.CreateTable("V", [Long("K"), Text("A"), Text("B"), Text("C"), Long("N")]);

    private static void DropAndAdd(JetDatabase database)
    {
        Assert.True(database.DropColumn("V", "B"));
        Assert.True(database.AddColumn("V", Text("D")));
    }

    private static void Insert(JetDatabase database, params (string Column, object Value)[] cells) =>
        Insert(database, "V", cells);

    private static void Insert(JetDatabase database, string tableName, params (string Column, object Value)[] cells)
    {
        var table = database.OpenTable(tableName);
        var values = new object?[table.Definition.Columns.Count];
        foreach ((string column, object value) in cells)
            values[table.Definition.FindColumn(column)!.Index] = value;
        table.Insert(values);
    }

    /// <summary>The three TDEF header counts that the drop and the add move, plus each surviving variable
    /// column's index — which is what a recomputed count would corrupt.</summary>
    private static string Describe(Action<string> run)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "varhw-");
        try
        {
            run(path);

            int definitionPage;
            string indexes;
            using (var database = JetDatabase.Open(path))
            {
                TableDef table = database.Catalog.FindTable("V")!;
                definitionPage = table.DefinitionPage;
                indexes = string.Join(" ", table.Columns
                    .Where(c => !c.IsFixedLength)
                    .OrderBy(c => c.VariableIndex)
                    .Select(c => $"{c.Name}:{c.VariableIndex}"));
            }

            using var channel = PageChannel.Open(path, readOnly: true);
            byte[] page = channel.ReadPage(definitionPage).Span.ToArray();
            int At(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(offset, 2));

            return $"maxCols={At(channel.Format.TdefMaxColumnsOffset)} " +
                   $"varCount={At(channel.Format.TdefVariableColumnsOffset)} " +
                   $"colCount={At(channel.Format.TdefColumnCountOffset)} " +
                   $"varIndexes={indexes}";
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
