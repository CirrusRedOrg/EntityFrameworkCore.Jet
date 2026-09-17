using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Two DDL forms from Access's CONSTRAINT and CREATE TABLE syntax, run through ACE and through LibRed on copies of
/// the same file, must both be accepted or both refused, and leave the same table: the same relationships and the
/// same columns — type, AutoNumber, Required — and number the same ids on insert.
/// </summary>
/// <remarks>
/// <para><c>REFERENCES table</c> with no column list references the parent's primary key, pairing columns in
/// order whatever their names. It is refused when the parent has no primary key, when the column counts differ,
/// and for a table referencing itself whose key is declared later in the statement.</para>
/// <para><c>IDENTITY [(seed [, increment])]</c> is a column attribute, allowed after the type, NULL/NOT NULL or
/// another IDENTITY and before everything else. It makes a Long column an AutoNumber and is ignored on other
/// types. <c>COUNTER</c> and <c>AUTOINCREMENT</c> are types only: neither may trail a type the same way.</para>
/// </remarks>
[Collection(AceCollection.Name)]
public class ReferencesAndIdentityAccessTests : TempDatabaseTest
{
    private const string Parent = "CREATE TABLE P (Id LONG CONSTRAINT pkP PRIMARY KEY, Code TEXT(10) CONSTRAINT uqCode UNIQUE)";
    private const string Parent2 = "CREATE TABLE P2 (A LONG, B LONG, CONSTRAINT pkP2 PRIMARY KEY (A, B))";
    private const string NoKey = "CREATE TABLE P3 (Id LONG, Code TEXT(10) CONSTRAINT uq3 UNIQUE)";

    public static TheoryData<string, string, bool> References => new()
    {
        { Parent, "CREATE TABLE C (Id LONG, PId LONG REFERENCES P)", true },
        { Parent, "CREATE TABLE C (Id LONG, PId LONG CONSTRAINT fk REFERENCES P)", true },
        { Parent, "CREATE TABLE C (Id LONG, PId LONG, CONSTRAINT fk FOREIGN KEY (PId) REFERENCES P)", true },
        { Parent, "CREATE TABLE C (Id LONG, Code LONG, CONSTRAINT fk FOREIGN KEY (Code) REFERENCES P)", true },
        { Parent, "CREATE TABLE C (Id LONG, PId LONG, CONSTRAINT fk FOREIGN KEY (PId) REFERENCES P ON DELETE CASCADE)", true },
        { Parent2, "CREATE TABLE C (Id LONG, X LONG, Y LONG, CONSTRAINT fk FOREIGN KEY (X, Y) REFERENCES P2)", true },
        { Parent2, "CREATE TABLE C (Id LONG, X LONG, CONSTRAINT fk FOREIGN KEY (X) REFERENCES P2)", false },
        { NoKey, "CREATE TABLE C (Id LONG, PId LONG REFERENCES P3)", false },
        { Parent, "CREATE TABLE C (Id LONG, PId LONG);ALTER TABLE C ADD CONSTRAINT fk FOREIGN KEY (PId) REFERENCES P", true },
        { Parent, "CREATE TABLE C (Id LONG);ALTER TABLE C ADD COLUMN PId LONG REFERENCES P", true },
        { Parent, "CREATE TABLE C (Id LONG);ALTER TABLE C ADD COLUMN PId LONG CONSTRAINT fk REFERENCES P (Id)", true },
        { "", "CREATE TABLE C (Id LONG CONSTRAINT pkC PRIMARY KEY, ParentId LONG REFERENCES C)", true },
        { "", "CREATE TABLE C (Id LONG CONSTRAINT pkC PRIMARY KEY, ParentId LONG, CONSTRAINT fk FOREIGN KEY (ParentId) REFERENCES C)", true },
        { "", "CREATE TABLE C (ParentId LONG REFERENCES C, Id LONG CONSTRAINT pkC PRIMARY KEY)", false },
        { "", "CREATE TABLE C (Id LONG, ParentId LONG, CONSTRAINT fk FOREIGN KEY (ParentId) REFERENCES C, CONSTRAINT pkC PRIMARY KEY (Id))", false },
        { "", "CREATE TABLE C (Id LONG, PId LONG REFERENCES Nope)", false },
    };

