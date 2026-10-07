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

            int pageSize;
            using (var db = JetDatabase.Open(libredCopy, readOnly: false))
            {
                pageSize = db.Format.PageSize;
                new QueryEngine(db).ExecuteNonQuery($"DELETE FROM [{Table}] WHERE {Where}");
            }

            byte[] o = File.ReadAllBytes(orig), a = File.ReadAllBytes(aceCopy),
                   n = File.ReadAllBytes(noiseCopy), l = File.ReadAllBytes(libredCopy);

            HashSet<int> aceWrote = Changed(o, a, pageSize), housekeeping = Changed(o, n, pageSize), libredWrote = Changed(o, l, pageSize);
            var aceDelete = aceWrote.Except(housekeeping).ToHashSet();

            // Nothing LibRed touched is a page ACE left alone.
            int[] extra = [.. libredWrote.Except(aceDelete).Order()];
            Assert.True(extra.Length == 0, $"LibRed wrote pages ACE did not: {string.Join(", ", extra)}");

            // And every page it did touch came out byte for byte the same.
            int[] both = [.. aceDelete.Intersect(libredWrote).Order()];
            Assert.NotEmpty(both);
            foreach (int page in both)
                Assert.True(
                    a.AsSpan(page * pageSize, pageSize).SequenceEqual(l.AsSpan(page * pageSize, pageSize)),
                    $"page {page} (type 0x{o[page * pageSize]:X2}) differs from ACE's");

            output.WriteLine($"{both.Length} pages written by both engines, all identical");
        }
        finally
        {
            foreach (string f in new[] { orig, aceCopy, noiseCopy, libredCopy }) TemporaryDatabase.Delete(f);
        }
    }

    // A delete that gives space back to a page which had none. The page is the first of many, so it was full
    // and its bit in the table's free-pages map had been cleared; freeing space in it is what puts the bit back,
    // and a page whose bit stays clear is space no insert will ever use again. Both directions are asserted
    // here: ACE writing a page LibRed leaves alone is exactly the shape of that leak.
    [Fact]
    public void A_delete_that_frees_space_in_a_full_page_writes_what_ace_writes()
    {
        object? engine = CreateDbEngine();
        if (engine is null) { output.WriteLine("Skipped: DAO unavailable."); return; }

        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string orig = TemporaryDatabase.CopyPath(northwind, "delfree-orig-");
        try
        {
            using (var db = JetDatabase.Open(orig, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE TABLE Filled (Id LONG CONSTRAINT pk PRIMARY KEY, V TEXT(200))");
                for (int i = 0; i < 200; i++)
                    e.ExecuteNonQuery($"INSERT INTO Filled (Id, V) VALUES ({i}, '{new string((char)('a' + i % 26), 200)}')");
            }

            string aceCopy = TemporaryDatabase.CopyPath(orig, "delfree-ace-");
            string noiseCopy = TemporaryDatabase.CopyPath(orig, "delfree-noise-");
            string libredCopy = TemporaryDatabase.CopyPath(orig, "delfree-libred-");

            object ace = Invoke(engine, "OpenDatabase", aceCopy)!;
            try
            {
                object rs = Invoke(ace, "OpenRecordset", "SELECT * FROM Filled WHERE Id = 0")!;
                try { Invoke(rs, "Delete"); }
                finally { Invoke(rs, "Close"); }
            }
            finally { Invoke(ace, "Close"); }

            object quiet = Invoke(engine, "OpenDatabase", noiseCopy)!;
            try
            {
                object rs = Invoke(quiet, "OpenRecordset", "SELECT * FROM Filled")!;
                Invoke(rs, "Close");
            }
            finally { Invoke(quiet, "Close"); }

            int pageSize;
            using (var db = JetDatabase.Open(libredCopy, readOnly: false))
            {
                pageSize = db.Format.PageSize;
                new QueryEngine(db).ExecuteNonQuery("DELETE FROM Filled WHERE Id = 0");
            }

            byte[] o = File.ReadAllBytes(orig), a = File.ReadAllBytes(aceCopy),
                   n = File.ReadAllBytes(noiseCopy), l = File.ReadAllBytes(libredCopy);
            // Page 0 is left out: its modification counter moves for reasons that have nothing to do with the
            // delete, which is why the whole-file comparisons skip it too.
            var aceDelete = Changed(o, a, pageSize).Except(Changed(o, n, pageSize)).Where(p => p != 0).ToHashSet();
            var libredWrote = Changed(o, l, pageSize).Where(p => p != 0).ToHashSet();

            output.WriteLine($"ACE wrote [{string.Join(",", aceDelete.Order())}], "
                             + $"LibRed wrote [{string.Join(",", libredWrote.Order())}]");
            Assert.Empty(libredWrote.Except(aceDelete).Order());
            Assert.Empty(aceDelete.Except(libredWrote).Order());
            foreach (int page in aceDelete)
                Assert.True(
                    a.AsSpan(page * pageSize, pageSize).SequenceEqual(l.AsSpan(page * pageSize, pageSize)),
                    $"page {page} (type 0x{o[page * pageSize]:X2}) differs from ACE's");
        }
        finally { TemporaryDatabase.Delete(orig); }
    }

    private static HashSet<int> Changed(byte[] left, byte[] right, int pageSize)
    {
        var changed = new HashSet<int>();
        int pages = Math.Min(left.Length, right.Length) / pageSize;
        for (int p = 0; p < pages; p++)
            if (!left.AsSpan(p * pageSize, pageSize).SequenceEqual(right.AsSpan(p * pageSize, pageSize)))
                changed.Add(p);
        for (int p = pages; p < Math.Max(left.Length, right.Length) / pageSize; p++) changed.Add(p);
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