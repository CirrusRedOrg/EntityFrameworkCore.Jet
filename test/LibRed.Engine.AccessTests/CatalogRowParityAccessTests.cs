using System.Data.OleDb;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Engine.Tests;

// The MSysObjects row a CREATE writes, beyond the LvProp blob compared elsewhere. Access reads the whole
// database through this table, so a wrong Type, Flags or parent puts an object somewhere Access does not
// look for it.
//
// Id, ParentId and the timestamps are assigned per file and cannot be compared as values, so the parent is
// compared STRUCTURALLY — whose child is the table? — which is the part that carries meaning.
[Collection(AceCollection.Name)]
public class CatalogRowParityAccessTests : TempDatabaseTest
{
    public static TheoryData<string> Shapes =>
    [
        "CREATE TABLE W (A LONG, B TEXT(20), CONSTRAINT pk PRIMARY KEY (A))",
        "CREATE TABLE W (A LONG)",
        "CREATE TABLE W (A LONG, M LONGTEXT)",
        "CREATE TABLE W (A LONG NOT NULL, B TEXT(20) DEFAULT 'x', CONSTRAINT pk PRIMARY KEY (A))",
    ];

    [Theory]
    [MemberData(nameof(Shapes))]
    public void The_catalog_row_matches_ace(string sql)
    {
        string ace = Describe(sql, AceRun);
        Assert.Equal(ace, Describe(sql, LibRedRun));
        Assert.Contains("parent=Tables (Type=3)", ace);   // and it is the right container, not just the same one
        Assert.Contains("Type=1", ace);                   // a user table
    }

    // A view and a relationship are objects in the same catalog, written by the same two routines — their
    // container, flags, owner and permission rows are as much a part of what Access reads as a table's.
    [Fact]
    public void A_views_catalog_row_matches_ace()
    {
        const string sql = "CREATE VIEW W AS SELECT CompanyName FROM Shippers";
        string ace = Describe(sql, AceRun);
        Assert.Equal(ace, Describe(sql, LibRedRun));
        Assert.Contains("Type=5", ace);
    }

    [Fact]
    public void A_relationships_catalog_row_matches_ace()
    {
        const string sql = "CREATE TABLE P (Id LONG CONSTRAINT pk PRIMARY KEY);CREATE TABLE C (Id LONG, PId LONG);"
            + "ALTER TABLE C ADD CONSTRAINT W FOREIGN KEY (PId) REFERENCES P (Id)";
        string ace = Describe(sql, AceRun);
        Assert.Equal(ace, Describe(sql, LibRedRun));
        Assert.Contains("Type=8", ace);
    }

    private static void AceRun(string path, string sql)
    {
        using OleDbConnection connection = AceTestDatabase.Open(path);
        foreach (string statement in sql.Split(';'))
        {
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
    }

    private static void LibRedRun(string path, string sql)
    {
        using var database = JetDatabase.Open(path, readOnly: false);
        var engine = new QueryEngine(database);
        foreach (string statement in sql.Split(';')) engine.ExecuteNonQuery(statement);
    }

    /// <summary>Table W's MSysObjects row, column by column, with the per-file values left out and the
    /// parent resolved to the object it names.</summary>
    private static string Describe(string sql, Action<string, string> run)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "catrow-");
        try
        {
            run(path, sql);

            using var database = JetDatabase.Open(path, readOnly: true);
            TableDef objects = database.Catalog.FindTable("MSysObjects")!;
            int Col(string n) => objects.Columns.Single(c => c.Name == n).Index;
            int name = Col("Name"), id = Col("Id"), parent = Col("ParentId"), type = Col("Type");

            var rows = database.OpenTable("MSysObjects").Rows().ToList();
            object?[] table = rows.Single(r => r[name] as string == "W");
            string container = rows.Where(r => Equals(r[id], table[parent]))
                .Select(r => $"{r[name]} (Type={r[type]})")
                .FirstOrDefault() ?? $"unknown id {table[parent]}";

            // The owner and the permission rows ARE comparable: both engines write into a copy of one file, so
            // the per-file SID mask (page-00 §2.3) is the same for both and the masked account SIDs must be too.
            TableDef aces = database.Catalog.FindTable("MSysACEs")!;
            int aceObject = aces.Columns.Single(c => c.Name == "ObjectId").Index;
            int aceSid = aces.Columns.Single(c => c.Name == "SID").Index;
            int aceAcm = aces.Columns.Single(c => c.Name == "ACM").Index;
            var grants = database.OpenTable("MSysACEs").Rows()
                .Where(r => Equals(r[aceObject], table[id]))
                .Select(r => $"{Format(r[aceSid])}:0x{Convert.ToInt32(r[aceAcm]):X}")
                .Order(StringComparer.Ordinal);

            return string.Join(", ", objects.Columns
                       .Where(c => c.Name is not ("DateCreate" or "DateUpdate" or "Id" or "ParentId"))
                       .Select(c => $"{c.Name}={Format(table[c.Index])}"))
                + $", parent={container}, grants=[{string.Join(" ", grants)}]";
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string Format(object? value) => value switch
    {
        null => "<null>",
        // A SID is short and is the point of the comparison; a property blob is not, and only its size is.
        byte[] b => b.Length <= 8 ? Convert.ToHexString(b) : $"byte[{b.Length}]",
        _ => value.ToString() ?? "",
    };
}
