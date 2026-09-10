using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// LibRed creates the Access 2000 / 2002-2003 `.mdb` (Jet 4, version byte 0x01) as well as the ACCDB formats.
// Creating it is only half the claim — ACE has to open the result, read what LibRed wrote, and write into it
// itself, which is what separates a plausible file from a valid one.
public class CreatedJet4DatabaseAccessTests(ITestOutputHelper output)
{
    [Fact]
    public void Ace_reads_and_writes_a_jet4_database_libred_created()
    {
        string path = TemporaryDatabase.CreatePath("libred_jet4_ace_")
            .Replace(".accdb", ".mdb", StringComparison.OrdinalIgnoreCase);
        try
        {
            DatabaseCreator.CreateEmpty(path, version: 0x01);
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("People", [
                    new ColumnSpec("Id", JetDataType.Int32, 4, true),
                    new ColumnSpec("Name", JetDataType.Text, 100, false)]);
                db.Catalog.Invalidate();
                db.OpenTable("People").Insert([1, "Ada"]);
            }

            // Scoped, and closed before LibRed reopens: ACE buffers its writes, so holding the connection open
            // means reading the file back before they have reached it.
            using (var connection = AceTestDatabase.Open(path))
            {
                // ACE reads the row LibRed wrote...
                using (var select = connection.CreateCommand())
                {
                    select.CommandText = "SELECT Name FROM People WHERE Id = 1";
                    Assert.Equal("Ada", Convert.ToString(select.ExecuteScalar()));
                }

                // ...and writes one of its own, which needs the usage maps and free-space accounting to be right.
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO People (Id, Name) VALUES (2, 'Alan')";
                Assert.Equal(1, insert.ExecuteNonQuery());
            }

            // Both are visible to LibRed afterwards, so neither engine lost the other's work.
            using (var db = JetDatabase.Open(path))
            {
                Table people = db.OpenTable("People");
                int nameIndex = people.Definition.FindColumn("Name")!.Index;
                var names = people.Rows().Select(r => Convert.ToString(r[nameIndex])).Order().ToList();
                output.WriteLine($"  round trip: {string.Join(", ", names)}");
                Assert.Equal(["Ada", "Alan"], names);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
