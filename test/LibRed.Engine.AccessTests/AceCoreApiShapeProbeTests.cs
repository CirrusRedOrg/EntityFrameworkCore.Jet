using LibRed;
using LibRed.Catalog;
using LibRed.Data;
using LibRed.Formats;
using Xunit;

namespace LibRed.Engine.Tests;

// The Core-API arm of the write-validity work. The sweep drives LibRed through its SQL front door, so every
// column it builds has already passed AccessTypeMapper — where the width, precision and scale caps live. This
// goes in the other door, the one JetVersion.cs warns about: a ColumnSpec straight to the writer.
//
// TdefBuilder already validates column count, names, ids, the 510-byte field cap and the widest record, and
// EnsureStorable covers version-gated types. This asks what is left OVER those guards.
//
// The contract is NOT "every shape must be accepted" — it is that whether LibRed accepts or refuses the spec,
// the file left behind must be one ACE accepts. A refusal does not end the test: several shapes are refused by
// the row ENCODER rather than the declaration ("Column 'V' encoded to 4 bytes, expected 1"), so the malformed
// table was already committed and only the INSERT failed. The ladder runs either way.
//
// A named catalogue rather than random specs: the space is small and enumerable, and a random walk would
// regenerate the same few cases while naming none of them.
[Collection(AceCollection.Name)]
public class AceCoreApiShapeProbeTests(ITestOutputHelper output) : TempDatabaseTest
{
    /// <summary>The unconstrained table rung 4 writes into, created alongside every shape.</summary>
    private const string AceWriteTarget = "AceTarget";

    public static TheoryData<string> ShapeNames => [.. Shapes.Keys];

