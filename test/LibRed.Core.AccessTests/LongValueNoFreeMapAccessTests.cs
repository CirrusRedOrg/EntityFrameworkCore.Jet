using System.Data.OleDb;
using LibRed.IO;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Retiring a long value from a column that has an owned-pages map and no free-pages map (a free pointer of page 0,
/// as Access writes <c>MSysNameMap.NameMap</c>) leaves the same file through LibRed as through ACE
/// (docs/format/long-values.md). A single-page value's emptied page is released but stays owned and allocated; a
/// chained value's pages are freed. The values go in through LibRed, which is how such a column gets them; then one
/// is deleted through each engine on twin copies, and every page but page 0 — whose commit counter ACE advances —
/// must match.
/// </summary>
[Collection(AceCollection.Name)]
public class LongValueNoFreeMapAccessTests
{
    [Theory]
    [InlineData(300)]
    [InlineData(6000)]
    public void Deleting_a_value_leaves_the_maps_as_ace_does(int length)
    {
        string ace = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "lv-nofree-ace-");
        string libred = ace.Replace(".accdb", "-libred.accdb", StringComparison.OrdinalIgnoreCase);
        try
        {
            Execute(ace, "CREATE TABLE L (Id LONG PRIMARY KEY, M LONGTEXT)");
            DropFreeMap(ace, "L", "M");
            using (var db = JetDatabase.Open(ace, readOnly: false))
            {
                var table = db.OpenTable("L");
                for (int id = 1; id <= 3; id++) table.Insert([id, new string((char)('a' + id), length)]);
            }
            File.Copy(ace, libred);

            Execute(ace, "DELETE FROM L WHERE Id = 2");
            using (var db = JetDatabase.Open(libred, readOnly: false))
            {
                var table = db.OpenTable("L");
                table.Delete(table.RowsWhere([0], v => (int)v[0]! == 2).Single().Id);
            }

            byte[] expected = File.ReadAllBytes(ace), actual = File.ReadAllBytes(libred);
            Assert.Equal(expected.Length, actual.Length);
            const int pageSize = 4096;
            for (int page = 1; page < expected.Length / pageSize; page++)
                Assert.True(expected.AsSpan(page * pageSize, pageSize).SequenceEqual(actual.AsSpan(page * pageSize, pageSize)),
                    $"page {page} differs from ACE's");
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    private static void Execute(string path, string sql)
    {
        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Zeroes the free pointer of the column's §3.3.2 entry, found on the TDEF page by its own bytes.</summary>
    private static void DropFreeMap(string path, string tableName, string columnName)
    {
        int definitionPage, pageSize, freeOffset;
        byte[] entry;
        using (var db = JetDatabase.Open(path, readOnly: true))
        {
            var table = db.OpenTable(tableName);
            var format = table.Channel.Format;
            int id = table.Definition.RequireColumn(columnName).ColumnId;
            (int Row, int Page) owned = table.Definition.LongValueOwnedMaps[id], free = table.Definition.LongValueFreeMaps[id];
            entry = new byte[format.TdefLongValueMapEntrySize];
            BitConverter.GetBytes((ushort)id).CopyTo(entry, 0);
            PageBuffer.WriteRecordPointer(entry, format.TdefLongValueMapOwnedOffset, owned.Row, owned.Page);
            PageBuffer.WriteRecordPointer(entry, format.TdefLongValueMapFreeOffset, free.Row, free.Page);
            pageSize = format.PageSize;
            definitionPage = table.Definition.DefinitionPage * pageSize;
            freeOffset = format.TdefLongValueMapFreeOffset;
        }
        byte[] file = File.ReadAllBytes(path);
        int at = file.AsSpan(definitionPage, pageSize).IndexOf(entry);
        Assert.True(at >= 0, "the column's long-value map entry is not on its TDEF page");
        file.AsSpan(definitionPage + at + freeOffset, 4).Clear();
        File.WriteAllBytes(path, file);
    }
}