    public static TheoryData<string, bool> Identities => new()
    {
        { "Id INT NOT NULL IDENTITY", true },
        { "Id INT IDENTITY NOT NULL", true },
        { "Id INT IDENTITY", true },
        { "Id INT NOT NULL IDENTITY(5, 2)", true },
        { "Id INT NOT NULL IDENTITY (5, 2)", true },
        { "Id INT IDENTITY(5)", true },
        { "Id INT IDENTITY(5, -1)", true },
        { "Id IDENTITY NOT NULL", true },
        { "Id IDENTITY(5, 2)", true },
        { "Id LONG NOT NULL IDENTITY", true },
        { "Id INTEGER4 NOT NULL IDENTITY", true },
        { "Id INT NOT NULL IDENTITY PRIMARY KEY", true },
        { "Id INT IDENTITY DEFAULT 1", true },
        { "Id INT IDENTITY IDENTITY", true },
        { "Id COUNTER NOT NULL", true },
        { "Id COUNTER", true },
        { "Id AUTOINCREMENT(5, 2) NOT NULL", true },
        { "Id COUNTER NOT NULL IDENTITY(9, 3)", true },
        { "Id COUNTER(5, 2) IDENTITY(9, 3)", true },
        { "Id COUNTER(5, 2) IDENTITY", true },
        { "Id AUTOINCREMENT(5, 2) NOT NULL IDENTITY", true },
        // Accepted and ignored on anything but a Long.
        { "Id SHORT NOT NULL IDENTITY", true },
        { "Id BYTE IDENTITY", true },
        { "Id BIGINT NOT NULL IDENTITY", true },
        { "Id DOUBLE IDENTITY", true },
        { "Id GUID IDENTITY", true },
        { "Id TEXT(10) NOT NULL IDENTITY", true },
        { "Id TEXT IDENTITY(5, 2)", true },
        // Refused by both.
        { "Id INT PRIMARY KEY IDENTITY(5, 2)", false },
        { "Id INT CONSTRAINT pk PRIMARY KEY NOT NULL IDENTITY", false },
        { "Id INT DEFAULT 1 IDENTITY", false },
        { "Id INT NOT NULL IDENTITY(5, 2, 1)", false },
        { "Id INT NOT NULL IDENTITY()", false },
        { "Id INT NOT NULL AUTOINCREMENT", false },
        { "Id INT NOT NULL COUNTER", false },
        { "Identity LONG", false },
    };

    [Theory]
    [MemberData(nameof(References))]
    public void References_without_a_column_list_matches_ace(string parent, string statements, bool accepted)
        => AssertSameOutcome(parent.Length == 0 ? statements : $"{parent};{statements}", "C", insertColumn: null, accepted);

    [Theory]
    [MemberData(nameof(Identities))]
    public void Identity_matches_ace(string column, bool accepted)
        => AssertSameOutcome($"CREATE TABLE T ({column}, V TEXT(10))", "T", insertColumn: "V", accepted);

