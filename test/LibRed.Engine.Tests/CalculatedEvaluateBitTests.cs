using LibRed.Formats;
using Xunit;

namespace LibRed.Engine.Tests;

// A calculated column whose descriptor lacks the evaluate bit (extended flag 0x80) is one ACE knows as calculated but
// will not evaluate. Measured by clearing the bit on DAO-created columns: every read of the column fails as an invalid
// expression; a value result is still computed and stored on INSERT and on an UPDATE of a column it reads, while a Text
// or Memo one refuses both; an UPDATE that touches none of its inputs, and a DELETE, go through.
public class CalculatedEvaluateBitTests : TempDatabaseTest
{
    private static string Create(string column)
    {
        string path = TemporaryDatabase.CreatePath("calc-evalbit-");
        JetDatabase.Create(path);
        using (var db = JetDatabase.Open(path, readOnly: false))
        {
            var engine = new QueryEngine(db);
            engine.ExecuteNonQuery($"CREATE TABLE T (Id LONG, Qty LONG, A TEXT(20), Price CURRENCY, X {column})");
            engine.ExecuteNonQuery("INSERT INTO T (Id, Qty, A, Price) VALUES (1, 7, 'a1', 1)");
        }
        SetEvaluateBit(path, on: false);
        return path;
    }

    private static void SetEvaluateBit(string path, bool on)
    {
        int page, columnId;
        using (var db = JetDatabase.Open(path))
        {
            var t = db.Catalog.FindTable("T")!;
            page = t.DefinitionPage;
            columnId = t.FindColumn("X")!.ColumnId;
        }
        byte[] file = File.ReadAllBytes(path);
        JetFormatBase format = JetFormatBase.FromVersionByte(file[0x14]);
        int tdef = page * format.PageSize;
        int first = tdef + format.TdefRealIndexBlockOffset
            + BitConverter.ToInt32(file, tdef + format.TdefIndexCountOffset) * format.RealIndexEntrySize;
        int descriptor = Enumerable.Range(0, BitConverter.ToUInt16(file, tdef + format.TdefColumnCountOffset))
            .Select(i => first + i * format.ColumnDescriptorSize)
            .Single(d => BitConverter.ToUInt16(file, d + 5) == columnId);
        int offset = descriptor + format.ColumnExtendedFlagsOffset;
        file[offset] = on ? (byte)(file[offset] | 0x80) : (byte)(file[offset] & 0x7F);
        File.WriteAllBytes(path, file);
    }

    [Fact]
    public void A_text_result_refuses_to_be_computed_but_the_row_can_still_be_updated_and_deleted()
    {
        string path = Create("TEXT(40) AS ([A] & '-x')");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            Assert.ThrowsAny<Exception>(() => engine.ExecuteQuery("SELECT X FROM T").Rows.ToList());
            Assert.ThrowsAny<Exception>(() => engine.ExecuteNonQuery("INSERT INTO T (Id, Qty, A, Price) VALUES (2, 5, 'a2', 1)"));
            Assert.ThrowsAny<Exception>(() => engine.ExecuteNonQuery("UPDATE T SET A = 'b1' WHERE Id = 1"));
            Assert.Equal(1, engine.ExecuteNonQuery("UPDATE T SET Price = 3 WHERE Id = 1"));
            Assert.Equal(3m, engine.ExecuteQuery("SELECT Price FROM T").Rows.Single()[0]);
            Assert.Equal(1, engine.ExecuteNonQuery("DELETE FROM T WHERE Id = 1"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ACE caches a failed expression as its VBA error and then refuses the row on read, but an UPDATE of a column the
    // expression reads recomputes it — the one repair there is (measured in CalculatedColumnAccessTests). The update
    // never reads the cached result, so LibRed can make the same repair.
    [Fact]
    public void A_row_whose_result_is_a_cached_error_can_still_be_updated()
    {
        string path = TemporaryDatabase.CreatePath("calc-cachederr-");
        try
        {
            JetDatabase.Create(path);
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(db);
                engine.ExecuteNonQuery("CREATE TABLE T (Id LONG, Qty LONG, Price CURRENCY, X LONG AS ([Qty]*2))");
                engine.ExecuteNonQuery("INSERT INTO T (Id, Qty, Price) VALUES (1, 7, 1)");
            }

            // Turn the stored 14 into a cached error 94 ("invalid use of Null"): the status is the envelope's first field.
            byte[] file = File.ReadAllBytes(path);
            byte[] envelope = [.. new byte[16], 4, 0, 0, 0, 14, 0, 0, 0];
            int at = file.AsSpan().IndexOf(envelope);
            Assert.True(at >= 0);
            file[at] = 94;
            File.WriteAllBytes(path, file);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(db);
                Assert.ThrowsAny<Exception>(() => engine.ExecuteQuery("SELECT X FROM T").Rows.ToList());
                Assert.Equal(1, engine.ExecuteNonQuery("UPDATE T SET Price = 2"));
                Assert.Equal(1, engine.ExecuteNonQuery("UPDATE T SET Qty = 9"));
                Assert.Equal(18, Convert.ToInt32(engine.ExecuteQuery("SELECT X FROM T").Rows.Single()[0]));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_value_result_is_still_computed_and_stored_but_not_read()
    {
        string path = Create("LONG AS ([Qty]*2)");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(db);
                Assert.ThrowsAny<Exception>(() => engine.ExecuteQuery("SELECT X FROM T").Rows.ToList());
                Assert.Equal(1, engine.ExecuteNonQuery("INSERT INTO T (Id, Qty, A, Price) VALUES (2, 5, 'a2', 1)"));
                Assert.Equal(1, engine.ExecuteNonQuery("UPDATE T SET Qty = 9 WHERE Id = 1"));
            }

            // What was stored is the computed value, as ACE stores it: put the bit back and read it.
            SetEvaluateBit(path, on: true);
            using var read = JetDatabase.Open(path);
            Assert.Equal([18, 10], new QueryEngine(read).ExecuteQuery("SELECT X FROM T ORDER BY Id").Rows.Select(r => Convert.ToInt32(r[0])));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
