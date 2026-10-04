using System.Data.OleDb;
using LibRed;
using LibRed.Formats;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A leaf that has to split is cut where ACE cuts it. The cut is taken over the entries the page held
/// <b>before</b> the insert, so the side the new key falls on decides which page keeps the odd entry — the
/// two positions below exercise both branches of that rule.
/// </summary>
/// <remarks>
/// The table is filled with even keys through ACE, so both engines start from byte-identical, packed leaves;
/// one odd key is then inserted into a copy by each engine and the files are compared. Filling is the slow
/// part, so it is done once and the copies taken from it.
/// </remarks>
[Collection(AceCollection.Name)]
public class IndexSplitByteParityTests(ITestOutputHelper output)
{
    private const int EvenKeys = 900;

    [Fact]
    public void A_split_leaf_is_cut_where_ace_cuts_it()
    {
        string seeded = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "splitguard-seed-");
        try
        {
            using (OleDbConnection conn = AceTestDatabase.Open(seeded))
            {
                using (OleDbCommand ddl = conn.CreateCommand())
                {
                    ddl.CommandText = "CREATE TABLE SplitGuard (k LONG NOT NULL, CONSTRAINT pk PRIMARY KEY (k))";
                    ddl.ExecuteNonQuery();
                }
                for (int i = 1; i <= EvenKeys; i++)
                {
                    using OleDbCommand ins = conn.CreateCommand();
                    ins.CommandText = $"INSERT INTO SplitGuard (k) VALUES ({i * 2})";
                    ins.ExecuteNonQuery();
                }
            }

            // 101 lands below the splitting leaf's midpoint, 801 above it — the two branches of the rule.
            foreach (int key in new[] { 101, 801 })
                AssertSplitMatchesAce(seeded, key);
        }
        finally { TemporaryDatabase.Delete(seeded); }
    }

    private void AssertSplitMatchesAce(string seeded, int key)
    {
        string aceCopy = TemporaryDatabase.CopyPath(seeded, $"splitguard-ace-{key}-");
        string libredCopy = TemporaryDatabase.CopyPath(seeded, $"splitguard-libred-{key}-");
        try
        {
            using (OleDbConnection conn = AceTestDatabase.Open(aceCopy))
            {
                using OleDbCommand cmd = conn.CreateCommand();
                cmd.CommandText = $"INSERT INTO SplitGuard (k) VALUES ({key})";
                cmd.ExecuteNonQuery();
            }

            using (var db = JetDatabase.Open(libredCopy, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery($"INSERT INTO SplitGuard (k) VALUES ({key})");

            int tdef;
            JetFormatBase format;
            using (var db = JetDatabase.Open(seeded))
            {
                tdef = db.Catalog.FindTable("SplitGuard")!.DefinitionPage;
                format = db.Format;
            }

            int pageSize = format.PageSize;
            byte[] a = File.ReadAllBytes(aceCopy), l = File.ReadAllBytes(libredCopy);
            int pages = Math.Min(a.Length, l.Length) / pageSize;
            int checkedPages = 0;

            for (int p = 0; p < pages; p++)
            {
                // Only this index's own pages: the rest of the file is ACE's bookkeeping.
                if (!IsOurIndexPage(a, p, tdef, format) && !IsOurIndexPage(l, p, tdef, format)) continue;

                int aceFree = IndexTree.ReadFreeSpace(a.AsSpan(p * pageSize, pageSize), format);
                int libFree = IndexTree.ReadFreeSpace(l.AsSpan(p * pageSize, pageSize), format);
                Assert.True(aceFree == libFree,
                    $"key {key}, page {p}: free space {libFree} where ACE wrote {aceFree} — the leaf was cut elsewhere.");

                // Past the live end a page keeps whatever it held, and a page ACE appended past the old
                // end-of-file keeps ACE's uninitialised buffer, which is not reproducible. Compare the live
                // region, which is the split itself.
                int live = pageSize - aceFree;
                Assert.True(
                    a.AsSpan(p * pageSize, live).SequenceEqual(l.AsSpan(p * pageSize, live)),
                    $"key {key}, page {p}: live bytes differ from ACE's.");
                checkedPages++;
            }

            Assert.True(checkedPages >= 2, $"key {key}: expected the split leaf and its new sibling, saw {checkedPages}.");
            output.WriteLine($"key {key}: {checkedPages} index pages match ACE");
        }
        finally
        {
            TemporaryDatabase.Delete(aceCopy);
            TemporaryDatabase.Delete(libredCopy);
        }
    }

    private static bool IsOurIndexPage(byte[] file, int page, int tdef, JetFormatBase format) =>
        (page + 1) * format.PageSize <= file.Length
        && PageHeader.ReadType(file.AsSpan(page * format.PageSize)) is PageType.IntermediateIndexPage or PageType.LeafIndexPage
        && IndexTree.ReadOwner(file.AsSpan(page * format.PageSize, format.PageSize), format) == tdef;
}