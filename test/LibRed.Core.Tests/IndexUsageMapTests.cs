using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Each index carries its own usage map (index-data block <c>+0x22</c>) recording the pages of its B-tree.
/// Access marks the root at CREATE and adds every page a split allocates, so the map covers the whole tree.
/// Verified against ACE: the union of a table's index maps equals exactly the set of index pages ACE itself
/// marks, and LibRed reproduces that.
/// </summary>
public class IndexUsageMapTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    /// <summary>Every index page (types 0x03/0x04) in the file owned by the table's TDEF, by owner stamp.</summary>
    private static SortedSet<int> IndexPagesOwnedByTable(string path, Table table, JetFormatBase format)
    {
        long pageCount = new FileInfo(path).Length / format.PageSize;
        var pages = new SortedSet<int>();
        for (int p = 1; p < pageCount; p++)
        {
            ReadOnlySpan<byte> span = table.Channel.ReadPage(p).Span;
            if (span[0] is not (0x03 or 0x04)) continue;
            if (BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0x04, 4)) == table.Definition.DefinitionPage)
                pages.Add(p);
        }
        return pages;
    }

    /// <summary>The union of every index's own usage map, walking the TDEF to each 52-byte index block.</summary>
    private static SortedSet<int> UnionOfIndexMaps(Table table, JetFormatBase format)
    {
        var tdef = table.Channel.ReadPage(table.Definition.DefinitionPage);
        int dataCount = tdef.ReadInt32(format.TdefIndexCountOffset);
        int colCount = tdef.ReadUInt16(format.TdefColumnCountOffset);

        int pos = format.TdefRealIndexBlockOffset + dataCount * format.RealIndexEntrySize
                  + colCount * format.ColumnDescriptorSize;
        for (int i = 0; i < colCount; i++) pos += 2 + tdef.ReadUInt16(pos);

        var union = new SortedSet<int>();
        for (int i = 0; i < dataCount; i++)
        {
            int block = pos + i * 52;
            int mapRow = tdef.ReadByte(block + 0x22);
            int mapPage = tdef.ReadInt24(block + 0x23);
            var holder = new DataPage();
            holder.Read(table.Channel.ReadPage(mapPage), format);
            union.UnionWith(BitsOf(holder.GetRow(mapRow)));
        }
        return union;
    }

    private static IEnumerable<int> BitsOf(ReadOnlySpan<byte> record)
    {
        // Only inline maps arise here (an index B-tree well under 2 GB); a reference map would need expanding.
        if (record[0] != 0x00) throw new InvalidOperationException($"Unexpected usage-map type 0x{record[0]:X2}.");
        int start = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(1, 4));
        var pages = new List<int>();
        for (int k = 5; k < record.Length; k++)
            for (int b = 0; b < 8; b++)
                if ((record[k] & (1 << b)) != 0)
                    pages.Add(start + (k - 5) * 8 + b);
        return pages;
    }

    [Fact]
    public void An_index_usage_map_covers_every_btree_page_after_splits()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "idxmap-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("T",
                [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new("V", JetDataType.Int32, 4, IsFixedLength: true)],
                primaryKey: ["Id"]);
            db.CreateIndex("T", "IX_V", [("V", false)]);
            var table = db.OpenTable("T");

            var row = new object?[2];
            for (int i = 0; i < 4000; i++) // enough to split both B-trees several levels
            {
                row[0] = i;
                row[1] = i * 7 % 4000; // scatter V so its index isn't a degenerate append
                table.Insert(row);
            }

            SortedSet<int> actual = IndexPagesOwnedByTable(path, table, db.Format);
            SortedSet<int> mapped = UnionOfIndexMaps(table, db.Format);

            Assert.True(actual.Count > 2, "expected the B-trees to have split beyond their creation roots");
            Assert.Equal(actual, mapped); // no page missing, none spurious
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Index_map_coverage_matches_what_access_itself_marks()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "idxmap-ace-");
        try
        {
            using (var connection = OpenOleDb(path))
            {
                using (var c = connection.CreateCommand()) { c.CommandText = "CREATE TABLE T (Id LONG CONSTRAINT PK PRIMARY KEY, V LONG)"; c.ExecuteNonQuery(); }
                using (var c = connection.CreateCommand()) { c.CommandText = "CREATE INDEX IX_V ON T (V)"; c.ExecuteNonQuery(); }
                using var ins = connection.CreateCommand();
                ins.CommandText = "INSERT INTO T (Id, V) VALUES (?, ?)";
                var id = ins.CreateParameter(); ins.Parameters.Add(id);
                var v = ins.CreateParameter(); ins.Parameters.Add(v);
                for (int i = 0; i < 4000; i++) { id.Value = i; v.Value = i * 7 % 4000; ins.ExecuteNonQuery(); }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("T");

            // Access's own map must cover exactly the index pages it wrote — the invariant LibRed reproduces.
            Assert.Equal(IndexPagesOwnedByTable(path, table, db.Format), UnionOfIndexMaps(table, db.Format));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
