using System.Data.OleDb;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// Every route to a name has to validate it — a create, a rename, a view or stored procedure — because each
// reaches the same catalog bytes.
//
// A rename has to validate the new name, because it reaches the same bytes as a create.
//
// SchemaEditor.Create, AddIndex, AddForeignKey, AddColumn and AddCheckConstraint all call JetName.Validate:
// over 64 characters corrupts the file for ACE, and . ! ` [ ] make a name unreferenceable in ACE SQL, both
// verified. RenameTable and RenameColumn checked only for collisions, so they could write exactly the names
// Create refuses - the same guarded-on-create, unguarded-on-modify split as the index and record limits.
//
// It was not theoretical. Renaming a column to 100 characters left a database ACE would not open at all:
// "Unrecognized database format". A 100-character table name happened to survive, and the bracketed names
// only broke SQL that tried to reference them, so the column case is the one that did real damage - but the
// create path refuses all of them and a rename has no reason to be more permissive.
[Collection(AceCollection.Name)]
public class RenameNameValidationAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    // What ACE's own DDL does with each name, by route: a leading space and a control character are refused for
    // a table or a column, the forbidden characters and a 65th character for a view or a procedure too, and a
    // foreign key cannot take a name one of its table's indexes has. A trailing space is kept.
    public static TheoryData<string, string, bool> AceNames => new()
    {
        { "table, leading space", "CREATE TABLE [ Lead] (V TEXT(10))", false },
        { "table, trailing space", "CREATE TABLE [Trail ] (V TEXT(10))", true },
        { "table, tab inside", "CREATE TABLE [Ta\tb] (V TEXT(10))", false },
        { "table, control character inside", "CREATE TABLE [Ct\u0001l] (V TEXT(10))", false },
        { "column, leading space", "CREATE TABLE ColLead ([ V] TEXT(10))", false },
        { "column, control character inside", "CREATE TABLE ColCtl ([V\u0001W] TEXT(10))", false },
        { "view, dot", "CREATE VIEW [Vi.ew] AS SELECT CompanyName FROM Shippers", false },
        { "view, 65 characters", $"CREATE VIEW [{new string('V', 65)}] AS SELECT CompanyName FROM Shippers", false },
        { "procedure, bang", "CREATE PROCEDURE [Pr!oc] AS DELETE FROM Shippers WHERE ShipperID = 0", false },
        { "procedure, 65 characters", $"CREATE PROCEDURE [{new string('P', 65)}] AS DELETE FROM Shippers WHERE ShipperID = 0", false },
        { "procedure, leading space", "CREATE PROCEDURE [ Proc] AS DELETE FROM Shippers WHERE ShipperID = 0", false },
        { "foreign key named like an index of its table",
            "CREATE TABLE FkP (Id LONG CONSTRAINT pkP PRIMARY KEY);CREATE TABLE FkC (Id LONG, PId LONG);"
            + "CREATE INDEX Taken ON FkC (Id);ALTER TABLE FkC ADD CONSTRAINT Taken FOREIGN KEY (PId) REFERENCES FkP (Id)", false },
    };

    [Theory]
    [MemberData(nameof(AceNames))]
    public void Ace_accepts_or_refuses_a_name(string label, string ddl, bool accepted)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "ace-name-");
        string? refusal = null;
        using (OleDbConnection connection = AceTestDatabase.Open(path))
            foreach (string statement in ddl.Split(';'))
                try
                {
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText = statement;
                    command.ExecuteNonQuery();
                }
                catch (OleDbException e) { refusal = e.Message.Trim(); break; }
        output.WriteLine($"{label}: {refusal ?? "accepted"}");
        Assert.Equal(accepted, refusal is null);
    }

    // CREATE VIEW, unlike CREATE PROCEDURE, does not refuse a leading space: it drops it. A table keeps a
    // trailing one.
    [Fact]
    public void Ace_trims_a_view_names_leading_space_and_keeps_a_tables_trailing_one()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "ace-name-trim-");
        using (OleDbConnection connection = AceTestDatabase.Open(path))
            foreach (string statement in new[]
                     {
                         "CREATE VIEW [ View] AS SELECT CompanyName FROM Shippers",
                         "CREATE TABLE [Trail ] (V TEXT(10))",
                     })
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }

        using var database = JetDatabase.Open(path);
        Assert.Contains("View", database.Catalog.Queries.Keys);
        Assert.NotNull(database.Catalog.FindTable("Trail "));
    }

    // The same names through LibRed's own routes, which write the catalog directly and so have to refuse what
    // ACE's DDL would.
    public static TheoryData<string, string, bool> LibRedNames => new()
    {
        { "table", " Lead", false },
        { "table", "Trail ", true },
        { "table", "Ta\tb", false },
        { "table", "Ct\u0001l", false },
        { "column", " V", false },
        { "column", "V\u0001W", false },
        { "view", "Vi.ew", false },
        { "view", new string('V', 65), false },
        { "view", " View", false },
        { "procedure", "Pr!oc", false },
        { "procedure", new string('P', 65), false },
        { "procedure", " Proc", false },
    };

    [Theory]
    [MemberData(nameof(LibRedNames))]
    public void Libred_accepts_or_refuses_a_name_as_ace_does(string route, string name, bool accepted)
    {
        using var database = Fresh(out _);
        Action create = route switch
        {
            "table" => () => database.CreateTable(name, Specs()),
            "column" => () => database.CreateTable("Cols", [new ColumnSpec(name, JetDataType.Int32, 4, IsFixedLength: true)]),
            "view" => () => database.CreateView(name, new ViewSpec(
                Distinct: false, [new ViewColumnSpec("Value", null)], [new ViewTableSpec("Probe", null)], [], null)),
            _ => () => database.CreateActionQuery(name, new ActionQuerySpec(ActionQueryKind.DataDefinition, DdlSql: "DROP TABLE Probe")),
        };

        Exception? error = Record.Exception(create);
        output.WriteLine($"{route} [{name}]: {error?.Message ?? "accepted"}");
        Assert.Equal(accepted, error is null);
    }

    [Fact]
    public void Libred_refuses_a_foreign_key_named_like_an_index_of_its_table()
    {
        using var database = Fresh(out _);
        database.CreateTable("Parent", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["Id"]);
        database.CreateTable("Child",
            [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true), new ColumnSpec("PId", JetDataType.Int32, 4, IsFixedLength: true)]);
        database.CreateIndex("Child", "Taken", [("Id", false)]);

        var error = Assert.Throws<InvalidOperationException>(() => database.AddForeignKey("Child",
            new RelationshipSpec("Taken", "Parent", [("PId", "Id")], IsEnforced: true, CascadeUpdate: false, CascadeDelete: false)));
        Assert.Contains("already has an index named 'Taken'", error.Message);
    }

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