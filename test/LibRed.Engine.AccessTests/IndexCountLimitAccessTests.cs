using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Where the Jet/ACE "32 indexes per table" limit actually bites, measured against ACE rather than assumed.
//
// A TDEF carries two counts: index-DATA blocks at 0x33 (a real B-tree each) and logical index-INFO blocks
// at 0x2F. They are not the same number. An incoming relationship - one where THIS table is the referenced
// end - adds a logical block that reuses an existing data block, so a table many others point at gains
// logical blocks without gaining data blocks. EF Core's ComplexNavigationsSharedType model drove Level1 to
// 33 logical against only 18 data, and the resulting file is unreadable by Access: -1206 "Unrecognized
// database format", the table missing from the object list, and nothing anywhere naming indexes as the
// cause. LibRed read the same file back without complaint, because the only count it validated was 0x33.
//
// This pins the boundary from below: a table sitting at exactly 32 logical blocks must be readable by ACE.
// If that ever fails, the real limit is lower than 32 and the write-side cap is wrong.
[Collection(AceCollection.Name)]
public class IndexCountLimitAccessTests : TempDatabaseTest
{
    /// <summary>A parent with a primary key plus <paramref name="incoming"/> child tables referencing it, so
    /// the parent ends with 1 + <paramref name="incoming"/> logical index blocks and exactly one data block.</summary>
    private static void BuildHub(QueryEngine engine, int incoming)
    {
        engine.ExecuteNonQuery("CREATE TABLE Hub (Id LONG PRIMARY KEY)");
        for (int i = 0; i < incoming; i++)
        {
            engine.ExecuteNonQuery($"CREATE TABLE Child{i} (Id LONG PRIMARY KEY, HubId LONG REFERENCES Hub (Id))");
        }
    }

    [Fact]
    public void Ace_reads_a_table_with_thirty_two_logical_index_blocks()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "idxcap-");

        // 1 primary key + 31 incoming relationships = 32 logical blocks, against a single data block.
        using (var db = TemporaryDatabase.OpenTracked(path, readOnly: false))
        {
            BuildHub(new QueryEngine(db), incoming: 31);

            var tdef = db.ReadTableDefinition(db.Catalog.FindTable("Hub")!.DefinitionPage);
            Assert.Equal(32, tdef.LogicalIndexCount); // 0x2F - at the limit
            Assert.Equal(1, tdef.IndexCount);      // 0x33 - nowhere near it
        }

        // The question this test exists to answer: does the real engine accept it?
        using var connection = AceTestDatabase.Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Hub";
        Assert.Equal(0, Convert.ToInt32(command.ExecuteScalar()));
    }

    // The other side of the boundary. This was originally an ACE cross-check: with the cap absent, LibRed
    // would build the 33-block table and ACE would then refuse to read it - the same single-page,
    // one-data-block shape as above with one more incoming relationship, which isolated the logical count as
    // the cause (Level1 in the ComplexNavigationsSharedType file carried a continuation page as well, so it
    // could not settle that on its own). Those measurements are recorded in page-02d-constraints.md.
    //
    // LibRed now refuses to build it, so the artefact can no longer be produced and ACE cannot be asked. What
    // remains is the guard that matters: the incremental path must reject the 33rd, because nothing
    // downstream will. This is the relationship shape, where 0x33 stays legal throughout.
    [Fact]
    public void LibRed_refuses_a_thirty_third_incoming_relationship()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "idxcap33-");

        using var db = TemporaryDatabase.OpenTracked(path, readOnly: false);
        var engine = new QueryEngine(db);

        var thrown = Assert.Throws<NotSupportedException>(() => BuildHub(engine, incoming: 32));
        Assert.Contains("33 logical index blocks", thrown.Message);

        // The table it refused on is still readable, and still at the limit rather than over it: the check
        // runs before the TDEF is touched, so a rejection leaves the file exactly as it was.
        var tdef = db.ReadTableDefinition(db.Catalog.FindTable("Hub")!.DefinitionPage);
        Assert.Equal(32, tdef.LogicalIndexCount);
        Assert.Equal(1, tdef.IndexCount);
    }

    // The same limit reached the other way: plain indexes, no relationships anywhere. Microsoft documents the
    // rule as "Number of indexes in a table: 32, including indexes created internally to maintain table
    // relationships, single-field and composite indexes" - so one budget covers both kinds, which is what
    // these two shapes together demonstrate.
    //
    // Note this canNOT isolate the data-block count (0x33) the way the pair above isolates the logical count.
    // Every data block needs a logical block to name it and point at it, so data <= logical always holds and
    // 33 plain indexes push BOTH counts to 33 at once. The data cap is structurally unreachable on its own,
    // which is why the logical count is the one worth enforcing: capping it caps the other by construction.
    [Fact]
    public void LibRed_refuses_a_thirty_third_plain_index()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "idxplain33-");

        const int limit = 32;

        using var db = TemporaryDatabase.OpenTracked(path, readOnly: false);
        var engine = new QueryEngine(db);
        engine.ExecuteNonQuery(
            "CREATE TABLE Wide (" + string.Join(", ", Enumerable.Range(0, limit + 1).Select(i => $"C{i} LONG")) + ")");

        for (int i = 0; i < limit; i++)
        {
            engine.ExecuteNonQuery($"CREATE INDEX IX{i} ON Wide (C{i})");
        }

        var thrown = Assert.Throws<NotSupportedException>(
            () => engine.ExecuteNonQuery($"CREATE INDEX IX{limit} ON Wide (C{limit})"));
        Assert.Contains("33 index-data blocks", thrown.Message);

        // Both counts move together in this shape - a plain index is one data block and one logical block -
        // so it reaches the cap on 0x33 and 0x2F at the same moment, unlike the relationship shape above.
        var tdef = db.ReadTableDefinition(db.Catalog.FindTable("Wide")!.DefinitionPage);
        Assert.Equal(limit, tdef.IndexCount);
        Assert.Equal(limit, tdef.LogicalIndexCount);
    }
}
