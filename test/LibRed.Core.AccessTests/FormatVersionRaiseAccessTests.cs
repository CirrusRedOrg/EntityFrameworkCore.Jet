using System.Data.OleDb;
using LibRed;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Storage;
using LibRed.Tests.Shared;
using Xunit;

namespace LibRed.Core.Tests;

// Raising a database's format version in place — what DDL does when it meets a type the file is too old to
// store. The version byte is not free to move: it is paired with the format identifier, and ACE's own raise
// rewrites more of page 0 than the one byte.
[Collection(AceCollection.Name)]
public class FormatVersionRaiseAccessTests(ITestOutputHelper output)
{
    // RaiseFormatVersion wrote the version byte without checking the format identifier. A Jet MDB carries
    // "Standard Jet DB", which Detect pairs with 0x00/0x01 only — so one successful CREATE TABLE with a
    // BIGINT produced a file neither LibRed nor Access could ever open again. Creation already refused the
    // same pair; only the upgrade path did not.
    [Fact]
    public void Raising_a_jet_mdb_to_an_ACE_version_is_refused()
    {
        string path = TemporaryDatabase.CreatePath("raise-mdb-", ".mdb");
        try
        {
            DatabaseCreator.CreateEmpty(path, version: 0x01);   // a real Access 2000 .mdb

            using (var channel = PageChannel.Open(path, readOnly: false))
            {
                Assert.False(channel.Format.IsAccdb);
                var error = Assert.Throws<NotSupportedException>(() => channel.RaiseFormatVersion(0x05));
                output.WriteLine(error.Message);
                Assert.Contains("Jet MDB", error.Message);
            }

            // And the file is untouched, so it still opens as what it was.
            using var reopened = JetDatabase.Open(path);
            Assert.Equal(JetVersion.Version4, reopened.Format.Version);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The counterpart: raising an ACCDB. A 2010-format file carries 0x15 = 0x01, and the raise used to move
    // only 0x14, leaving a (0x05, 0x01) pair ACE opens but never writes: ACE's own raise clears the minor. One
    // base file, copied, so ACE's raise and LibRed's start from identical bytes and page 0 can be compared whole
    // — everything but the commit-byte table (§2.2), which moves for any write.
    [Theory]
    [InlineData("BIGINT", JetVersion.Version16_2016, 0x05)]
    [InlineData("DATETIME2", JetVersion.Version17_2019, 0x06)]
    public void Raising_a_2010_format_accdb_rewrites_page_zero_as_ACE_does(
        string typeName, JetVersion target, byte expectedVersion)
    {
        Assert.SkipUnless(AceTestDatabase.SupportsColumnType(TestDatabases.NorthwindAccdb, typeName),
            AceTestDatabase.UnsupportedColumnTypeReason(typeName));

        string basePath = TemporaryDatabase.CreatePath("raise-base-");
        string acePath = TemporaryDatabase.CreatePath("raise-ace-");
        string libPath = TemporaryDatabase.CreatePath("raise-lib-");
        try
        {
            DatabaseCreator.CreateEmpty(basePath, version: 0x03);
            Assert.Equal(0x01, PageZero(basePath, JetFormatBase.MinorVersionOffset));
            File.Copy(basePath, acePath, overwrite: true);
            File.Copy(basePath, libPath, overwrite: true);

            using (var connection = AceTestDatabase.Open(acePath))
                Exec(connection, $"CREATE TABLE Raised (K {typeName})");

            using (var db = JetDatabase.Open(libPath, readOnly: false))
                Assert.True(db.EnsureFormatAtLeast(target));

            byte[] ace = PageZeroBytes(acePath), lib = PageZeroBytes(libPath);
            Assert.Equal(expectedVersion, ace[JetFormatBase.VersionOffset]);
            Assert.Equal(0x00, ace[JetFormatBase.MinorVersionOffset]);
            var differences = Enumerable.Range(0, CommitByteTableStart)
                .Where(i => ace[i] != lib[i])
                .Select(i => $"0x{i:X3} ace={ace[i]:X2} lib={lib[i]:X2}")
                .ToList();
            Assert.True(differences.Count == 0, string.Join("; ", differences));

            // And ACE works in the file LibRed raised.
            using var reopened = AceTestDatabase.Open(libPath);
            Exec(reopened, $"CREATE TABLE AfterRaise (K LONG, V {typeName})");
            Exec(reopened, "INSERT INTO AfterRaise (K) VALUES (1)");
            using var read = reopened.CreateCommand();
            read.CommandText = "SELECT COUNT(*) FROM AfterRaise";
            Assert.Equal(1, Convert.ToInt32(read.ExecuteScalar()));
        }
        finally
        {
            TemporaryDatabase.Delete(basePath);
            TemporaryDatabase.Delete(acePath);
            TemporaryDatabase.Delete(libPath);
        }
    }

    private static byte PageZero(string path, int offset) => PageZeroBytes(path)[offset];

    /// <summary>Page 0 starts its user commit-byte table here; every write moves a slot in it.</summary>
    private const int CommitByteTableStart = 0xE00;

    private static byte[] PageZeroBytes(string path)
    {
        using var channel = PageChannel.Open(path, readOnly: true);
        return channel.ReadPage(0).Span.ToArray();
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
