using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// NOT NULL (Required) columns: Access stores a boolean <c>Required</c> property per column in the LvProp
/// blob (absent for a nullable column). LibRed writes it byte-faithfully, reads it back onto
/// <see cref="ColumnDef.IsNullable"/>, and ACE enforces it on a LibRed-created table.
/// </summary>
public class RequiredColumnTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    private const string Ddl = "CREATE TABLE T (Id counter PRIMARY KEY, Req int NOT NULL, Opt int, Def int DEFAULT 7 NOT NULL)";

    // The LibRed CreateTable equivalent of Ddl.
    private static void CreateViaLibRed(string path)
    {
        using var db = JetDatabase.Open(path, readOnly: false);
        db.CreateTable("T",
        [
            new("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true),
            new("Req", JetDataType.Int32, 4, IsFixedLength: true, IsNullable: false),
            new("Opt", JetDataType.Int32, 4, IsFixedLength: true),
            new("Def", JetDataType.Int32, 4, IsFixedLength: true, IsNullable: false),
        ],
        primaryKey: ["Id"],
        columnDefaults: [("Def", "7")]);
    }

    private static byte[] ReadLvProp(string path, string table)
    {
        using var db = JetDatabase.Open(path);
        var msys = db.OpenTable("MSysObjects");
        int nameI = msys.Definition.FindColumn("Name")!.Index;
        int lvpI = msys.Definition.FindColumn("LvProp")!.Index;
        foreach (var row in msys.Rows())
            if (row[nameI] as string == table && row[lvpI] is byte[] blob)
                return blob;
        return [];
    }

    [Fact]
    public void Required_property_blob_matches_access_byte_for_byte()
    {
        string acePath = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "req-ace-");
        string libPath = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "req-lib-");
        try
        {
            using (var conn = OpenOleDb(acePath))
            { using var c = conn.CreateCommand(); c.CommandText = Ddl; c.ExecuteNonQuery(); }
            CreateViaLibRed(libPath);

            byte[] ace = ReadLvProp(acePath, "T");
            byte[] lib = ReadLvProp(libPath, "T");
            Assert.True(ace.AsSpan().SequenceEqual(lib),
                $"ace={Convert.ToHexString(ace)}\nlib={Convert.ToHexString(lib)}");
        }
        finally { foreach (var p in new[] { acePath, libPath }) TemporaryDatabase.Delete(p); }
    }

    [Fact]
    public void Libred_reads_required_back_as_not_nullable()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "req-read-");
        try
        {
            using (var conn = OpenOleDb(path))
            { using var c = conn.CreateCommand(); c.CommandText = Ddl; c.ExecuteNonQuery(); }

            using var db = JetDatabase.Open(path);
            var def = db.OpenTable("T").Definition;
            Assert.False(def.FindColumn("Req")!.IsNullable);
            Assert.False(def.FindColumn("Def")!.IsNullable);
            Assert.True(def.FindColumn("Opt")!.IsNullable);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Access_enforces_required_on_a_libred_created_table()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "req-enf-");
        try
        {
            CreateViaLibRed(path);
            using var conn = OpenOleDb(path);

            // Omitting the required Req column: Access rejects it (proving our Required property "took").
            using (var c = conn.CreateCommand())
            {
                c.CommandText = "INSERT INTO T (Opt) VALUES (5)";
                var ex = Assert.Throws<OleDbException>(() => c.ExecuteNonQuery());
                Assert.Contains("Req", ex.Message);
            }
            // Providing it succeeds (Def takes its default).
            using (var c = conn.CreateCommand())
            { c.CommandText = "INSERT INTO T (Req) VALUES (5)"; Assert.Equal(1, c.ExecuteNonQuery()); }
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
