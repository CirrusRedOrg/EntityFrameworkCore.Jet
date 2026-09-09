using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.IO;
using Xunit;

namespace LibRed.Engine.Tests;

// Relationships touch more places than anything else a CREATE writes: an index on the child's FK columns,
// an outgoing block in the child's TDEF, an incoming block in the parent's, and a row per column pair in
// MSysRelationships carrying the cascade flags. All of it matches ACE, cascade flags included
// (grbit 256 = ON UPDATE CASCADE, 4096 = ON DELETE CASCADE, 4352 = both).
[Collection(AceCollection.Name)]
public class RelationshipByteParityAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    private const string Parent = "CREATE TABLE P (Id LONG, K LONG, CONSTRAINT ppk PRIMARY KEY (Id))";

    public static TheoryData<string> Shapes =>
    [
        "CREATE TABLE C (Id LONG, PId LONG, CONSTRAINT cpk PRIMARY KEY (Id), "
            + "CONSTRAINT fk FOREIGN KEY (PId) REFERENCES P (Id))",
        "CREATE TABLE C (Id LONG, PId LONG, CONSTRAINT cpk PRIMARY KEY (Id), "
            + "CONSTRAINT fk FOREIGN KEY (PId) REFERENCES P (Id) ON DELETE CASCADE)",
        "CREATE TABLE C (Id LONG, PId LONG, CONSTRAINT cpk PRIMARY KEY (Id), "
            + "CONSTRAINT fk FOREIGN KEY (PId) REFERENCES P (Id) ON UPDATE CASCADE)",
        "CREATE TABLE C (Id LONG, PId LONG, CONSTRAINT cpk PRIMARY KEY (Id), "
            + "CONSTRAINT fk FOREIGN KEY (PId) REFERENCES P (Id) ON DELETE CASCADE ON UPDATE CASCADE)",
        "CREATE TABLE C (PId LONG, CONSTRAINT fk FOREIGN KEY (PId) REFERENCES P (Id))",
    ];

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Msys_relationships_rows_match_ace(string childSql)
    {
        string? ace = Relationships(childSql, AceRun);
        Assert.SkipWhen(ace is null, "ACE would not create this relationship.");

        output.WriteLine(ace);
        Assert.Equal(ace, Relationships(childSql, LibRedRun));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Both_table_definitions_match_ace(string childSql)
    {
        foreach (string table in new[] { "P", "C" })
        {
            var aceDef = Definition(childSql, table, AceRun);
            Assert.SkipWhen(aceDef is null, "ACE would not create this relationship.");

            (byte[] ace, string names) = aceDef!.Value;
            (byte[] libred, string libredNames) = Definition(childSql, table, LibRedRun)!.Value;

            output.WriteLine($"{table}: {ace.Length} bytes, indexes [{names}]");
            Assert.Equal(names, libredNames);
            Assert.Equal(Convert.ToHexString(ace), Convert.ToHexString(libred));
        }
    }

    private static void AceRun(string path, string[] statements)
    {
        using OleDbConnection connection = AceTestDatabase.Open(path);
        foreach (string s in statements)
        {
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = s;
            command.ExecuteNonQuery();
        }
    }

    private static void LibRedRun(string path, string[] statements)
    {
        using var database = JetDatabase.Open(path, readOnly: false);
        var engine = new QueryEngine(database);
        foreach (string s in statements) engine.ExecuteNonQuery(s);
    }

    private static string? Relationships(string childSql, Action<string, string[]> create)
    {
        string path = Build(childSql, create);
        if (path is null) return null;
        try
        {
            using var database = JetDatabase.Open(path, readOnly: true);
            TableDef rel = database.Catalog.FindTable("MSysRelationships")!;
            int Col(string name) => rel.Columns.Single(c => c.Name == name).Index;
            int szObject = Col("szObject"), szReferenced = Col("szReferencedObject");
            int szColumn = Col("szColumn"), szReferencedColumn = Col("szReferencedColumn");
            int grbit = Col("grbit"), icolumn = Col("icolumn"), szRelationship = Col("szRelationship");

            var rows = new List<string>();
            foreach (object?[] row in database.OpenTable("MSysRelationships").Rows())
            {
                if (row[szObject] as string is not ("C" or "P")) continue;
                rows.Add($"{row[szRelationship]}: {row[szObject]}.{row[szColumn]} -> "
                    + $"{row[szReferenced]}.{row[szReferencedColumn]} grbit={row[grbit]} icol={row[icolumn]}");
            }
            rows.Sort(StringComparer.Ordinal);
            return string.Join(" | ", rows);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static (byte[] Bytes, string IndexNames)? Definition(
        string childSql, string table, Action<string, string[]> create)
    {
        string path = Build(childSql, create);
        if (path is null) return null;
        try
        {
            int definitionPage;
            string names;
            using (var database = JetDatabase.Open(path, readOnly: true))
            {
                TableDef def = database.Catalog.FindTable(table)!;
                definitionPage = def.DefinitionPage;
                names = string.Join(", ", def.Indexes.Select(i => i.Name));
            }

            using var channel = PageChannel.Open(path, readOnly: true);
            byte[] page = channel.ReadPage(definitionPage).Span.ToArray();
            int length = BinaryPrimitives.ReadInt32LittleEndian(
                page.AsSpan(channel.Format.TdefLengthOffset, 4));
            return (length > 0 && length <= page.Length ? page.AsSpan(0, length).ToArray() : page, names);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>A database with the parent and the given child, or null if the engine refused the DDL.</summary>
    private static string Build(string childSql, Action<string, string[]> create)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "rel-");
        try { create(path, [Parent, childSql]); }
        catch (OleDbException) { TemporaryDatabase.Delete(path); return null!; }
        return path;
    }
}
