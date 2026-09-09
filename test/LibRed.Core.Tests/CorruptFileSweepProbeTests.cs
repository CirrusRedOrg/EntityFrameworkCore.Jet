using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Structural mutation sweep: take a good database, corrupt one field of one page header, and drive every
// read path over the result.
//
// The contract is not "must not throw" — a damaged file SHOULD throw. It is that a damaged file must throw
// InvalidDataException, LibRed's one signal for "these bytes are not a valid database". A caller that wants
// to tell a corrupt file from a bug in the reader has nothing else to catch, and .NET gives it no help: the
// exceptions a parser leaks by accident (ArgumentOutOfRangeException, IndexOutOfRangeException,
// EndOfStreamException) share no base class with InvalidDataException, which derives from SystemException
// rather than IOException. An unhandled one does not degrade, it crashes the caller.
//
// The source databases are AUTHORED HERE, not read from a corpus. Two reasons that matters:
//
//   * Coverage is otherwise bounded by whatever one fixture happens to contain. Northwind has no multi-page
//     TDEF, no chained long value, no dropped-then-added column and no deep index, so those decode paths
//     would never see a corrupt byte at all. LibRed can synthesise a file page-by-page, so the awkward
//     shapes are ours to make.
//   * A file LibRed wrote tests our reader against our own writer. A leak then points at one of the two,
//     rather than at "ACE wrote something we mishandle".
//
// Northwind stays in the set as the one ACE-authored shape, since agreeing with a real Access file is the
// whole point of the format work.
public class CorruptFileSweepProbeTests(ITestOutputHelper output)
{
    /// <summary>The exception types a malformed file is allowed to produce.</summary>
    private static bool IsExpected(Exception e) =>
        e is InvalidDataException                       // the contract: these bytes are not a database
          or NotSupportedException                      // a real feature LibRed does not implement yet
          or UnauthorizedAccessException;               // encrypted, wrong or missing password

    [Fact]
    public void No_structural_mutation_escapes_an_unexpected_exception_type()
    {
        // Deliberately small and fixed-seed: this is a REGRESSION guard, not a search. It runs the same 600
        // mutations every time, so once green it stays green and finds nothing new. Discovery means raising
        // this and varying the seed in an offline run — done at 2500 (12,500 mutations, 208k table reads,
        // 236k index walks, ~2 min) with no leaks, which is what the committed size is guarding.
        const int perSource = 120;
        var random = new Random(20260907);

        string directory = Path.Combine(Path.GetTempPath(), $"libred-fuzz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var leaks = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int mutations = 0, reachedCatalog = 0, readTables = 0, walkedIndexes = 0;
        try
        {
            foreach ((string shape, string source) in Sources(directory))
            {
                byte[] clean = File.ReadAllBytes(source);
                int pageSize = 4096;
                int pages = clean.Length / pageSize;
                output.WriteLine($"{shape}: {pages} pages");

                for (int i = 0; i < perSource; i++)
                {
                    // Page 0 is the database header and has its own tests; offsets below 64 are the page
                    // header, where the pointers, counts and offsets live. A byte flipped anywhere else
                    // lands in string or numeric DATA, which changes a value and proves nothing.
                    int page = random.Next(1, pages);
                    int offset = random.Next(0, 64);
                    byte value = (byte)random.Next(256);

                    byte[] mutated = (byte[])clean.Clone();
                    mutated[page * pageSize + offset] = value;

                    string path = Path.Combine(directory, $"{shape}-{i:000}.accdb");
                    File.WriteAllBytes(path, mutated);
                    mutations++;

                    string where = $"{shape} page {page} offset 0x{offset:X2} = 0x{value:X2}";
                    foreach ((string stage, Exception? leak) in Exercise(path))
                    {
                        if (stage.StartsWith("catalog: ok", StringComparison.Ordinal)) reachedCatalog++;
                        if (stage.StartsWith("table '", StringComparison.Ordinal)
                            && stage.Contains("': ok", StringComparison.Ordinal)) readTables++;
                        if (stage.StartsWith("index '", StringComparison.Ordinal)
                            && stage.Contains("': ok", StringComparison.Ordinal)) walkedIndexes++;
                        if (leak is null) continue;

                        // One line per distinct (exception type, throwing frame): a hundred mutations of one
                        // field are one bug, and the first one's coordinates are enough to reproduce it.
                        string frame = leak.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "(no frame)";
                        if (seen.Add($"{leak.GetType().Name}|{frame}"))
                        {
                            leaks.Add($"{leak.GetType().Name} at {frame}  [{where}, {stage}]");
                            output.WriteLine("  " + leaks[^1]);
                        }
                    }
                    File.Delete(path);
                }
            }
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* best effort */ }
        }

        // A run where every mutation was rejected at `open` would pass while proving nothing, so the depth
        // the mutations actually reached is asserted, not just reported. An earlier version of this had a
        // broken counter reading "read a table 0 times" and still passed.
        output.WriteLine($"{mutations} mutations: reached the catalog {reachedCatalog} times, " +
                         $"read a table {readTables} times, walked an index {walkedIndexes} times, " +
                         $"{leaks.Count} distinct leaks");
        Assert.True(reachedCatalog > 0, "no mutation got past open — the sweep proved nothing");
        Assert.True(readTables > 0, "no mutation reached a single row — the sweep proved nothing");
        Assert.True(walkedIndexes > 0, "no mutation reached an index page — the sweep proved nothing");
        Assert.Empty(leaks);
    }

    /// <summary>
    /// The good databases to corrupt: one ACE-authored fixture, then shapes LibRed synthesises itself to
    /// reach structures the fixture does not contain.
    /// </summary>
    private static IEnumerable<(string Shape, string Path)> Sources(string directory)
    {
        yield return ("northwind", TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "fuzz-nw-"));