    [Theory]
    [MemberData(nameof(ShapeNames))]
    public void Either_libred_refuses_the_shape_or_ace_accepts_the_file(string shape)
    {
        Shape definition = Shapes[shape];
        output.WriteLine($"{shape}: {definition.Why}");

        string path = TemporaryDatabase.CreatePath("libred-coreshape-");
        File.Delete(path);
        try
        {
            LibRedConnection.CreateDatabase($"Data Source={path}", collation: null, version: definition.Version);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable(AceWriteTarget,
                [
                    new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("V", JetDataType.Text, 100, IsFixedLength: false),
                ]);

                try
                {
                    definition.Apply(db);
                    output.WriteLine("   LibRed ACCEPTED it.");
                }
                catch (Exception ex)
                {
                    // A guard fired — better, but not the end of the test: an encoder-level refusal leaves the
                    // malformed table committed, so the ladder still has to see what it left.
                    output.WriteLine(
                        $"   LibRed REFUSED it: {ex.GetType().Name}: {AceValidityLadder.Flatten(ex.Message)}");
                    output.WriteLine("   (checking the file the refusal left behind)");
                }
            }

            AceValidityLadder.Verdict verdict = AceValidityLadder.Check(path, AceWriteTarget);
            foreach (string typedOnly in verdict.TypedOnly) output.WriteLine($"   (typed-read only) {typedOnly}");

            Assert.True(verdict.Finding is null,
                $"The '{shape}' specification left a file ACE will not take.\n"
                + $"  {definition.Why}\n  {verdict.Finding}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private sealed record Shape(string Why, Action<JetDatabase> Apply, JetVersion Version = JetVersion.Version12_2007);

    private static readonly Dictionary<string, Shape> Shapes = new()
    {
        // ---- Precision and scale, which nothing checked until these shapes found it: the width check covers
        // only the fixed 17 bytes. TdefBuilder.ValidateNumericPrecision now refuses a wrong pair, and
        // EffectivePrecision resolves a declared 0 (which means "unspecified") to ACE's 18.
        ["decimal-precision-zero"] = new("FixedPoint declaring precision 0 — no digits at all",
            db => Numeric(db, precision: 0, scale: 0)),

        ["decimal-precision-over-28"] = new("FixedPoint declaring precision 99; ACE's maximum is 28",
            db => Numeric(db, precision: 99, scale: 0)),

        ["decimal-scale-over-precision"] = new("FixedPoint with scale 10 under precision 4 — more decimals than digits",
            db => Numeric(db, precision: 4, scale: 10)),

        ["decimal-scale-over-28"] = new("FixedPoint with scale 99",
            db => Numeric(db, precision: 18, scale: 99)),

        // ---- Declared length not matching the type's natural width. ValidateFieldWidth enforces only the
        // UPPER bound (510 bytes), so a too-NARROW type passes the declaration untouched.
        ["int32-one-byte"] = new("Int32 declared 1 byte wide instead of 4",
            db => Scalar(db, JetDataType.Int32, 1, fixedLength: true, value: 7)),

        ["double-three-bytes"] = new("Double declared 3 bytes wide instead of 8",
            db => Scalar(db, JetDataType.Double, 3, fixedLength: true, value: 1.5d)),

        ["guid-four-bytes"] = new("Guid declared 4 bytes wide instead of 16",
            db => Scalar(db, JetDataType.Guid, 4, fixedLength: false, value: Guid.NewGuid())),

        ["currency-two-bytes"] = new("Currency declared 2 bytes wide instead of 8",
            db => Scalar(db, JetDataType.Currency, 2, fixedLength: true, value: 1.2345m)),

        ["datetime-four-bytes"] = new("DateTime declared 4 bytes wide instead of 8",
            db => Scalar(db, JetDataType.DateTime, 4, fixedLength: true, value: new DateTime(2026, 1, 1))),

        ["int32-over-wide"] = new("Int32 declared 400 bytes wide — inside the 510 cap, far past the type",
            db => Scalar(db, JetDataType.Int32, 400, fixedLength: true, value: 7)),

        // ---- Text is UTF-16 on disk, so an odd byte count cannot hold whole characters, and a zero-width
        // column cannot hold any.
        ["text-odd-byte-length"] = new("Text declared 7 bytes wide — UTF-16 needs an even count",
            db => Scalar(db, JetDataType.Text, 7, fixedLength: false, value: "abc")),

        ["text-zero-length"] = new("Text declared 0 bytes wide",
            db => Scalar(db, JetDataType.Text, 0, fixedLength: false, value: "")),

        // ---- Storage form contradicting the type. Memo and OLE are long values addressed by a 12-byte
        // descriptor; there is no fixed-width form of one.
        ["memo-declared-fixed"] = new("Memo declared fixed-length",
            db => Scalar(db, JetDataType.Memo, 0, fixedLength: true, value: "text")),

        ["ole-declared-fixed"] = new("OLE declared fixed-length",
            db => Scalar(db, JetDataType.Ole, 0, fixedLength: true, value: new byte[] { 1, 2, 3 })),

        // ---- AutoNumber. ACE's COUNTER is Int32 (or a Replication-ID Guid), and a zero increment would make
        // every generated value identical.
        ["autonumber-on-text"] = new("IsAutoNumber on a Text column",
            db => db.CreateTable("S",
            [
                new ColumnSpec("Id", JetDataType.Text, 50, IsFixedLength: false, IsAutoNumber: true),
            ])),

        ["autonumber-on-double"] = new("IsAutoNumber on a Double column",
            db => db.CreateTable("S",
            [
                new ColumnSpec("Id", JetDataType.Double, 8, IsFixedLength: true, IsAutoNumber: true),
            ])),

        ["autonumber-zero-increment"] = new("AutoNumber with Increment 0 — every row gets the same id",
            db =>
            {
                db.CreateTable("S",
                [
                    new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true,
                        Seed: 1, Increment: 0),
                    new ColumnSpec("V", JetDataType.Int32, 4, IsFixedLength: true),
                ], primaryKey: ["Id"]);
                Storage.Table table = db.OpenTable("S");
                table.Insert([null, 1]);
                table.Insert([null, 2]);
            }),

        // ---- Undocumented descriptor flags Access sets on ITS OWN catalog columns. TdefBuilder's own comment
        // says user-table columns leave these clear; nothing stops a caller setting them.
        ["system-flags-on-user-column"] = new("SystemFlags 0x10/0x20 (system-catalog, security-id) on a user column",
            db => db.CreateTable("S",
            [
                new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true, SystemFlags: 0x10),
                new ColumnSpec("Owner", JetDataType.Binary, 100, IsFixedLength: false, SystemFlags: 0x20),
            ])),

        // ---- The compression flag is meaningful only on Text and Memo; AccessTypeMapper refuses it elsewhere
        // rather than dropping it, and that refusal is SQL-side only.
        ["compression-flag-on-int"] = new("SupportsCompressedUnicode on an Int32 column",
            db => db.CreateTable("S",
            [
                new ColumnSpec("V", JetDataType.Int32, 4, IsFixedLength: true, SupportsCompressedUnicode: true),
            ])),

        // ---- A sparse column-id space. Ids are legal individually and the record check uses the high-water,
        // so a two-column table numbered 0 and 100 sizes its null bitmap for 101 columns.
        ["sparse-column-ids"] = new("Two columns with ids 0 and 100",
            db => db.CreateTable("S",
            [
                new ColumnSpec("A", JetDataType.Int32, 4, IsFixedLength: true, ColumnId: 0),
                new ColumnSpec("B", JetDataType.Int32, 4, IsFixedLength: true, ColumnId: 100),
            ])),

        // ---- Index shapes. A text index key is capped at 510 bytes with a truncation checksum; several
        // max-width columns together go far past that.
        ["index-over-wide-key"] = new("A 5-column index over 255-character text columns — ~2550 bytes of key",
            db =>
            {
                ColumnSpec[] columns = [.. Enumerable.Range(0, 5).Select(i =>
                    new ColumnSpec($"C{i}", JetDataType.Text, 510, IsFixedLength: false))];
                db.CreateTable("S", columns);
                db.CreateIndex("S", "IX", [.. columns.Select(c => (c.Name, false))]);
                db.OpenTable("S").Insert([.. Enumerable.Range(0, 5).Select(object? (_) => new string('x', 255))]);
            }),

        ["index-on-memo"] = new("An index over a Memo column holding more than the key can carry",
            db =>
            {
                db.CreateTable("S", [new ColumnSpec("M", JetDataType.Memo, 0, IsFixedLength: false)]);
                db.CreateIndex("S", "IX", [("M", false)]);
                db.OpenTable("S").Insert([new string('m', 20000)]);
            }),

        // ---- A version-gated type in a file too old for it. The guard exists (EnsureStorable); this is the
        // regression test for the specific hole JetVersion.cs describes.
        ["bigint-in-ace12"] = new("Int64 (needs ACE 16) written into an ACE 12 file",
            db => db.CreateTable("S", [new ColumnSpec("V", JetDataType.Int64, 8, IsFixedLength: false)])),

        ["datetime2-in-ace12"] = new("DateTimeExtended (needs ACE 17) written into an ACE 12 file",
            db => db.CreateTable("S",
                [new ColumnSpec("V", JetDataType.DateTimeExtended, 42, IsFixedLength: true)])),

        // ---- Raw row values that do not match the declared column types, straight past the SQL binder.
        ["insert-wrong-clr-type"] = new("Table.Insert handing a string to an Int32 column",
            db =>
            {
                db.CreateTable("S", [new ColumnSpec("V", JetDataType.Int32, 4, IsFixedLength: true)]);
                db.OpenTable("S").Insert(["not a number"]);
            }),

        ["insert-too-many-values"] = new("Table.Insert handing three values to a one-column table",
            db =>
            {
                db.CreateTable("S", [new ColumnSpec("V", JetDataType.Int32, 4, IsFixedLength: true)]);
                db.OpenTable("S").Insert([1, 2, 3]);
            }),

        // ---- A calculated column whose expression cannot be evaluated by anyone.
        ["calculated-nonsense-expression"] = new("A calculated column over a column that does not exist",
            db => db.CreateTable("S",
            [
                new ColumnSpec("A", JetDataType.Int32, 4, IsFixedLength: true),
                ColumnSpec.Calculated("C", JetDataType.Int32, "[NoSuchColumn] * 2"),
            ])),
    };

    /// <summary>One column holding the shape under test, plus a row — several only misbehave once a value is
    /// laid out against the bad descriptor.</summary>
    private static void Scalar(JetDatabase db, JetDataType type, int length, bool fixedLength, object? value)
    {
        db.CreateTable("S", [new ColumnSpec("V", type, length, IsFixedLength: fixedLength)]);
        db.OpenTable("S").Insert([value]);
    }

    private static void Numeric(JetDatabase db, byte precision, byte scale)
    {
        db.CreateTable("S",
        [
            new ColumnSpec("V", JetDataType.FixedPoint, 17, IsFixedLength: true, Precision: precision, Scale: scale),
        ]);
        db.OpenTable("S").Insert([1.5m]);
    }
}
