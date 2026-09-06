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

    private static void AceRun(string path, string sql)
    {
        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void LibRedRun(string path, string sql)
    {
        using var database = JetDatabase.Open(path, readOnly: false);
        new QueryEngine(database).ExecuteNonQuery(sql);
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

            return string.Join(", ", objects.Columns
                       .Where(c => c.Name is not ("DateCreate" or "DateUpdate" or "Id" or "ParentId" or "Owner"))
                       .Select(c => $"{c.Name}={Format(table[c.Index])}"))
                + $", parent={container}";
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string Format(object? value) => value switch
    {
        null => "<null>",
        byte[] b => $"byte[{b.Length}]",
        _ => value.ToString() ?? "",
    };
}
