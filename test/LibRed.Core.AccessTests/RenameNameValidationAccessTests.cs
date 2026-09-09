using System.Data.OleDb;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// A rename has to validate the new name, because it reaches the same bytes as a create.
//
// TableCreator.Create, AddIndex, AddForeignKey, AddColumn and AddCheckConstraint all call JetName.Validate:
// over 64 characters corrupts the file for ACE, and . ! ` [ ] make a name unreferenceable in ACE SQL, both
// verified. RenameTable and RenameColumn checked only for collisions, so they could write exactly the names
// Create refuses - the same guarded-on-create, unguarded-on-modify split as the index and record limits.
//
// It was not theoretical. Renaming a column to 100 characters left a database ACE would not open at all:
// "Unrecognized database format". A 100-character table name happened to survive, and the bracketed names
// only broke SQL that tried to reference them, so the column case is the one that did real damage - but the
// create path refuses all of them and a rename has no reason to be more permissive.
public class RenameNameValidationAccessTests : TempDatabaseTest
{
    private static List<ColumnSpec> Specs() =>
    [
        new("Id", JetDataType.Int32, 4, IsFixedLength: true),
        new("Value", JetDataType.Text, 100, IsFixedLength: false),
    ];

    public static TheoryData<string> InvalidNames => new()
    {
        new string('N', 100),   // past the 64-character limit
        "Bad[Name]",            // brackets
        "Bad!Name",             // bang
        "Bad.Name",             // dot
        "Bad`Name",             // backtick
    };

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public void A_table_cannot_be_renamed_to_a_name_create_would_refuse(string name)
    {
        using var database = Fresh(out string path);
        Assert.ThrowsAny<Exception>(() => database.RenameTable("Probe", name));
        Assert.NotNull(database.Catalog.FindTable("Probe"));
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public void A_column_cannot_be_renamed_to_a_name_create_would_refuse(string name)
    {
        using var database = Fresh(out string path);
        Assert.ThrowsAny<Exception>(() => database.RenameColumn("Probe", "Value", name));
        Assert.Contains(database.Catalog.FindTable("Probe")!.Columns, c => c.Name == "Value");
    }

    // The case that actually corrupted the file, kept end-to-end: refuse the rename, and prove ACE still
    // opens what is left.
    [Fact]
    public void Refusing_the_rename_leaves_a_database_ace_can_still_open()
    {
        string path;
        using (var database = Fresh(out path))
        {
            Assert.ThrowsAny<Exception>(() => database.RenameColumn("Probe", "Value", new string('N', 100)));
        }

        using var connection = AceTestDatabase.Open(path);
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT Value FROM Probe";
        Assert.Equal("hello", read.ExecuteScalar());
    }

    private static JetDatabase Fresh(out string path)
    {
        path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "rename-name-");
        var database = JetDatabase.Open(path, readOnly: false);
        database.CreateTable("Probe", Specs(), primaryKey: ["Id"]);
        database.OpenTable("Probe").Insert([1, "hello"]);
        return database;
    }
}
