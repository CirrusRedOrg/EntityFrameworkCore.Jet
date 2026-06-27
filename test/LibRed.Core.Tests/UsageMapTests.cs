using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

public class UsageMapTests
{
    [Fact]
    public void Inline_usage_map_lists_owned_data_pages()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var pages = db.OpenTable("MSysObjects").UsageMap.DataPages().ToList();

        Assert.Equal([17, 274, 323], pages);
    }

    [Fact]
    public void Usage_map_excludes_stale_orphan_pages()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        // MSysNavPaneObjectIDs has an orphan page (stale owner stamp) that a naive
        // owner-scan would double-count. The real usage map excludes it, so the decoded
        // row count matches the TDEF's own count exactly.
        var table = db.OpenTable("MSysNavPaneObjectIDs");
        int tdefRows = db.ReadTableDefinition(table.Definition.DefinitionPage).RowCount;

        Assert.Equal(tdefRows, table.Rows().Count());
    }
}
