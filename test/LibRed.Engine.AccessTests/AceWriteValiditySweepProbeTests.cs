using System.Data;
using System.Data.OleDb;
using System.Text;
using LibRed;
using LibRed.Catalog;
using LibRed.Data;
using LibRed.Formats;
using Xunit;

namespace LibRed.Engine.Tests;

// The OUTBOUND direction of CorruptFileSweepProbeTests: that one feeds damaged bytes INTO LibRed's reader; this
// runs LibRed's WRITER over randomised workloads and asks whether ACE refuses the result. The oracle is
// AceValidityLadder.
//
// A sweep rather than more hand-written parity tests because the ~130 ACE cross-checks each pin a shape someone
// was already looking at. What they miss is the INTERACTION — a column dropped after an index was built over it,
// a long value relocated into space a deleted row freed, a format raised mid-rebuild.
//
// DAO Compact & Repair is deliberately not a rung: a stricter bar than "ACE opens and round-trips", which the
// repo has not claimed (README lists it as open), so it would bury real finds under known noise.
//
// Committed small and fixed-seed — a regression guard, not a search. Discovery means:
//
//     $env:LIBRED_ACE_SWEEP = 400 ; $env:LIBRED_ACE_SWEEP_SEED = 7
//
[Collection(AceCollection.Name)]
public class AceWriteValiditySweepProbeTests(ITestOutputHelper output) : TempDatabaseTest
{
    /// <summary>Workloads run when nothing overrides it. Small on purpose — see the note above.</summary>
    private const int CommittedWorkloads = 8;

    [Fact]
    public void No_generated_workload_produces_a_file_ace_calls_invalid()
    {
        int workloads = ReadInt("LIBRED_ACE_SWEEP", CommittedWorkloads);
        int seed = ReadInt("LIBRED_ACE_SWEEP_SEED", 20260912);

        // Asked once: a BIGINT workload on an ACE 12 engine would fail for a reason unrelated to the file.
        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        bool bigInt = AceTestDatabase.SupportsColumnType(northwind, "BIGINT");
        output.WriteLine($"seed {seed}, {workloads} workloads; ACE supports BIGINT={bigInt}");

        // FINDING = a file ACE will not accept, which is what this exists for. LEAK = a statement that threw
        // outside the refusal contract; a real defect but a caller-facing one, so the workload carries on and
        // the ladder still answers whether it left a readable file.
        var findings = new List<string>();
        var leaks = new List<Leak>();
        for (int i = 0; i < workloads; i++)
        {
            int workloadSeed = seed + i;
            Plan plan = Plan.Generate(new Random(workloadSeed), bigInt);

            Outcome outcome = RunAndValidate(plan, plan.Steps.Count);
            leaks.AddRange(outcome.Leaks);

            if (outcome.AceVerdict is not { } failure) continue;

            // Replay prefix by prefix to name the culprit. The plan comes purely from the seed and never
            // branches on whether a statement succeeded, so a replay is the same sequence.
            string located = Locate(plan);
            findings.Add($"seed {workloadSeed} ({plan.Describe()}): {failure}\n{located}");
            output.WriteLine(findings[^1]);
        }

        string[] grouped = leaks
            .GroupBy(l => l.Signature)
            .Select(g => $"  x{g.Count(),-5} {g.Key}\n           e.g. {g.First().Example}")
            .ToArray();
        foreach (string leak in grouped) output.WriteLine(leak);

        Assert.True(findings.Count == 0 && leaks.Count == 0,
            $"{findings.Count} of {workloads} workloads produced a file ACE would not accept:\n\n"
            + string.Join("\n\n", findings)
            + $"\n\n{leaks.Count} statement(s) in {grouped.Length} shape(s) failed outside the refusal "
            + $"contract:\n\n" + string.Join("\n", grouped));
    }

    /// <summary>A workload's two verdicts — see the note in the test body.</summary>
    private sealed record Outcome(string? AceVerdict, List<Leak> Leaks);

