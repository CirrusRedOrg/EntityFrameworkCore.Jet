using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// LibRed enforces Jet/ACE object-name rules (verified vs ACE 2026-07-12): max 64 chars — a longer name makes ACE
/// reject the whole file — and no <c>. ! ` [ ]</c>, which make a name unreferenceable in ACE SQL.
/// </summary>
public class JetNameValidationTests
{
    [Theory]
    [InlineData("")]                                   // empty
    [InlineData("Co.l")]                               // period
    [InlineData("Co!l")]                               // exclamation
    [InlineData("Co`l")]                               // grave accent
    [InlineData("Co[l")]                               // left bracket
    [InlineData("Co]l")]                               // right bracket
    public void Rejects_forbidden_characters(string name) =>
        Assert.Throws<ArgumentException>(() => JetName.Validate(name, "column name"));

    [Fact]
    public void Rejects_over_64_chars() =>
        Assert.Throws<ArgumentException>(() => JetName.Validate(new string('c', 65)));

    [Theory]
    [InlineData("Col")]
    [InlineData("Co l")]        // interior space — ACE allows it
    [InlineData("Co#l")]        // # / % / & / quotes all round-trip on ACE
    [InlineData("Cölé")]        // unicode
    public void Accepts_valid_names(string name)
    {
        JetName.Validate(name);           // no throw
        JetName.Validate(new string('c', 64)); // exactly the max is allowed
    }

    [Fact]
    public void CreateTable_rejects_a_too_long_column_name()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nv-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var ex = Assert.Throws<ArgumentException>(() => db.CreateTable("T",
                [new ColumnSpec(new string('c', 100), JetDataType.Int32, 4, IsFixedLength: true)],
                primaryKey: null));
            Assert.Contains("64", ex.Message);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void CreateTable_rejects_a_forbidden_table_name()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nv-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            Assert.Throws<ArgumentException>(() => db.CreateTable("My.Table",
                [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["K"]));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
