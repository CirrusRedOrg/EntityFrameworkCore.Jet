using System.Data.OleDb;
using System.Text;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Rebuilding an index (an <c>ALTER COLUMN</c> on an indexed column) gives it a new owned-pages usage-map
/// row, in the two writes described in <c>docs/format/page-05-usage-maps.md</c> §9: a fresh row appended and
/// stamped with the new root's bit, whose bytes are then abandoned, and a re-lay that reclaims the old
/// record. This compares the whole file against ACE's own answer, which is the only measurement that sees
/// both halves.
/// </summary>
/// <remarks>
/// Each half was got wrong once, in a way weaker measurements could not catch. The abandoned copy decides
/// the diff on a single byte and lies inside the region free space already covers, so free-space accounting
/// says nothing about it. The re-lay only shows up when the recycled row is <b>not the last</b> — which an
/// ACE-built schema never produces, because a long-value column declared in <c>CREATE TABLE</c> takes its map
/// rows before the index's. Adding the Memo column afterwards is what puts the index's row in the middle, so
/// the last shape below is the one that matters and the first three are the shapes that were already covered.
/// </remarks>
public class OwnedMapRecycleAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    private const int PageSize = 4096;

    [Theory]
    [InlineData("one index", "CREATE TABLE T ( A LONG, B LONG );CREATE INDEX ixB ON T (B)",
        "INSERT INTO T (A,B) VALUES (11,22)", "")]
    [InlineData("two indexes on the target", "CREATE TABLE T ( A LONG, B LONG );CREATE INDEX ix1 ON T (B);CREATE INDEX ix2 ON T (B)",
        "INSERT INTO T (A,B) VALUES (11,22)", "")]
    [InlineData("memo declared with the table", "CREATE TABLE T ( A LONG, M MEMO, B LONG );CREATE INDEX ixB ON T (B)",
        "INSERT INTO T (A,M,B) VALUES (11,'hi',22)", "")]
    // The index's map row is no longer last: its record is reclaimed and the long-value maps slide up.
    [InlineData("memo added after the index", "CREATE TABLE T ( A LONG, B LONG );CREATE INDEX ixB ON T (B)",
        "INSERT INTO T (A,B) VALUES (11,22)", "ALTER TABLE T ADD COLUMN M LONGTEXT")]
    public void Libred_rebuilds_the_index_byte_for_byte_with_ace(string label, string create, string insert, string then)
    {
        string start = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "recycle-start-");
        string ace = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "recycle-ace-");
        string libred = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "recycle-lib-");
        try
        {
            // ACE builds the schema, so both engines start from a file only ACE has written.
            using (OleDbConnection connection = AceTestDatabase.Open(start))
            {
                foreach (string statement in create.Split(';')) Exec(connection, statement);
                Exec(connection, insert);
                if (then.Length > 0) Exec(connection, then);
            }

            File.Copy(start, ace, overwrite: true);
            using (OleDbConnection connection = AceTestDatabase.Open(ace))
                Exec(connection, "ALTER TABLE T ALTER COLUMN B DOUBLE");

            File.Copy(start, libred, overwrite: true);
            using (var database = JetDatabase.Open(libred, readOnly: false))
                database.AlterColumn("T", "B", new ColumnSpec("B", JetDataType.Double, 8, IsFixedLength: true));

            output.WriteLine($"{label}: comparing {new FileInfo(ace).Length / PageSize} pages");
            Assert.Equal("", Difference(ace, libred));
        }
        finally
        {
            TemporaryDatabase.Delete(start);
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    /// <summary>Every differing byte of every page, except the two environmental spots: page 0 carries the
    /// database modification counter, and MSysObjects' data page (owner 2) carries the table's DateUpdate
    /// wall clock. What remains covers the TDEF, the data pages, the index B-tree, the usage maps and the
    /// global free map.</summary>
    private static string Difference(string acePath, string libredPath)
    {
        byte[] ace = File.ReadAllBytes(acePath), libred = File.ReadAllBytes(libredPath);
        var differences = new StringBuilder();
        int pages = Math.Max(ace.Length, libred.Length) / PageSize;
        for (int page = 1; page < pages; page++)
        {
            int at = page * PageSize;
            bool inAce = at + PageSize <= ace.Length, inLibRed = at + PageSize <= libred.Length;
            if (inAce && inLibRed && BitConverter.ToInt32(ace, at + 4) == 2) continue;
            if (!inAce || !inLibRed)
            {
                differences.AppendLine($"page {page}: present in {(inAce ? "ACE" : "LibRed")} only");
                continue;
            }
            for (int i = 0, shown = 0; i < PageSize && shown < 8; i++)
                if (ace[at + i] != libred[at + i])
                {
                    differences.AppendLine(
                        $"page {page} (type 0x{ace[at]:X2} owner {BitConverter.ToInt32(ace, at + 4)}) " +
                        $"+0x{i:X3}: ace={ace[at + i]:X2} libred={libred[at + i]:X2}");
                    shown++;
                }
        }
        return differences.ToString();
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