    /// <summary>A statement that failed outside the refusal contract. Grouped by signature when reported: one
    /// root cause routinely accounts for scores of statements.</summary>
    private sealed record Leak(string Signature, string Example);

    /// <summary>The floor the sweep stands on: an EMPTY database at each format, opened by ACE. If this fails
    /// for a version, every workload at that version fails too and says nothing about its statements — which is
    /// how the 0x04 defect first surfaced.</summary>
    [Theory]
    [InlineData(JetVersion.Version4, ".mdb")]
    [InlineData(JetVersion.Version12_2007, ".accdb")]
    [InlineData(JetVersion.Version14_2010, ".accdb")]
    [InlineData(JetVersion.Version16_2016, ".accdb")]
    [InlineData(JetVersion.Version17_2019, ".accdb")]
    public void Ace_opens_an_empty_database_at_every_format_libred_creates(JetVersion version, string extension)
    {
        string path = TemporaryDatabase.CreatePath("libred-empty-", extension);
        File.Delete(path);
        try
        {
            LibRedConnection.CreateDatabase($"Data Source={path}", collation: null, version: version);

            using var stream = File.OpenRead(path);
            stream.Seek(0x14, SeekOrigin.Begin);
            output.WriteLine($"{version}: version byte 0x{stream.ReadByte():X2}");
            stream.Dispose();

            using OleDbConnection connection = AceTestDatabase.Open(path, attempts: 3);
            using DataTable? tables = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, null);
            Assert.NotNull(tables);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Creating an ACE 15 database is refused outright — see the evidence in
    /// <see cref="Ace_refuses_the_0x04_version_byte_and_nothing_else_about_the_file"/>.</summary>
    [Fact]
    public void Libred_refuses_to_create_an_ace_15_database()
    {
        string path = TemporaryDatabase.CreatePath("libred-ace15-create-", ".accdb");
        File.Delete(path);
        try
        {
            NotSupportedException refused = Assert.Throws<NotSupportedException>(() =>
                LibRedConnection.CreateDatabase($"Data Source={path}", collation: null,
                    version: JetVersion.Version15_2013));

            output.WriteLine(refused.Message);
            Assert.Contains("Version14_2010", refused.Message);   // the caller is told what to ask for instead
            Assert.False(File.Exists(path), "a refused creation must not leave a file behind");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>The measurement the creation guard rests on: ACE does not merely avoid the 0x04 version byte,
    /// it REFUSES a file carrying one, and restamping 0x14 to 0x03 makes the identical bytes open. The spec
    /// previously inferred "reserved" from absence; this is the stronger fact.</summary>
    /// <remarks>Built at 2010 and stamped by hand, since DatabaseCreator now refuses to write 0x04 — the
    /// evidence must not depend on the guard being absent.</remarks>
    [Fact]
    public void Ace_refuses_the_0x04_version_byte_and_nothing_else_about_the_file()
    {
        string path = TemporaryDatabase.CreatePath("libred-ace15-", ".accdb");
        File.Delete(path);
        try
        {
            LibRedConnection.CreateDatabase($"Data Source={path}", collation: null,
                version: JetVersion.Version14_2010);

            // Sanity: as created, at 0x03, ACE opens it. Without this the refusal below would prove nothing —
            // a file ACE rejects for some unrelated reason would look identical.
            using (OleDbConnection asBuilt = AceTestDatabase.Open(path, attempts: 3))
                Assert.NotNull(asBuilt.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, null));

            Stamp(path, 0x04);
            Assert.Throws<InvalidOperationException>(() => AceTestDatabase.Open(path, attempts: 2).Dispose());

            Stamp(path, 0x03);
            using OleDbConnection restored = AceTestDatabase.Open(path, attempts: 3);
            Assert.NotNull(restored.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, null));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void Stamp(string path, byte version)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        stream.Seek(0x14, SeekOrigin.Begin);
        stream.WriteByte(version);
    }

    /// <summary>
    /// Builds the database, applies the first <paramref name="steps"/> statements, then walks the ACE ladder.
    /// </summary>
    private Outcome RunAndValidate(Plan plan, int steps)
    {
        var leaks = new List<Leak>();
        string path = TemporaryDatabase.CreatePath("libred-ace-sweep-", plan.Extension);
        File.Delete(path);   // CreateDatabase synthesises the file and refuses an existing one
        try
        {
            LibRedConnection.CreateDatabase($"Data Source={path}", plan.Collation, plan.Version);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(db);
                for (int i = 0; i < steps; i++)
                {
                    Step step = plan.Steps[i];
                    try
                    {
                        step.Run(db, engine);
                    }
                    catch (Exception ex) when (IsRefusal(ex))
                    {
                        // Allowed: a duplicate key, a column an earlier step dropped, an unimplemented feature.
                        // What matters is that the refusal leaves a file ACE accepts — the ladder's job.
                        _ = ex;
                    }
                    catch (Exception ex)
                    {
                        // Outside the contract. Recorded, and the workload CARRIES ON — statements are atomic,
                        // so the question is whether this left a readable file, and stopping would never ask it.
                        leaks.Add(new Leak($"{ex.GetType().Name}: {Flatten(ex.Message)}", step.Label));
                    }
                }
            }

            AceValidityLadder.Verdict verdict = AceValidityLadder.Check(path, Plan.AceWriteTarget);
            leaks.AddRange(verdict.TypedOnly.Select(t => new Leak(t, plan.Describe())));
            return new Outcome(verdict.Finding, leaks);
        }
        catch (Exception ex)
        {
            return new Outcome($"rung 0 (LibRed itself): {ex.GetType().Name}: {Flatten(ex.Message)}", leaks);
        }
        finally { TemporaryDatabase.Delete(path); }
    }


    /// <summary>Replays the plan prefix by prefix to name the first statement ACE will not survive.</summary>
    private string Locate(Plan plan)
    {
        for (int steps = 1; steps <= plan.Steps.Count; steps++)
        {
            if (RunAndValidate(plan, steps).AceVerdict is not { } failure) continue;

            var report = new StringBuilder($"  first bad prefix is {steps} step(s):\n");
            for (int i = 0; i < steps; i++)
                report.Append($"    {(i == steps - 1 ? "->" : "  ")} {plan.Steps[i].Label}\n");
            return report.Append($"    {failure}").ToString();
        }

        // The whole plan failed but no prefix of it does. That is a real result, not a harness bug: it means the
        // damage needs the complete sequence, so the log below IS the reproduction.
        return "  no shorter prefix reproduces it; the full plan is:\n"
            + string.Join("\n", plan.Steps.Select(s => $"       {s.Label}"));
    }

    private static void Execute(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>The exception types a generated statement may legitimately raise. Everything else — a null
    /// reference, an index out of range, an InvalidDataException from LibRed's reader over its own writes — is
    /// a defect and escapes to be reported.</summary>
    private static bool IsRefusal(Exception ex) =>
        ex is NotSupportedException            // a feature LibRed does not implement yet
           or InvalidOperationException        // a constraint, a missing object, a bad declared size
           or ArgumentException
           or FormatException
           or OverflowException;

    private static string Flatten(string message) =>
        string.Join(' ', message.Split('\n', '\r').Select(l => l.Trim()).Where(l => l.Length > 0));

    private static int ReadInt(string variable, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(variable), out int value) && value > 0 ? value : fallback;

    // ---------------------------------------------------------------------------------------------------
    // The generator
    // ---------------------------------------------------------------------------------------------------

    private sealed record Step(string Label, Action<JetDatabase, QueryEngine> Run);

    /// <summary>A workload, fixed at generation time. It never branches on execution, so a seed always yields
    /// the same sequence — which is what lets <see cref="Locate"/> bisect a failure.</summary>
    private sealed class Plan
    {
        /// <summary>The unconstrained table rung 4 writes into. Every plan creates it first.</summary>
        public const string AceWriteTarget = "AceTarget";

        public required JetVersion Version { get; init; }
        public required Collation Collation { get; init; }
        public required List<Step> Steps { get; init; }

        /// <summary>Jet 4 is an <c>.mdb</c>; everything from ACE 12 on is an <c>.accdb</c>.</summary>
        public string Extension => Version <= JetVersion.Version4 ? ".mdb" : ".accdb";

        public string Describe() =>
            $"{Version}, collation v{Collation.Version}, {Steps.Count} steps";

        public static Plan Generate(Random random, bool bigInt)
        {
            // Jet 4 through ACE 17. Version3 LibRed does not create at all; Version15_2013 it now refuses,
            // because ACE will not open a 0x04 file — see Libred_refuses_to_create_an_ace_15_database.
            JetVersion version = Pick(random,
            [
                JetVersion.Version4, JetVersion.Version12_2007, JetVersion.Version14_2010,
                JetVersion.Version16_2016, JetVersion.Version17_2019,
            ]);

            var generator = new Generator(random, version, bigInt);
            return new Plan
            {
                Version = version,
                Collation = random.Next(2) == 0 ? Collation.GeneralLegacy : Collation.General,
                Steps = generator.Build(8 + random.Next(24)),
            };
        }

        private static T Pick<T>(Random random, IReadOnlyList<T> options) => options[random.Next(options.Count)];
    }

    /// <summary>Builds statements against a MODEL of the schema, not the live database, so most of them are
    /// meaningful. The model drifts whenever a statement is refused, and that drift is welcome — it produces
    /// sequences a fixture never would.</summary>
    private sealed class Generator(Random random, JetVersion version, bool bigInt)
    {
        private readonly List<TableModel> _tables = [];
        private readonly List<Step> _steps = [];
        private int _names;

        private sealed record Column(string Name, string Sql, JetDataType Kind, bool Indexable);
        private sealed record TableModel(string Name, List<Column> Columns, List<string> Indexes);

        public List<Step> Build(int operations)
        {
            // Rung 4's target, kept OUT of the model so no generated statement can drop it — otherwise an "ACE
            // cannot write" verdict would usually mean the harness had removed the table.
            Sql($"CREATE TABLE [{Plan.AceWriteTarget}] ([K] LONG, [V] TEXT(50))");

            CreateTable();   // one real table before the random walk, so early operations have something to hit

            for (int i = 0; i < operations; i++)
            {
                switch (random.Next(14))
                {
                    case 0: case 1: CreateTable(); break;
                    case 2: case 3: case 4: case 5: Insert(); break;
                    case 6: Update(); break;
                    case 7: Delete(); break;
                    case 8: CreateIndex(); break;
                    case 9: AddColumn(); break;
                    case 10: DropColumn(); break;
                    case 11: AlterColumn(); break;
                    case 12: DropObject(); break;
                    case 13: CreateView(); break;
                }
            }

            return _steps;
        }

        // -- operations ---------------------------------------------------------------------------------

        private void CreateTable()
        {
            string name = $"T{++_names}";
            // An AutoNumber PK on every table: gives FKs something to point at, and puts the high-water
            // machinery under every workload rather than a chosen few.
            var columns = new List<Column> { new("Id", "COUNTER", JetDataType.Int32, true) };
            int width = 1 + random.Next(8);
            for (int i = 0; i < width; i++) columns.Add(NewColumn($"C{i}"));

            string body = string.Join(", ", columns.Select(c =>
                c.Name == "Id" ? "[Id] COUNTER PRIMARY KEY" : $"[{c.Name}] {c.Sql}{(random.Next(6) == 0 ? " NOT NULL" : "")}"));

            Sql($"CREATE TABLE [{name}] ({body})");
            _tables.Add(new TableModel(name, columns, []));

            // Sometimes an FK back to an earlier table — the step that puts a relationship's logical-index
            // linkage on two TDEFs at once, a shape ACE is fussy about.
            if (_tables.Count > 1 && random.Next(3) == 0)
            {
                TableModel parent = _tables[random.Next(_tables.Count - 1)];
                if (parent.Columns.Any(c => c.Name == "Id"))
                {
                    Sql($"ALTER TABLE [{name}] ADD COLUMN [Ref] LONG");
                    columns.Add(new Column("Ref", "LONG", JetDataType.Int32, true));
                    string action = Pick(["", " ON DELETE CASCADE", " ON UPDATE CASCADE", " ON DELETE SET NULL"]);
                    Sql($"ALTER TABLE [{name}] ADD CONSTRAINT [FK{_names}] "
                        + $"FOREIGN KEY ([Ref]) REFERENCES [{parent.Name}] ([Id]){action}");
                }
            }
        }

        private void Insert()
        {
            if (Table() is not { } table) return;
            Column[] columns = table.Columns.Where(c => c.Sql != "COUNTER").ToArray();
            if (columns.Length == 0) return;

            int rows = Pick([1, 1, 1, 3, 20, 120]);   // 120 crosses the 255-rows-per-page ceiling when repeated
            for (int r = 0; r < rows; r++)
            {
                var parameters = new Dictionary<string, object?>();
                for (int i = 0; i < columns.Length; i++) parameters[$"p{i}"] = Value(columns[i].Kind);

                Sql($"INSERT INTO [{table.Name}] ({string.Join(", ", columns.Select(c => $"[{c.Name}]"))}) "
                    + $"VALUES ({string.Join(", ", columns.Select((_, i) => $"@p{i}"))})",
                    parameters);
            }
        }

        private void Update()
        {
            if (Table() is not { } table) return;
            Column[] columns = table.Columns.Where(c => c.Sql != "COUNTER").ToArray();
            if (columns.Length == 0) return;

            Column target = Pick(columns);
            // Changing a long value's length forces the row to relocate — where a stale index entry or an
            // unreclaimed LVAL page shows up.
            Sql($"UPDATE [{table.Name}] SET [{target.Name}] = @v",
                new Dictionary<string, object?> { ["v"] = Value(target.Kind) });
        }

        private void Delete()
        {
            if (Table() is not { } table) return;
            // Partial far more often than total — a half-emptied page is the interesting one, and deletion is
            // where writer parity breaks: dead bytes have no reader to catch a wrong marker.
            Sql(random.Next(5) == 0
                ? $"DELETE FROM [{table.Name}]"
                : $"DELETE FROM [{table.Name}] WHERE [Id] > {random.Next(50)}");
        }

        private void CreateIndex()
        {
            if (Table() is not { } table) return;
            Column[] indexable = table.Columns.Where(c => c.Indexable).ToArray();
            if (indexable.Length == 0) return;

            int columns = 1 + random.Next(Math.Min(3, indexable.Length));
            string[] chosen = indexable.OrderBy(_ => random.Next()).Take(columns)
                .Select(c => $"[{c.Name}]{(random.Next(2) == 0 ? " DESC" : "")}").ToArray();

            string name = $"IX{table.Name}_{table.Indexes.Count}";
            string unique = random.Next(4) == 0 ? "UNIQUE " : "";
            Sql($"CREATE {unique}INDEX [{name}] ON [{table.Name}] ({string.Join(", ", chosen)})");
            table.Indexes.Add(name);
        }

        private void AddColumn()
        {
            if (Table() is not { } table) return;
            Column column = NewColumn($"A{table.Columns.Count}");
            Sql($"ALTER TABLE [{table.Name}] ADD COLUMN [{column.Name}] {column.Sql}");
            table.Columns.Add(column);
        }

        private void DropColumn()
        {
            if (Table() is not { } table) return;
            Column[] droppable = table.Columns.Where(c => c.Name != "Id").ToArray();
            if (droppable.Length == 0) return;

            Column column = Pick(droppable);
            Sql($"ALTER TABLE [{table.Name}] DROP COLUMN [{column.Name}]");
            table.Columns.Remove(column);
        }

        private void AlterColumn()
        {
            if (Table() is not { } table) return;
            Column[] alterable = table.Columns.Where(c => c.Name != "Id").ToArray();
            if (alterable.Length == 0) return;

            Column column = Pick(alterable);
            Column replacement = NewColumn(column.Name);
            Sql($"ALTER TABLE [{table.Name}] ALTER COLUMN [{column.Name}] {replacement.Sql}");
            table.Columns[table.Columns.IndexOf(column)] = replacement;
        }

        private void DropObject()
        {
            if (Table() is not { } table) return;

            if (table.Indexes.Count > 0 && random.Next(2) == 0)
            {
                string index = Pick(table.Indexes);
                Sql($"DROP INDEX [{index}] ON [{table.Name}]");
                table.Indexes.Remove(index);
                return;
            }

            Sql($"DROP TABLE [{table.Name}]");
            _tables.Remove(table);
        }

        private void CreateView()
        {
            if (Table() is not { } table) return;
            Sql($"CREATE VIEW [V{++_names}] AS SELECT * FROM [{table.Name}]");
        }

        // -- column and value menus ---------------------------------------------------------------------

        private Column NewColumn(string name)
        {
            (string sql, JetDataType kind, bool indexable)[] menu =
            [
                ("LONG", JetDataType.Int32, true),
                ("SMALLINT", JetDataType.Int16, true),
                ("BYTE", JetDataType.Byte, true),
                ("REAL", JetDataType.Single, true),
                ("FLOAT", JetDataType.Double, true),
                ("CURRENCY", JetDataType.Currency, true),
                ("DATETIME", JetDataType.DateTime, true),
                ("BIT", JetDataType.Boolean, true),
                ("GUID", JetDataType.Guid, true),
                ("DECIMAL(18,4)", JetDataType.FixedPoint, true),
                ("DECIMAL(28,0)", JetDataType.FixedPoint, true),
                // Both ends of the text width range: 1 character, and the 255 ACE caps a char column at. The
                // wide one is also what pushes an index key past the 510-byte entry limit into truncation.
                ("TEXT(1)", JetDataType.Text, true),
                ("TEXT(50)", JetDataType.Text, true),
                ("TEXT(255)", JetDataType.Text, true),
                ("CHAR(10)", JetDataType.Text, true),
                ("VARCHAR(255) WITH COMPRESSION", JetDataType.Text, true),
                ("BINARY(16)", JetDataType.Binary, true),
                ("VARBINARY(510)", JetDataType.Binary, true),
                ("MEMO", JetDataType.Memo, true),
                ("OLEOBJECT", JetDataType.Ole, false),
            ];

            var options = menu.ToList();
            // BIGINT only where both the format and the installed engine can hold it; its presence is what
            // raises the version byte mid-sequence. The provider reads it correctly, so rung 3 can judge it.
            if (bigInt && version >= JetVersion.Version16_2016) options.Add(("BIGINT", JetDataType.Int64, true));

            // DATETIME2 is ABSENT deliberately: ACE's OLE DB provider cannot read the type back (throws on some
            // values, returns the WRONG MONTH on others — see the "Reading DATETIME2 through ACE's own drivers"
            // footnote in docs/format/data-types.md), so rung 3 reports the reader's defect whatever LibRed
            // wrote, and an early sweep duly "found" one. Covered by DateTime2CreatedDatabaseAccessTests, which
            // reads through scalar functions instead.

            (string sql, JetDataType kind, bool indexable) chosen = Pick(options);
            return new Column(name, chosen.sql, chosen.kind, chosen.indexable);
        }

        /// <summary>Nulls and extremes are deliberately common: the sign transforms in index-key encoding and
        /// the boundaries in long-value storage are where a writer disagrees with ACE, and middle values agree
        /// with almost any encoding.</summary>
        private object? Value(JetDataType kind)
        {
            if (random.Next(8) == 0) return null;

            return kind switch
            {
                JetDataType.Boolean => random.Next(2) == 0,
                JetDataType.Byte => (byte)Pick([0, 1, 127, 255]),
                JetDataType.Int16 => (short)Pick([0, -1, 1, short.MaxValue, short.MinValue]),
                JetDataType.Int32 => Pick([0, -1, 1, int.MaxValue, int.MinValue, random.Next()]),
                JetDataType.Int64 => Pick([0L, -1L, 1L, long.MaxValue, long.MinValue]),
                JetDataType.Single => Pick([0f, -1f, 1f, float.MaxValue, float.MinValue, float.Epsilon]),
                JetDataType.Double => Pick([0d, -1d, 1d, double.MaxValue, double.MinValue, double.Epsilon]),
                // Currency is OLE Automation's CY: an int64 scaled by 10,000, so exactly four decimal places.
                JetDataType.Currency => Pick([0m, -1.2345m, 922337203685477.5807m, -922337203685477.5808m]),
                JetDataType.FixedPoint => Pick([0m, -1m, 1.2345m, 79228162514264337593543950335m / 1000000000m]),
                // Below the epoch the OA time fraction stays positive, which is where date ordering diverges.
                JetDataType.DateTime or JetDataType.DateTimeExtended => Pick(
                [
                    new DateTime(1899, 12, 30), new DateTime(1800, 6, 15, 13, 45, 30), new DateTime(1, 1, 1),
                    new DateTime(9999, 12, 31, 23, 59, 59), DateTime.UnixEpoch, new DateTime(2026, 9, 12),
                ]),
                JetDataType.Guid => Guid.NewGuid(),
                JetDataType.Binary or JetDataType.Ole => Bytes(),
                _ => Text(),
            };
        }

        /// <summary>Text that crosses the awkward boundaries: empty, the compressed-Unicode common case, a
        /// string that cannot be compressed, astral characters, and lengths that push a long value from inline
        /// to a single LVAL page to a chain.</summary>
        private string Text() => Pick<Func<string>>(
        [
            () => "",
            () => "a",
            () => new string('x', 255),
            () => "O'Brien \"quoted\" [bracketed] `backticked`",
            () => "naïve café Ω日本語",          // non-Latin1: defeats compressed-Unicode encoding
            () => "\U0001F600\U00020000",        // above the BMP — surrogate pairs in an index key
            () => "ȩ́",               // combining marks: one grapheme, three code units
            () => new string('m', Pick([1, 40, 2000, 4000, 8000, 60000])),
        ])();

        private byte[] Bytes()
        {
            int length = Pick([0, 1, 16, 255, 510, 4000, 20000]);
            var bytes = new byte[length];
            random.NextBytes(bytes);
            return bytes;
        }

        // -- plumbing -----------------------------------------------------------------------------------

        private TableModel? Table() =>
            _tables.Count == 0 ? null : _tables[random.Next(_tables.Count)];

        private T Pick<T>(IReadOnlyList<T> options) => options[random.Next(options.Count)];

        private void Sql(string sql, Dictionary<string, object?>? parameters = null)
            => _steps.Add(new Step(Summarise(sql, parameters),
                (_, engine) => engine.ExecuteNonQuery(sql, parameters)));

        /// <summary>A one-line label for the failure log — parameter values folded in, long text elided.</summary>
        private static string Summarise(string sql, Dictionary<string, object?>? parameters)
        {
            if (parameters is null or { Count: 0 }) return sql;
            string rendered = string.Join(", ", parameters.Select(p => $"{p.Key}={Render(p.Value)}"));
            return $"{sql}   -- {rendered}";
        }

        private static string Render(object? value) => value switch
        {
            null => "NULL",
            string s => s.Length > 24 ? $"'{s[..12]}…' ({s.Length} chars)" : $"'{s}'",
            byte[] b => $"{b.Length} bytes",
            _ => value.ToString() ?? "",
        };
    }
}
