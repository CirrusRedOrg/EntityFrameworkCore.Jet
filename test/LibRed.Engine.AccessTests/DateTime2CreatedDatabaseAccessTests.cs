using System.Data.OleDb;
using LibRed;
using LibRed.Data;
using LibRed.Engine;
using LibRed.Formats;
using Xunit;

namespace LibRed.Engine.Tests;

// The end of the DATETIME2 story: a file LibRed synthesised from nothing, at the format the type needs,
// holding a value LibRed wrote — opened and agreed with by Access's own engine.
//
// Everything up to here proves LibRed self-consistent, which proves little: LibRed reading back what LibRed
// wrote would pass just as happily on an encoding ACE has never seen. This is the direction that counts.
//
// The value is read back through ACE's own Year()/Month()/Day()/Hour()/Minute()/Second() rather than by
// materialising the column, because ACE's OLE DB provider cannot return a Date/Time Extended value correctly —
// it hands back a DBTIMESTAMP with the month one short, and throws outright for January. Those scalar
// functions are computed inside the engine and are right (see docs/format/data-types.md), so they read the
// stored bytes without going through the broken conversion. Asserting on them tests our file, not their bug.
[Collection(AceCollection.Name)]
public class DateTime2CreatedDatabaseAccessTests : TempDatabaseTest
{
    // The upgrade path, end to end and against the real engine. LibRed creates an ACE 12 file, then a DDL
    // statement needing Date/Time Extended raises it to ACE 17 in place — which is what Access does, but doing
    // it ourselves means the file was rewritten by us rather than by them. So the question is not whether
    // LibRed can still read it (it wrote it) but whether ACE will still open it at all.
    [Fact]
    public void Ace_opens_a_database_libred_upgraded_in_place_and_reads_the_datetime2_it_forced()
    {
        var value = new DateTime(2021, 3, 4, 5, 6, 7).AddTicks(1234567);

        string path = TemporaryDatabase.CreatePath("libred-upgrade-ace-");
        File.Delete(path);
        try
        {
            // Created at the DEFAULT format — ACE 12, the one that cannot hold the type.
            LibRedConnection.CreateDatabase($"Data Source={path}");
            Assert.Equal(0x02, VersionByte(path));

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(db);
                engine.ExecuteNonQuery("CREATE TABLE `E` (`Id` INTEGER PRIMARY KEY, `V` DATETIME2 NULL)");
                Assert.Equal(JetVersion.Version17_2019, db.Format.Version);

                engine.ExecuteNonQuery("INSERT INTO `E` (`Id`, `V`) VALUES (1, @v)",
                    new Dictionary<string, object?> { ["v"] = value });
            }

            Assert.Equal(0x06, VersionByte(path));

            using var connection = AceTestDatabase.Open(path);
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT Year(V), Month(V), Day(V), Hour(V), Minute(V), Second(V) FROM E WHERE Id = 1";
            using OleDbDataReader reader = command.ExecuteReader();

            Assert.True(reader.Read());
            Assert.Equal(
                [value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second],
                Enumerable.Range(0, 6).Select(i => Convert.ToInt32(reader.GetValue(i))).ToArray());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static byte VersionByte(string path)
    {
        using var stream = File.OpenRead(path);
        stream.Seek(0x14, SeekOrigin.Begin);
        return (byte)stream.ReadByte();
    }

    [Fact]
    public void Ace_reads_a_datetime2_value_libred_wrote_into_a_database_libred_created()
    {
        // Sub-second ticks deliberately: an ordinary 8-byte DATETIME could not carry them, so a value that
        // survives to here could only have gone through the 42-byte encoding.
        var value = new DateTime(2021, 3, 4, 5, 6, 7).AddTicks(1234567);

        string path = TemporaryDatabase.CreatePath("libred-dt2-ace-");
        File.Delete(path);   // CreateDatabase synthesises the file and refuses an existing one
        try
        {
            LibRedConnection.CreateDatabase($"Data Source={path}", version: JetVersion.Version17_2019);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(db);
                engine.ExecuteNonQuery("CREATE TABLE `E` (`Id` INTEGER PRIMARY KEY, `V` DATETIME2 NULL)");
                engine.ExecuteNonQuery("INSERT INTO `E` (`Id`, `V`) VALUES (1, @v)",
                    new Dictionary<string, object?> { ["v"] = value });
            }

            using var connection = AceTestDatabase.Open(path);
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT Year(V), Month(V), Day(V), Hour(V), Minute(V), Second(V) FROM E WHERE Id = 1";
            using OleDbDataReader reader = command.ExecuteReader();

            Assert.True(reader.Read());
            Assert.Equal(
                [value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second],
                Enumerable.Range(0, 6).Select(i => Convert.ToInt32(reader.GetValue(i))).ToArray());
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
