using System.Buffers.Binary;
using System.Data.OleDb;
using System.Globalization;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Engine.Tests;

// PROBE: where does ACE cut a full index leaf that has to split?
//
// The insert parity probe showed the two engines splitting to different fill levels, but it does it inside a
// three-level cascade on a table with three indexes, where nothing can be attributed. This builds the
// isolated case instead:
//
//   1. ACE creates a one-column-key table and fills it with EVEN keys, ascending. Ascending inserts take the
//      right-edge split both engines already agree on, so the leaves end up packed and identical.
//   2. That file is copied. One copy gets a single ODD key through ACE, the other the same key through
//      LibRed. The key lands inside a full leaf, so exactly one leaf must split, with no cascade to confuse
//      the measurement.
//   3. The leaf's free space on each side is the split point.
//
// The odd key is varied across the range so the rule can be read as a function of WHERE the new entry falls.
[Collection(AceCollection.Name)]
public class IndexSplitPointProbeTest(ITestOutputHelper output)
{
    private const int PageSize = 4096;
    private const int EvenKeys = 900;   // enough for several full leaves at ~9 bytes an entry

    [Theory]
    [InlineData(1)]       // BELOW every existing key — the index minimum, pos 0
    [InlineData(3)]       // near the low end of the first leaf
    [InlineData(101)]     // inside the first leaf
    [InlineData(801)]     // past the midpoint of the splitting leaf
    [InlineData(603)]     // exactly ON the midpoint of the 602 entries the leaf held
    [InlineData(605)]     // one past it
    [InlineData(1799)]    // just below the maximum — still not the maximum
    public void Probe_where_ace_cuts_a_full_leaf(int oddKey)
    {
        string seeded = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), $"split-seed-{oddKey}-");
        string aceCopy = "", libredCopy = "";
        try
        {
            using (OleDbConnection conn = AceTestDatabase.Open(seeded))
            {
                Exec(conn, "CREATE TABLE SplitProbe (k LONG NOT NULL, CONSTRAINT pk PRIMARY KEY (k))");
                for (int i = 1; i <= EvenKeys; i++)
                    Exec(conn, $"INSERT INTO SplitProbe (k) VALUES ({i * 2})");
            }

            aceCopy = TemporaryDatabase.CopyPath(seeded, $"split-ace-{oddKey}-");
            libredCopy = TemporaryDatabase.CopyPath(seeded, $"split-libred-{oddKey}-");

            using (OleDbConnection conn = AceTestDatabase.Open(aceCopy))
                Exec(conn, $"INSERT INTO SplitProbe (k) VALUES ({oddKey})");

            using (var db = JetDatabase.Open(libredCopy, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(
                    $"INSERT INTO SplitProbe (k) VALUES ({oddKey.ToString(CultureInfo.InvariantCulture)})");

            byte[] o = File.ReadAllBytes(seeded), a = File.ReadAllBytes(aceCopy), l = File.ReadAllBytes(libredCopy);

            int tdef;
            using (var db = JetDatabase.Open(seeded)) tdef = db.Catalog.FindTable("SplitProbe")!.DefinitionPage;

            output.WriteLine($"PROBE key {oddKey}: sizes orig={o.Length} ace={a.Length} libred={l.Length}");
            foreach (int page in Union(Changed(o, a), Changed(o, l)).Order())
            {
                if (!Owned(o, page, tdef) && !Owned(a, page, tdef) && !Owned(l, page, tdef)) continue;
                output.WriteLine($"PROBE   page {page} (was 0x{Type(o, page):X2}):"
                    + $" free orig={Free(o, page)} ace={Free(a, page)} libred={Free(l, page)}"
                    + $" | prefix orig={Prefix(o, page)} ace={Prefix(a, page)} libred={Prefix(l, page)}"
                    + $" | {(Same(a, l, page) ? "IDENTICAL" : $"differs first @0x{FirstDiff(a, l, page):X3}")}");
            }
        }
        finally
        {
            foreach (string f in new[] { seeded, aceCopy, libredCopy })
                if (f.Length > 0) TemporaryDatabase.Delete(f);
        }
    }

    private static void Exec(OleDbConnection conn, string sql)
    {
        using OleDbCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static byte Type(byte[] f, int p) => (p + 1) * PageSize <= f.Length ? f[p * PageSize] : (byte)0xFF;

    private static bool Owned(byte[] f, int p, int tdef) =>
        (p + 1) * PageSize <= f.Length && f[p * PageSize] is 0x03 or 0x04
        && BinaryPrimitives.ReadInt32LittleEndian(f.AsSpan(p * PageSize + 4, 4)) == tdef;

    private static int Free(byte[] f, int p) => (p + 1) * PageSize <= f.Length
        ? BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(p * PageSize + 2, 2)) : -1;

    private static int Prefix(byte[] f, int p) => (p + 1) * PageSize <= f.Length
        ? BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(p * PageSize + 0x18, 2)) : -1;

    private static bool Same(byte[] x, byte[] y, int p) =>
        (p + 1) * PageSize <= Math.Min(x.Length, y.Length)
        && x.AsSpan(p * PageSize, PageSize).SequenceEqual(y.AsSpan(p * PageSize, PageSize));

    private static IEnumerable<int> Union(HashSet<int> x, HashSet<int> y) => x.Union(y);

    private static int FirstDiff(byte[] x, byte[] y, int p)
    {
        int limit = Math.Min(Math.Min(x.Length, y.Length) - p * PageSize, PageSize);
        for (int i = 0; i < limit; i++)
            if (x[p * PageSize + i] != y[p * PageSize + i]) return i;
        return -1;
    }

    private static HashSet<int> Changed(byte[] left, byte[] right)
    {
        var changed = new HashSet<int>();
        int pages = Math.Min(left.Length, right.Length) / PageSize;
        for (int p = 0; p < pages; p++)
            if (!left.AsSpan(p * PageSize, PageSize).SequenceEqual(right.AsSpan(p * PageSize, PageSize)))
                changed.Add(p);
        for (int p = pages; p < Math.Max(left.Length, right.Length) / PageSize; p++) changed.Add(p);
        return changed;
    }
}
