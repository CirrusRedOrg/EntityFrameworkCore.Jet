using System.Data.OleDb;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Engine.Tests;

// The LvProp blob — a table's extended properties, where Required (NOT NULL), DefaultValue and
// CheckConstraints live, none of them being in the column descriptor.
//
// system-catalog.md says LibRed writes all three "byte-for-byte vs ACE"; nothing compared them. They match,
// including the incremental paths: applying DEFAULT then NOT NULL through ALTER produces the same blob as
// declaring both at CREATE, so the property order is not history-dependent.
[Collection(AceCollection.Name)]
public class LvPropByteParityAccessTests : TempDatabaseTest
{
    public static TheoryData<string> Shapes =>
    [
        "CREATE TABLE W (Id LONG PRIMARY KEY, A LONG)",
        "CREATE TABLE W (Id LONG PRIMARY KEY, A LONG NOT NULL)",
        "CREATE TABLE W (Id LONG PRIMARY KEY, A LONG NOT NULL, B TEXT(20) NOT NULL)",
        "CREATE TABLE W (Id LONG PRIMARY KEY, A LONG DEFAULT 7)",
        "CREATE TABLE W (Id LONG PRIMARY KEY, A TEXT(20) DEFAULT 'hi')",
        "CREATE TABLE W (Id LONG PRIMARY KEY, A DATETIME DEFAULT NOW())",
        "CREATE TABLE W (Id LONG PRIMARY KEY, A LONG DEFAULT 7 NOT NULL)",
        "CREATE TABLE W (Id LONG PRIMARY KEY, A LONG DEFAULT 7, B TEXT(20) DEFAULT 'x')",
        "CREATE TABLE W (Id LONG PRIMARY KEY, A LONG, CONSTRAINT ck CHECK (A > 0))",
        "CREATE TABLE W (Id LONG PRIMARY KEY, A LONG) ;; ALTER TABLE W ALTER COLUMN A LONG NOT NULL",
        "CREATE TABLE W (Id LONG PRIMARY KEY, A LONG) ;; ALTER TABLE W ALTER COLUMN A LONG DEFAULT 7",
        "CREATE TABLE W (Id LONG PRIMARY KEY, A LONG) ;; ALTER TABLE W ALTER COLUMN A LONG DEFAULT 7"
            + " ;; ALTER TABLE W ALTER COLUMN A LONG NOT NULL",
    ];

    [Theory]
    [MemberData(nameof(Shapes))]
    public void The_property_blob_matches_ace(string sql)
    {
        byte[]? ace = Blob(sql, AceRun);
        Assert.SkipWhen(ace is null, "ACE would not run this DDL.");
        Assert.Equal(Convert.ToHexString(ace!), Convert.ToHexString(Blob(sql, LibRedRun)!));
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

    /// <summary>The LvProp blob on table W's MSysObjects row.</summary>
    private static byte[]? Blob(string sql, Action<string, string[]> create)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "lvprop-");
        try
        {
            try { create(path, sql.Split(";;", StringSplitOptions.TrimEntries)); }
            catch (OleDbException) { return null; }

            using var database = JetDatabase.Open(path, readOnly: true);
            TableDef objects = database.Catalog.FindTable("MSysObjects")!;
            int nameCol = objects.Columns.Single(c => c.Name == "Name").Index;
            int lvCol = objects.Columns.Single(c => c.Name == "LvProp").Index;

            foreach (object?[] row in database.OpenTable("MSysObjects").Rows())
                if (row[nameCol] as string == "W")
                    return row[lvCol] as byte[] ?? [];
            return null;
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
