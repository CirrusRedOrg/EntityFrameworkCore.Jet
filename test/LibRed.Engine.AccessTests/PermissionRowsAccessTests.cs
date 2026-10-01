using System.Data.OleDb;
using System.Reflection;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The <c>MSysACEs</c> rows a new object gets are its container's inheritable grants, the Creator's becoming the
/// owner's (system-catalog §11) — so they differ from database to database. Each is checked in two whose Tables
/// containers grant differently: a fresh DAO database, whose owner row comes out <c>0xF00FE</c>, and Northwind,
/// whose Tables container also grants the admin user <c>0xFFEFF</c>.
/// </summary>
[Collection(AceCollection.Name)]
public class PermissionRowsAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    private static readonly string[] Statements =
    [
        "CREATE TABLE PermT (Id LONG CONSTRAINT pkPermT PRIMARY KEY)",
        "CREATE VIEW PermV AS SELECT Id FROM PermT",
        "CREATE TABLE PermC (Id LONG, P LONG, CONSTRAINT fkPerm FOREIGN KEY (P) REFERENCES PermT (Id))",
    ];

    [Theory]
    [InlineData("fresh")]
    [InlineData("northwind")]
    public void A_new_objects_permission_rows_are_the_ones_ace_writes(string database)
    {
        string origin = database == "fresh"
            ? CreateEmptyThroughDao()
            : TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "perm-nw-");
        string ace = TemporaryDatabase.CopyPath(origin, "perm-ace-"), libred = TemporaryDatabase.CopyPath(origin, "perm-libred-");
        try
        {
            using (OleDbConnection connection = AceTestDatabase.Open(ace))
                foreach (string sql in Statements)
                {
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }
            using (var db = JetDatabase.Open(libred, readOnly: false))
            {
                var engine = new QueryEngine(db);
                foreach (string sql in Statements) engine.ExecuteNonQuery(sql);
            }

            string expected = Grants(ace), actual = Grants(libred);
            output.WriteLine($"ACE    {expected}\nLibRed {actual}");
            Assert.Equal(expected, actual);
        }
        finally
        {
            foreach (string path in new[] { origin, ace, libred }) TemporaryDatabase.Delete(path);
        }
    }

    /// <summary>Each new object's MSysACEs rows in the order they are stored: its name, then each row's account,
    /// mask and inheritability.</summary>
    private static string Grants(string path)
    {
        using var db = JetDatabase.Open(path, readOnly: true);
        Table objects = db.OpenTable("MSysObjects");
        int idCol = objects.Definition.RequireColumn("Id").Index, nameCol = objects.Definition.RequireColumn("Name").Index;
        var names = objects.Rows()
            .Where(r => r[nameCol] is string n && (n.StartsWith("Perm", StringComparison.Ordinal) || n.Contains("fkPerm", StringComparison.Ordinal)))
            .ToDictionary(r => (int)r[idCol]!, r => (string)r[nameCol]!);

        Table aces = db.OpenTable("MSysACEs");
        TableDef def = aces.Definition;
        int oid = def.RequireColumn("ObjectId").Index, sid = def.RequireColumn("SID").Index,
            acm = def.RequireColumn("ACM").Index, inherit = def.RequireColumn("FInheritable").Index;
        return string.Join("; ", aces.Rows()
            .Where(r => names.ContainsKey((int)r[oid]!))
            .GroupBy(r => names[(int)r[oid]!])
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}: " + string.Join(", ", g.Select(r =>
                $"{Convert.ToHexString((byte[])r[sid]!)} 0x{(int)r[acm]!:X6}{((bool)r[inherit]! ? " inheritable" : "")}"))));
    }

    private static string CreateEmptyThroughDao()
    {
        object engine = AceTestDatabase.CreateDaoEngine() ?? throw new InvalidOperationException("DAO is not registered.");
        string path = TemporaryDatabase.CreatePath("perm-fresh-");
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
        Invoke(Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!, "Close");
        return path;
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);
}
