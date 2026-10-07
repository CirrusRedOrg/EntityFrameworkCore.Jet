using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class PublicTableWriteTests
{
    [Fact]
    public void Updates_and_deletes_keep_indexes_consistent_even_with_an_incomplete_change_mask()
    {
        string path = Path.Combine(Path.GetTempPath(), $"public-table-{Guid.NewGuid():N}.accdb");
        try
        {
            JetDatabase.Create(path);
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("T", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["Id"]);
            Table table = db.OpenTable("T");
            IndexDef index = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
            table.Insert([1]);
            table.Insert([2]);
            RowId id = table.Rows().WithIds().First(r => Equals(r.Values[0], 1)).Id;

            db.CreateTable("Other", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["Id"]);
            Table other = db.OpenTable("Other");
            Assert.Throws<InvalidDataException>(() => other.Update(id, [9]));
            Assert.Throws<InvalidDataException>(() => other.Delete(id));

            Assert.Throws<ConstraintViolationException>(() => table.Update(id, [2], new HashSet<int>()));
            Assert.Single(table.SeekRows(index, [1]));
            Assert.Single(table.SeekRows(index, [2]));

            table.Update(id, [3], new HashSet<int>());
            Assert.Empty(table.SeekRows(index, [1]));
            Assert.Single(table.SeekRows(index, [3]));
            table.Delete(id);
            Assert.Empty(table.SeekRows(index, [3]));
            Assert.Single(table.Rows());
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}