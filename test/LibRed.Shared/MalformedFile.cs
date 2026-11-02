using LibRed.Catalog;
using LibRed.Storage;

namespace LibRed.Tests.Shared;

/// <summary>
/// What a damaged database file is allowed to do to a reader, the good databases to damage, and the walk that
/// drives every read path over the result. Shared by the fixed-seed sweep in LibRed.Core.Tests and the
/// coverage-guided harness in LibRed.Core.Fuzz, so the two cannot disagree about what counts as a leak.
/// </summary>
/// <remarks>
/// The contract is not "must not throw" — a damaged file SHOULD throw. It is that a damaged file must throw
/// InvalidDataException, LibRed's one signal for "these bytes are not a valid database". A caller that wants
/// to tell a corrupt file from a bug in the reader has nothing else to catch, and .NET gives it no help: the
/// exceptions a parser leaks by accident (ArgumentOutOfRangeException, IndexOutOfRangeException,
/// EndOfStreamException) share no base class with InvalidDataException, which derives from SystemException
/// rather than IOException. An unhandled one does not degrade, it crashes the caller.
/// </remarks>
internal static class MalformedFile
{
    /// <summary>The exception types a malformed file is allowed to produce.</summary>
    public static bool IsExpected(Exception e) =>
        e is InvalidDataException                       // the contract: these bytes are not a database
          or NotSupportedException                      // a real feature LibRed does not implement yet
          or UnauthorizedAccessException;               // encrypted, wrong or missing password

    /// <summary>
    /// The good databases to corrupt: one ACE-authored fixture, then shapes LibRed synthesises itself to
    /// reach structures the fixture does not contain. Each is written into <paramref name="directory"/>.
    /// </summary>
    /// <remarks>
    /// The source databases are AUTHORED HERE, not read from a corpus. Two reasons that matters:
    /// <list type="bullet">
    /// <item>Coverage is otherwise bounded by whatever one fixture happens to contain. Northwind has no multi-page
    /// TDEF, no chained long value, no dropped-then-added column and no deep index, so those decode paths
    /// would never see a corrupt byte at all. LibRed can synthesise a file page-by-page, so the awkward
    /// shapes are ours to make.</item>
    /// <item>A file LibRed wrote tests our reader against our own writer. A leak then points at one of the two,
    /// rather than at "ACE wrote something we mishandle".</item>
    /// </list>
    /// Northwind stays in the set as the one ACE-authored shape, since agreeing with a real Access file is the
    /// whole point of the format work.
    /// </remarks>
    public static IEnumerable<(string Shape, string Path)> Sources(string directory, string northwind)
    {
        string copy = Path.Combine(directory, "source-northwind.accdb");
        File.Copy(northwind, copy);
        yield return ("northwind", copy);

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
        JetDatabase.Create(path);
        using (var database = JetDatabase.Open(path, readOnly: false))
            build(database);
        return path;
    }

    /// <summary>
    /// Walks everything a caller would, one stage at a time and <b>continuing past failures</b>: open, list
    /// the catalog, then each table's rows separately. With <paramref name="write"/>, in each user table still
    /// listed, the first of its first 32 rows the write accepts is also rewritten in place, deleted and inserted
    /// again before its indexes are walked.
    /// </summary>
    /// <remarks>
    /// Per-stage on purpose. Stopping at the first exception means one damaged TDEF hides every path behind
    /// it, and the interesting leak is rarely the first thing that breaks.
    /// </remarks>
    public static IEnumerable<(string Stage, Exception? Leak)> Exercise(string path, string? password = null, bool write = false)
    {
        JetDatabase? database = null;
        try
        {
            (string open, Exception? leak) = Try("open", () => database = JetDatabase.Open(path, readOnly: !write, password: password));
            yield return (open, leak);
            if (database is null) yield break;

            List<TableDefinition> tables = [];
            yield return Try("catalog", () => tables = [.. database.Catalog.Tables]);

            foreach (TableDefinition table in tables)
            {
                // A write allocates through page 0's free-pages map wherever it points, and so does ACE: aimed at a
                // table's own map, ACE takes that table's live pages without looking at them (measured,
                // docs/format/page-05-usage-maps.md §9.1). So on a damaged file a write can overwrite the catalog's
                // pages, and a table listed above is gone when the walk reaches it — or reaches its indexes, after
                // its own write. Asking for it by name then gets the answer for a table that is not there; that is
                // a refusal of the name, not a leak, so the walk only follows what is still listed.
                bool listed = true;
                if (write)
                {
                    yield return Try($"find '{table.Name}'", () => listed = database.Catalog.FindTable(table.Name) is not null);
                    if (!listed) continue;
                }

                int rows = 0;
                (string read, Exception? rowLeak) = Try($"table '{table.Name}'", () =>
                {
                    foreach (object?[] _ in database.OpenTable(table.Name).Rows()) rows++;
                });
                yield return ($"{read} ({rows} rows)", rowLeak);

                // A write reads what it changes — the slot it rewrites, the free-space map it allocates from,
                // the index entry it removes — and takes every value it finds there as its next offset. Writing
                // the row back with its own values keeps the workload valid on an undamaged file. On a damaged one
                // the row's values can break its own damaged schema — a Null key, a value past its column, a record
                // past the page — and a write refuses them with InvalidOperationException, as ACE refuses them too.
                // So here that is a refusal, not a leak, and the next row is tried; what a write may not do is leak
                // anything else. The rewrite declares no column changed and leaves Table.Update to find that none
                // did: declaring them all would also "update" a write-once column, which ACE refuses whatever the
                // value, and so refuse every row. Only the first rows are tried: when they are all refused, the damage
                // is the table's — a schema whose every record is too large — and trying each of thousands of rows
                // spends the fuzzer's time limit on the same refusal.
                if (write && !table.IsSystem)
                    yield return Try($"write '{table.Name}'", () =>
                    {
                        Table opened = database.OpenTable(table.Name);
                        foreach ((RowId id, object?[] values) in opened.Rows().WithIds().Take(32).ToList())
                        {
                            try
                            {
                                opened.Update(id, values, new HashSet<int>());
                                opened.Delete(id);
                                opened.Insert(values);
                                break;
                            }
                            catch (InvalidOperationException) { }
                        }
                    });
                if (write)
                {
                    yield return Try($"find '{table.Name}' again", () => listed = database.Catalog.FindTable(table.Name) is not null);
                    if (!listed) continue;
                }

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