    // A relationship needs the same storage type on both sides, whatever the lengths — an AutoNumber being a Long on
    // either side. Measured over every pairing of the column types; these are representative.
    public static TheoryData<string, string, bool> RelationshipTypes => new()
    {
        { "COUNTER", "LONG", true },
        { "LONG", "LONG", true },
        { "LONG", "COUNTER", true },
        { "COUNTER", "COUNTER", true },
        { "TEXT(10)", "TEXT(20)", true },
        { "TEXT(20)", "CHAR(10)", true },
        { "DECIMAL(10, 2)", "DECIMAL(12, 4)", true },
        { "BINARY(8)", "VARBINARY(8)", true },
        { "GUID", "GUID", true },
        { "LONG", "TEXT(10)", false },
        { "SHORT", "COUNTER", false },
        { "LONG", "SHORT", false },
        { "LONG", "BIGINT", false },
        { "SHORT", "LONG", false },
        { "DOUBLE", "SINGLE", false },
        { "CURRENCY", "DECIMAL(10, 2)", false },
        { "TEXT(10)", "MEMO", false },
        { "DATETIME", "DOUBLE", false },
    };

    [Theory]
    [MemberData(nameof(RelationshipTypes))]
    public void A_relationship_between_column_types_matches_ace(string parentType, string childType, bool accepted)
    {
        AssertSameOutcome($"CREATE TABLE P (Id {parentType} CONSTRAINT pkP PRIMARY KEY);CREATE TABLE C (K LONG, PId {childType} REFERENCES P (Id))",
            "C", insertColumn: null, accepted);
        AssertSameOutcome($"CREATE TABLE P (Id {parentType} CONSTRAINT pkP PRIMARY KEY);CREATE TABLE C (K LONG, PId {childType});ALTER TABLE C ADD CONSTRAINT fk FOREIGN KEY (PId) REFERENCES P (Id)",
            "C", insertColumn: null, accepted);
    }