        yield return ("wide-tdef", Author(directory, "wide-tdef", database =>
        {
            // 200 columns spills the definition onto continuation pages, so the TDEF chain gets fuzzed.
            database.CreateTable("Wide",
                [.. Enumerable.Range(0, 200).Select(i => new ColumnSpec($"c{i}", JetDataType.Int32, 4, IsFixedLength: true))]);
            var table = database.OpenTable("Wide");
            var row = new object?[200];
            for (int i = 0; i < 200; i++) row[i] = i;
            table.Insert(row);
        }));

        yield return ("long-values", Author(directory, "long-values", database =>
        {
            database.CreateTable("Docs",
                [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                 new ColumnSpec("Body", JetDataType.Memo, 0, IsFixedLength: false)]);
            var table = database.OpenTable("Docs");
            // Inline, single-page and chained, so all three long-value storage forms are present.
            foreach ((int id, int length) in new[] { (1, 20), (2, 2000), (3, 40_000) })
                table.Insert([id, new string((char)('a' + id), length)]);
        }));

        yield return ("split-index", Author(directory, "split-index", database =>
        {
            database.CreateTable("Keyed",
                [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                 new ColumnSpec("V", JetDataType.Text, 60, IsFixedLength: false)],
                primaryKey: ["K"]);
            var table = database.OpenTable("Keyed");
            // Enough rows to drive the B-tree past a single leaf, so node pages exist to corrupt.
            for (int i = 0; i < 1500; i++) table.Insert([i, $"value {i}"]);
        }));

        yield return ("altered-columns", Author(directory, "altered-columns", database =>
        {
            database.CreateTable("Churn",
                [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                 new ColumnSpec("A", JetDataType.Text, 30, IsFixedLength: false),
                 new ColumnSpec("B", JetDataType.Text, 30, IsFixedLength: false),
                 new ColumnSpec("N", JetDataType.Int32, 4, IsFixedLength: true)]);
            var table = database.OpenTable("Churn");
            table.Insert([1, "a1", "b1", 11]);

            // The gapped index space and burned ids of a table that has been edited — the shapes that
            // produced the row-layout bugs, which a freshly created table cannot reach.
            database.DropColumn("Churn", "B");
            database.AddColumn("Churn", new ColumnSpec("D", JetDataType.Text, 30, IsFixedLength: false));
            database.OpenTable("Churn").Insert([2, "a2", 22, "d2"]);
            database.AlterColumnTypeInPlace("Churn", "N",
                new ColumnSpec("N", JetDataType.Text, 20, IsFixedLength: false));
        }));
    }

    /// <summary>Synthesises a database from scratch and applies <paramref name="build"/> to it.</summary>
    private static string Author(string directory, string name, Action<JetDatabase> build)
    {
        string path = Path.Combine(directory, $"source-{name}.accdb");
        DatabaseCreator.CreateEmpty(path);
        using (var database = JetDatabase.Open(path, readOnly: false))
            build(database);
        return path;
    }

    /// <summary>
    /// Walks everything a caller would, one stage at a time and <b>continuing past failures</b>: open, list
    /// the catalog, then each table's rows separately.
    /// </summary>
    /// <remarks>
    /// Per-stage on purpose. Stopping at the first exception means one damaged TDEF hides every path behind
    /// it, and the interesting leak is rarely the first thing that breaks.
    /// </remarks>
    private static IEnumerable<(string Stage, Exception? Leak)> Exercise(string path)
    {
        JetDatabase? database = null;
        try
        {
            (string open, Exception? leak) = Try("open", () => database = JetDatabase.Open(path));
            yield return (open, leak);
            if (database is null) yield break;

            List<TableDef> tables = [];
            yield return Try("catalog", () => tables = [.. database.Catalog.Tables]);

            foreach (TableDef table in tables)
            {
                int rows = 0;
                (string read, Exception? rowLeak) = Try($"table '{table.Name}'", () =>
                {
                    foreach (object?[] _ in database.OpenTable(table.Name).Rows()) rows++;
                });
                yield return ($"{read} ({rows} rows)", rowLeak);

                // A table SCAN reads the usage map and the data pages and never descends a B-tree, so
                // without this every index page the sweep corrupts goes unread — the split-index shape was
                // building node pages that nothing then looked at. Walking the entries decodes every page
                // of the index, which is more of it than a handful of seeks would touch.
                foreach (IndexDef index in table.Indexes)
                {
                    int entries = 0;
                    (string walked, Exception? indexLeak) =
                        Try($"index '{table.Name}.{index.Name}'", () =>
                        {
                            Table opened = database.OpenTable(table.Name);
                            foreach ((byte[] _, RowId _) in
                                     new IndexCursor(opened.Channel, index.RootPage).RawEntries()) entries++;
                        });
                    yield return ($"{walked} ({entries} entries)", indexLeak);
                }
            }
        }
        finally { database?.Dispose(); }
    }

    private static (string Outcome, Exception? Leak) Try(string stage, Action action)
    {
        try
        {
            action();
            return ($"{stage}: ok", null);
        }
        catch (Exception e) when (IsExpected(e))
        {
            return ($"{stage}: rejected — {e.GetType().Name}: {Summarise(e.Message)}", null);
        }
        catch (Exception e)
        {
            return ($"{stage}: !! LEAKED {e.GetType().Name}: {Summarise(e.Message)}", e);
        }
    }

    private static string Summarise(string message)
    {
        string one = message.ReplaceLineEndings(" ").Trim();
        return one.Length <= 100 ? one : one[..100] + "…";
    }
}
