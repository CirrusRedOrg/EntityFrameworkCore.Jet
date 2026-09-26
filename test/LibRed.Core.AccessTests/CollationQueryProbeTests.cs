using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Which collation ACE compares text in when a query compares it — the column's, or the database's.
//
// The two agree in every file Microsoft's tools write (page-02b §3.4: the per-column collation always equals
// page 0's), so a normal file cannot tell them apart. A stamped one can: CollationV1PatchProbeTests' Stamp
// writes a collation onto one column or onto page 0 alone. Croatian v1 against General v1 is the instrument —
// an order that reorders plain letters: 'č' is a letter after 'c' and 'lj' one after 'l', so General has
// 'ča' < 'cb' and 'lja' < 'lm' and Croatian the reverse.
//
// The answer is the DATABASE's, for every comparison a query makes — ORDER BY, GROUP BY, WHERE against a
// literal either way round, literal against literal, an expression, MIN/MAX — while the column's own
// collation goes only into its index keys. No index on the columns here, so ACE's comparison answers rather
// than an index walk.
[Collection(AceCollection.Name)]
public class CollationQueryProbeTests
{
    private static readonly Collation GeneralV1 = Collation.General;
    private static readonly Collation CroatianV1 = new(CollatingOrder.Croatian, Collation.GeneralVersion);

    private static readonly string[] Values = ["cb", "ča", "ca", "d", "lm", "lja", "l"];

    private static readonly string[] GeneralOrder = ["ca", "ča", "cb", "d", "l", "lja", "lm"];
    private static readonly string[] CroatianOrder = ["ca", "cb", "ča", "d", "l", "lm", "lja"];

    [Fact]
    public void A_column_stamped_croatian_in_a_general_database_compares_as_general()
    {
        Dictionary<string, string[]> ace = Probe(stampHeader: false, stampColumn: true);
        AssertComparesIn(ace, GeneralOrder, below: ["ča", "ca"], literal: "general", minMax: "ca / lm");
    }

    [Fact]
    public void A_general_column_in_a_database_stamped_croatian_compares_as_croatian()
    {
        Dictionary<string, string[]> ace = Probe(stampHeader: true, stampColumn: false);
        AssertComparesIn(ace, CroatianOrder, below: ["ca"], literal: "croatian", minMax: "ca / lja");
    }

    // GROUP BY takes values to be equal exactly where '=' does: in the collation, where 'ß' is 'ss', case folds and
    // trailing spaces go — but an accent still separates. Held in both General orders.
    [Theory]
    [InlineData(0)]
    [InlineData(Collation.GeneralVersion)]
    public void Group_by_folds_what_the_collation_folds(byte version)
    {
        string path = TemporaryDatabase.CreatePath("collation-group-", ".accdb");
        try
        {
            DatabaseCreator.CreateEmpty(path, collation: new Collation(CollatingOrder.General, version));
            using OleDbConnection ace = AceTestDatabase.Open(path);
            Exec(ace, "CREATE TABLE G (Id LONG, S TEXT(20))");
            string[] values = ["Straße", "STRASSE", "strasse ", "x", "X", "café", "cafe"];
            for (int i = 0; i < values.Length; i++)
            {
                using OleDbCommand insert = ace.CreateCommand();
                insert.CommandText = "INSERT INTO G (Id, S) VALUES (?, ?)";
                insert.Parameters.AddWithValue("i", i);
                insert.Parameters.AddWithValue("s", values[i]);
                insert.ExecuteNonQuery();
            }

            Assert.Equal(["1", "1", "3", "2"], Read(ace, "SELECT COUNT(*) FROM G GROUP BY S"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void AssertComparesIn(
        Dictionary<string, string[]> ace, string[] order, string[] below, string literal, string minMax)
    {
        foreach (string query in (string[])["order S", "order P", "group S", "group P", "order expr"])
            Assert.Equal(order, ace[query]);
        Assert.Equal([.. order.Select(v => v.ToUpperInvariant())], ace["order ucase"]);
        foreach (string query in (string[])["S below", "P below", "literal above S"])
            Assert.Equal(below, ace[query]);
        Assert.Equal([literal], ace["literal ča"]);
        Assert.Equal([literal], ace["literal lja"]);
        Assert.Equal([minMax], ace["minmax S"]);
        Assert.Equal([minMax], ace["minmax P"]);
    }

    private static Dictionary<string, string[]> Probe(bool stampHeader, bool stampColumn)
    {
        string path = TemporaryDatabase.CreatePath("collation-query-", ".accdb");
        try
        {
            DatabaseCreator.CreateEmpty(path, collation: GeneralV1);
            using (OleDbConnection connection = AceTestDatabase.Open(path))
                Exec(connection, "CREATE TABLE T (Id LONG, S TEXT(20), P TEXT(20))");

            CollationV1PatchProbeTests.Stamp(path, "T", stampColumn ? "S" : null, CroatianV1, header: stampHeader);

            // The stamp landed where it was meant to, or the answer says nothing about where ACE looks.
            using (var check = JetDatabase.Open(path))
            {
                TableDef table = check.Catalog.FindTable("T")!;
                Assert.Equal(stampHeader ? CroatianV1 : GeneralV1, check.Collation);
                Assert.Equal(stampColumn ? CroatianV1 : GeneralV1, table.FindColumn("S")!.Collation);
                Assert.Equal(GeneralV1, table.FindColumn("P")!.Collation);
            }

            using OleDbConnection ace = AceTestDatabase.Open(path);
            for (int i = 0; i < Values.Length; i++)
            {
                using OleDbCommand insert = ace.CreateCommand();
                insert.CommandText = "INSERT INTO T (Id, S, P) VALUES (?, ?, ?)";
                insert.Parameters.AddWithValue("i", i);
                insert.Parameters.AddWithValue("s", Values[i]);
                insert.Parameters.AddWithValue("p", Values[i]);
                insert.ExecuteNonQuery();
            }

            return new Dictionary<string, string[]>
            {
                ["order S"] = Read(ace, "SELECT S FROM T ORDER BY S"),
                ["order P"] = Read(ace, "SELECT P FROM T ORDER BY P"),
                ["group S"] = Read(ace, "SELECT S FROM T GROUP BY S"),
                ["group P"] = Read(ace, "SELECT P FROM T GROUP BY P"),
                ["order expr"] = Read(ace, "SELECT S FROM T ORDER BY S & ''"),
                ["order ucase"] = Read(ace, "SELECT UCASE(S) FROM T ORDER BY UCASE(S)"),
                ["S below"] = Read(ace, "SELECT S FROM T WHERE S < 'cb' ORDER BY Id"),
                ["P below"] = Read(ace, "SELECT P FROM T WHERE P < 'cb' ORDER BY Id"),
                ["literal above S"] = Read(ace, "SELECT S FROM T WHERE 'cb' > S ORDER BY Id"),
                ["literal ča"] = Read(ace, "SELECT TOP 1 IIF('ča' < 'cb', 'general', 'croatian') FROM T"),
                ["literal lja"] = Read(ace, "SELECT TOP 1 IIF('lja' < 'lm', 'general', 'croatian') FROM T"),
                ["minmax S"] = Read(ace, "SELECT MIN(S) & ' / ' & MAX(S) FROM T"),
                ["minmax P"] = Read(ace, "SELECT MIN(P) & ' / ' & MAX(P) FROM T"),
            };
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string[] Read(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        var values = new List<string>();
        using OleDbDataReader reader = command.ExecuteReader();
        while (reader.Read()) values.Add(reader.IsDBNull(0) ? "NULL" : reader.GetValue(0).ToString()!);
        return [.. values];
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
