using System.Reflection;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Conformance: a calculated column's cached result must decode to the value ACE reads back.
//
// ACE stores the result in the variable-length section wrapped in an envelope (page-02b §3.4a), and the
// descriptor's type is a PROMOTED storage type — the payload's own length is what says how to decode it.
// Neither fact is guessable from the descriptor, so the only way to hold them is to have ACE author a
// column of every type and compare.
//
// LibRed cannot create a calculated column (Access SQL has no syntax for one), so DAO's object model is
// the author here — the same path Access's UI uses. Each column gets its own table because ACE validates
// the expression when the TableDef is appended, and one rejected expression would take the rest with it.
public class CalculatedColumnAccessTests(ITestOutputHelper output)
{
    private const int UseJet = 2;
    private const int DbBoolean = 1, DbByte = 2, DbInteger = 3, DbLong = 4, DbCurrency = 5,
                      DbSingle = 6, DbDouble = 7, DbDate = 8, DbText = 10, DbMemo = 12;

    /// <summary>(column, DAO type, text size, expression) — one per type DAO will accept.</summary>
    private static readonly (string Name, int Type, int Size, string Expression)[] Calculated =
    [
        ("CText",  DbText,     40, "[A] & \"-x\""),
        ("CMemo",  DbMemo,      0, "[A] & \"-memo\""),
        ("CLong",  DbLong,      0, "[Qty]*2"),
        ("CInt",   DbInteger,   0, "[Qty]+1"),
        ("CByte",  DbByte,      0, "[Qty]+2"),
        ("CDbl",   DbDouble,    0, "[Qty]/4"),
        ("CSng",   DbSingle,    0, "[Qty]/8"),
        ("CCur",   DbCurrency,  0, "[Price]*2"),
        ("CDate",  DbDate,      0, "[D1]+1"),
        ("CBool",  DbBoolean,   0, "[Qty]>1"),
    ];

    // Row 3 is the one that matters twice over: Qty=0 makes [Qty]>1 store False (whose null-bitmap bit is
    // set anyway), and the NULLs make [D1]+1 evaluate to Null (stored as a zero-length payload).
    private static readonly string[] Seed =
    [
        "INSERT INTO {0} (Id, Qty, Price, A, D1) VALUES (1, 7, 12.5, 'hello', #2003-09-29#)",
        "INSERT INTO {0} (Id, Qty, Price, A, D1) VALUES (2, 3, 0.25, 'zz', #1999-01-02#)",
        "INSERT INTO {0} (Id, Qty, Price, A, D1) VALUES (3, 0, 0, NULL, NULL)",
    ];

