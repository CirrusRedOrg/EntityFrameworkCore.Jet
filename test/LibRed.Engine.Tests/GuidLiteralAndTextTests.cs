using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// An indexed GUID column against GUID literals and text. Expected values are ACE's, measured with the same
/// statements: <c>{guid {…}}</c> and <c>{…}</c> are GUID literals, and text that reads as a GUID is stored as one.
/// </summary>
public class GuidLiteralAndTextTests : TempDatabaseTest
{
    private const string G = "6F9619FF-8B86-D011-B42D-00C04FC964FF";

    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "guid-lit-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE Q (Id LONG, G GUID)");
        e.ExecuteNonQuery("CREATE INDEX IxG ON Q (G)");
        e.ExecuteNonQuery($"INSERT INTO Q (Id, G) VALUES (1, {{{G}}})");
        return e;
    }

    private static int Count(QueryEngine e, string where) =>
        Convert.ToInt32(e.ExecuteQuery($"SELECT COUNT(*) FROM Q WHERE {where}").Rows.First()[0]);

    [Theory]
    [InlineData("[G] = {guid {" + G + "}}", 1)]
    [InlineData("[G] = {" + G + "}", 1)]
    [InlineData("[G] = '{" + G + "}'", 1)]
    [InlineData("[G] = '{guid {" + G + "}}'", 0)]
    public void An_indexed_guid_column_compares_with_literals_and_text(string where, int expected) =>
        Assert.Equal(expected, Count(Seeded(), where));

    [Theory]
    [InlineData("{guid {" + G + "}}")]
    [InlineData("{" + G + "}")]
    [InlineData("'{" + G + "}'")]
    [InlineData("'" + G + "'")]
    [InlineData("'{guid {" + G + "}}'")]
    public void A_guid_literal_or_guid_text_inserts_into_an_indexed_guid_column(string value)
    {
        var e = Seeded();
        e.ExecuteNonQuery($"INSERT INTO Q (Id, G) VALUES (2, {value})");
        Assert.Equal(Guid.Parse(G), e.ExecuteQuery("SELECT G FROM Q WHERE Id = 2").Rows.First()[0]);
    }
}