    // The constraints on an ADD COLUMN apply to the new column, as they do in CREATE TABLE. A primary key needs a
    // value in every row, and a table has only one.
    public static TheoryData<string, bool> AddColumnConstraints => new()
    {
        { "CREATE TABLE T (V TEXT(10));ALTER TABLE T ADD COLUMN Code TEXT(10) UNIQUE", true },
        { "CREATE TABLE T (V TEXT(10));ALTER TABLE T ADD COLUMN Code TEXT(10) CONSTRAINT uq UNIQUE", true },
        { "CREATE TABLE T (V TEXT(10));ALTER TABLE T ADD COLUMN Code TEXT(10) NOT NULL CONSTRAINT uq UNIQUE", true },
        { "CREATE TABLE T (V TEXT(10));ALTER TABLE T ADD COLUMN Id LONG PRIMARY KEY", true },
        { "CREATE TABLE T (V TEXT(10));ALTER TABLE T ADD COLUMN Id LONG CONSTRAINT pk PRIMARY KEY", true },
        { "CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, V TEXT(10));ALTER TABLE T ADD COLUMN Id2 LONG CONSTRAINT pk2 PRIMARY KEY", false },
        { "CREATE TABLE T (V TEXT(10));INSERT INTO T (V) VALUES ('a');INSERT INTO T (V) VALUES ('b');ALTER TABLE T ADD COLUMN Id LONG CONSTRAINT pk PRIMARY KEY", false },
        { "CREATE TABLE T (V TEXT(10));INSERT INTO T (V) VALUES ('a');INSERT INTO T (V) VALUES ('b');ALTER TABLE T ADD COLUMN Code TEXT(10) CONSTRAINT uq UNIQUE", true },
        { "CREATE TABLE T (V TEXT(10));INSERT INTO T (V) VALUES ('a');INSERT INTO T (V) VALUES ('b');ALTER TABLE T ADD COLUMN Id COUNTER CONSTRAINT pk PRIMARY KEY", true },
        // Existing rows are numbered 1, 2, …; the default counter carries on after them, any other restarts at its seed.
        { "CREATE TABLE T (V TEXT(10));INSERT INTO T (V) VALUES ('a');INSERT INTO T (V) VALUES ('b');ALTER TABLE T ADD COLUMN Id COUNTER(5, 2)", true },
        { "CREATE TABLE T (V TEXT(10));INSERT INTO T (V) VALUES ('a');INSERT INTO T (V) VALUES ('b');ALTER TABLE T ADD COLUMN Id COUNTER(1, 1)", true },
        { "CREATE TABLE T (V TEXT(10));INSERT INTO T (V) VALUES ('a');INSERT INTO T (V) VALUES ('b');ALTER TABLE T ADD COLUMN Id INT IDENTITY", true },
        { "CREATE TABLE T (V TEXT(10));INSERT INTO T (V) VALUES ('a');INSERT INTO T (V) VALUES ('b');ALTER TABLE T ADD COLUMN Id COUNTER(2, 1)", true },
        { "CREATE TABLE T (V TEXT(10));INSERT INTO T (V) VALUES ('a');INSERT INTO T (V) VALUES ('b');ALTER TABLE T ADD COLUMN Id COUNTER(1, 5)", true },
        { "CREATE TABLE T (V TEXT(10));INSERT INTO T (V) VALUES ('a');INSERT INTO T (V) VALUES ('b');INSERT INTO T (V) VALUES ('c');ALTER TABLE T ADD COLUMN Id COUNTER(10, -1)", true },
        { "CREATE TABLE T (V TEXT(10));INSERT INTO T (V) VALUES ('a');INSERT INTO T (V) VALUES ('b');INSERT INTO T (V) VALUES ('c');DELETE FROM T WHERE V = 'a';ALTER TABLE T ADD COLUMN Id COUNTER", true },
        { "CREATE TABLE T (V TEXT(10));ALTER TABLE T ADD COLUMN Id COUNTER(5, 2)", true },
        { "CREATE TABLE P (Id LONG CONSTRAINT pkP PRIMARY KEY);CREATE TABLE T (V TEXT(10));ALTER TABLE T ADD COLUMN PId LONG CONSTRAINT uq UNIQUE REFERENCES P", true },
        // The same primary-key rules through the other two ways of adding one.
        { "CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, V TEXT(10), W LONG);ALTER TABLE T ADD CONSTRAINT pk2 PRIMARY KEY (W)", false },
        { "CREATE TABLE T (V TEXT(10), W LONG);INSERT INTO T (V) VALUES ('a');ALTER TABLE T ADD CONSTRAINT pk PRIMARY KEY (W)", false },
        { "CREATE TABLE T (V TEXT(10), W LONG);INSERT INTO T (V) VALUES ('a');CREATE INDEX pk ON T (W) WITH PRIMARY", false },
        { "CREATE TABLE T (V TEXT(10), W LONG);INSERT INTO T (V, W) VALUES ('a', 1);CREATE INDEX pk ON T (W) WITH PRIMARY", true },
        // WITH DISALLOW NULL needs a value in every key column too; IGNORE NULL does not.
        { "CREATE TABLE T (V TEXT(10), W LONG);INSERT INTO T (V) VALUES ('a');CREATE INDEX ix ON T (W) WITH DISALLOW NULL", false },
        { "CREATE TABLE T (V TEXT(10), W LONG);INSERT INTO T (V) VALUES ('a');CREATE INDEX ix ON T (V, W) WITH DISALLOW NULL", false },
        { "CREATE TABLE T (V TEXT(10), W LONG);INSERT INTO T (V, W) VALUES ('a', 1);CREATE INDEX ix ON T (W) WITH DISALLOW NULL", true },
        { "CREATE TABLE T (V TEXT(10), W LONG);INSERT INTO T (V) VALUES ('a');CREATE INDEX ix ON T (W) WITH IGNORE NULL", true },
    };

    [Theory]
    [MemberData(nameof(AddColumnConstraints))]
    public void Add_column_constraints_match_ace(string statements, bool accepted)
        => AssertSameOutcome(statements, "T", insertColumn: "V", accepted);

    [Fact]
    public void Identity_on_alter_table_matches_ace()
    {
        AssertSameOutcome("CREATE TABLE T (V TEXT(10));ALTER TABLE T ADD COLUMN Id INT NOT NULL IDENTITY", "T", "V", accepted: true);
        AssertSameOutcome("CREATE TABLE T (Id INT, V TEXT(10));ALTER TABLE T ALTER COLUMN Id INT IDENTITY", "T", "V", accepted: true);
    }

