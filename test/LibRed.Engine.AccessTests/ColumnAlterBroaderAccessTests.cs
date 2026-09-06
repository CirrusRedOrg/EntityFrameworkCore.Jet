using System.Data.OleDb;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

[Collection(AceCollection.Name)]
public class ColumnAlterBroaderAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    [Theory]
    [InlineData("none")]
    [InlineData("other")]
    [InlineData("incoming")]
    public void Target_default_required_unique_and_foreign_keys_match_ACE(string relationship)
    {
        string ace = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-alter-rel-");
        using (var c = AceTestDatabase.Open(ace))
        {
            Exec(c, "CREATE TABLE Parent (Code LONG PRIMARY KEY, Ref TEXT(20) UNIQUE)");
            Exec(c, "INSERT INTO Parent VALUES (7, '123')");
            Exec(c, "INSERT INTO Parent VALUES (8, '456')");
            Exec(c, "CREATE TABLE Probe (Id LONG PRIMARY KEY, Payload TEXT(20) NOT NULL DEFAULT '123', Code LONG)");
            Exec(c, "CREATE UNIQUE INDEX UX_Payload ON Probe (Payload)");
            Exec(c, "INSERT INTO Probe (Id, Code) VALUES (1, 7)");
            if (relationship == "other") Exec(c, "ALTER TABLE Probe ADD CONSTRAINT FK_Other FOREIGN KEY (Code) REFERENCES Parent (Code)");
            if (relationship == "incoming")
            {
                Exec(c, "CREATE TABLE Child (Id LONG PRIMARY KEY, ProbeId LONG REFERENCES Probe (Id))");
                Exec(c, "INSERT INTO Child VALUES (1, 1)");
            }
        }
        string libred = TemporaryDatabase.CopyPath(ace, "libred-alter-rel-");
        const string alter = "ALTER TABLE Probe ALTER COLUMN Payload LONGCHAR";
        Exception? aceError;
        using (var c = AceTestDatabase.Open(ace)) aceError = Record.Exception(() => Exec(c, alter));
        Exception? libredError;
        using (var db = JetDatabase.Open(libred, readOnly: false))
            libredError = Record.Exception(() => new QueryEngine(db).ExecuteNonQuery(alter));
        output.WriteLine($"{relationship}: ACE={aceError?.Message ?? "accepted"}; LibRed={libredError?.Message ?? "accepted"}");
        Assert.Equal(aceError is null, libredError is null);
        foreach (string path in new[] { ace, libred })
        {
            using var c = AceTestDatabase.Open(path);
            // The retained default must still produce '123', colliding with the retained unique index.
            Assert.Throws<OleDbException>(() => Exec(c, "INSERT INTO Probe (Id, Code) VALUES (2, 8)"));
            Assert.Throws<OleDbException>(() => Exec(c, "INSERT INTO Probe VALUES (2, NULL, 8)"));
            if (relationship == "other") Assert.Throws<OleDbException>(() => Exec(c, "INSERT INTO Probe VALUES (2, '456', 999)"));
            if (relationship == "incoming") Assert.Throws<OleDbException>(() => Exec(c, "DELETE FROM Probe WHERE Id = 1"));
            Exec(c, "INSERT INTO Probe VALUES (2, '456', 8)");
            using var query = c.CreateCommand();
            query.CommandText = "SELECT Id FROM Probe WHERE Payload = '456'";
            Assert.Equal(2, Convert.ToInt32(query.ExecuteScalar()));
            Exec(c, "INSERT INTO Parent VALUES (9, '789')");
            Exec(c, "UPDATE Probe SET Payload = '789' WHERE Id = 1");
            Exec(c, "INSERT INTO Probe (Id, Code) VALUES (3, 7)");
            query.CommandText = "SELECT Payload FROM Probe WHERE Id = 3";
            Assert.Equal("123", Convert.ToString(query.ExecuteScalar()));
        }
    }

    [Theory]
    [InlineData("TEXT(20)", "LONGCHAR", "123", false)]
    [InlineData("TEXT(20)", "LONGCHAR", "123", true)]
    [InlineData("LONGCHAR", "TEXT(20)", "123", false)]
    [InlineData("LONG", "LONGCHAR", "123", false)]
    [InlineData("LONGCHAR", "LONG", "123", false)]
    [InlineData("LONGCHAR", "LONG", "abc", false)]
    [InlineData("LONGCHAR", "TEXT(2)", "abcdef", false)]
    [InlineData("BINARY(10)", "LONGBINARY", "binary", false)]
    [InlineData("LONGBINARY", "BINARY(10)", "binary", false)]
    public void Conversion_matches_ACE_and_preserves_other_constraints(string from, string to, string value, bool indexed)
    {
        string ace = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-alter-broad-");
        using (var connection = AceTestDatabase.Open(ace))
        {
            Exec(connection, $"CREATE TABLE Probe (Id LONG PRIMARY KEY, Payload {from}, Code LONG NOT NULL, Stamp LONG DEFAULT 17, CONSTRAINT CK_Code CHECK (Code > 0))");
            Exec(connection, "CREATE UNIQUE INDEX UX_Code ON Probe (Code)");
            if (indexed) Exec(connection, "CREATE INDEX IX_Payload ON Probe (Payload)");
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO Probe (Id, Payload, Code) VALUES (1, ?, 7)";
            object payload = value == "binary" ? new byte[] { 1, 2, 3 } : from == "LONG" ? 123 : value;
            insert.Parameters.Add("p", value == "binary" ? OleDbType.VarBinary : from == "LONG" ? OleDbType.Integer : OleDbType.VarWChar).Value = payload;
            insert.ExecuteNonQuery();
        }
        string libred = TemporaryDatabase.CopyPath(ace, "libred-alter-broad-");
        byte[] before = File.ReadAllBytes(libred);
        string sql = $"ALTER TABLE Probe ALTER COLUMN Payload {to}";
        Exception? aceError;
        using (var connection = AceTestDatabase.Open(ace)) aceError = Record.Exception(() => Exec(connection, sql));
        Exception? libredError;
        using (var db = JetDatabase.Open(libred, readOnly: false))
            libredError = Record.Exception(() => new QueryEngine(db).ExecuteNonQuery(sql));
        output.WriteLine($"ACE: {aceError?.Message ?? "accepted"}; LibRed: {libredError?.Message ?? "accepted"}");
        Assert.Equal(aceError is null, libredError is null);
        Assert.Equal(Read(ace), Read(libred));
        if (libredError is not null) Assert.Equal(before, File.ReadAllBytes(libred));
        foreach (string path in new[] { ace, libred })
        {
            using var connection = AceTestDatabase.Open(path);
            Assert.Throws<OleDbException>(() => Exec(connection, "INSERT INTO Probe (Id, Code) VALUES (2, 7)"));
            Assert.Throws<OleDbException>(() => Exec(connection, "INSERT INTO Probe (Id, Code) VALUES (2, -1)"));
            Assert.Throws<OleDbException>(() => Exec(connection, "INSERT INTO Probe (Id) VALUES (2)"));
            Exec(connection, "INSERT INTO Probe (Id, Code) VALUES (2, 8)");
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Stamp FROM Probe WHERE Id = 2";
            Assert.Equal(17, Convert.ToInt32(command.ExecuteScalar()));
        }
    }

    private static string Read(string path)
    {
        using var connection = AceTestDatabase.Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Payload, Code, Stamp FROM Probe ORDER BY Id";
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
            for (int i = 0; i < reader.FieldCount; i++)
            {
                object value = reader.GetValue(i);
                result.Add(value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!);
            }
        return string.Join("|", result);
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
