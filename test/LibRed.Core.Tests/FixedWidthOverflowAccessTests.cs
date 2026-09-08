using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using LibRed.Tests.Shared;
using Xunit;

namespace LibRed.Core.Tests;

// The declared-width rule, measured on BOTH storage forms rather than asserted on one.
//
// LibRed used to pad a fixed text/binary column to width and silently TRUNCATE an over-long value, while the
// variable-width path raised. Two behaviours for one mistake, and the fixed one was pinned by a unit test that
// asserted the truncation instead of measuring it — the ACE suite that test's comment cited had never existed.
// This is that missing measurement: ACE refuses the over-long value on all four column shapes, with one
// message, so LibRed does too.
//
// The CHAR/BINARY half also establishes that the fixed form is reachable from ordinary ACE DDL — it is not a
// LibRed-only construct that only the Core API can produce.
public class FixedWidthOverflowAccessTests
{
    [Fact]
    public void ACE_stores_char_and_binary_as_fixed_length_columns()
    {
        string path = TemporaryDatabase.CreatePath("fixed-width-shape-");
        try
        {
            CreateProbeTable(path);
            using var db = JetDatabase.Open(path);
            TableDef probe = db.OpenTable("Probe").Definition;

            Assert.True(probe.FindColumn("C")!.IsFixedLength);      // CHAR(3)
            Assert.False(probe.FindColumn("V")!.IsFixedLength);     // TEXT(3)
            Assert.True(probe.FindColumn("B")!.IsFixedLength);      // BINARY(3)
            Assert.False(probe.FindColumn("W")!.IsFixedLength);     // VARBINARY(3)
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Theory]
    [InlineData("C", "'abcdef'")]   // fixed text
    [InlineData("V", "'abcdef'")]   // variable text
    [InlineData("B", "0x0102030405")]   // fixed binary
    [InlineData("W", "0x0102030405")]   // variable binary
    public void ACE_refuses_an_over_long_value_whether_the_column_is_fixed_or_variable(string column, string literal)
    {
        string path = TemporaryDatabase.CreatePath($"fixed-width-{column}-");
        try
        {
            CreateProbeTable(path);
            using var connection = AceTestDatabase.Open(path);
            using var insert = connection.CreateCommand();
            insert.CommandText = $"INSERT INTO Probe ({column}) VALUES ({literal})";

            Assert.Contains("too small to accept",
                Assert.ThrowsAny<Exception>(() => insert.ExecuteNonQuery()).Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LibRed_refuses_the_same_value_the_same_way(bool fixedLength)
    {
        string path = TemporaryDatabase.CreatePath($"fixed-width-libred-{fixedLength}-");
        try
        {
            CreateProbeTable(path);
            using var db = JetDatabase.Open(path);
            Table probe = db.OpenTable("Probe");
            ColumnDef target = probe.Definition.FindColumn(fixedLength ? "C" : "V")!;

            var values = new object?[probe.Definition.Columns.Count];
            values[target.Index] = "abcdef";

            Assert.Contains("too small to accept",
                Assert.Throws<InvalidOperationException>(() => probe.Insert(values)).Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void CreateProbeTable(string path)
    {
        DatabaseCreator.CreateEmpty(path);
        using var connection = AceTestDatabase.Open(path);
        using OleDbCommand create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE Probe (C CHAR(3), V TEXT(3), B BINARY(3), W VARBINARY(3))";
        create.ExecuteNonQuery();
    }
}
