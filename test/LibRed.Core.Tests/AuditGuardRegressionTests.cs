using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Storage;
using LibRed.Storage.Types;
using Xunit;

namespace LibRed.Core.Tests;

// The guard half of the audit: checks that existed on one path and not on its sibling, and corrupt-input
// paths that escaped as the wrong exception type. None of these needs ACE — they are about LibRed refusing
// what it should refuse, and reporting corruption as corruption.
public class AuditGuardRegressionTests
{
    // LibRed's signal for a damaged file is InvalidDataException. EndOfStreamException derives from
    // IOException and shares no catchable base with it, so a caller handling corruption misses it entirely —
    // which is what a truncated or empty file used to produce, from the two page-0 reads that run before any
    // channel exists.
    [Theory]
    [InlineData(0)]        // empty
    [InlineData(64)]       // shorter than the page-0 header
    [InlineData(300)]      // header readable, shorter than one page
    public void A_truncated_file_reports_corruption_not_end_of_stream(int length)
    {
        string path = TemporaryDatabase.CreatePath($"truncated-{length}-");
        try
        {
            byte[] file = new byte[4096];
            DatabaseCreator.BuildDefinitionPage(
                version: 0x02, isAccdb: true, codePage: 1252,
                collation: Collation.GeneralLegacy, creationDays: 45000.25).CopyTo(file, 0);
            File.WriteAllBytes(path, file[..length]);

            Assert.ThrowsAny<InvalidDataException>(() => JetDatabase.Open(path).Dispose());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Page 0's creation date is an OLE Automation double straight out of the file, decoded in the very first
    // thing an open does. NaN, infinity, or anything past DateTime's range used to escape as
    // ArgumentOutOfRangeException from inside AddDays.
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e18)]
    [InlineData(-1e18)]
    public void An_impossible_creation_date_reports_corruption(double days)
    {
        string path = TemporaryDatabase.CreatePath("baddate-");
        try
        {
            byte[] file = new byte[4096 * 3];
            DatabaseCreator.BuildDefinitionPage(
                version: 0x02, isAccdb: true, codePage: 1252,
                collation: Collation.GeneralLegacy, creationDays: days).CopyTo(file, 0);
            File.WriteAllBytes(path, file);

            Assert.ThrowsAny<InvalidDataException>(() => JetDatabase.Open(path).Dispose());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // DATETIME2 is 42 ASCII bytes "<day>:<time>:<precision>". The width was checked, the CONTENT was not, so
    // a damaged value escaped as ArgumentOutOfRangeException (s[..-1] on a missing colon), FormatException
    // (non-digits) or an overflow — never the InvalidDataException every other type reports.
    [Theory]
    [InlineData("no colons here at all, but exactly 42 bytes!")]
    [InlineData("12345:only one colon and padding to 42.....")]
    [InlineData("abcdefghijklmnopqrs:tuvwxyzabcdefghijklmn:07")]
    [InlineData("9999999999999999999:0000000000000000000:07")]
    public void A_damaged_extended_datetime_reports_corruption(string text)
    {
        byte[] value = System.Text.Encoding.ASCII.GetBytes(text.PadRight(42)[..42]);
        var column = new ColumnDef
        {
            Name = "D", Type = JetDataType.DateTimeExtended, Index = 0, Length = 42, IsFixedLength = true,
        };
        Assert.ThrowsAny<InvalidDataException>(() => JetTypeCodec.Decode(column, value));
    }

    // A fixed text/binary column is space- or zero-padded to width, and the same code truncated an over-long
    // value just as silently — so CHAR(3) accepted 'abcdef' and stored 'abc' where the variable column of the
    // same width raised. ACE refuses both (FixedWidthOverflowAccessTests).
    [Fact]
    public void An_over_long_value_is_refused_on_a_fixed_column()
    {
        var text = new ColumnDef { Name = "C", Type = JetDataType.Text, Index = 0, Length = 6, IsFixedLength = true };
        Assert.Contains("too small to accept",
            Assert.Throws<InvalidOperationException>(() => JetTypeCodec.Encode(text, "abcdef")).Message);
    }

    // Every JetDatabase DDL entry point takes a raw ColumnSpec and hands it to the writer. The version gate
    // lived only on the SQL path, so a Core caller could write a BIGINT descriptor into an ACE 12 file — a
    // column Access cannot read, produced by the API whose docs said that could not happen.
    [Fact]
    public void Core_DDL_refuses_a_type_the_file_is_too_old_for()
    {
        string path = TemporaryDatabase.CreatePath("versiongate-");
        try
        {
            DatabaseCreator.CreateEmpty(path, version: 0x02);        // ACE 12
            using var db = JetDatabase.Open(path, readOnly: false);

            var error = Assert.Throws<NotSupportedException>(() => db.CreateTable("T",
                [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                 new ColumnSpec("Big", JetDataType.Int64, 8, IsFixedLength: false)]));
            Assert.Contains("Access 2016", error.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ACE rejects a duplicate index name, and every lookup downstream resolves an index by name with
    // First/FirstOrDefault — so two blocks sharing one name made DROP INDEX remove an arbitrary one.
    [Fact]
    public void A_duplicate_index_name_is_refused()
    {
        string path = TemporaryDatabase.CreatePath("dupindex-");
        try
        {
            DatabaseCreator.CreateEmpty(path);
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("T", [Long("K"), Long("A"), Long("B")]);

            db.CreateIndex("T", "IX", [("A", false)], isUnique: false, isPrimary: false,
                disallowNull: false, ignoreNulls: false);
            Assert.Throws<InvalidOperationException>(() => db.CreateIndex("T", "IX", [("B", false)],
                isUnique: false, isPrimary: false, disallowNull: false, ignoreNulls: false));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The TDEF header holds a single seed/increment pair, so a second AutoNumber column would be written with
    // the 0x04 flag and no counter of its own. ALTER's promote path always refused this; CREATE silently took
    // the first and ignored the rest, and ADD COLUMN checked nothing.
    [Fact]
    public void A_table_may_only_have_one_autonumber_column()
    {
        string path = TemporaryDatabase.CreatePath("twocounters-");
        try
        {
            DatabaseCreator.CreateEmpty(path);
            using var db = JetDatabase.Open(path, readOnly: false);

            Assert.Throws<NotSupportedException>(() => db.CreateTable("T",
                [Counter("A"), Counter("B")]));

            db.CreateTable("U", [Counter("A"), Long("K")]);
            Assert.Throws<NotSupportedException>(() => db.AddColumn("U", Counter("C")));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static ColumnSpec Long(string name) => new(name, JetDataType.Int32, 4, IsFixedLength: true);
    private static ColumnSpec Counter(string name) =>
        new(name, JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true, Seed: 1, Increment: 1);
}
