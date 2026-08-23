using LibRed.Data;
using Xunit;

namespace LibRed.Ado.Tests;

// The ADO batch splitter drops fragments that hold no statement. EF Core sends
// migrationBuilder.Sql("--Before") as a whole command, and a trailing comment must not be treated as the
// batch's last statement — ExecuteBatch reports the LAST statement's result, so a trailing `-- done` would
// otherwise mask the real statement's rows-affected.
public class CommentOnlyBatchTests
{
    private static LibRedConnection OpenTemp()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "cmtb-");
        var c = new LibRedConnection($"Data Source={path}");
        c.Open();
        Exec(c, "CREATE TABLE `T` (`Id` LONG NOT NULL PRIMARY KEY)");
        return c;
    }

    private static void Exec(LibRedConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static int NonQuery(LibRedConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteNonQuery();
    }

    // What migrationBuilder.Sql("--Before") produces.
    [Fact]
    public void A_command_that_is_only_a_comment_succeeds()
    {
        using LibRedConnection c = OpenTemp();
        Assert.Equal(0, NonQuery(c, "--Before"));
    }

    // The regression the skip exists to prevent: the insert's count must survive a trailing comment.
    [Fact]
    public void A_trailing_comment_does_not_mask_rows_affected()
    {
        using LibRedConnection c = OpenTemp();
        Assert.Equal(1, NonQuery(c, "INSERT INTO `T` (`Id`) VALUES (1); -- done"));
        Assert.Equal(1, NonQuery(c, "INSERT INTO `T` (`Id`) VALUES (2);\r\n-- trailing note\r\n"));
    }

    // ... and @@ROWCOUNT, which EF reads back through a second statement in the same batch, must agree.
    [Fact]
    public void Rowcount_survives_a_trailing_comment()
    {
        using LibRedConnection c = OpenTemp();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO `T` (`Id`) VALUES (7); -- note\r\nSELECT @@ROWCOUNT;";
        Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));
    }

    // A comment between two statements is skipped without disturbing either.
    [Fact]
    public void A_comment_between_statements_is_skipped()
    {
        using LibRedConnection c = OpenTemp();
        Assert.Equal(1, NonQuery(c,
            "INSERT INTO `T` (`Id`) VALUES (3);\r\n-- in between\r\nINSERT INTO `T` (`Id`) VALUES (4);"));

        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM `T`";
        Assert.Equal(2, Convert.ToInt32(cmd.ExecuteScalar()));
    }
}
