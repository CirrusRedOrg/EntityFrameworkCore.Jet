using Xunit;

namespace LibRed.Engine.Tests;

// Deleting several rows from one page has to leave every surviving row readable.
//
// Reclaiming a deleted row's space (RowInserter.ReclaimRow) closes the gap by sliding the rows below it up
// and turning the emptied slot into a zero-length tombstone. The first version decided which slots to move
// by comparing offsets, which leaves behind a tombstone sitting at exactly the deleted row's offset — it
// then absorbs that row's length and starves the next live row to zero, so a later scan fails with "Row is
// too short to be an inline record". Slot offsets are non-increasing with slot index, so the rows below are
// simply the LATER slots; moving by index is what makes tombstones travel with them.
//
// Single deletes could not catch it: the two rules agree until a tombstone is already on the page.
public class DeleteCompactionTests
{
    [Theory]
    [InlineData("Id = 1", new[] { 2, 3, 4 })]
    [InlineData("Id = 2", new[] { 1, 3, 4 })]
    [InlineData("Id = 4", new[] { 1, 2, 3 })]
    [InlineData("Id IN (1, 2)", new[] { 3, 4 })]
    [InlineData("Id IN (1, 3)", new[] { 2, 4 })]
    [InlineData("Id IN (2, 3)", new[] { 1, 4 })]
    [InlineData("Id IN (3, 4)", new[] { 1, 2 })]
    [InlineData("Id IN (1, 2, 3)", new[] { 4 })]
    [InlineData("Id IN (2, 3, 4)", new[] { 1 })]
    [InlineData("Id > 0", new int[0])]
    public void Survivors_stay_readable(string where, int[] remaining)
    {
        string path = TemporaryDatabase.CreatePath("delete-compaction-");
        File.Delete(path);
        try
        {
            LibRed.Data.LibRedConnection.CreateDatabase($"Data Source={path}");
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);
            engine.ExecuteNonQuery("CREATE TABLE `T` (`Id` LONG, `V` TEXT(20))");
            for (int i = 1; i <= 4; i++)
                engine.ExecuteNonQuery($"INSERT INTO `T` (`Id`, `V`) VALUES ({i}, 'value{i}')");

            engine.ExecuteNonQuery($"DELETE FROM `T` WHERE {where}");

            Assert.Equal(remaining, engine.ExecuteQuery("SELECT `Id` FROM `T`").Rows
                .Select(r => Convert.ToInt32(r[0])).OrderBy(x => x));

            // The values have to survive the move, not just the ids — compaction relocates the bytes.
            foreach (int id in remaining)
                Assert.Equal($"value{id}",
                    engine.ExecuteQuery($"SELECT `V` FROM `T` WHERE `Id` = {id}").Rows.Single()[0]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Deleting and re-inserting repeatedly must not run the page out of space.
    [Fact]
    public void Churn_reuses_the_reclaimed_space()
    {
        string path = TemporaryDatabase.CreatePath("delete-churn-");
        File.Delete(path);
        try
        {
            LibRed.Data.LibRedConnection.CreateDatabase($"Data Source={path}");
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);
            engine.ExecuteNonQuery("CREATE TABLE `T` (`Id` LONG, `V` TEXT(200))");

            for (int i = 0; i < 200; i++)
            {
                engine.ExecuteNonQuery($"INSERT INTO `T` (`Id`, `V`) VALUES ({i}, '{new string('x', 150)}')");
                engine.ExecuteNonQuery($"DELETE FROM `T` WHERE `Id` = {i}");
            }

            Assert.Empty(engine.ExecuteQuery("SELECT `Id` FROM `T`").Rows);
            engine.ExecuteNonQuery("INSERT INTO `T` (`Id`, `V`) VALUES (999, 'last')");
            Assert.Equal(999, Convert.ToInt32(
                engine.ExecuteQuery("SELECT `Id` FROM `T`").Rows.Single()[0]));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
