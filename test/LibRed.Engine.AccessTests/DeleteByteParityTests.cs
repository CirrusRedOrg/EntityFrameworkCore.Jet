using System.Reflection;
using LibRed;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A delete writes the bytes ACE's delete writes. Two engines delete the same row from two copies of the
/// same file and the copies are compared page by page.
/// </summary>
/// <remarks>
/// ACE writes to any file it opens, so its own housekeeping has to be separated from its delete: a third
/// copy is opened and closed through DAO without deleting anything, and the pages that alone changes are
/// ACE's bookkeeping, not part of the comparison. What is asserted is the two things that were measured to
/// hold — every page both engines wrote came out identical, and LibRed wrote no page ACE left alone.
/// </remarks>
[Collection(AceCollection.Name)]
public class DeleteByteParityTests(ITestOutputHelper output)
{
    private const int PageSize = 4096;

    [Fact]
    public void A_delete_writes_the_same_bytes_ace_writes()
    {
        object? engine = CreateDbEngine();
        if (engine is null) { output.WriteLine("Skipped: DAO unavailable."); return; }

        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string orig = TemporaryDatabase.CopyPath(northwind, "delparity-orig-");
        string aceCopy = TemporaryDatabase.CopyPath(northwind, "delparity-ace-");
        string noiseCopy = TemporaryDatabase.CopyPath(northwind, "delparity-noise-");
        string libredCopy = TemporaryDatabase.CopyPath(northwind, "delparity-libred-");
        try
        {
            const string Table = "Order Details";
            const string Where = "OrderID = 10248 AND ProductID = 11";

            object ace = Invoke(engine, "OpenDatabase", aceCopy)!;
            try
            {
                object rs = Invoke(ace, "OpenRecordset", $"SELECT * FROM [{Table}] WHERE {Where}")!;
                try { Invoke(rs, "Delete"); }
                finally { Invoke(rs, "Close"); }
            }
            finally { Invoke(ace, "Close"); }

            object quiet = Invoke(engine, "OpenDatabase", noiseCopy)!;
            try
            {
                object rs = Invoke(quiet, "OpenRecordset", $"SELECT * FROM [{Table}]")!;
                Invoke(rs, "Close");
            }
            finally { Invoke(quiet, "Close"); }

            using (var db = JetDatabase.Open(libredCopy, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery($"DELETE FROM [{Table}] WHERE {Where}");

            byte[] o = File.ReadAllBytes(orig), a = File.ReadAllBytes(aceCopy),
                   n = File.ReadAllBytes(noiseCopy), l = File.ReadAllBytes(libredCopy);

            HashSet<int> aceWrote = Changed(o, a), housekeeping = Changed(o, n), libredWrote = Changed(o, l);
            var aceDelete = aceWrote.Except(housekeeping).ToHashSet();

            // Nothing LibRed touched is a page ACE left alone.
            int[] extra = [.. libredWrote.Except(aceDelete).Order()];
            Assert.True(extra.Length == 0, $"LibRed wrote pages ACE did not: {string.Join(", ", extra)}");

            // And every page it did touch came out byte for byte the same.
            int[] both = [.. aceDelete.Intersect(libredWrote).Order()];
            Assert.NotEmpty(both);
            foreach (int page in both)
                Assert.True(
                    a.AsSpan(page * PageSize, PageSize).SequenceEqual(l.AsSpan(page * PageSize, PageSize)),
                    $"page {page} (type 0x{o[page * PageSize]:X2}) differs from ACE's");

            output.WriteLine($"{both.Length} pages written by both engines, all identical");
        }
        finally
        {
            foreach (string f in new[] { orig, aceCopy, noiseCopy, libredCopy }) TemporaryDatabase.Delete(f);
        }
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

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);

    private static object? CreateDbEngine()
    {
        AceTestDatabase.ReleaseAbandonedComObjects();
        foreach (int n in new[] { 170, 160, 150, 140, 130, 120 })
        {
            Type? type = Type.GetTypeFromProgID($"DAO.DBEngine.{n}");
            if (type is null) continue;
            try { return Activator.CreateInstance(type); }
            catch (Exception) { /* registered but not instantiable in this bitness */ }
        }
        return null;
    }
}