    private static void AssertSameOutcome(string statements, string table, string? insertColumn, bool? accepted)
    {
        string[] sql = statements.Split(';');
        (string ace, string? aceError) = Run(sql, table, insertColumn, (path, statement) =>
        {
            using OleDbConnection connection = AceTestDatabase.Open(path);
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        });
        (string libred, string? libredError) = Run(sql, table, insertColumn, (path, statement) =>
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            new QueryEngine(db).ExecuteNonQuery(statement);
        });

        // Say which side refused and why: a bare True/False cannot tell a real difference from a statement this ACE
        // cannot run at all (a type it predates, say).
        if (accepted is bool expected && expected != (aceError is null))
        {
            Assert.Fail(expected
                ? $"ACE refused what the test expects it to accept. {aceError}"
                : $"ACE accepted what the test expects it to refuse: {ace}");
        }
        if (ace != libred)
        {
            Assert.Fail($"LibRed and ACE differ.{Environment.NewLine}"
                + $"ACE:    {ace} {aceError}{Environment.NewLine}"
                + $"LibRed: {libred} {libredError}");
        }
    }

    /// <summary>Runs the statements on a fresh copy and describes the result: refused, with the statement and error
    /// that refused it, or the table's columns and relationships as LibRed's catalog reads them, then the ids three
    /// inserts receive.</summary>
    private static (string Description, string? Error) Run(string[] sql, string table, string? insertColumn,
        Action<string, string> execute)
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "refident-");
        foreach (string statement in sql)
        {
            try
            {
                execute(path, statement);
            }
            catch (Exception e)
            {
                return ("refused", $"[{statement}] {e.GetType().Name}: {e.Message}");
            }
        }

        var description = new List<string>();
        string? counter;
        using (var db = JetDatabase.Open(path))
        {
            TableDef t = db.Catalog.FindTable(table)!;
            description.AddRange(t.Columns.Select(c =>
                $"{c.Name} {c.Type} autonumber={c.IsAutoNumber} required={!c.IsNullable}"));
            description.AddRange(db.Catalog.ForeignKeysOf(table).Select(fk =>
                $"relation {fk.Table}->{fk.ReferencedTable} [{string.Join(", ", fk.Columns.Select(c => $"{c.Column}={c.ReferencedColumn}"))}] cascadeDelete={fk.CascadeDelete}"));
            // Names are left out: ACE gives an unnamed constraint's index a random one.
            description.AddRange(t.Indexes
                .Select(ix => $"index primary={ix.IsPrimaryKey} unique={ix.IsUnique} [{string.Join(",", ix.Columns.Select(c => c.Column.Name))}]")
                .Order(StringComparer.Ordinal));
            counter = t.Columns.FirstOrDefault(c => c.IsAutoNumber)?.Name;
        }

        if (counter is not null && insertColumn is not null)
        {
            using (OleDbConnection connection = AceTestDatabase.Open(path))
            {
                for (int i = 0; i < 3; i++)
                {
                    using OleDbCommand insert = connection.CreateCommand();
                    insert.CommandText = $"INSERT INTO [{table}] ([{insertColumn}]) VALUES (NULL)";
                    insert.ExecuteNonQuery();
                }
                using OleDbCommand read = connection.CreateCommand();
                read.CommandText = $"SELECT [{counter}] FROM [{table}] ORDER BY [{counter}]";
                using OleDbDataReader reader = read.ExecuteReader();
                var ids = new List<string>();
                while (reader.Read()) ids.Add(Convert.ToString(reader.GetValue(0))!);
                description.Add($"ids {string.Join(",", ids)}");
            }
        }

        return (string.Join("; ", description), null);
    }
}
