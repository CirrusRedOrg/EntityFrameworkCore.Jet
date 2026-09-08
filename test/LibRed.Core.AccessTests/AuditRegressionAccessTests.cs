using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Storage;
using LibRed.Tests.Shared;
using Xunit;

namespace LibRed.Core.Tests;

// Regressions for the spec-vs-code audit. Each one is a defect that shipped, so each is pinned by the
// smallest sequence that reproduced it rather than by a unit test of the fix — most of these were bugs
// precisely because a single path looked correct in isolation and only diverged from its sibling.
public class AuditRegressionAccessTests(ITestOutputHelper output)
{
    // ------------------------------------------------------------------ format version

    // RaiseFormatVersion wrote the version byte without checking the format identifier. A Jet MDB carries
    // "Standard Jet DB", which Detect pairs with 0x00/0x01 only — so one successful CREATE TABLE with a
    // BIGINT produced a file neither LibRed nor Access could ever open again. Creation already refused the
    // same pair; only the upgrade path did not.
    [Fact]
    public void Raising_a_jet_mdb_to_an_ACE_version_is_refused()
    {
        // A synthetic Jet 4 file: identifier "Standard Jet DB", version byte 0x01. Built rather than copied
        // because the repo has no .mdb fixture, and the pairing is all this test needs.
        string path = TemporaryDatabase.CreatePath("raise-mdb-", ".mdb");
        try
        {
            byte[] file = new byte[4096 * 3];
            DatabaseCreator.BuildDefinitionPage(
                version: 0x01, isAccdb: false, codePage: 1252,
                collation: Collation.GeneralLegacy, creationDays: 45000.25).CopyTo(file, 0);
            File.WriteAllBytes(path, file);

            using var channel = PageChannel.Open(path, readOnly: false);
            Assert.False(channel.Format.IsAccdb);
            var error = Assert.Throws<NotSupportedException>(() => channel.RaiseFormatVersion(0x05));
            output.WriteLine(error.Message);
            Assert.Contains("Jet MDB", error.Message);

            // And the file is untouched, so it still opens.
            Assert.Equal(0x01, file[JetFormatBase.VersionOffset]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The counterpart: raising an ACCDB is fine, and ACE still opens the result. A 2010-format file carries
    // 0x15 = 0x01, and the raise moves only 0x14 — a (0x05, 0x01) pair the spec had never observed, so this
    // measures that ACE accepts it rather than assuming so.
    [Fact]
    public void Raising_a_2010_format_accdb_leaves_the_minor_byte_and_ACE_still_opens_it()
    {
        string path = TemporaryDatabase.CreatePath("raise-accdb-");
        try
        {
            DatabaseCreator.CreateEmpty(path, version: 0x03);
            Assert.Equal(0x01, PageZero(path, 0x15));

            using (var db = JetDatabase.Open(path, readOnly: false))
                Assert.True(db.EnsureFormatAtLeast(JetVersion.Version16_2016));

            Assert.Equal(0x05, PageZero(path, 0x14));
            Assert.Equal(0x01, PageZero(path, 0x15));   // untouched by the raise

            using var connection = AceTestDatabase.Open(path);
            Exec(connection, "CREATE TABLE AfterRaise (K LONG, V TEXT(20))");
            Exec(connection, "INSERT INTO AfterRaise (K, V) VALUES (1, 'ok')");
            using var read = connection.CreateCommand();
            read.CommandText = "SELECT V FROM AfterRaise WHERE K = 1";
            Assert.Equal("ok", read.ExecuteScalar());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ------------------------------------------------------------------ foreign keys

    // LibRed accepted a relationship whose parent key was a plain non-unique index. ACE refuses it — "No
    // unique index found for the referenced field of the primary table" — so LibRed was writing a
    // relationship ACE would not have created, over an index the DROP guard did not consider protected.
    [Fact]
    public void A_foreign_key_needs_a_unique_parent_index()
    {
        string path = TemporaryDatabase.CreatePath("fk-parent-unique-");
        try
        {
            using var db = JetDatabase.Open(CreateWithAce(path, [
                "CREATE TABLE P (A LONG, Descr TEXT(20))",
                "CREATE INDEX IXP ON P (A)",                    // NOT unique
                "CREATE TABLE C (B LONG)",
            ]), readOnly: false);

            var error = Assert.Throws<InvalidOperationException>(() => db.AddForeignKey("C",
                new RelationshipSpec("FK", "P", [("B", "A")], IsEnforced: true, CascadeUpdate: false, CascadeDelete: false)));
            output.WriteLine(error.Message);
            Assert.Contains("No unique index found", error.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A parent's incoming-relationship index_num came from the logical block COUNT, while the child side used
    // max + 1. Dropping a relationship removes a block without renumbering index_num, so after a drop the
    // count sits below the max and the next incoming block collided with a live one.
    [Fact]
    public void An_incoming_relationship_number_does_not_collide_after_a_drop()
    {
        string path = TemporaryDatabase.CreatePath("fk-index-num-");
        try
        {
            CreateWithAce(path, [
                "CREATE TABLE P (A LONG, CONSTRAINT PKP PRIMARY KEY (A))",
                "CREATE TABLE C1 (B LONG)",
                "CREATE TABLE C2 (B LONG)",
                "CREATE TABLE C3 (B LONG)",
            ]);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.AddForeignKey("C1", Fk("FK1", "P"));
                db.AddForeignKey("C2", Fk("FK2", "P"));
                Assert.True(db.DropConstraint("C1", "FK1"));    // leaves a gap below the max index_num
                db.AddForeignKey("C3", Fk("FK3", "P"));
            }

            // ACE reading the parent proves the two surviving incoming blocks are not claiming one number.
            using var connection = AceTestDatabase.Open(path);
            Exec(connection, "INSERT INTO P (A) VALUES (1)");
            Exec(connection, "INSERT INTO C2 (B) VALUES (1)");
            Exec(connection, "INSERT INTO C3 (B) VALUES (1)");
            using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM P INNER JOIN C3 ON P.A = C3.B";
            Assert.Equal(1, Convert.ToInt32(count.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ------------------------------------------------------------------ ALTER COLUMN re-lay

    // BuildRelaidRecord sized the variable section from the ROW's stored numVar while the retyped column's
    // descriptor takes its index from the TDEF's 0x2B. ADD COLUMN is metadata-only, so every pre-existing row
    // carries fewer slots than 0x2B — and the retyped column then landed on the wrong slot, which is the file
    // ACE rejects with "A column Id is incorrect".
    [Fact]
    public void A_retype_after_adding_a_variable_column_keeps_rows_readable()
    {
        string path = TemporaryDatabase.CreatePath("relay-addcol-");
        try
        {
            CreateWithAce(path, ["CREATE TABLE T (K LONG, A LONG, B TEXT(30))"]);
            using (var connection = AceTestDatabase.Open(path))
                Exec(connection, "INSERT INTO T (K, A, B) VALUES (1, 42, 'b1')");

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.True(db.AddColumn("T", new ColumnSpec("C", JetDataType.Text, 30, IsFixedLength: false)));
                db.AlterColumnTypeInPlace("T", "A", new ColumnSpec("A", JetDataType.Text, 20, IsFixedLength: false));
            }

            using var ace = AceTestDatabase.Open(path);
            using var read = ace.CreateCommand();
            read.CommandText = "SELECT A, B FROM T WHERE K = 1";
            using OleDbDataReader reader = read.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("42", reader.GetString(0));
            Assert.Equal("b1", reader.GetString(1));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The same function derived hasVar from the schema alone, dropping the "ColumnId < storedCount" qualifier
    // its two siblings carry. A row written before the table's first variable ADD has no variable trailer at
    // all, so parsing it as though it had one reads fixed bytes as an offset table.
    [Fact]
    public void A_retype_reads_rows_written_before_the_first_variable_column()
    {
        string path = TemporaryDatabase.CreatePath("relay-hasvar-");
        try
        {
            CreateWithAce(path, ["CREATE TABLE T (K LONG, A LONG, N LONG)"]);   // all fixed: no trailer
            using (var connection = AceTestDatabase.Open(path))
                Exec(connection, "INSERT INTO T (K, A, N) VALUES (1, 42, 7)");

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.True(db.AddColumn("T", new ColumnSpec("M", JetDataType.Text, 30, IsFixedLength: false)));
                db.AlterColumnTypeInPlace("T", "A", new ColumnSpec("A", JetDataType.Double, 8, IsFixedLength: true));
            }

            using var ace = AceTestDatabase.Open(path);
            using var read = ace.CreateCommand();
            read.CommandText = "SELECT A, N FROM T WHERE K = 1";
            using OleDbDataReader reader = read.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(42d, reader.GetDouble(0));
            Assert.Equal(7, reader.GetInt32(1));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The re-lay pinned the fixed-region length to rows[0] and applied it to every row. ADD COLUMN of a FIXED
    // column is metadata-only, so a table legitimately holds short rows written before it alongside full-width
    // ones — and one length for both truncates the long rows or drags the short rows' variable data upward.
    [Fact]
    public void A_retype_handles_rows_of_two_different_fixed_widths()
    {
        string path = TemporaryDatabase.CreatePath("relay-mixed-width-");
        try
        {
            CreateWithAce(path, ["CREATE TABLE T (K LONG, A LONG, B TEXT(30))"]);
            using (var connection = AceTestDatabase.Open(path))
                Exec(connection, "INSERT INTO T (K, A, B) VALUES (1, 11, 'first')");

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.True(db.AddColumn("T", new ColumnSpec("X", JetDataType.Int32, 4, IsFixedLength: true)));
                Insert(db, "T", ("K", 2), ("A", 22), ("B", "second"), ("X", 99));   // now a WIDER row
                db.AlterColumnTypeInPlace("T", "A", new ColumnSpec("A", JetDataType.Double, 8, IsFixedLength: true));
            }

            using var ace = AceTestDatabase.Open(path);
            using var read = ace.CreateCommand();
            read.CommandText = "SELECT K, A, B, X FROM T ORDER BY K";
            using OleDbDataReader reader = read.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(11d, reader.GetDouble(1));
            Assert.Equal("first", reader.GetString(2));
            Assert.True(reader.IsDBNull(3));                 // written before X existed
            Assert.True(reader.Read());
            Assert.Equal(22d, reader.GetDouble(1));
            Assert.Equal("second", reader.GetString(2));
            Assert.Equal(99, reader.GetInt32(3));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The re-lay reaches RowEncoder.AssembleRow but never RowEncoder.Encode, where the declared-width check
    // used to live — so a narrowing retype wrote values wider than the column declares.
    [Fact]
    public void A_retype_that_would_overflow_the_new_width_is_refused()
    {
        string path = TemporaryDatabase.CreatePath("relay-width-");
        try
        {
            CreateWithAce(path, ["CREATE TABLE T (K LONG, N LONG)"]);
            using (var connection = AceTestDatabase.Open(path))
                Exec(connection, "INSERT INTO T (K, N) VALUES (1, 1234567890)");

            using var db = JetDatabase.Open(path, readOnly: false);
            var error = Assert.Throws<InvalidOperationException>(() => db.AlterColumnTypeInPlace(
                "T", "N", new ColumnSpec("N", JetDataType.Text, 4, IsFixedLength: false)));   // 2 characters
            output.WriteLine(error.Message);
            Assert.Contains("too small to accept", error.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Narrowing a variable column was a bare descriptor edit that never looked at the rows, leaving values
    // wider than the declaration — exactly what the insert-time check exists to prevent.
    [Fact]
    public void Narrowing_a_column_below_its_stored_values_is_refused()
    {
        string path = TemporaryDatabase.CreatePath("narrow-");
        try
        {
            CreateWithAce(path, ["CREATE TABLE T (K LONG, V TEXT(100))"]);
            using (var connection = AceTestDatabase.Open(path))
                Exec(connection, "INSERT INTO T (K, V) VALUES (1, 'a value far longer than five')");

            using var db = JetDatabase.Open(path, readOnly: false);
            var error = Assert.Throws<InvalidOperationException>(() => db.AlterColumn(
                "T", "V", new ColumnSpec("V", JetDataType.Text, 10, IsFixedLength: false)));
            output.WriteLine(error.Message);
            Assert.Contains("cannot be narrowed", error.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Retyping AWAY from DECIMAL left precision and scale sitting in the descriptor's LANGID bytes, because
    // only the decimal arm of that type-keyed union was ever written.
    [Fact]
    public void Retyping_away_from_decimal_restores_the_collation_bytes()
    {
        string path = TemporaryDatabase.CreatePath("decimal-union-");
        try
        {
            using var db = JetDatabase.Open(CreateWithAce(path,
                ["CREATE TABLE T (K LONG, D DECIMAL(12,3))"]), readOnly: false);

            db.AlterColumnTypeInPlace("T", "D", new ColumnSpec("D", JetDataType.Text, 50, IsFixedLength: false));
            db.Catalog.Invalidate();

            ColumnDef retyped = db.Catalog.FindTable("T")!.FindColumn("D")!;
            output.WriteLine($"collation after retype: {retyped.Collation}");
            Assert.Equal(db.Collation, retyped.Collation);   // NOT 0x030C, the old precision/scale pair
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ------------------------------------------------------------------ property blob

    // Adding a table CHECK dropped the whole owner-"" property block and wrote back only CheckConstraints,
    // taking every other table-level property with it — ValidationRule above all, which LibRed reads and
    // reports but does not re-emit.
    // No public API authors a designer ValidationRule, so this pins the invariant where the defect actually
    // was: the read-modify-write over the property list. The old code did RemoveOwner("") then re-added only
    // CheckConstraints, which took every other table-level property with it.
    [Fact]
    public void Replacing_the_check_property_keeps_the_tables_other_properties()
    {
        byte[] blob = Catalog.PropertyBlob.Write(
        [
            new Catalog.PropertyBlob.Property("", Catalog.PropertyBlob.ValidationRuleProperty, "[V]>0"),
            new Catalog.PropertyBlob.Property("", Catalog.PropertyBlob.ValidationTextProperty, "V must be positive"),
            new Catalog.PropertyBlob.Property("V", Catalog.PropertyBlob.DefaultValueProperty, "0"),
        ]);

        // The same shape the CHECK paths now use: replace one table-owned property, leave the rest alone.
        var properties = Catalog.PropertyBlob.Read(blob).ToList();
        properties.RemoveAll(p => p.Owner.Length == 0 && p.Name == Catalog.PropertyBlob.CheckConstraintsProperty);
        properties.Add(new Catalog.PropertyBlob.Property(
            "", Catalog.PropertyBlob.CheckConstraintsProperty,
            Catalog.PropertyBlob.WriteCheckList([("CK_T", "V < 100")])));
        byte[] updated = Catalog.PropertyBlob.Write(properties, blob.AsSpan(0, 4));

        (string? rule, string? text) = Catalog.PropertyBlob.ReadValidation(updated, "");
        Assert.Equal("[V]>0", rule);
        Assert.Equal("V must be positive", text);
        Assert.Contains(Catalog.PropertyBlob.ReadCheckConstraints(updated), c => c.Name == "CK_T");
        Assert.Contains(Catalog.PropertyBlob.Read(updated), p => p.Owner == "V" && p.Value == "0");
        Assert.Equal(blob[..4], updated[..4]);          // and the signature is carried across, not restamped
    }

    // ------------------------------------------------------------------ helpers

    private static RelationshipSpec Fk(string name, string parent) =>
        new(name, parent, [("B", "A")], IsEnforced: true, CascadeUpdate: false, CascadeDelete: false);

    private static string CreateWithAce(string path, string[] ddl)
    {
        DatabaseCreator.CreateEmpty(path);
        using var connection = AceTestDatabase.Open(path);
        foreach (string sql in ddl) Exec(connection, sql);
        return path;
    }

    private static void Insert(JetDatabase db, string table, params (string Column, object Value)[] cells)
    {
        Table t = db.OpenTable(table);
        var values = new object?[t.Definition.Columns.Count];
        foreach ((string column, object value) in cells)
            values[t.Definition.FindColumn(column)!.Index] = value;
        t.Insert(values);
    }

    private static byte PageZero(string path, int offset)
    {
        using var channel = PageChannel.Open(path, readOnly: true);
        return channel.ReadPage(0).Span[offset];
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
