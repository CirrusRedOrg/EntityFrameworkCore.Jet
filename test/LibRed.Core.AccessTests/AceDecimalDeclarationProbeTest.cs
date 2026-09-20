using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// What precision and scale does ACE actually stamp on a NUMERIC/DECIMAL column's descriptor?
//
// TdefBuilder.EffectivePrecision resolves a ColumnSpec's default Precision of 0 to 18 before writing, on the
// grounds that 18 is ACE's own default for a bare DECIMAL. That figure was inherited from AccessTypeMapper
// (`column.Size ?? 18`) rather than measured, so this measures it: ACE creates the columns, LibRed reads the
// descriptor bytes back.
[Collection(AceCollection.Name)]
public class AceDecimalDeclarationProbeTest(ITestOutputHelper output)
{
    [Fact]
    public void What_ace_stamps_for_each_decimal_declaration()
    {
        (string Column, string Declared)[] cases =
        [
            ("Bare",        "DECIMAL"),
            ("BareNumeric", "NUMERIC"),
            ("PrecOnly",    "DECIMAL(10)"),
            ("PrecScale",   "DECIMAL(10,3)"),
            ("MaxPrec",     "DECIMAL(28,4)"),
        ];

        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-decl-");
        try
        {
            using (OleDbConnection connection = AceTestDatabase.Open(path))
            {
                using OleDbCommand create = connection.CreateCommand();
                create.CommandText =
                    $"CREATE TABLE DecDecl ({string.Join(", ", cases.Select(c => $"{c.Column} {c.Declared}"))})";
                create.ExecuteNonQuery();
            }

            using var db = JetDatabase.Open(path, readOnly: true);
            TableDef table = db.Catalog.Tables.Single(t => t.Name == "DecDecl");
            foreach ((string column, string declared) in cases)
            {
                ColumnDef def = table.Columns.Single(c => c.Name == column);
                output.WriteLine(
                    $"{declared,-15} -> type {def.Type}, precision {def.Precision}, scale {def.Scale}, "
                    + $"length {def.Length}, fixed {def.IsFixedLength}");
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>
    /// Whether ACE will accept an out-of-range precision or scale at all — asked directly, by declaring one.
    /// The 1..28 / 0..p bounds LibRed enforces were taken from <c>AccessTypeMapper</c>, and the earlier probe
    /// only tried declarations that were already in range, so it could not tell "ACE refuses 0" from "nobody
    /// asked for 0".
    /// </summary>
    [Theory]
    [InlineData("DECIMAL(0)")]
    [InlineData("DECIMAL(0,0)")]
    [InlineData("DECIMAL(29)")]      // one past the maximum precision
    [InlineData("DECIMAL(5,7)")]     // scale beyond its own precision
    [InlineData("DECIMAL(28,28)")]   // scale equal to precision — the legal edge
    [InlineData("DECIMAL(1,0)")]     // the minimum
    public void Whether_ace_accepts_an_out_of_range_decimal_declaration(string declared)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-range-");
        try
        {
            using (OleDbConnection connection = AceTestDatabase.Open(path))
            {
                using OleDbCommand create = connection.CreateCommand();
                create.CommandText = $"CREATE TABLE DecRange (V {declared})";
                try { create.ExecuteNonQuery(); }
                catch (Exception ex)
                {
                    output.WriteLine($"{declared,-15} -> ACE REFUSES: {ex.Message.Trim()}");
                    return;
                }
            }

            using var db = JetDatabase.Open(path, readOnly: true);
            ColumnDef def = db.Catalog.Tables.Single(t => t.Name == "DecRange").Columns.Single(c => c.Name == "V");
            output.WriteLine(
                $"{declared,-15} -> ACE ACCEPTS, stamps precision {def.Precision}, scale {def.Scale}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>
    /// What a <c>DECIMAL(28,28)</c> can actually hold. ACE accepts the declaration, but all 28 digits sit after
    /// the point, so there are none in front and every value must be under 1 — a column it is easier to declare
    /// than to use. Worth confirming ACE agrees, because LibRed's guard derives the limit arithmetically
    /// (<c>10^(p-s)</c>, here <c>10^0</c> = 1) and a declaration nothing can store would make that untestable.
    /// </summary>
    [Theory]
    [InlineData("0.5")]
    [InlineData("0.1234567890123456789012345678")]   // all 28 decimals used
    [InlineData("0.9999999999999999999999999999")]   // the largest value that fits
    [InlineData("1")]                                // the first that does not
    public void What_a_decimal_28_28_can_hold(string literal)
    {
        var value = decimal.Parse(literal, System.Globalization.CultureInfo.InvariantCulture);

        string acePath = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-2828-");
        try
        {
            using OleDbConnection connection = AceTestDatabase.Open(acePath);
            using (OleDbCommand create = connection.CreateCommand())
            {
                create.CommandText = "CREATE TABLE Dec2828 (Id LONG, V DECIMAL(28,28))";
                create.ExecuteNonQuery();
            }

            try
            {
                using OleDbCommand insert = connection.CreateCommand();
                insert.CommandText = $"INSERT INTO Dec2828 (Id, V) VALUES (1, {literal})";
                insert.ExecuteNonQuery();

                using OleDbCommand back = connection.CreateCommand();
                back.CommandText = "SELECT CStr(V) FROM Dec2828 WHERE Id = 1";
                output.WriteLine($"{literal,-32} ACE    stores {back.ExecuteScalar()}");
            }
            catch (Exception ex) { output.WriteLine($"{literal,-32} ACE    REFUSES: {ex.Message.Trim()}"); }
        }
        finally { TemporaryDatabase.Delete(acePath); }

        string path = TemporaryDatabase.CreatePath("libred-2828-");
        File.Delete(path);
        try
        {
            Storage.DatabaseCreator.CreateEmpty(path);
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("D",
            [
                new ColumnSpec("V", JetDataType.FixedPoint, 17, IsFixedLength: true, Precision: 28, Scale: 28),
            ]);

            try
            {
                db.OpenTable("D").Insert([value]);
                output.WriteLine($"{literal,-32} LibRed stores {db.OpenTable("D").Rows().First()[0]}");
            }
            catch (Exception ex)
            {
                output.WriteLine($"{literal,-32} LibRed REFUSES: {ex.GetType().Name}: {ex.Message.Trim()}");
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

}
