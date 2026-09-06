using System.Data.OleDb;
using Xunit;

namespace LibRed.Engine.Tests;

[Collection(AceCollection.Name)]
public class ColumnIdBoundaryAccessTests : TempDatabaseTest
{
    [Theory]
    [InlineData("LONGCHAR", false)]
    [InlineData("LONGCHAR", true)]
    [InlineData("LONGBINARY", false)]
    [InlineData("LONGBINARY", true)]
    public void Long_value_identity_alter_consumes_the_last_id(string type, bool libred)
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "long-column-id-");
        string create = "CREATE TABLE ColumnBoundary (C0 " + type + "," + string.Join(",", Enumerable.Range(1, 253).Select(i => $"C{i} BYTE")) + ")";
        string alter = $"ALTER TABLE ColumnBoundary ALTER COLUMN C0 {type}";
        object payload = type == "LONGCHAR" ? new string('x', 8000) : Enumerable.Range(0, 8000).Select(i => (byte)i).ToArray();
        if (libred)
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new LibRed.Engine.QueryEngine(db);
            engine.ExecuteNonQuery(create);
            engine.ExecuteNonQuery("INSERT INTO ColumnBoundary (C0, C1) VALUES (@p, 7)", new Dictionary<string, object?> { ["p"] = payload });
            byte[] original = Snapshot(path);
            db.BeginTransaction();
            engine.ExecuteNonQuery(alter);
            db.Rollback();
            Assert.Equal(original, Snapshot(path));
            engine.ExecuteNonQuery(alter);
            byte[] before = Snapshot(path);
            Assert.Throws<NotSupportedException>(() => engine.ExecuteNonQuery(alter));
            Assert.Equal(before, Snapshot(path));
        }
        else
        {
            using var connection = AceTestDatabase.Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = create;
            command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO ColumnBoundary (C0, C1) VALUES (?, 7)";
            command.Parameters.Add("p", type == "LONGCHAR" ? OleDbType.LongVarWChar : OleDbType.LongVarBinary, 8000).Value = payload;
            command.ExecuteNonQuery();
            command.Parameters.Clear();
            command.CommandText = alter;
            command.ExecuteNonQuery();
            Assert.Contains("Too many fields", Assert.Throws<OleDbException>(() => command.ExecuteNonQuery()).Message);
        }
        using var reopened = AceTestDatabase.Open(path);
        using var read = reopened.CreateCommand();
        read.CommandText = "SELECT C1 FROM ColumnBoundary";
        Assert.Equal(7, Convert.ToInt32(read.ExecuteScalar()));
        read.CommandText = "SELECT C0 FROM ColumnBoundary";
        if (payload is string text) Assert.Equal(text, Assert.IsType<string>(read.ExecuteScalar()));
        else Assert.Equal((byte[])payload, Assert.IsType<byte[]>(read.ExecuteScalar()));
    }

    // The NOT NULL rows matter beyond covering another declaration: nullability never reaches the ALTER
    // spec (the SQL layer builds it with NotNull false and applies Required separately), so comparing it
    // when deciding whether an identity ALTER is a no-op silently excluded every required column — the
    // same statement burned an id, and threw here at 255, purely because the column was required.
    [Theory]
    [InlineData("BYTE", false, true)]
    [InlineData("BIT", false, true)]
    [InlineData("SHORT", false, true)]
    [InlineData("LONG", false, true)]
    [InlineData("LONG", true, true)]
    [InlineData("SINGLE", false, true)]
    [InlineData("DOUBLE", false, true)]
    [InlineData("CURRENCY", false, true)]
    [InlineData("DATETIME", false, true)]
    [InlineData("GUID", false, true)]
    [InlineData("TEXT(10)", false, true)]
    [InlineData("TEXT(10)", true, true)]
    [InlineData("BINARY(10)", false, true)]
    [InlineData("DECIMAL(10,2)", false, true)]
    [InlineData("DECIMAL(10,2)", true, true)]
    [InlineData("LONGCHAR", false, false)]
    [InlineData("LONGBINARY", false, false)]
    public void LibRed_identity_alter_matches_measured_ACE_at_exhaustion(string type, bool required, bool accepted)
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "libred-id-identity-");
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            var engine = new LibRed.Engine.QueryEngine(database);
            string declaration = required ? $"{type} NOT NULL" : type;
            engine.ExecuteNonQuery("CREATE TABLE ColumnBoundary (C0 " + declaration + "," + string.Join(",", Enumerable.Range(1, 254).Select(i => $"C{i} BYTE")) + ")");
            engine.ExecuteNonQuery(required
                ? $"INSERT INTO ColumnBoundary (C0, C1) VALUES ({SampleFor(type)}, 7)"
                : "INSERT INTO ColumnBoundary (C1) VALUES (7)");
            byte[] before = Snapshot(path);
            string alter = $"ALTER TABLE ColumnBoundary ALTER COLUMN C0 {type}";
            if (accepted) engine.ExecuteNonQuery(alter);
            else Assert.Throws<NotSupportedException>(() => engine.ExecuteNonQuery(alter));
            Assert.Equal(before, Snapshot(path));
        }
        using var connection = AceTestDatabase.Open(path);
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT C1 FROM ColumnBoundary";
        Assert.Equal(7, Convert.ToInt32(read.ExecuteScalar()));
    }

    [Theory]
    [InlineData(254)]
    [InlineData(255)]
    public void LibRed_retyping_enforces_the_ACE_id_limit_and_preserves_the_file(int columns)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "libred-column-id-");
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            var engine = new LibRed.Engine.QueryEngine(database);
            engine.ExecuteNonQuery("CREATE TABLE ColumnBoundary (" + string.Join(",", Enumerable.Range(0, columns).Select(i => $"C{i} LONG")) + ")");
            engine.ExecuteNonQuery("INSERT INTO ColumnBoundary (C0) VALUES (7)");
            if (columns == 254) engine.ExecuteNonQuery("ALTER TABLE ColumnBoundary ALTER COLUMN C0 SHORT");
            byte[] before = Snapshot(path);
            string nextType = columns == 254 ? "LONG" : "SHORT";
            Assert.Throws<NotSupportedException>(() => engine.ExecuteNonQuery($"ALTER TABLE ColumnBoundary ALTER COLUMN C0 {nextType}"));
            Assert.Equal(before, Snapshot(path));
        }
        using var reopened = AceTestDatabase.Open(path);
        using var read = reopened.CreateCommand();
        read.CommandText = "SELECT C0 FROM ColumnBoundary";
        Assert.Equal(7, Convert.ToInt32(read.ExecuteScalar()));
    }

    // ACE's own answer to the question the theory above asserts. Every other ACE arm here uses nullable
    // columns, so without this the NOT NULL rows would be asserting inferred behaviour: that an identity
    // ALTER at an exhausted high-water is accepted whatever the column's nullability.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ace_identity_alter_at_exhaustion_ignores_nullability(bool required)
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-id-identity-");
        using var connection = AceTestDatabase.Open(path);
        using var command = connection.CreateCommand();

        command.CommandText = "CREATE TABLE ColumnBoundary (C0 " + (required ? "LONG NOT NULL" : "LONG") + ","
            + string.Join(",", Enumerable.Range(1, 254).Select(i => $"C{i} BYTE")) + ")";
        command.ExecuteNonQuery();
        command.CommandText = required
            ? "INSERT INTO ColumnBoundary (C0, C1) VALUES (1, 7)"
            : "INSERT INTO ColumnBoundary (C1) VALUES (7)";
        command.ExecuteNonQuery();

        command.CommandText = "ALTER TABLE ColumnBoundary ALTER COLUMN C0 LONG";
        Exception? failure = Record.Exception(() => command.ExecuteNonQuery());

        Assert.True(
            failure is null,
            $"ACE rejected an identity ALTER on a {(required ? "NOT NULL" : "nullable")} column at 255 columns: "
            + $"{failure?.Message.Trim()} — so nullability does matter, and the identity short-circuit in "
            + "AlterColumn must take it into account after all.");
    }

    /// <summary>A literal a NOT NULL column of <paramref name="type"/> will accept, so the required rows can
    /// seed C0 as well.</summary>
    private static string SampleFor(string type) => type switch
    {
        "TEXT(10)" => "'abc'",
        "DECIMAL(10,2)" => "1.25",
        _ => "1",
    };

    private static byte[] Snapshot(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var bytes = new MemoryStream();
        file.CopyTo(bytes);
        return bytes.ToArray();
    }

}
