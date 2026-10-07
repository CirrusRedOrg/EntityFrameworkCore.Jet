using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// What a column's declaration becomes on disk must not depend on which DDL route wrote it. Two declarations were
// being honoured on CREATE TABLE and not on the others:
//   - NUMERIC/DECIMAL precision. ACE never writes precision 0 — it leaves a column ACE cannot read — so a declared 0
//     is stamped as ACE's default 18, and a precision or scale ACE would refuse is refused, on CREATE TABLE,
//     ADD COLUMN and ALTER COLUMN alike. The SQL front end already resolves both before it reaches Core, so only a
//     direct Core caller could get a 0 or an invalid pair through.
//   - WITH COMPRESSION on a Text/Memo column: the capable flag (descriptor 0x10, bit 0x01), which the SQL front end
//     hands to ADD COLUMN just as it does to CREATE TABLE.
// ALTER COLUMN leaves the extended flags alone by design (page-02b §3.8 lists every byte a retype changes, and 0x10 is not
// one), and the ALTER COLUMN statement has no compression clause, so nothing is asserted about compression there.
public class ColumnDeclarationTests
{
    private const byte AcesDefaultPrecision = 18;

    private static ColumnSpec Numeric(string name, byte precision, byte scale) =>
        new(name, JetDataType.FixedPoint, 17, IsFixedLength: true, Precision: precision, Scale: scale);

    private static JetDatabase NewDatabase(out string path, string prefix)
    {
        path = TemporaryDatabase.CreatePath(prefix);
        JetDatabase.Create(path);
        return JetDatabase.Open(path, readOnly: false);
    }

    private static ColumnDef Column(JetDatabase db, string table, string column)
    {
        db.Catalog.Invalidate();
        return db.Catalog.FindTable(table)!.RequireColumn(column);
    }

    // ---- precision ----

    [Fact]
    public void Create_table_stamps_a_declared_precision_of_zero_as_18()
    {
        using JetDatabase db = NewDatabase(out string path, "decl-create-");
        try
        {
            db.CreateTable("T",
            [
                new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                Numeric("V", precision: 0, scale: 0),
            ]);

            Assert.Equal(AcesDefaultPrecision, Column(db, "T", "V").Precision);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Add_column_stamps_a_declared_precision_of_zero_as_18()
    {
        using JetDatabase db = NewDatabase(out string path, "decl-add-");
        try
        {
            db.CreateTable("T", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)]);

            Assert.True(db.AddColumn("T", Numeric("V", precision: 0, scale: 0)));

            Assert.Equal(AcesDefaultPrecision, Column(db, "T", "V").Precision);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Add_column_keeps_a_declared_precision_and_scale()
    {
        using JetDatabase db = NewDatabase(out string path, "decl-add-keep-");
        try
        {
            db.CreateTable("T", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)]);

            db.AddColumn("T", Numeric("V", precision: 12, scale: 3));

            ColumnDef added = Column(db, "T", "V");
            Assert.Equal(12, added.Precision);
            Assert.Equal(3, added.Scale);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Theory]
    [InlineData(false)]   // AlterColumn — decides the change needs the in-place retype and calls it
    [InlineData(true)]    // AlterColumnTypeInPlace directly
    public void Alter_column_stamps_a_declared_precision_of_zero_as_18(bool directly)
    {
        using JetDatabase db = NewDatabase(out string path, "decl-alter-");
        try
        {
            db.CreateTable("T",
            [
                new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                new ColumnSpec("N", JetDataType.Int32, 4, IsFixedLength: true),
            ]);
            ColumnSpec target = Numeric("N", precision: 0, scale: 0);

            if (directly) db.AlterColumnTypeInPlace("T", "N", target);
            else db.AlterColumn("T", "N", target);

            ColumnDef retyped = Column(db, "T", "N");
            Assert.Equal(JetDataType.FixedPoint, retyped.Type);
            Assert.Equal(AcesDefaultPrecision, retyped.Precision);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A precision above ACE's maximum of 28, and a scale beyond its own precision, are the two pairs ACE refuses.
    [Theory]
    [InlineData(29, 0)]
    [InlineData(5, 7)]
    public void Every_route_refuses_a_precision_ace_would_refuse_and_writes_nothing(byte precision, byte scale)
    {
        using JetDatabase db = NewDatabase(out string path, "decl-refuse-");
        try
        {
            db.CreateTable("T",
            [
                new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                new ColumnSpec("N", JetDataType.Int32, 4, IsFixedLength: true),
            ]);

            Assert.Throws<NotSupportedException>(() => db.AddColumn("T", Numeric("V", precision, scale)));
            Assert.Null(db.Catalog.FindTable("T")!.FindColumn("V"));

            Assert.Throws<NotSupportedException>(() => db.AlterColumn("T", "N", Numeric("N", precision, scale)));
            Assert.Throws<NotSupportedException>(() => db.AlterColumnTypeInPlace("T", "N", Numeric("N", precision, scale)));
            Assert.Equal(JetDataType.Int32, Column(db, "T", "N").Type);

            Assert.Throws<NotSupportedException>(() => db.CreateTable("U",
                [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true), Numeric("V", precision, scale)]));
            Assert.Null(db.Catalog.FindTable("U"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ---- WITH COMPRESSION ----

    [Theory]
    [InlineData(JetDataType.Text, 100)]
    [InlineData(JetDataType.Memo, 0)]
    public void Add_column_and_create_table_write_the_compression_flag_alike(JetDataType type, int length)
    {
        using JetDatabase db = NewDatabase(out string path, "decl-compress-");
        try
        {
            db.CreateTable("T",
            [
                new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                new ColumnSpec("Packed", type, length, IsFixedLength: false, SupportsCompressedUnicode: true),
                new ColumnSpec("Plain", type, length, IsFixedLength: false),
            ]);
            db.AddColumn("T", new ColumnSpec("AddedPacked", type, length, IsFixedLength: false, SupportsCompressedUnicode: true));
            db.AddColumn("T", new ColumnSpec("AddedPlain", type, length, IsFixedLength: false));

            Assert.True(Column(db, "T", "Packed").SupportsCompressedUnicode);
            Assert.False(Column(db, "T", "Plain").SupportsCompressedUnicode);
            Assert.True(Column(db, "T", "AddedPacked").SupportsCompressedUnicode);
            Assert.False(Column(db, "T", "AddedPlain").SupportsCompressedUnicode);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