    [Fact]
    public void Decodes_the_same_calculated_values_as_ace()
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calculated-");
        try
        {
            string[] created = CreateFixture(engine!, path);
            Assert.NotEmpty(created);
            output.WriteLine($"DAO created: {string.Join(", ", created)}");

            Dictionary<string, List<object?>> ace = SeedAndReadWithAce(path, created);

            // Opening the catalog at all is half the regression: ACE gives a calculated Memo a long-value
            // map entry while declaring it Text, and the catalog reads EVERY table definition — so a guard
            // that rejected the entry made every table in the database unreadable, not just that one.
            using var db = JetDatabase.Open(path, readOnly: true);
            Assert.NotEmpty(db.Catalog.Tables);

            var mismatches = new List<string>();
            foreach (string name in created)
            {
                Table table = db.OpenTable("T_" + name);
                int index = table.Definition.FindColumn(name)!.Index;
                List<object?> libred = [.. table.Rows().Select(r => r[index])];
                List<object?> expected = ace[name];

                output.WriteLine($"  {name,-6} ACE [{Format(expected)}]  LibRed [{Format(libred)}]");
                if (libred.Count != expected.Count)
                {
                    mismatches.Add($"{name}: {expected.Count} rows from ACE, {libred.Count} from LibRed");
                    continue;
                }
                for (int i = 0; i < expected.Count; i++)
                    if (!Matches(expected[i], libred[i]))
                        mismatches.Add($"{name} row {i + 1}: ACE {Describe(expected[i])}, LibRed {Describe(libred[i])}");
            }

            Assert.Empty(mismatches);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Every case above has the declared type and the expression's type agreeing, which is the only shape DAO
    // produces by default -- and while they agree, the payload's width is enough to recover its type. CDbl is
    // the one conversion ACE allows in a calculated column, and it accepts it against ANY declared type, so it
    // is the way to build a column where the descriptor (the promoted type of the expression) and the real
    // result type disagree. That is where a width-derived guess reads the wrong type or throws, and only the
    // ResultType property gets it right.
    [Theory]
    [InlineData(DbLong, "Int32")]        // descriptor Double, ResultType Int32,    4-byte payload
    [InlineData(DbCurrency, "Currency")] // descriptor Double, ResultType Currency, 8-byte payload
    [InlineData(DbSingle, "Single")]     // descriptor Double, ResultType Single,   4-byte payload
    [InlineData(DbInteger, "Int16")]     // descriptor Double, ResultType Int16,    2-byte payload
    public void Decodes_a_calculated_column_whose_declared_and_expression_types_differ(int declaredType, string expected)
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calculated-mismatch-");
        try
        {
            CreateTable(engine!, path, "C", declaredType, 0, "CDbl([Qty])");
            using (var connection = AceTestDatabase.Open(path))
            {
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO T_C (Id, Qty, Price, A, D1) VALUES (1, 7, 1, 'a', #2003-09-29#)";
                insert.ExecuteNonQuery();
            }

            using var db = JetDatabase.Open(path, readOnly: true);
            Table table = db.OpenTable("T_C");
            ColumnDef column = table.Definition.FindColumn("C")!;

            output.WriteLine($"descriptor={column.Type} ResultType={column.CalculatedResultType} " +
                             $"expression=\"{column.CalculatedExpression}\"");

            // The descriptor really does disagree -- otherwise this theory proves nothing.
            Assert.Equal(JetDataType.Double, column.Type);
            Assert.Equal(expected, column.CalculatedResultType?.ToString());
            Assert.Equal("CDbl([Qty])", column.CalculatedExpression);

            object? value = table.Rows().Single()[column.Index];
            Assert.NotNull(value);
            Assert.Equal(7m, Convert.ToDecimal(value));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The write side. A row LibRed inserts must be byte-identical to the one ACE writes for the same inputs:
    // the stored value is a cache neither engine re-derives on read, so anything less than byte parity is a
    // divergence nothing would report.
    [Theory]
    [InlineData("CLong")]
    [InlineData("CInt")]
    [InlineData("CByte")]
    [InlineData("CDbl")]
    [InlineData("CSng")]
    [InlineData("CCur")]
    [InlineData("CBool")]
    [InlineData("CText")]
    // A calculated Memo comparable byte for byte too, as long as its result still inlines: the slot then
    // carries the payload rather than a page number, and nothing in it varies between the two writers.
    [InlineData("CMemo")]
    public void Writes_the_same_envelope_ace_writes(string name)
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calculated-write-");
        try
        {
            (int type, int size, string expression) = Calculated.Single(c => c.Name == name) is var spec
                ? (spec.Type, spec.Size, spec.Expression) : default;
            CreateTable(engine!, path, name, type, size, expression);
            string table = "T_" + name;

            // Row 1 written by ACE, row 2 by LibRed, from identical inputs.
            using (var connection = AceTestDatabase.Open(path))
            {
                using var insert = connection.CreateCommand();
                insert.CommandText =
                    $"INSERT INTO {table} (Id, Qty, Price, A, D1) VALUES (1, 7, 12.5, 'hello', #2003-09-29#)";
                insert.ExecuteNonQuery();
            }

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Table t = db.OpenTable(table);
                var values = new object?[t.Definition.Columns.Count];
                values[t.Definition.FindColumn("Id")!.Index] = 2;
                values[t.Definition.FindColumn("Qty")!.Index] = 7;
                values[t.Definition.FindColumn("Price")!.Index] = 12.5m;
                values[t.Definition.FindColumn("A")!.Index] = "hello";
                values[t.Definition.FindColumn("D1")!.Index] = new DateTime(2003, 9, 29);
                t.Insert(values);
            }

            (byte[] fromAce, byte[] fromLibRed) = StoredEnvelopes(path, table, name);
            output.WriteLine($"ACE    {Convert.ToHexString(fromAce)}");
            output.WriteLine($"LibRed {Convert.ToHexString(fromLibRed)}");
            Assert.Equal(fromAce, fromLibRed);

            // And ACE must read its own value back out of the row LibRed wrote.
            using var check = AceTestDatabase.Open(path);
            using var select = check.CreateCommand();
            select.CommandText = $"SELECT [{name}] FROM [{table}] ORDER BY Id";
            using var reader = select.ExecuteReader();
            var read = new List<object?>();
            while (reader.Read()) read.Add(reader.IsDBNull(0) ? null : reader.GetValue(0));
            Assert.Equal(2, read.Count);
            Assert.Equal(Describe(read[0]), Describe(read[1]));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A calculated Memo result outgrows the row exactly as a plain memo does, and then has to reach an LVAL
    // page. Unlike the value types above, the two slots CANNOT be compared byte for byte -- each descriptor
    // names the page its own value landed on -- so what is asserted is the thing that actually matters: both
    // sides really spilled rather than inlined, and ACE reads its own answer back out of the row LibRed
    // wrote. A wrongly formed payload (or one compressed when ACE would not have) fails that last step.
    [Fact]
    public void Spills_an_oversized_calculated_memo_to_a_long_value_page()
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        const string name = "CMemoLong";
        const string expected = "hello" + " long enough to outgrow the row";
        string path = TemporaryDatabase.CreatePath("calculated-spill-");
        try
        {
            CreateTable(engine!, path, name, DbMemo, 0, "[A] & \" long enough to outgrow the row\"");
            string table = "T_" + name;

            using (var connection = AceTestDatabase.Open(path))
            {
                using var insert = connection.CreateCommand();
                insert.CommandText =
                    $"INSERT INTO {table} (Id, Qty, Price, A, D1) VALUES (1, 7, 12.5, 'hello', #2003-09-29#)";
                insert.ExecuteNonQuery();
            }

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Table t = db.OpenTable(table);
                var values = new object?[t.Definition.Columns.Count];
                values[t.Definition.FindColumn("Id")!.Index] = 2;
                values[t.Definition.FindColumn("Qty")!.Index] = 7;
                values[t.Definition.FindColumn("Price")!.Index] = 12.5m;
                values[t.Definition.FindColumn("A")!.Index] = "hello";
                values[t.Definition.FindColumn("D1")!.Index] = new DateTime(2003, 9, 29);
                t.Insert(values);
            }

            (byte[] fromAce, byte[] fromLibRed) = StoredEnvelopes(path, table, name);
            output.WriteLine($"ACE    {Convert.ToHexString(fromAce)}");
            output.WriteLine($"LibRed {Convert.ToHexString(fromLibRed)}");

            // A bare 12-byte descriptor with a non-inline flag is the proof it spilled; an inline slot would
            // carry its payload with it and the test would be passing on a value that never left the row.
            Assert.Equal(LibRed.Formats.LongValueFormat.DescriptorSize, fromAce.Length);
            Assert.Equal(LibRed.Formats.LongValueFormat.DescriptorSize, fromLibRed.Length);
            Assert.NotEqual(
                LibRed.Formats.LongValueFormat.FlagInline,
                (byte)(fromLibRed[3] & LibRed.Formats.LongValueFormat.FlagMask));

            using var check = AceTestDatabase.Open(path);
            using var select = check.CreateCommand();
            select.CommandText = $"SELECT [{name}] FROM [{table}] ORDER BY Id";
            using var reader = select.ExecuteReader();
            var read = new List<object?>();
            while (reader.Read()) read.Add(reader.IsDBNull(0) ? null : reader.GetValue(0));
            Assert.Equal(2, read.Count);
            Assert.Equal(expected, read[0]);
            Assert.Equal(read[0], read[1]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Which text payloads ACE compresses inside a calculated envelope. A calculated TEXT is compressed; a
    // calculated MEMO never is. StoredType folds Memo onto Text, which hid the difference and had LibRed
    // compressing both -- and that is not cosmetic, because compressing shrinks the envelope back under the
    // 64-byte inline limit and so decides whether the value ever reaches a long-value page.
    [Theory]
    [InlineData("PText", DbText, 40, "[A] & \"-x\"", true)]
    [InlineData("PMemoShort", DbMemo, 0, "[A] & \"-memo\"", false)]
    public void Records_when_ace_compresses_a_calculated_text_payload(
        string name, int type, int size, string expression, bool compressed)
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calc-compress-");
        try
        {
            CreateTable(engine!, path, name, type, size, expression);
            string table = "T_" + name;
            using (var connection = AceTestDatabase.Open(path))
            {
                foreach (int id in (int[])[1, 2])
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText =
                        $"INSERT INTO {table} (Id, Qty, Price, A, D1) "
                        + $"VALUES ({id}, 7, 12.5, 'hello', #2003-09-29#)";
                    insert.ExecuteNonQuery();
                }
            }

            // Both shapes here store the payload in the row, so the compressed-text marker FFFE is visible in
            // the slot itself. It cannot occur by accident: UTF-16LE of ASCII always has 00 in the high byte.
            (byte[] first, _) = StoredEnvelopes(path, table, name);
            string hex = Convert.ToHexString(first);
            output.WriteLine($"{name,-11} {hex}");
            Assert.Equal(compressed, hex.Contains("FFFE", StringComparison.Ordinal));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A calculated column's expression lives in the table's property blob, NOT in its 25-byte descriptor, so
    // a DDL operation that rebuilds the TDEF can carry the 0xC0 flag across and still lose the expression --
    // leaving a column ACE cannot evaluate. ADD COLUMN and DROP COLUMN both rebuild, so both are asked.
    [Theory]
    [InlineData("add")]
    [InlineData("drop")]
    public void Rebuilding_a_tdef_preserves_a_calculated_column(string operation)
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        const string name = "CLong";
        string path = TemporaryDatabase.CreatePath("calc-rebuild-");
        try
        {
            CreateTable(engine!, path, name, DbLong, 0, "[Qty]*2");
            string table = "T_" + name;
            using (var connection = AceTestDatabase.Open(path))
            {
                using var insert = connection.CreateCommand();
                insert.CommandText =
                    $"INSERT INTO {table} (Id, Qty, Price, A, D1) VALUES (1, 7, 12.5, 'hello', #2003-09-29#)";
                insert.ExecuteNonQuery();
            }

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                if (operation == "add")
                    db.AddColumn(table, new ColumnSpec("Extra", JetDataType.Int32, 4, IsFixedLength: true));
                else
                    db.DropColumn(table, "Price");
            }

            using (var db = JetDatabase.Open(path, readOnly: true))
            {
                ColumnDef column = db.OpenTable(table).Definition.FindColumn(name)!;
                output.WriteLine($"after {operation}: calculated={column.IsCalculated} "
                                 + $"expression={column.CalculatedExpression ?? "(lost)"} "
                                 + $"resultType={column.CalculatedResultType?.ToString() ?? "(lost)"}");
                Assert.True(column.IsCalculated);
                Assert.Equal("[Qty]*2", column.CalculatedExpression);
                Assert.Equal(JetDataType.Int32, column.CalculatedResultType);
            }

            // The engine that has to live with the result gets the last word.
            using var check = AceTestDatabase.Open(path);
            using var select = check.CreateCommand();
            select.CommandText = $"SELECT [{name}] FROM [{table}]";
            Assert.Equal(14, Convert.ToInt32(select.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A calculated column is derived, so writing one directly is refused rather than obeyed or silently
    // dropped. Measured: ACE rejects an INSERT that names one and an UPDATE that sets one alike, with
    // "Cannot update 'x'; field not updateable." LibRed used to accept both and quietly compute the value
    // instead -- which is the worse of the two failures, because the caller's value simply vanishes.
    [Fact]
    public void Refuses_an_explicit_value_for_a_calculated_column()
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        const string name = "CLong";
        string path = TemporaryDatabase.CreatePath("calc-explicit-");
        try
        {
            CreateTable(engine!, path, name, DbLong, 0, "[Qty]*2");
            string table = "T_" + name;

            using (var connection = AceTestDatabase.Open(path))
            {
                using (var seed = connection.CreateCommand())
                {
                    seed.CommandText =
                        $"INSERT INTO {table} (Id, Qty, Price, A, D1) VALUES (1, 7, 12.5, 'hello', #2003-09-29#)";
                    seed.ExecuteNonQuery();
                }

                string aceInsert = Attempt(connection,
                    $"INSERT INTO {table} (Id, Qty, [{name}]) VALUES (2, 3, 99)");
                string aceUpdate = Attempt(connection,
                    $"UPDATE {table} SET [{name}] = 99 WHERE Id = 1");
                output.WriteLine($"  ACE INSERT: {aceInsert}");
                output.WriteLine($"  ACE UPDATE: {aceUpdate}");
                Assert.Contains("not updateable", aceInsert, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("not updateable", aceUpdate, StringComparison.OrdinalIgnoreCase);
            }

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Table t = db.OpenTable(table);
                ColumnDef calculated = t.Definition.FindColumn(name)!;

                var values = new object?[t.Definition.Columns.Count];
                values[t.Definition.FindColumn("Id")!.Index] = 2;
                values[t.Definition.FindColumn("Qty")!.Index] = 5;
                values[calculated.Index] = 99;                  // explicit, and wrong: [Qty]*2 would be 10
                var onInsert = Assert.Throws<InvalidOperationException>(() => t.Insert(values));
                Assert.Contains("not updateable", onInsert.Message, StringComparison.OrdinalIgnoreCase);

                // On UPDATE the slot always holds the cached value -- callers hand back the row they read --
                // so it is naming the column as changed that is refused, not the value being there.
                var (id, row) = t.Rows().WithIds().Single();
                row[calculated.Index] = 99;
                var onUpdate = Assert.Throws<InvalidOperationException>(
                    () => t.Update(id, row, new HashSet<int> { calculated.Index }));
                Assert.Contains("not updateable", onUpdate.Message, StringComparison.OrdinalIgnoreCase);
            }

            // Nothing got through either engine: the seeded row still holds ACE's own answer, alone.
            using var check = AceTestDatabase.Open(path);
            using var select = check.CreateCommand();
            select.CommandText = $"SELECT Id, [{name}] FROM [{table}] ORDER BY Id";
            using var reader = select.ExecuteReader();
            var rows = new List<(int Id, int Value)>();
            while (reader.Read()) rows.Add((reader.GetInt32(0), Convert.ToInt32(reader.GetValue(1))));
            Assert.Equal([(1, 14)], rows);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string Attempt(System.Data.Common.DbConnection connection, string sql)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
            return "ACCEPTED";
        }
        catch (Exception ex) { return $"refused -- {ex.Message.Trim()}"; }
    }

    // The other direction: a calculated column LibRed AUTHORS, which ACE has to accept, evaluate, and
    // recompute. Access SQL has no syntax for one, so DAO's object model was previously the only way to get
    // one into a file at all -- meaning nothing here can be cross-checked short of asking ACE to read it.
    [Theory]
    [InlineData(JetDataType.Int32, "[Qty]*2", 14, 6)]
    [InlineData(JetDataType.Int16, "[Qty]+1", 8, 4)]
    [InlineData(JetDataType.Double, "[Qty]/4", 1.75, 0.75)]
    // Each expression must give a DIFFERENT answer at Qty=7 and Qty=3, so the re-read below proves ACE
    // recomputed rather than handing back the cache LibRed wrote.
    [InlineData(JetDataType.Boolean, "[Qty]>5", true, false)]
    [InlineData(JetDataType.Text, "\"n=\" & [Qty]", "n=7", "n=3")]
    // A Memo result reaches its value through a long-value descriptor, so this one also exercises the map
    // being created for a column whose DECLARED type is Text.
    [InlineData(JetDataType.Memo, "\"n=\" & [Qty] & \" and a tail long enough to outgrow the row\"",
        "n=7 and a tail long enough to outgrow the row", "n=3 and a tail long enough to outgrow the row")]
    public void Creates_a_calculated_column_ace_accepts(
        JetDataType resultType, string expression, object atSeven, object atThree)
    {
        string path = TemporaryDatabase.CreatePath("calc-create-");
        try
        {
            DatabaseCreator.CreateEmpty(path);
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T", [
                    new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("Qty", JetDataType.Int32, 4, IsFixedLength: true),
                    ColumnSpec.Calculated("C", resultType, expression),
                ]);

                Table t = db.OpenTable("T");
                var values = new object?[t.Definition.Columns.Count];
                values[t.Definition.FindColumn("Id")!.Index] = 1;
                values[t.Definition.FindColumn("Qty")!.Index] = 7;
                t.Insert(values);
            }

            // A calculated column forces the Access 2010 floor its own FCMinReadVer declares, so the file
            // must have been raised from the 2007 version byte it was created with.
            using (var db = JetDatabase.Open(path, readOnly: true))
            {
                Assert.True(db.Format.Version >= LibRed.Formats.JetVersion.Version14_2010,
                    $"version {db.Format.Version}");
                ColumnDef c = db.OpenTable("T").Definition.FindColumn("C")!;
                Assert.True(c.IsCalculated);
                Assert.Equal(expression, c.CalculatedExpression);
                Assert.Equal(resultType, c.CalculatedResultType);
            }

            // Compared as text: ACE hands an Int16 result back as Int16 and a Boolean as Boolean, so the
            // CLR type of the InlineData literal is not the thing under test -- the value is.
            using var connection = AceTestDatabase.Open(path);
            using (var select = connection.CreateCommand())
            {
                select.CommandText = "SELECT C FROM T";
                object? read = select.ExecuteScalar();
                output.WriteLine($"{resultType,-8} {expression,-10} Qty=7 -> {read}");
                Assert.Equal(atSeven.ToString(), read?.ToString());
            }

            // And ACE must RECOMPUTE it, not merely read back the cache LibRed wrote.
            using (var update = connection.CreateCommand())
            {
                update.CommandText = "UPDATE T SET Qty = 3";
                update.ExecuteNonQuery();
            }
            using (var select = connection.CreateCommand())
            {
                select.CommandText = "SELECT C FROM T";
                object? read = select.ExecuteScalar();
                output.WriteLine($"{resultType,-8} {expression,-10} Qty=3 -> {read}");
                Assert.Equal(atThree.ToString(), read?.ToString());
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Validation is mandatory, not a courtesy: an expression ACE rejects produces a column it refuses to read
    // at all, so LibRed must never author one. These are the four refusal shapes ACE has.
    [Theory]
    [InlineData("CInt([Qty])", "cannot be used")]          // policy whitelist -- only CDbl converts
    [InlineData("Now()+0", "cannot be used")]              // volatile
    [InlineData("Left$([A], 2)", "'$'")]                   // accepted by the designer, unpopulatable
    [InlineData("[C]*2", "refers to itself")]
    [InlineData("[Missing]*2", "refers to another table")]
    public void Refuses_to_author_an_expression_ace_would_reject(string expression, string expected)
    {
        string path = TemporaryDatabase.CreatePath("calc-invalid-");
        try
        {
            DatabaseCreator.CreateEmpty(path);
            using var db = JetDatabase.Open(path, readOnly: false);
            var ex = Assert.Throws<LibRed.Storage.Calculated.CalculatedExpressionException>(
                () => db.CreateTable("T", [
                new ColumnSpec("Qty", JetDataType.Int32, 4, IsFixedLength: true),
                new ColumnSpec("A", JetDataType.Text, 40, IsFixedLength: false),
                ColumnSpec.Calculated("C", JetDataType.Int32, expression),
                ]));
            output.WriteLine($"{expression,-16} -> {ex.Message}");
            Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ACE recomputes a calculated column only when the UPDATE writes a column its expression READS, and
    // leaves the cached value alone otherwise. LibRed has to match both halves: recomputing always would
    // write bytes ACE never would, and never recomputing would leave a value ACE would have refreshed.
    [Fact]
    public void Recomputes_only_when_a_referenced_column_changes()
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calculated-update-");
        try
        {
            CreateTable(engine!, path, "C", DbLong, 0, "[Qty]*2");
            using (var connection = AceTestDatabase.Open(path))
            {
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO T_C (Id, Qty, Price, A, D1) VALUES (1, 7, 1, 'a', #2003-09-29#)";
                insert.ExecuteNonQuery();
            }

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Table t = db.OpenTable("T_C");
                ColumnDef a = t.Definition.FindColumn("A")!;
                ColumnDef qty = t.Definition.FindColumn("Qty")!;
                ColumnDef c = t.Definition.FindColumn("C")!;

                // 'A' is not referenced by [Qty]*2, so the cache must survive untouched.
                var (id, values) = t.Rows().WithIds().Single();
                values[a.Index] = "changed";
                t.Update(id, values, new HashSet<int> { a.Index });
                Assert.Equal(14, Convert.ToInt32(t.Rows().Single()[c.Index]));

                // 'Qty' is referenced, so this must recompute.
                (id, values) = t.Rows().WithIds().Single();
                values[qty.Index] = 10;
                t.Update(id, values, new HashSet<int> { qty.Index });
                Assert.Equal(20, Convert.ToInt32(t.Rows().Single()[c.Index]));
            }

            // ACE is the arbiter that the rows LibRed rewrote are still coherent.
            using var check = AceTestDatabase.Open(path);
            using var select = check.CreateCommand();
            select.CommandText = "SELECT A, C FROM T_C WHERE Id = 1";
            using var reader = select.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("changed", reader.GetValue(0));
            Assert.Equal(20, Convert.ToInt32(reader.GetValue(1)));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A zero-length payload is Null for a value type but the EMPTY STRING for a text one -- on disk the two
    // are the same bytes, since an empty string also encodes to nothing, and ACE resolves the ambiguity
    // towards "". Reading it back as null instead would be a silent disagreement about a value that is
    // never recomputed.
    [Theory]
    [InlineData("Left([A],2)", DbText, "")]          // text result over a Null input -> ""
    [InlineData("[D1]+1", DbDate, null)]             // date result over a Null input -> Null
    public void Reads_an_empty_payload_the_way_ace_does(string expression, int declaredType, string? expected)
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calculated-empty-");
        try
        {
            CreateTable(engine!, path, "C", declaredType, declaredType == DbText ? 60 : 0, expression);
            object? fromAce;
            using (var connection = AceTestDatabase.Open(path))
            {
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO T_C (Id, Qty, Price, A, D1) VALUES (1, 7, 1, NULL, NULL)";
                insert.ExecuteNonQuery();

                using var select = connection.CreateCommand();
                select.CommandText = "SELECT C FROM T_C WHERE Id = 1";
                object? read = select.ExecuteScalar();
                fromAce = read is DBNull ? null : read;
            }

            using var db = JetDatabase.Open(path, readOnly: true);
            Table t = db.OpenTable("T_C");
            object? fromLibRed = t.Rows().Single()[t.Definition.FindColumn("C")!.Index];

            output.WriteLine($"ACE {Describe(fromAce)}  LibRed {Describe(fromLibRed)}");
            Assert.Equal(expected, fromLibRed);
            Assert.Equal(fromAce, fromLibRed);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>The raw stored slot for the calculated column of rows 1 and 2 — ACE's, then LibRed's.</summary>
    private static (byte[] Ace, byte[] LibRed) StoredEnvelopes(string path, string table, string column)
    {
        using var db = JetDatabase.Open(path, readOnly: true);
        Table t = db.OpenTable(table);
        int index = t.Definition.FindColumn(column)!.Index;
        ColumnDef id = t.Definition.FindColumn("Id")!;
        var decoder = new RowDecoder(t.Definition.Columns, t.Channel.Format);

        // Deliberately NOT t.Rows(): decoding a row whose calculated column holds a cached error throws by
        // design, and the probes that need those bytes are exactly the ones that produce them. Only the raw
        // slot and the Id are wanted here, and Id is a fixed Int32 immediately after the 2-byte column count.
        var byId = new Dictionary<int, byte[]>();
        foreach (int pageNumber in t.UsageMap.DataPages())
        {
            LibRed.Pages.DataPage page = db.ReadDataPage(pageNumber);
            for (int row = 0; row < page.RowCount; row++)
            {
                if (page.Rows[row].IsDeleted) continue;
                byte[] raw = page.GetRow(row).ToArray();
                byId[BitConverter.ToInt32(raw, 2 + id.FixedOffset)] = decoder.CalculatedRaw(raw)[index];
            }
        }
        return (byId[1], byId[2]);
    }

    // DELETE does not encode a row, so it is still allowed — but it frees the deleted row's long values,
    // and a calculated Memo reaches its pages through a descriptor while being declared Text. Keying that
    // off the declared type walked past it and orphaned the pages; ACE must still read the table after.
    [Fact]
    public void Deleting_a_row_frees_a_calculated_memo_and_leaves_the_table_readable()
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calculated-delete-");
        try
        {
            string[] created = CreateFixture(engine!, path);
            Assert.Contains("CMemo", created);
            SeedAndReadWithAce(path, ["CMemo"]);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Table table = db.OpenTable("T_CMemo");
                RowId first = table.Rows().WithIds().First().Id;
                table.Delete(first);
            }

            using (var db = JetDatabase.Open(path, readOnly: true))
            {
                Table table = db.OpenTable("T_CMemo");
                int index = table.Definition.FindColumn("CMemo")!.Index;
                List<object?> remaining = [.. table.Rows().Select(r => r[index])];
                Assert.Equal(["zz-memo", "-memo"], remaining);
            }

            // ACE is the arbiter of whether the delete left the file coherent.
            using var connection = AceTestDatabase.Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT CMemo FROM T_CMemo ORDER BY Id";
            using var reader = command.ExecuteReader();
            var ace = new List<object?>();
            while (reader.Read()) ace.Add(reader.IsDBNull(0) ? null : reader.GetValue(0));
            Assert.Equal(["zz-memo", "-memo"], ace);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Builds the database and one table per calculated column, returning those DAO accepted.</summary>
    private string[] CreateFixture(object engine, string path)
    {
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", UseJet)!;
        object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;

        var created = new List<string>();
        foreach ((string name, int type, int size, string expression) in Calculated)
        {
            try
            {
                AppendCalculatedTable(database, name, type, size, expression);
                created.Add(name);
            }
            catch (TargetInvocationException ex)
            {
                // A future ACE could narrow what an expression may contain; skip rather than fail on it.
                output.WriteLine($"  {name,-6} rejected by DAO: {ex.InnerException?.Message.Trim()}");
            }
        }

        Invoke(database, "Close");
        return [.. created];
    }

    /// <summary>Each financial function in its shortest legal VBA form and again with every argument
    /// supplied. MSDN says a calculated field must supply every parameter even when VBA makes it optional,
    /// but Round, Weekday, MonthName and WeekdayName are all measured accepted without theirs — so the rule
    /// is per-function and these ten have to be asked rather than assumed.</summary>
    private static readonly (string Name, string Expression)[] FinancialArity =
    [
        ("PmtMin",    "Pmt(0.005, 60, [Qty])"),
        ("PmtFull",   "Pmt(0.005, 60, [Qty], 0, 0)"),
        ("FvMin",     "FV(0.005, 60, [Qty])"),
        ("FvFull",    "FV(0.005, 60, [Qty], 0, 0)"),
        ("PvMin",     "PV(0.005, 60, [Qty])"),
        ("PvFull",    "PV(0.005, 60, [Qty], 0, 0)"),
        ("NPerMin",   "NPer(0.005, -100, [Qty])"),
        ("NPerFull",  "NPer(0.005, -100, [Qty], 0, 0)"),
        ("RateMin",   "Rate(60, -100, [Qty])"),
        ("RateFull",  "Rate(60, -100, [Qty], 0, 0, 0.1)"),
        ("IPmtMin",   "IPmt(0.005, 1, 60, [Qty])"),
        ("IPmtFull",  "IPmt(0.005, 1, 60, [Qty], 0, 0)"),
        ("PPmtMin",   "PPmt(0.005, 1, 60, [Qty])"),
        ("PPmtFull",  "PPmt(0.005, 1, 60, [Qty], 0, 0)"),
        ("DdbMin",    "DDB([Qty], 100, 5, 1)"),
        ("DdbFull",   "DDB([Qty], 100, 5, 1, 2)"),
        ("Sln",       "SLN([Qty], 100, 5)"),          // no optional arguments at all
        ("Syd",       "SYD([Qty], 100, 5, 1)"),       // likewise
    ];

    // Which optional arguments a calculated column lets you leave out decides what LibRed's evaluator must
    // accept. Guessing it the permissive way would let LibRed author an expression ACE then refuses; guessing
    // it strict would refuse one ACE allows. Only ACE can say, so ask it.
    [Fact]
    public void Records_which_financial_functions_accept_an_omitted_optional()
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calc-arity-");
        try
        {
            object workspace = Invoke(engine!, "CreateWorkspace", "", "admin", "", UseJet)!;
            object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;

            var accepted = new List<string>();
            foreach ((string name, string expression) in FinancialArity)
            {
                try
                {
                    AppendCalculatedTable(database, name, DbDouble, 0, expression);
                    accepted.Add(name);
                    output.WriteLine($"  {name,-9} ACCEPTED  {expression}");
                }
                catch (TargetInvocationException ex)
                {
                    output.WriteLine($"  {name,-9} refused   {expression}"
                                     + $"  -- {ex.InnerException?.Message.Trim()}");
                }
            }

            Invoke(database, "Close");

            // Measured: ACE accepts all ten in both forms. So Mid and InStr are the EXCEPTIONS to MSDN's
            // "supply every parameter" rule rather than examples of it, and the evaluator is right to take
            // these optionals as optional. The Full rows double as the control -- a refusal there would mean
            // the probe was malformed rather than the arity rule being interesting.
            Assert.Equal(FinancialArity.Select(f => f.Name).ToList(), accepted);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Creates a database holding a single table with one calculated column.</summary>
    private static void CreateTable(object engine, string path, string name, int type, int size, string expression)
    {
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", UseJet)!;
        object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
        AppendCalculatedTable(database, name, type, size, expression);
        Invoke(database, "Close");
    }

    /// <summary>Appends table <c>T_&lt;name&gt;</c>: the base columns every expression reads from, plus the
    /// one calculated column under test.</summary>
    private static void AppendCalculatedTable(object database, string name, int type, int size, string expression)
    {
        object tdf = Invoke(database, "CreateTableDef", "T_" + name)!;
        object fields = GetProperty(tdf, "Fields")!;
        Invoke(fields, "Append", Invoke(tdf, "CreateField", "Id", DbLong)!);
        Invoke(fields, "Append", Invoke(tdf, "CreateField", "Qty", DbLong)!);
        Invoke(fields, "Append", Invoke(tdf, "CreateField", "Price", DbCurrency)!);
        // DAO defaults AllowZeroLength to False, which makes ACE reject '' outright — and the empty string is
        // exactly what separates "Null" from "empty" in a calculated result, so the fixture has to permit it.
        object textField = Invoke(tdf, "CreateField", "A", DbText, 20)!;
        SetProperty(textField, "AllowZeroLength", true);
        Invoke(fields, "Append", textField);
        Invoke(fields, "Append", Invoke(tdf, "CreateField", "D1", DbDate)!);

        object field = size > 0
            ? Invoke(tdf, "CreateField", name, type, size)!
            : Invoke(tdf, "CreateField", name, type)!;
        SetProperty(field, "Expression", expression);
        Invoke(fields, "Append", field);
        Invoke(GetProperty(database, "TableDefs")!, "Append", tdf);
    }

    /// <summary>Seeds the base columns through ACE — which computes and caches each result — then reads the
    /// calculated values back the same way, so the expectation is ACE's own answer.</summary>
    private static Dictionary<string, List<object?>> SeedAndReadWithAce(string path, string[] created)
    {
        var values = new Dictionary<string, List<object?>>();
        using var connection = AceTestDatabase.Open(path);
        foreach (string name in created)
        {
            foreach (string template in Seed)
            {
                using var insert = connection.CreateCommand();
                insert.CommandText = string.Format(template, "T_" + name);
                insert.ExecuteNonQuery();
            }

            using var select = connection.CreateCommand();
            select.CommandText = $"SELECT [{name}] FROM [T_{name}] ORDER BY Id";
            using var reader = select.ExecuteReader();
            var read = new List<object?>();
            while (reader.Read()) read.Add(reader.IsDBNull(0) ? null : reader.GetValue(0));
            values[name] = read;
        }
        return values;
    }

    /// <summary>Compares across two providers, which disagree about width and decimal scale but not value:
    /// ACE hands back a Currency zero as <c>0.0000</c> where LibRed says <c>0</c>.</summary>
    private static bool Matches(object? ace, object? libred)
    {
        if (ace is null) return libred is null;
        if (libred is null) return false;
        if (ace is string || libred is string) return string.Equals(ace.ToString(), libred.ToString(), StringComparison.Ordinal);
        if (ace is bool || libred is bool) return Convert.ToBoolean(ace) == Convert.ToBoolean(libred);
        if (ace is DateTime || libred is DateTime) return Convert.ToDateTime(ace) == Convert.ToDateTime(libred);
        return Convert.ToDecimal(ace) == Convert.ToDecimal(libred);
    }

    private static string Format(IEnumerable<object?> values) => string.Join(", ", values.Select(Describe));

    private static string Describe(object? value) => value switch
    {
        null => "<null>",
        string s => $"\"{s}\"",
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss"),
        _ => $"{value} ({value.GetType().Name})",
    };

    private static object? CreateDbEngine()
    {
        foreach (int n in new[] { 170, 160, 150, 140, 130, 120 })
        {
            Type? type = Type.GetTypeFromProgID($"DAO.DBEngine.{n}");
            if (type is null) continue;
            try { return Activator.CreateInstance(type); }
            catch (Exception) { /* registered but not instantiable in this bitness */ }
        }
        return null;
    }

    // What ACE writes for a conversion over Null. CDbl does NOT propagate Null the way every other function
    // here does -- the VBA conversions raise on it -- so ACE is caching an error state rather than a value,
    // and its own reader then refuses the column. Worth having the bytes: they say whether an "error" is
    // distinguishable on disk from a Null, which is the only way LibRed could reproduce it.
    [Fact]
    public void Records_what_ace_stores_for_a_conversion_over_null()
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calc-cdblnull-");
        try
        {
            object workspace = Invoke(engine!, "CreateWorkspace", "", "admin", "", UseJet)!;
            object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
            AppendCalculatedTable(database, "CvNull", DbDouble, 0, "CDbl([Qty])/3");   // raises on Null
            AppendCalculatedTable(database, "PlainNull", DbDouble, 0, "[Qty]/3");      // propagates Null
            AppendCalculatedTable(database, "CvText", DbDouble, 0, "CDbl([A])");       // raises on non-numeric
            // Does the expression service SHORT-CIRCUIT IIf? VBA's does not -- it evaluates both branches, so
            // this idiom would still raise -- but the expression service is not VBA, and if it does short
            // circuit then this is the guard to point people at.
            AppendCalculatedTable(database, "CvGuard", DbDouble, 0, "IIf(IsNull([Qty]), 0, CDbl([Qty]))");
            Invoke(database, "Close");

            // Row 1 is the good case for each, row 2 the bad one: a Null argument for the first two, and a
            // non-numeric string for the third, which is a DIFFERENT error from the same function.
            using (var connection = AceTestDatabase.Open(path))
                foreach (string table in (string[])["T_CvNull", "T_PlainNull", "T_CvText", "T_CvGuard"])
                    foreach ((int id, string qty, string a) in ((int, string, string)[])
                             [(1, "7", "'12'"), (2, "NULL", "'hello'")])
                    {
                        using var insert = connection.CreateCommand();
                        insert.CommandText = $"INSERT INTO {table} (Id, Qty, Price, A, D1) VALUES "
                            + $"({id}, {qty}, 12.5, {a}, #2003-09-29#)";
                        insert.ExecuteNonQuery();
                    }

            foreach ((string table, string column) in ((string, string)[])
                     [("T_CvNull", "CvNull"), ("T_PlainNull", "PlainNull"), ("T_CvText", "CvText")])
            {
                (byte[] good, byte[] bad) = StoredEnvelopes(path, table, column);
                output.WriteLine($"  {column,-10} ok  {Convert.ToHexString(good)}");
                output.WriteLine($"  {column,-10} bad {Convert.ToHexString(bad)}");
            }

            // The status field carries the VBA error number, and 0 is its success code. 94 is "invalid use
            // of Null" and 13 is "type mismatch" -- the two ways CDbl can fail -- while a function that
            // PROPAGATES Null leaves the field zero and simply stores no payload.
            Assert.Equal(94u, Status(path, "T_CvNull", "CvNull"));
            Assert.Equal(13u, Status(path, "T_CvText", "CvText"));
            Assert.Equal(0u, Status(path, "T_PlainNull", "PlainNull"));

            // And the expression service SHORT-CIRCUITS IIf: the CDbl arm is never reached over a Null, so
            // this stores a clean 0 rather than error 94. (Access's VBA-side IIf does not short circuit, but
            // VBA is not what computes a calculated column.) That makes the guard a real remedy, which is
            // why LibRed's evaluator takes only the selected branch too.
            Assert.Equal(0u, Status(path, "T_CvGuard", "CvGuard"));
            Assert.Equal("0", AceScalar(path, "SELECT CvGuard FROM T_CvGuard WHERE Id = 2"));

            // And LibRed must not read an error back as Null: the payload is empty in both cases, so only
            // the status tells them apart, and a wrong answer here is invisible.
            using var db = JetDatabase.Open(path, readOnly: true);
            var ex = Assert.Throws<LibRed.Storage.Calculated.CalculatedExpressionException>(
                () => db.OpenTable("T_CvNull").Rows().ToList());
            output.WriteLine($"  LibRed reads it as: {ex.Message}");
            Assert.Contains("invalid use of Null", ex.Message, StringComparison.OrdinalIgnoreCase);

            // The propagating one still reads as a plain Null, so the guard has not swallowed that case.
            Table plain = db.OpenTable("T_PlainNull");
            int index = plain.Definition.FindColumn("PlainNull")!.Index;
            Assert.Null(plain.Rows().Last()[index]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>The envelope's leading status for the second (bad-input) row of a probe table.</summary>
    private static uint Status(string path, string table, string column)
    {
        (_, byte[] bad) = StoredEnvelopes(path, table, column);
        return bad.Length >= 4 ? BitConverter.ToUInt32(bad, 0) : 0;
    }

    // Does compact-and-repair clear a cached error? It is the reason LibRed refuses to WRITE one: if the
    // only way out of the state is a compact, then writing it is close to unrecoverable for a caller who
    // cannot run Access. Worth knowing whether that assumption holds.
    [Fact]
    public void Records_whether_compacting_clears_a_cached_error()
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calc-compact-");
        string compacted = path.Replace(".accdb", "-c.accdb", StringComparison.OrdinalIgnoreCase);
        try
        {
            CreateTable(engine!, path, "CvNull", DbDouble, 0, "CDbl([Qty])/3");
            using (var connection = AceTestDatabase.Open(path))
                foreach ((int id, string qty) in ((int, string)[])[(1, "7"), (2, "NULL")])
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = $"INSERT INTO T_CvNull (Id, Qty, Price, A, D1) VALUES "
                        + $"({id}, {qty}, 12.5, 'hello', #2003-09-29#)";
                    insert.ExecuteNonQuery();
                }

            output.WriteLine($"  before: status {Status(path, "T_CvNull", "CvNull")}, "
                             + $"ACE reads {AceScalar(path, "SELECT CvNull FROM T_CvNull WHERE Id = 2")}");

            string outcome;
            try { Invoke(engine!, "CompactDatabase", path, compacted); outcome = "ACCEPTED"; }
            catch (TargetInvocationException ex)
            { outcome = $"refused -- {ex.InnerException?.Message.Trim()}"; }
            output.WriteLine($"  compact: {outcome}");

            if (outcome != "ACCEPTED") return;
            uint afterCompact = Status(compacted, "T_CvNull", "CvNull");
            output.WriteLine($"  after : status {afterCompact}, "
                             + $"ACE reads {AceScalar(compacted, "SELECT CvNull FROM T_CvNull WHERE Id = 2")}");
            output.WriteLine($"  rows  : {AceScalar(compacted, "SELECT COUNT(*) FROM T_CvNull")}");

            // Compacting does NOT clear it, and the row survives intact. What clears it is an UPDATE that
            // writes a column the expression READS, forcing a recompute -- which works even though the row
            // cannot be SELECTed, because the update never has to read the calculated column.
            output.WriteLine($"  update: {AceExec(compacted, "UPDATE T_CvNull SET Qty = 9 WHERE Id = 2")}");
            uint afterUpdate = Status(compacted, "T_CvNull", "CvNull");
            output.WriteLine($"  fixed : status {afterUpdate}, "
                             + $"ACE reads {AceScalar(compacted, "SELECT CvNull FROM T_CvNull WHERE Id = 2")}");

            // The reason the write-side refusal earns its divergence: the state is STICKY. A compact leaves
            // it, and the only way out is supplying a value for the very column that was Null.
            Assert.Equal(94u, Status(path, "T_CvNull", "CvNull"));
            Assert.Equal(94u, afterCompact);
            Assert.Equal(0u, afterUpdate);
        }
        finally { TemporaryDatabase.Delete(path); TemporaryDatabase.Delete(compacted); }
    }

    // Where ACE's constant declared lengths come from, or at least whether they ever move. 39 for a value
    // type, 509 for Text whatever size was asked for, 0 for a Memo result -- sampled before, swept here
    // across every type DAO will create, three Text sizes, and an expression long enough to rule out any
    // dependence on the expression itself.
    [Fact]
    public void Records_the_declared_length_of_every_calculated_type()
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calc-lengths-");
        try
        {
            object workspace = Invoke(engine!, "CreateWorkspace", "", "admin", "", UseJet)!;
            object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;

            const string longExpression = "[Qty]*2+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0+0";
            (string Name, int Type, int Size, string Expression)[] cases =
            [
                ("LBool", DbBoolean, 0, "[Qty]>1"), ("LByte", DbByte, 0, "[Qty]+2"),
                ("LInt", DbInteger, 0, "[Qty]+1"),  ("LLong", DbLong, 0, "[Qty]*2"),
                ("LCur", DbCurrency, 0, "[Price]*2"), ("LSng", DbSingle, 0, "[Qty]/8"),
                ("LDbl", DbDouble, 0, "[Qty]/4"),   ("LDate", DbDate, 0, "[D1]+1"),
                ("LTxt1", DbText, 1, "[A] & \"x\""), ("LTxt60", DbText, 60, "[A] & \"x\""),
                ("LTxt255", DbText, 255, "[A] & \"x\""), ("LMemo", DbMemo, 0, "[A] & \"m\""),
                ("LGuid", 15, 0, "[A] & \"g\""),    ("LBin", 9, 0, "[A] & \"b\""),
                ("LLongExpr", DbLong, 0, longExpression),
            ];
            var made = new List<string>();
            foreach ((string name, int type, int size, string expression) in cases)
            {
                try { AppendCalculatedTable(database, name, type, size, expression); made.Add(name); }
                catch (TargetInvocationException ex)
                { output.WriteLine($"  {name,-10} not created: {ex.InnerException?.Message.Trim()}"); }
            }
            Invoke(database, "Close");

            using var db = JetDatabase.Open(path, readOnly: true);
            var lengths = new List<int>();
            foreach (string name in made)
            {
                ColumnDef c = db.OpenTable("T_" + name).Definition.FindColumn(name)!;
                output.WriteLine($"  {name,-10} descriptor {c.Type,-9} length {c.Length,-4} "
                                 + $"result {c.CalculatedResultType?.ToString() ?? "-"}");
                lengths.Add(c.Length);
            }

            // Nothing moves them: not the requested Text size, not the expression's length, not the type
            // beyond the four-way split. Binary's 510 is the one the earlier sampling missed, because
            // nothing had ever created a calculated Binary column.
            Assert.All(lengths, l => Assert.Contains(l, (int[])[0, 39, 509, 510]));
            Assert.Equal(509, db.OpenTable("T_LTxt255").Definition.FindColumn("LTxt255")!.Length);
            Assert.Equal(510, db.OpenTable("T_LBin").Definition.FindColumn("LBin")!.Length);
            Assert.Equal(39, db.OpenTable("T_LGuid").Definition.FindColumn("LGuid")!.Length);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ---- value-matrix parity ------------------------------------------------------------------------
    //
    // Byte-parity elsewhere covers one row of ordinary inputs, and the whitelist is enumerated function by
    // function -- but every function is otherwise trusted on its VBA definition rather than measured against
    // ACE per value. This pushes a matrix of the values that actually break things through BOTH engines and
    // requires them to agree: nulls, the empty string, zero, negatives, a tiny Currency, a non-ASCII
    // character, and the pre-epoch dates whose time fraction runs backwards.
    //
    // ACE writes rows 1-5 and LibRed writes 101-105 from identical inputs; ACE then reads all ten, so the
    // comparison is between ACE's own answer and LibRed's, seen through one reader.
    private static readonly (int Id, object? Qty, object? Price, string? A, DateTime? D1)[] Matrix =
    [
        (1, 7, 12.5m, "hello", new DateTime(2003, 9, 29)),
        (2, 0, 0m, "", new DateTime(1899, 12, 30)),                       // zeros, empty string, the epoch
        (3, -5, -3.25m, "  pad  ", new DateTime(1899, 12, 29, 18, 0, 0)), // negatives, padding, pre-epoch PM
        (4, null, null, null, null),                                       // every input Null
        (5, 3, 0.0001m, "ß", new DateTime(1899, 12, 29, 6, 0, 0)),        // tiny Currency, non-ASCII, pre-epoch AM
    ];

    private static readonly (string Name, int Type, int Size, string Expression)[] MatrixColumns =
    [
        ("PQtyX2",  DbLong,     0, "[Qty]*2"),
        ("PQtyDiv", DbDouble,   0, "[Qty]/4"),
        ("PRound",  DbCurrency, 0, "Round([Price]*3, 2)"),    // the OA double->decimal rounding scar
        ("PAmp",    DbText,    40, "[A] & \"-x\""),           // '&' swallows Null
        ("PPlus",   DbText,    40, "[A] + \"-x\""),           // '+' propagates it
        ("PTrim",   DbText,    40, "Trim([A])"),
        ("PLen",    DbLong,     0, "Len([A])"),               // 0 for empty, Null for Null
        ("PDate",   DbDate,     0, "[D1]+1"),
        ("PYear",   DbLong,     0, "Year([D1])"),
        ("PIif",    DbText,    40, "IIf([Qty]>0, \"pos\", \"non\")"),
        ("PSgn",    DbInteger,  0, "Sgn([Qty])"),
        ("PAbs",    DbCurrency, 0, "Abs([Price])"),
        ("PCDbl",   DbDouble,   0, "CDbl([Qty])/3"),
        ("PStr",    DbText,    40, "Str([Qty])"),
        ("PLeft",   DbText,    40, "Left([A],2)"),
    ];

    [Fact]
    public void Computes_the_same_values_as_ace_across_the_matrix()
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calc-matrix-");
        try
        {
            object workspace = Invoke(engine!, "CreateWorkspace", "", "admin", "", UseJet)!;
            object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
            var created = new List<(string Name, string Expression)>();
            foreach ((string name, int type, int size, string expression) in MatrixColumns)
            {
                try { AppendCalculatedTable(database, name, type, size, expression); created.Add((name, expression)); }
                catch (TargetInvocationException ex)
                {
                    output.WriteLine($"  {name,-8} not authored: {ex.InnerException?.Message.Trim()}");
                }
            }
            Invoke(database, "Close");
            Assert.NotEmpty(created);

            var mismatches = new List<string>();
            var aceCannotReadItself = new List<string>();
            foreach ((string name, string expression) in created)
            {
                string table = "T_" + name;
                using (var connection = AceTestDatabase.Open(path))
                    foreach (var row in Matrix)
                    {
                        using var insert = connection.CreateCommand();
                        insert.CommandText = $"INSERT INTO {table} (Id, Qty, Price, A, D1) VALUES "
                            + $"({row.Id}, {Literal(row.Qty)}, {Literal(row.Price)}, {Literal(row.A)}, {Literal(row.D1)})";
                        insert.ExecuteNonQuery();
                    }

                var refused = new HashSet<int>();
                using (var db = JetDatabase.Open(path, readOnly: false))
                {
                    Table t = db.OpenTable(table);
                    foreach (var row in Matrix)
                    {
                        var values = new object?[t.Definition.Columns.Count];
                        values[t.Definition.FindColumn("Id")!.Index] = row.Id + 100;
                        values[t.Definition.FindColumn("Qty")!.Index] = row.Qty;
                        values[t.Definition.FindColumn("Price")!.Index] = row.Price;
                        values[t.Definition.FindColumn("A")!.Index] = row.A;
                        values[t.Definition.FindColumn("D1")!.Index] = row.D1;
                        // LibRed refuses a row whose calculated value would be a cached ERROR rather than a
                        // value -- see the CDbl-over-Null note below. ACE accepts such a row and writes the
                        // unreadable state, so a refusal here is the divergence, not a failure.
                        try { t.Insert(values); }
                        catch (LibRed.Storage.Calculated.CalculatedExpressionException) { refused.Add(row.Id); }
                    }
                }

                // Read row by row and record a failure as a value: a row ACE cannot read is itself a result,
                // and which SIDE it lands on is the whole point -- ACE's own row failing means the expression
                // is at fault, only LibRed's failing means the bytes are.
                Dictionary<int, string> byId = [];
                using (var connection = AceTestDatabase.Open(path))
                    foreach (int id in Matrix.SelectMany(r => (int[])[r.Id, r.Id + 100]))
                    {
                        using var select = connection.CreateCommand();
                        select.CommandText = $"SELECT [{name}] FROM [{table}] WHERE Id = {id}";
                        try
                        {
                            using var reader = select.ExecuteReader();
                            byId[id] = reader.Read()
                                ? reader.IsDBNull(0) ? "(null)" : Describe(reader.GetValue(0))
                                : "(missing)";
                        }
                        catch (Exception ex) { byId[id] = $"UNREADABLE: {ex.Message.Split('.')[0]}"; }
                    }

                foreach (var row in Matrix)
                {
                    string ace = byId.GetValueOrDefault(row.Id, "(missing)");
                    string libred = byId.GetValueOrDefault(row.Id + 100, "(missing)");
                    output.WriteLine($"  {name,-8} row {row.Id}  ACE {ace,-28} LibRed {libred}");

                    if (ace.StartsWith("UNREADABLE", StringComparison.Ordinal))
                    {
                        aceCannotReadItself.Add($"{name} row {row.Id}");
                        // LibRed must not merely disagree here — it must have declined to write the row at
                        // all, which is the whole point of the divergence.
                        Assert.Contains(row.Id, refused);
                        continue;
                    }
                    Assert.DoesNotContain(row.Id, refused);   // nothing else may be refused
                    if (ace != libred) mismatches.Add($"{name} [{expression}] row {row.Id}: ACE {ace}, LibRed {libred}");
                }
            }

            Assert.Empty(mismatches);

            // A deliberate LibRed divergence, not an ACE defect. The VBA conversions do NOT propagate Null,
            // they raise on it, so CDbl(Null) is an error rather than a Null result -- and CDbl is the only
            // conversion a calculated column may contain. ACE accepts the row and caches the error, after
            // which its own reader refuses that row and only a compact-and-repair clears it. LibRed refuses
            // the INSERT instead, so the unreadable state never reaches the file. Pinned exactly, so a NEW
            // unreadable case shows up rather than being absorbed into this one.
            Assert.Equal(["PCDbl row 4"], aceCannotReadItself);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>An Access SQL literal for a matrix value, so ACE is seeded with exactly what LibRed is.</summary>
    private static string Literal(object? value) => value switch
    {
        null => "NULL",
        string s => $"'{s.Replace("'", "''", StringComparison.Ordinal)}'",
        DateTime d => $"#{d:yyyy-MM-dd HH:mm:ss}#",
        decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!,
    };

    // Why InStr and CDec fail as a SYNTAX error rather than the policy refusal every other rejected function
    // gets. The standing guess was that the calculated-column parser does not know those names at all -- but
    // InStr in its four-argument form IS accepted, so it must know that one. The discriminator is whether a
    // KNOWN function called with the wrong arity produces the same message as an unknown name: if it does,
    // "syntax error" says nothing about the name, and our grammar should not treat the two differently.
    [Fact]
    public void Records_how_ace_distinguishes_a_syntax_error_from_a_policy_refusal()
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calc-syntax-");
        try
        {
            object workspace = Invoke(engine!, "CreateWorkspace", "", "admin", "", UseJet)!;
            object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;

            var accepted = new List<string>();
            Dictionary<string, string> outcomes = [];
            foreach ((string name, string expression) in ((string, string)[])
            [
                ("i2",      "InStr([A],\"a\")"),          // known short: reported as a syntax error
                ("i3",      "InStr(1,[A],\"a\")"),
                ("i3alt",   "InStr([A],\"a\",1)"),
                ("i4",      "InStr(1,[A],\"a\",0)"),      // known accepted
                // With 'H' against "hello" the compare mode is visible: binary gives 0, text gives 1.
                ("i3case",  "InStr(1,[A],\"H\")"),
                ("i4case0", "InStr(1,[A],\"H\",0)"),
                ("i4case1", "InStr(1,[A],\"H\",1)"),
                ("cdec",    "CDec([Qty])"),
                ("unknown", "NotAFunction([Qty])"),        // a name nothing could know
                ("policy",  "CInt([Qty])"),                // known, refused by policy
                ("toomany", "Abs([Qty],2)"),               // whitelisted name, wrong arity
                ("toofew",  "Abs()"),
                ("control", "Abs([Qty])"),                 // whitelisted name, right arity
                // Does the EXPRESSION service accept SQL's backtick identifier quoting? It is not the SQL
                // parser, so it may only know [brackets] -- and that decides whether LibRed can store a
                // backtick expression verbatim or must rewrite it.
                ("backtick", "`Qty`*2"),
            ])
            {
                string outcome;
                try { AppendCalculatedTable(database, name, DbLong, 0, expression); outcome = "ACCEPTED"; }
                catch (TargetInvocationException ex)
                { outcome = ex.InnerException?.Message.Trim() ?? ex.Message.Trim(); }
                output.WriteLine($"  {name,-8} {expression,-22} -> {outcome}");
                if (outcome == "ACCEPTED") accepted.Add(name);
                outcomes[name] = outcome;
            }

            Invoke(database, "Close");

            // The discriminator. An UNKNOWN name gets the policy refusal, while a WHITELISTED name called
            // with the wrong number of arguments gets "Syntax error in expression" -- so a syntax error says
            // nothing about whether ACE knows the function, which is the reverse of the obvious reading.
            Assert.Contains("cannot be used", outcomes["unknown"], StringComparison.Ordinal);
            Assert.Contains("cannot be used", outcomes["policy"], StringComparison.Ordinal);
            Assert.Contains("Syntax error", outcomes["toomany"], StringComparison.Ordinal);
            Assert.Contains("Syntax error", outcomes["toofew"], StringComparison.Ordinal);
            Assert.Contains("Syntax error", outcomes["i2"], StringComparison.Ordinal);   // InStr with two
            Assert.Contains("Syntax error", outcomes["cdec"], StringComparison.Ordinal);
            Assert.Equal("ACCEPTED", outcomes["i3"]);                                     // ...but three is fine
            Assert.Equal("ACCEPTED", outcomes["i4"]);

            // Design-time acceptance has repeatedly not meant usable here -- the '$' variants and an index on
            // a calculated column are both accepted and then reject every insert -- so ask what each accepted
            // form actually DOES with a row before treating it as a shape LibRed must support.
            Dictionary<string, string> values = [];
            foreach (string name in accepted)
            {
                string insert = AceExec(path,
                    $"INSERT INTO T_{name} (Id, Qty, Price, A, D1) VALUES (1, 7, 12.5, 'hello', #2003-09-29#)");
                values[name] = insert == "ACCEPTED" ? AceScalar(path, $"SELECT [{name}] FROM T_{name}") : "-";
                output.WriteLine($"    {name,-8} insert: {insert,-12} value: {values[name]}");
            }

            // Three-argument InStr is genuinely usable, not merely accepted, and its omitted compare defaults
            // to case-INSENSITIVE -- 'H' finds "hello" at 1, while an explicit 0 does not find it at all.
            Assert.Equal("0", values["i3"]);
            Assert.Equal("1", values["i3case"]);
            Assert.Equal("0", values["i4case0"]);
            Assert.Equal("1", values["i4case1"]);

            // The other three-argument shape is the familiar trap: accepted at design time, and then its
            // cached value cannot be read, because 'start' got a non-numeric. Same family as the '$' variants
            // and an index on a calculated column.
            Assert.StartsWith("error", values["i3alt"], StringComparison.Ordinal);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Is backtick quoting rejected by the EXPRESSION SERVICE generally, or only in a calculated column? ACE's
    // SQL parser accepts backticks everywhere -- EF Core's Jet provider quotes every identifier that way --
    // so if a CHECK constraint (also stored as expression text in LvProp, also evaluated by the service)
    // rejects them too, the difference is the service's lexer rather than anything calculated-specific.
    [Fact]
    public void Records_whether_the_expression_service_knows_backtick_quoting()
    {
        string path = TemporaryDatabase.CreatePath("calc-tick-");
        try
        {
            DatabaseCreator.CreateEmpty(path);
            using (var connection = AceTestDatabase.Open(path))
            {
                Execute(connection, "CREATE TABLE T (Id LONG, Qty LONG)");
                Execute(connection, "INSERT INTO T (Id, Qty) VALUES (1, 5)");

                // Query context: the SQL parser, which does know backticks.
                using var select = connection.CreateCommand();
                select.CommandText = "SELECT `Qty` FROM `T` WHERE `Qty` > 1";
                output.WriteLine($"  query  : {select.ExecuteScalar()}");
            }

            // Expression-service context: a CHECK constraint over the same identifier.
            output.WriteLine($"  check `` : {AceExec(path, "ALTER TABLE T ADD CONSTRAINT ck CHECK (`Qty` > 0)")}");
            output.WriteLine($"  check [] : {AceExec(path, "ALTER TABLE T ADD CONSTRAINT ck2 CHECK ([Qty] > 0)")}");
            output.WriteLine($"  insert   : {AceExec(path, "INSERT INTO T (Id, Qty) VALUES (2, 7)")}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void Execute(System.Data.Common.DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // ---- ALTER probes -------------------------------------------------------------------------------
    //
    // Three things decide what LibRed's ALTER paths have to do, and none is guessable:
    //   1. renaming a column a calculated expression READS -- does ACE rewrite the stored expression text,
    //      or does it leave a dangling reference?
    //   2. changing the expression on a table that already has rows -- are the cached values recomputed at
    //      once, or left stale until something touches each row?
    //   3. can the result type be changed at all, and can an ordinary column become calculated?
    [Fact]
    public void Records_what_ace_does_when_a_calculated_column_is_altered()
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calc-alter-");
        try
        {
            CreateTable(engine!, path, "CLong", DbLong, 0, "[Qty]*2");
            using (var connection = AceTestDatabase.Open(path))
            {
                using var insert = connection.CreateCommand();
                insert.CommandText =
                    "INSERT INTO T_CLong (Id, Qty, Price, A, D1) VALUES (1, 7, 12.5, 'hello', #2003-09-29#)";
                insert.ExecuteNonQuery();
            }

            object workspace = Invoke(engine!, "CreateWorkspace", "", "admin", "", UseJet)!;

            // Control first: DAO refuses to mutate a field of a SAVED TableDef whether or not anything is
            // calculated, so a refusal there says nothing about calculated columns.
            output.WriteLine($"  [control] DAO rename an ordinary column: {DaoField(workspace, path,
                "T_CLong", "A", f => SetProperty(f, "Name", "Renamed"))}");
            output.WriteLine($"  [control] DAO set Expression           : {DaoField(workspace, path,
                "T_CLong", "CLong", f => SetProperty(f, "Expression", "[Qty]*3"))}");

            // What LibRed actually implements is reachable through ACE SQL, so ask about those instead. Each
            // gets its own table, so one refusal cannot decide the next.
            // StillReads: what ACE returns for the calculated column afterwards, or null where the operation
            // leaves it unreadable. ACE does NOT protect the reference -- dropping a column an expression
            // reads is accepted and simply breaks the calculated column, which is worth knowing before
            // deciding whether LibRed should refuse where ACE does not.
            (string Name, string Sql, string? StillReads)[] operations =
            [
                ("AltAdd",     "ALTER TABLE T_AltAdd ADD COLUMN Extra LONG",       "14"),
                ("AltRetype",  "ALTER TABLE T_AltRetype ALTER COLUMN Qty DOUBLE",  "14"),  // a column it READS
                ("AltDropRef", "ALTER TABLE T_AltDropRef DROP COLUMN Qty",         null),  // ditto, removed
                ("AltDropCal", "ALTER TABLE T_AltDropCal DROP COLUMN AltDropCal",  null),  // the calculated one
                ("AltWiden",   "ALTER TABLE T_AltWiden ALTER COLUMN A TEXT(60)",   "14"),  // unrelated column
            ];

            object database = Invoke(workspace, "OpenDatabase", path)!;
            foreach ((string name, _, _) in operations)
                AppendCalculatedTable(database, name, DbLong, 0, "[Qty]*2");
            Invoke(database, "Close");

            foreach ((string name, string sql, string? stillReads) in operations)
            {
                using (var connection = AceTestDatabase.Open(path))
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText =
                        $"INSERT INTO T_{name} (Id, Qty, Price, A, D1) VALUES (1, 7, 12.5, 'x', #2003-09-29#)";
                    insert.ExecuteNonQuery();
                }

                string outcome = AceExec(path, sql);
                string read = AceScalar(path, $"SELECT [{name}] FROM T_{name}");
                output.WriteLine($"  {name,-11}: {outcome}");
                output.WriteLine($"    expression : {LibRedExpression(path, "T_" + name, name)}");
                output.WriteLine($"    ACE reads  : {read}");

                // Every one of these is ACCEPTED -- including the two that break the calculated column.
                Assert.Equal("ACCEPTED", outcome);
                if (stillReads is not null) Assert.Equal(stillReads, read);
                else Assert.StartsWith("error", read, StringComparison.Ordinal);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // LibRed's OWN alter paths over a table that has a calculated column. ADD and DROP of an unrelated column
    // are covered above; these rebuild around it, rename what it reads, or remove it.
    //
    // The rename case is a fixed bug, and a nasty shape: renaming Qty left the expression saying [Qty]*2, so
    // an operation naming a DIFFERENT column silently made the calculated one unreadable to ACE. Nothing but
    // asking ACE would have shown it -- LibRed read the table back quite happily.
    [Fact]
    public void Libred_alter_keeps_a_calculated_column_working()
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calc-libalter-");
        try
        {
            object workspace = Invoke(engine!, "CreateWorkspace", "", "admin", "", UseJet)!;
            object created = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
            foreach (string name in (string[])["LibRetype", "LibRename", "LibDropCalc"])
                AppendCalculatedTable(created, name, DbLong, 0, "[Qty]*2");
            Invoke(created, "Close");

            foreach (string name in (string[])["LibRetype", "LibRename", "LibDropCalc"])
                using (var connection = AceTestDatabase.Open(path))
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText =
                        $"INSERT INTO T_{name} (Id, Qty, Price, A, D1) VALUES (1, 7, 12.5, 'x', #2003-09-29#)";
                    insert.ExecuteNonQuery();
                }

            // Retyping a column the expression reads leaves the expression alone and ACE still evaluates it,
            // which is what ACE's own ALTER COLUMN does.
            Apply("LibRetype", db => db.AlterColumn(
                "T_LibRetype", "Price", new ColumnSpec("Price", JetDataType.Double, 8, IsFixedLength: true)));
            Assert.Equal("[Qty]*2 / Int32", LibRedExpression(path, "T_LibRetype", "LibRetype"));
            Assert.Equal("14", AceScalar(path, "SELECT [LibRetype] FROM T_LibRetype"));

            // Renaming one REPOINTS the expression, so the column keeps working.
            Apply("LibRename", db => db.RenameColumn("T_LibRename", "Qty", "Amount"));
            Assert.Equal("[Amount]*2 / Int32", LibRedExpression(path, "T_LibRename", "LibRename"));
            Assert.Equal("14", AceScalar(path, "SELECT [LibRename] FROM T_LibRename"));

            // And the calculated column itself can be dropped, leaving a table ACE still opens.
            Apply("LibDropCalc", db => db.DropColumn("T_LibDropCalc", "LibDropCalc"));
            Assert.Equal("(column gone)", LibRedExpression(path, "T_LibDropCalc", "LibDropCalc"));
            Assert.Equal("7", AceScalar(path, "SELECT Qty FROM T_LibDropCalc"));

            // Dropping a column an expression READS is refused -- deliberately unlike ACE, which accepts it
            // and leaves a calculated column nothing can evaluate or repair. Dropping the calculated column
            // first is the way through, and then the referenced column goes freely.
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var refused = Assert.Throws<InvalidOperationException>(
                    () => db.DropColumn("T_LibRetype", "Qty"));
                output.WriteLine($"  drop a referenced column: {refused.Message}");
                Assert.Contains("calculated column that reads", refused.Message, StringComparison.Ordinal);

                db.DropColumn("T_LibRetype", "LibRetype");
                Assert.True(db.DropColumn("T_LibRetype", "Qty"));
            }
            Assert.Equal("1", AceScalar(path, "SELECT Id FROM T_LibRetype"));

            void Apply(string label, Action<JetDatabase> operation)
            {
                using var db = JetDatabase.Open(path, readOnly: false);
                operation(db);
                output.WriteLine($"  {label} applied");
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Opens the database with DAO, applies <paramref name="mutate"/> to one field of a saved
    /// TableDef, and reports what happened.</summary>
    private static string DaoField(object workspace, string path, string table, string column, Action<object> mutate)
    {
        object? database = null;
        try
        {
            database = Invoke(workspace, "OpenDatabase", path)!;
            object tdf = Invoke(GetProperty(database, "TableDefs")!, "Item", table)!;
            object field = Invoke(GetProperty(tdf, "Fields")!, "Item", column)!;
            mutate(field);
            return "ACCEPTED";
        }
        catch (Exception ex)
        {
            return $"refused -- {(ex as TargetInvocationException)?.InnerException?.Message.Trim() ?? ex.Message.Trim()}";
        }
        finally { if (database is not null) try { Invoke(database, "Close"); } catch { /* already closed */ } }
    }

    private static string AceExec(string path, string sql)
    {
        try
        {
            using var connection = AceTestDatabase.Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
            return "ACCEPTED";
        }
        catch (Exception ex) { return $"refused -- {ex.Message.Trim()}"; }
    }

    private static string AceScalar(string path, string sql)
    {
        try
        {
            using var connection = AceTestDatabase.Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToString(command.ExecuteScalar()) ?? "(null)";
        }
        catch (Exception ex) { return $"error -- {ex.Message.Trim()}"; }
    }

    private static string LibRedExpression(string path, string table, string column)
    {
        try
        {
            using var db = JetDatabase.Open(path, readOnly: true);
            ColumnDef? c = db.OpenTable(table).Definition.FindColumn(column);
            if (c is null) return "(column gone)";
            return $"{c.CalculatedExpression ?? "(none)"} / {c.CalculatedResultType?.ToString() ?? "(none)"}";
        }
        catch (Exception ex) { return $"error -- {ex.Message.Trim()}"; }
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);

    private static object? GetProperty(object target, string member) =>
        target.GetType().InvokeMember(member, BindingFlags.GetProperty, null, target, null);

    private static void SetProperty(object target, string member, object? value) =>
        target.GetType().InvokeMember(member, BindingFlags.SetProperty, null, target, [value]);
}
