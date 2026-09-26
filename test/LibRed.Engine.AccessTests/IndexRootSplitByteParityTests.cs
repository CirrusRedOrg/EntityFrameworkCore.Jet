using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Index splits that ACE and LibRed must write byte for byte alike: a root that splits (it keeps its page and
/// both halves move out), the cut at every position a new key can land, and the cut on a leaf whose entries
/// differ in length, where it is decided by bytes rather than by count.
/// </summary>
/// <remarks>
/// Each case seeds a table through ACE, then applies one further insert to one copy through ACE and to another
/// through LibRed, and compares every page the table's indexes own — the dead bytes past each page's live end
/// included, since ACE's are reproducible.
/// </remarks>
[Collection(AceCollection.Name)]
public class IndexRootSplitByteParityTests
{
    private const int PageSize = 4096;

    // 204-character keys, so a leaf holds 17 and splits on the 18th.
    private static string Wide(int i) => $"K{i:D4}" + new string('x', 200);

    [Theory]
    [InlineData(95, "a key in the upper half")]
    [InlineData(5, "a key that sorts first")]
    public void A_splitting_root_leaf_keeps_its_page(int key, string _)
    {
        string seeded = Seed("CREATE TABLE RootSplit (k TEXT(255) CONSTRAINT pk PRIMARY KEY)",
            Enumerable.Range(1, 17).Select(i => $"INSERT INTO RootSplit (k) VALUES ('{Wide(i * 10)}')"));
        try { AssertInsertMatchesAce(seeded, "RootSplit", $"INSERT INTO RootSplit (k) VALUES ('{Wide(key)}')"); }
        finally { TemporaryDatabase.Delete(seeded); }
    }

    [Fact]
    public void A_splitting_root_node_keeps_its_page()
    {
        // Keys spread so every leaf keeps taking new ones: the root node fills at 16 separators and the 17th
        // splits it. Found by inserting until the root's own children became nodes.
        static string Spread(int i) => $"{(char)('A' + i * 7 % 26)}{i:D3}" + new string((char)('a' + i % 26), 200);
        string seeded = Seed("CREATE TABLE NodeSplit (k TEXT(255) CONSTRAINT pk PRIMARY KEY)",
            Enumerable.Range(1, 226).Select(i => $"INSERT INTO NodeSplit (k) VALUES ('{Spread(i)}')"));
        try
        {
            byte[] before = File.ReadAllBytes(seeded);
            Assert.True(IndexPages(before, Tdef(seeded, "NodeSplit")).Count(p => before[p * PageSize] == 0x03) == 1,
                "The seed was meant to leave a single root node.");
            AssertInsertMatchesAce(seeded, "NodeSplit", $"INSERT INTO NodeSplit (k) VALUES ('{Spread(227)}')");
        }
        finally { TemporaryDatabase.Delete(seeded); }
    }

    [Fact]
    public void A_leaf_is_cut_where_ace_cuts_it_at_every_position()
    {
        // A non-root leaf of 17 equal entries whose parent has room: 180…340 on the right of a first split.
        string seeded = Seed("CREATE TABLE SplitPos (k TEXT(255) CONSTRAINT pk PRIMARY KEY)",
            Enumerable.Range(1, 34).Select(i => $"INSERT INTO SplitPos (k) VALUES ('{Wide(i * 10)}')"));
        try
        {
            for (int position = 0; position <= 17; position++)
                AssertInsertMatchesAce(seeded, "SplitPos", $"INSERT INTO SplitPos (k) VALUES ('{Wide(175 + position * 10)}')");
        }
        finally { TemporaryDatabase.Delete(seeded); }
    }

    [Fact]
    public void A_leaf_of_unequal_entries_is_cut_by_bytes()
    {
        // Labels 0…144 in id order land all over the text index; entries are 38 to 40 bytes stored, and the
        // 146th splits a 91-entry leaf 45 and 47 where halving the count would keep 46.
        static string Label(int i) => $"Bulk label number {i} " + new string('L', 30);
        string seeded = Seed("CREATE TABLE Labels (Id LONG CONSTRAINT pkLabels PRIMARY KEY, Label TEXT(60))",
            new[] { "CREATE INDEX ixLabel ON Labels (Label)" }
                .Concat(Enumerable.Range(0, 145).Select(i => $"INSERT INTO Labels (Id, Label) VALUES ({i}, '{Label(i)}')")));
        try { AssertInsertMatchesAce(seeded, "Labels", $"INSERT INTO Labels (Id, Label) VALUES (145, '{Label(145)}')"); }
        finally { TemporaryDatabase.Delete(seeded); }
    }

    [Fact]
    public void An_ascending_load_splits_nodes_at_the_right_edge_as_ace_does()
    {
        // 400 ascending keys fill 24 leaves of 17, so the root node overflows while each new separator is its
        // last entry. The whole load goes through each engine from the same empty table. A load this size also
        // takes ACE past the point where it places pages differently (in aligned groups), so the trees are
        // compared by shape — each level left to right — rather than by page number.
        string empty = Seed("CREATE TABLE Ascending (k TEXT(255) CONSTRAINT pk PRIMARY KEY)", []);
        string ace = TemporaryDatabase.CopyPath(empty, "ascending-ace-");
        string libred = TemporaryDatabase.CopyPath(empty, "ascending-libred-");
        try
        {
            string[] inserts = [.. Enumerable.Range(1, 400).Select(i => $"INSERT INTO Ascending (k) VALUES ('{Wide(i)}')")];
            using (OleDbConnection conn = AceTestDatabase.Open(ace))
                foreach (string sql in inserts)
                {
                    using OleDbCommand cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
            using (var db = JetDatabase.Open(libred, readOnly: false))
            {
                var engine = new QueryEngine(db);
                foreach (string sql in inserts) engine.ExecuteNonQuery(sql);
            }

            int tdef = Tdef(empty, "Ascending");
            byte[] a = File.ReadAllBytes(ace), l = File.ReadAllBytes(libred);
            Assert.True(IndexPages(a, tdef).Count(p => a[p * PageSize] == 0x03) >= 3, "The load was meant to split a node.");
            Assert.Equal(Shape(a, tdef), Shape(l, tdef));
        }
        finally
        {
            foreach (string path in new[] { empty, ace, libred }) TemporaryDatabase.Delete(path);
        }
    }

    // CREATE INDEX over existing rows writes the tree that inserting the keys one at a time in order would: the
    // root keeps its page, full leaves are compressed before they split, nodes split at the right edge, and
    // pages come from the allocator in the order those splits happen. 40 rows give one node over three leaves;
    // 400 give two levels of nodes, the lower one split.
    [Theory]
    [InlineData(40)]
    [InlineData(400)]
    public void Create_index_writes_what_ace_writes(int rows)
    {
        string seeded = Seed("CREATE TABLE Indexed (Id LONG, K TEXT(255))", Enumerable.Range(1, rows).Select(i =>
            $"INSERT INTO Indexed VALUES ({i}, '{(char)('A' + i * 7 % 26)}{i:D4}{new string((char)('a' + i % 26), 200)}')"));
        try
        {
            int tdef = Tdef(seeded, "Indexed");
            string ace = TemporaryDatabase.CopyPath(seeded, "createindex-ace-");
            string libred = TemporaryDatabase.CopyPath(seeded, "createindex-libred-");
            try
            {
                const string ddl = "CREATE INDEX ixK ON Indexed (K)";
                using (OleDbConnection conn = AceTestDatabase.Open(ace))
                using (OleDbCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = ddl;
                    cmd.ExecuteNonQuery();
                }
                using (var db = JetDatabase.Open(libred, readOnly: false))
                    new QueryEngine(db).ExecuteNonQuery(ddl);

                byte[] a = File.ReadAllBytes(ace), l = File.ReadAllBytes(libred);
                Assert.True(a.Length == l.Length, $"ACE's file has {a.Length / PageSize} pages, LibRed's {l.Length / PageSize}.");
                Assert.True(IndexPages(a, tdef).Any(p => a[p * PageSize] == 0x03), "The index was meant to have a node.");
                // The index's pages, and the definition that points at its root.
                foreach (int page in IndexPages(a, tdef).Union(IndexPages(l, tdef)).Append(tdef))
                {
                    int differ = Enumerable.Range(0, PageSize).FirstOrDefault(b => a[page * PageSize + b] != l[page * PageSize + b], -1);
                    Assert.True(differ < 0, $"{rows} rows: page {page} differs from ACE's at 0x{differ:X3}.");
                }
            }
            finally
            {
                TemporaryDatabase.Delete(ace);
                TemporaryDatabase.Delete(libred);
            }
        }
        finally { TemporaryDatabase.Delete(seeded); }
    }

    private static string Seed(string create, IEnumerable<string> statements)
    {
        string seeded = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "rootsplit-seed-");
        using OleDbConnection conn = AceTestDatabase.Open(seeded);
        foreach (string sql in statements.Prepend(create))
        {
            using OleDbCommand cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        return seeded;
    }

    private static int Tdef(string path, string table)
    {
        using var db = JetDatabase.Open(path);
        return db.Catalog.FindTable(table)!.DefinitionPage;
    }

    private static void AssertInsertMatchesAce(string seeded, string table, string insert)
    {
        int tdef = Tdef(seeded, table);
        string ace = TemporaryDatabase.CopyPath(seeded, "rootsplit-ace-");
        string libred = TemporaryDatabase.CopyPath(seeded, "rootsplit-libred-");
        try
        {
            using (OleDbConnection conn = AceTestDatabase.Open(ace))
            using (OleDbCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = insert;
                cmd.ExecuteNonQuery();
            }
            using (var db = JetDatabase.Open(libred, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(insert);

            byte[] a = File.ReadAllBytes(ace), l = File.ReadAllBytes(libred);
            Assert.True(IndexPages(a, tdef).Count > IndexPages(File.ReadAllBytes(seeded), tdef).Count,
                $"{insert[..Math.Min(60, insert.Length)]}…: the insert was meant to split a page.");
            Assert.True(a.Length == l.Length, $"{insert}: ACE's file has {a.Length / PageSize} pages, LibRed's {l.Length / PageSize}.");
            foreach (int page in IndexPages(a, tdef).Union(IndexPages(l, tdef)))
            {
                int differ = Enumerable.Range(0, PageSize).FirstOrDefault(
                    b => a[page * PageSize + b] != l[page * PageSize + b], -1);
                Assert.True(differ < 0, $"{insert[..Math.Min(60, insert.Length)]}…: page {page} differs from ACE's at 0x{differ:X3}.");
            }
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    /// <summary>Each level of the tree, top down and left to right along the sibling links, as
    /// <c>entries/prefix/free</c> per page — everything about the tree but where its pages are.</summary>
    private static string Shape(byte[] file, int tdef)
    {
        List<int> pages = IndexPages(file, tdef);
        int At(int page, int offset) => BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(page * PageSize + offset, 4));
        var levels = new List<string>();
        foreach (IGrouping<byte, int> level in pages.GroupBy(p => file[p * PageSize + 0x1A]).OrderByDescending(g => g.Key))
        {
            var byPage = level.ToHashSet();
            var shapes = new List<string>();
            for (int p = level.Single(q => At(q, 0x0C) == 0); p != 0 && byPage.Contains(p); p = At(p, 0x10))
            {
                int entries = 0;
                for (int i = 0x1B; i < 0x1E0; i++) entries += System.Numerics.BitOperations.PopCount(file[p * PageSize + i]);
                shapes.Add($"{entries}/{BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(p * PageSize + 0x18))}/" +
                           $"{BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(p * PageSize + 2))}");
            }
            levels.Add($"level {level.Key}: {string.Join(" ", shapes)}");
        }
        return string.Join("\n", levels);
    }

    private static List<int> IndexPages(byte[] file, int tdef)
    {
        var pages = new List<int>();
        for (int p = 0; p < file.Length / PageSize; p++)
            if (file[p * PageSize] is 0x03 or 0x04
                && BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(p * PageSize + 4, 4)) == tdef)
                pages.Add(p);
        return pages;
    }
}
