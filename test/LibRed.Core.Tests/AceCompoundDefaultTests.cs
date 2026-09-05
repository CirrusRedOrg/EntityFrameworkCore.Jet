using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// A Jet/ACE column default is a per-row EXPRESSION, not a static value. ACE's OLE DB *DDL parser* only accepts a
// narrow subset in CREATE TABLE ... DEFAULT (a literal, or a simple function call like NOW()) — it rejects
// concatenation, literal arithmetic and nested calls with "Syntax error in CREATE TABLE statement". But that is
// a front-end limitation only: ACE's expression *service*, used when reading a stored default, evaluates the
// full expression. So a compound default that LibRed writes straight to LvProp (bypassing ACE's DDL parser) is
// read and applied by ACE on insert. Verifies LibRed's SQL surface is a superset of ACE's DDL here.
public class AceCompoundDefaultTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Theory]
    [InlineData("TEXT", "\"INV-\" & Year(Now())", "INV-2026")]  // double-quoted string, & concat, nested call
    [InlineData("LONG", "1 + 2", "3")]                          // literal arithmetic (ACE's DDL parser rejects)
    [InlineData("LONG", "Year(Now())", "2026")]                 // nested function call (ACE's DDL parser rejects)
    public void Access_reads_and_applies_a_libred_written_compound_default(string type, string def, string expected)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "cx-");
        try
        {
            ColumnSpec v = type == "TEXT"
                ? new ColumnSpec("V", JetDataType.Text, 20, IsFixedLength: false)
                : new ColumnSpec("V", JetDataType.Int32, 4, IsFixedLength: true);

            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("T",
                    [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true), v],
                    primaryKey: ["K"],
                    columnDefaults: [("V", def)]);

            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO T (K) VALUES (1)"; c.ExecuteNonQuery(); }
            object? value;
            using (var c = conn.CreateCommand()) { c.CommandText = "SELECT V FROM T"; value = c.ExecuteScalar(); }

            // The expected value is stable except the year — pin the year cases to the current year at run time.
            string want = expected.Replace("2026", DateTime.Now.Year.ToString());
            Assert.Equal(want, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
