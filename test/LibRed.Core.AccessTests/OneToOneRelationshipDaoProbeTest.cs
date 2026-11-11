using System.Data.OleDb;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Probe: what a one-to-one relationship (DAO <c>dbRelationUnique</c>, <c>grbit</c> <c>0x01</c>) is on disk, and
/// whether LibRed's schema changes keep one intact. Reports; asserts nothing. ACE's SQL cannot create one, so each
/// case relates a child three ways on copies of one start file — SQL <c>FOREIGN KEY</c>, DAO with attributes 0 and
/// DAO with <c>dbRelationUnique</c> — and diffs them; then drops, renames the table of, and renames the column of the
/// one-to-one through ACE on one copy and LibRed on another, and diffs those.
/// </summary>
/// <remarks>Every file is kept under <c>%TEMP%\libred-one-to-one-probe</c> for a byte diff.</remarks>
[Collection(AceCollection.Name)]
public class OneToOneRelationshipDaoProbeTest(ITestOutputHelper output)
{
    private const int OneToOne = 1;
    private static readonly string KeepDirectory = Path.Combine(Path.GetTempPath(), "libred-one-to-one-probe");

    [Theory(Explicit = true)]
    [InlineData("ChildPlain", "ParentId", "Parent", "Id")]        // no index on the child column
    [InlineData("ChildUnique", "ParentId", "Parent", "Id")]       // EF's shape: a unique index over it already
    [InlineData("ChildPk", "Id", "Parent", "Id")]                 // the child column is the child's primary key
    [InlineData("ChildTwo", "A,B", "Parent2", "A,B")]             // two columns
    [InlineData("ChildDup", "ParentId", "Parent", "Id")]          // two child rows share a key already
    [InlineData("ChildNulls", "ParentId", "Parent", "Id")]        // two child rows with a null key
    public void Dao_creates_a_one_to_one(string child, string columns, string parent, string referenced)
    {
        Assert.SkipWhen(AceTestDatabase.CreateDaoEngine() is null, "DAO is not registered in this process.");
        Directory.CreateDirectory(KeepDirectory);
        string[] childColumns = columns.Split(','), parentColumns = referenced.Split(',');
        string relation = $"FK_{child}";

        string start = Keep(CreateStart(), $"{child}-start");
        string sql = Keep(start, $"{child}-sql"), many = Keep(start, $"{child}-dao-many"), one = Keep(start, $"{child}-dao-one");

        output.WriteLine($"SQL FOREIGN KEY: {Outcome(() => Exec(sql,
            $"ALTER TABLE {child} ADD CONSTRAINT {relation} FOREIGN KEY ({columns}) REFERENCES {parent} ({referenced})"))}");
        output.WriteLine($"DAO attributes 0: {Outcome(() => DaoRelate(many, relation, parent, child, parentColumns, childColumns, 0))}");
        string oneOutcome = Outcome(() => DaoRelate(one, relation, parent, child, parentColumns, childColumns, OneToOne));
        output.WriteLine($"DAO dbRelationUnique: {oneOutcome}");

        output.WriteLine($"-- SQL vs DAO attributes 0:\n{DropTableParityAccessTests.Difference(sql, many)}");
        output.WriteLine($"-- DAO attributes 0 vs dbRelationUnique:\n{DropTableParityAccessTests.Difference(many, one)}");
        foreach ((string label, string path) in (ReadOnlySpan<(string, string)>)[("sql", sql), ("dao-many", many), ("dao-one", one)])
            output.WriteLine($"{label}: {Describe(path, child)}");
        if (oneOutcome != "accepted") return;

        Follow(one, $"{child}-drop",
            ace => Exec(ace, $"ALTER TABLE {child} DROP CONSTRAINT {relation}"),
            db => db.DropConstraint(child, relation), child);
        Follow(one, $"{child}-rename-table",
            ace => DaoDatabase(ace, database => database.TableDefs[child].Name = child + "X"),
            db => db.RenameTable(child, child + "X"), child + "X");
        Follow(one, $"{child}-rename-column",
            ace => DaoDatabase(ace, database => database.TableDefs[child].Fields[childColumns[0]].Name = childColumns[0] + "X"),
            db => db.RenameColumn(child, childColumns[0], childColumns[0] + "X"), child);
    }

    // The same change through ACE on one copy of the one-to-one and through LibRed on another.
    private void Follow(string source, string label, Action<string> throughAce, Func<JetDatabase, bool> throughLibRed, string child)
    {
        string ace = Keep(source, $"{label}-ace"), libred = Keep(source, $"{label}-libred");
        string aceOutcome = Outcome(() => throughAce(ace));
        string libredOutcome = Outcome(() =>
        {
            using var db = JetDatabase.Open(libred, readOnly: false);
            if (!throughLibRed(db)) throw new InvalidOperationException("LibRed returned false");
        });
        output.WriteLine($"== {label}: ACE {aceOutcome}; LibRed {libredOutcome}");
        output.WriteLine($"ACE:    {Describe(ace, child)}");
        output.WriteLine($"LibRed: {Describe(libred, child)}");
        output.WriteLine($"-- ACE vs LibRed:\n{DropTableParityAccessTests.Difference(ace, libred)}");
    }

    private static string CreateStart()
    {
        string start = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "one-to-one-start-");
        using (OleDbConnection connection = AceTestDatabase.Open(start))
            foreach (string sql in (string[])
                     [
                         "CREATE TABLE Parent (Id LONG, CONSTRAINT PK_Parent PRIMARY KEY (Id))",
                         "CREATE TABLE Parent2 (A LONG, B LONG, CONSTRAINT PK_Parent2 PRIMARY KEY (A, B))",
                         "CREATE TABLE ChildPlain (Id LONG, ParentId LONG, CONSTRAINT PK_ChildPlain PRIMARY KEY (Id))",
                         "CREATE TABLE ChildUnique (Id LONG, ParentId LONG, CONSTRAINT PK_ChildUnique PRIMARY KEY (Id))",
                         "CREATE UNIQUE INDEX IX_ChildUnique_ParentId ON ChildUnique (ParentId)",
                         "CREATE TABLE ChildPk (Id LONG, CONSTRAINT PK_ChildPk PRIMARY KEY (Id))",
                         "CREATE TABLE ChildTwo (Id LONG, A LONG, B LONG, CONSTRAINT PK_ChildTwo PRIMARY KEY (Id))",
                         "CREATE TABLE ChildDup (Id LONG, ParentId LONG, CONSTRAINT PK_ChildDup PRIMARY KEY (Id))",
                         "CREATE TABLE ChildNulls (Id LONG, ParentId LONG, CONSTRAINT PK_ChildNulls PRIMARY KEY (Id))",
                         "INSERT INTO Parent VALUES (1)",
                         "INSERT INTO Parent VALUES (2)",
                         "INSERT INTO Parent2 VALUES (1, 1)",
                         "INSERT INTO ChildPlain VALUES (1, 1)",
                         "INSERT INTO ChildUnique VALUES (1, 1)",
                         "INSERT INTO ChildPk VALUES (1)",
                         "INSERT INTO ChildTwo VALUES (1, 1, 1)",
                         "INSERT INTO ChildDup VALUES (1, 1)",
                         "INSERT INTO ChildDup VALUES (2, 1)",
                         "INSERT INTO ChildNulls VALUES (1, NULL)",
                         "INSERT INTO ChildNulls VALUES (2, NULL)",
                     ])
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        OleDbConnection.ReleaseObjectPool();
        return start;
    }

    private static string Keep(string source, string name)
    {
        string path = Path.Combine(KeepDirectory, name + ".accdb");
        File.Copy(source, path, overwrite: true);
        return path;
    }

    private static void Exec(string path, string sql)
    {
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        using (OleDbCommand command = connection.CreateCommand())
        {
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        OleDbConnection.ReleaseObjectPool();
    }

    // DAO: CreateRelation(name, primary table, foreign table, attributes); Field.Name = primary field,
    // Field.ForeignName = foreign field.
    private static void DaoRelate(string path, string name, string parent, string child, string[] parentColumns,
        string[] childColumns, int attributes) =>
        DaoDatabase(path, database =>
        {
            dynamic relation = database.CreateRelation(name, parent, child, attributes);
            for (int i = 0; i < parentColumns.Length; i++)
            {
                dynamic field = relation.CreateField(parentColumns[i]);
                field.ForeignName = childColumns[i];
                relation.Fields.Append(field);
            }
            database.Relations.Append(relation);
        });

    private static void DaoDatabase(string path, Action<dynamic> run)
    {
        dynamic engine = AceTestDatabase.CreateDaoEngine()!;
        dynamic database = engine.OpenDatabase(path);
        try { run(database); }
        finally
        {
            database.Close();
            AceTestDatabase.ReleaseAbandonedComObjects();
        }
    }

    // The child's indexes as DAO and LibRed see them, and the probed relationship's attributes as DAO and LibRed read them.
    private static string Describe(string path, string child)
    {
        var dao = new List<string>();
        string daoOutcome = Outcome(() =>
        {
            dynamic engine = AceTestDatabase.CreateDaoEngine()!;
            dynamic database = engine.OpenDatabase(path, false, true);
            try
            {
                dynamic indexes = database.TableDefs[child].Indexes;
                for (int i = 0; i < indexes.Count; i++)
                {
                    dynamic ix = indexes[i];
                    dao.Add($"{ix.Name}{(ix.Primary ? " [PK]" : "")}{(ix.Foreign ? " [FK]" : "")}{(ix.Unique ? " [U]" : "")}");
                }
                dynamic relations = database.Relations;
                for (int i = 0; i < relations.Count; i++)
                    if (((string)relations[i].Name).StartsWith("FK_Child", StringComparison.Ordinal))
                        dao.Add($"rel {relations[i].Name} attributes 0x{(int)relations[i].Attributes:X}");
            }
            finally
            {
                database.Close();
                AceTestDatabase.ReleaseAbandonedComObjects();
            }
        });

        string libred = "";
        string libredOutcome = Outcome(() =>
        {
            using var db = JetDatabase.Open(path, readOnly: true);
            var table = db.Catalog.FindTable(child) ?? throw new InvalidOperationException($"LibRed cannot find {child}.");
            libred = string.Join(", ", table.Indexes.Select(i => $"{i.Name} #{i.RealIndexOrdinal} 0x{(int)i.Flags:X4}")) + " | " +
                     string.Join(", ", db.Catalog.Relationships.Where(r => r.Name.StartsWith("FK_Child", StringComparison.Ordinal))
                         .Select(r => $"{r.Name} one={r.IsOneToOne}"));
        });
        return $"DAO [{(daoOutcome == "accepted" ? string.Join(", ", dao) : daoOutcome)}] " +
               $"LibRed [{(libredOutcome == "accepted" ? libred : libredOutcome)}]";
    }

    private static string Outcome(Action run)
    {
        try
        {
            run();
            return "accepted";
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException
                                      or OleDbException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException
                                      or NotSupportedException or NotImplementedException)
        {
            return $"{e.GetType().Name}: {e.GetBaseException().Message}";
        }
    }
}
