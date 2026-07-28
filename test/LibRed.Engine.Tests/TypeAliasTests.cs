using System.Linq;
using LibRed;
using LibRed.Catalog;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// CREATE TABLE type-name aliases, mapped to the on-disk storage ACE itself produces (audited against the
/// ACE engine: it accepts these names and folds them onto Jet's base types, without a file-format upgrade).
/// </summary>
public class TypeAliasTests
{
    private static ColumnDef Column(string sqlType)
    {
        string path = Path.Combine(Path.GetTempPath(), $"alias-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery($"CREATE TABLE T (C {sqlType})");
        return e.Database.OpenTable("T").Definition.Columns.First(c => c.Name == "C");
    }

    [Theory]
    // SQL-Server aliases ACE folds onto the one 8-byte date / currency type — no narrower storage exists.
    [InlineData("SMALLDATETIME", JetDataType.DateTime, 8)]
    [InlineData("SMALLMONEY", JetDataType.Currency, 8)]
    // Size-less char/varchar/binary default to the MAXIMUM (255 chars = 510 bytes / 510 bytes), not 1.
    [InlineData("CHAR", JetDataType.Text, 510)]
    [InlineData("VARCHAR", JetDataType.Text, 510)]
    [InlineData("BINARY", JetDataType.Binary, 510)]
    // Bare TEXT is Memo (long text); TEXT(n) is a sized varchar.
    [InlineData("TEXT", JetDataType.Memo, 0)]
    [InlineData("TEXT(50)", JetDataType.Text, 100)]
    public void Alias_maps_to_ace_storage(string sqlType, JetDataType expectedType, int expectedLength)
    {
        ColumnDef c = Column(sqlType);
        Assert.Equal(expectedType, c.Type);
        Assert.Equal(expectedLength, c.Length);
    }

    [Fact]
    public void Bare_text_is_memo_but_sized_text_is_varchar()
    {
        Assert.False(Column("TEXT").IsFixedLength);
        Assert.Equal(JetDataType.Memo, Column("TEXT").Type);
        Assert.Equal(JetDataType.Text, Column("TEXT(50)").Type);
    }
}
