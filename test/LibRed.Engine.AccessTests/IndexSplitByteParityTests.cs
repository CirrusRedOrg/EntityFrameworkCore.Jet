using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed;
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
    private const int PageSize = 4096;
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
            using (var db = JetDatabase.Open(seeded)) tdef = db.Catalog.FindTable("SplitGuard")!.DefinitionPage;

            byte[] a = File.ReadAllBytes(aceCopy), l = File.ReadAllBytes(libredCopy);
            int pages = Math.Min(a.Length, l.Length) / PageSize;
            int checkedPages = 0;

            for (int p = 0; p < pages; p++)
            {
                // Only this index's own pages: the rest of the file is ACE's bookkeeping.
                if (!IsOurIndexPage(a, p, tdef) && !IsOurIndexPage(l, p, tdef)) continue;

                int aceFree = BinaryPrimitives.ReadUInt16LittleEndian(a.AsSpan(p * PageSize + 2, 2));
                int libFree = BinaryPrimitives.ReadUInt16LittleEndian(l.AsSpan(p * PageSize + 2, 2));
                Assert.True(aceFree == libFree,
                    $"key {key}, page {p}: free space {libFree} where ACE wrote {aceFree} — the leaf was cut elsewhere.");

                // Past the live end a page keeps whatever it held, and a page ACE appended past the old
                // end-of-file keeps ACE's uninitialised buffer, which is not reproducible. Compare the live
                // region, which is the split itself.
                int live = PageSize - aceFree;
                Assert.True(
                    a.AsSpan(p * PageSize, live).SequenceEqual(l.AsSpan(p * PageSize, live)),
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

    private static bool IsOurIndexPage(byte[] file, int page, int tdef) =>
        (page + 1) * PageSize <= file.Length
        && file[page * PageSize] is 0x03 or 0x04
        && BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(page * PageSize + 4, 4)) == tdef;
}
