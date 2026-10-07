using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.IO;
using LibRed.Pages;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Which usage-map row CREATE TABLE gives each index and each long-value column. Rows 0 and 1 are the table's
/// own owned/free maps; the rest follow the order the STATEMENT declares them — a long-value column takes two
/// rows where its column is written, an index takes one where its constraint is written. An index's row is
/// therefore not a function of the table's shape, and the same table written two ways lays out differently.
/// </summary>
/// <remarks>
/// Both engines run the identical statement here, because the order is a property of the text: driving LibRed
/// through the Core API instead would have nothing to reproduce it from.
/// </remarks>
[Collection(AceCollection.Name)]
public class CreateTableUsageMapOrderAccessTests(ITestOutputHelper output)
{
    [Theory]
    // A constraint written before the long-value column takes the earlier row…
    [InlineData("inline primary key", "CREATE TABLE T (Id LONG PRIMARY KEY, M MEMO)")]
    [InlineData("named inline primary key", "CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, M MEMO)")]
    // An inline constraint IS the tie case — it sits at the same point in the list as the column it is written
    // on, and takes the row first. `CREATE TABLE T (Id LONG, CONSTRAINT pk PRIMARY KEY (Id), M MEMO)` would say
    // the same thing about a TABLE-level constraint, and ACE gives it row 2 there, which is what rules out
    // "table-level constraints come last". It is not a case here because LibRed's grammar does not accept a
    // table constraint with columns still to come — every columnDefinition must precede every tableConstraint.
    // …and one written after it takes the later row, which is the shape a migration emits.
    [InlineData("table constraint after the memo", "CREATE TABLE T (Id LONG, M MEMO, CONSTRAINT pk PRIMARY KEY (Id))")]
    [InlineData("memo column first", "CREATE TABLE T (M MEMO, Id LONG CONSTRAINT pk PRIMARY KEY)")]
    // Several of each, interleaved both ways round.
    [InlineData("two indexes then two memos",
        "CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, A LONG CONSTRAINT u UNIQUE, M MEMO, N MEMO)")]
    [InlineData("an index between two memos", "CREATE TABLE T (M MEMO, A LONG CONSTRAINT u UNIQUE, N MEMO)")]
    [InlineData("a foreign key after a memo",
        "CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, M MEMO, PId LONG CONSTRAINT fk REFERENCES T (Id))")]
    // Controls: nothing to interleave.
    [InlineData("memo only", "CREATE TABLE T (Id LONG, M MEMO)")]
    [InlineData("index only", "CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, A LONG)")]
    [InlineData("OLE rather than memo", "CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, B LONGBINARY)")]
    public void Usage_map_rows_follow_the_order_the_statement_declares_them(string label, string sql)
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string ace = TemporaryDatabase.CopyPath(fixture, "maporder-ace-");
        string libred = TemporaryDatabase.CopyPath(fixture, "maporder-lib-");
        try
        {
            using (var connection = AceTestDatabase.Open(ace))
            using (OleDbCommand command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }

            using (var db = JetDatabase.Open(libred, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(sql);

            string expected = MapLayout(ace), actual = MapLayout(libred);
            output.WriteLine($"{label}: ACE {expected} | LibRed {actual}");
            Assert.Equal(expected, actual);
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    /// <summary>Each real index's usage-map row by data-block ordinal, then each long-value column's
    /// owned/free rows by column id — the layout of the table's primary usage-map page.</summary>
    private static string MapLayout(string path)
    {
        using var channel = PageChannel.Open(path, readOnly: true);
        TableDefinition table = new JetCatalog(channel).FindTable("T")!;
        var definition = new TableDefinition();
        definition.Read(channel, table.DefinitionPage);

        var parts = new List<string>();
        foreach (IndexDef index in definition.Indexes.OrderBy(i => i.RealIndexOrdinal))
            parts.Add($"index{index.RealIndexOrdinal}=row{index.UsageMap.Row}");
        foreach ((int columnId, (int owned, int _)) in definition.LongValueOwnedMaps.OrderBy(e => e.Key))
            parts.Add($"column{columnId}=rows{owned}/{definition.LongValueFreeMaps[columnId].Item1}");
        return string.Join(" ", parts);
    }
}