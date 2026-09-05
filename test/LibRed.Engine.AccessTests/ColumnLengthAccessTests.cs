using System.Data.OleDb;
using Xunit;

namespace LibRed.Engine.Tests;

// ACE rejects a value longer than a variable column's declared width — it neither stores nor clips it.
// RowEncoder.EnsureFitsDeclaredLength matches that; these are the measurements it holds to.
//
// The text case uses a literal, not a parameter: parameter Size has its own clipping rule
// (ParameterSizeAccessTests) and would confound the answer.
[Collection(AceCollection.Name)]
public class ColumnLengthAccessTests : TempDatabaseTest
{
    [Fact]
    public void Ace_variable_text_column_versus_an_overlong_value()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "collen-");

        using OleDbConnection connection = AceTestDatabase.Open(path);
        using (OleDbCommand ddl = connection.CreateCommand())
        {
            ddl.CommandText = "CREATE TABLE LenProbe (Id LONG PRIMARY KEY, V TEXT(5))";
            ddl.ExecuteNonQuery();
        }

        // Control: five must go in cleanly, so the rejection below is the overrun and not TEXT(5) meaning
        // something other than five characters.
        using (OleDbCommand ok = connection.CreateCommand())
        {
            ok.CommandText = "INSERT INTO LenProbe (Id, V) VALUES (1, 'abcde')";
            ok.ExecuteNonQuery();
        }

        string outcome;
        try
        {
            using OleDbCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO LenProbe (Id, V) VALUES (2, 'abcdef')";
            insert.ExecuteNonQuery();

            using OleDbCommand read = connection.CreateCommand();
            read.CommandText = "SELECT V FROM LenProbe WHERE Id = 2";
            var stored = (string?)read.ExecuteScalar();
            outcome = stored is null
                ? "accepted, stored NULL"
                : $"accepted, stored '{stored}' ({stored.Length} chars)";
        }
        catch (OleDbException ex)
        {
            outcome = $"rejected: {ex.Message.Trim()}";
        }

        Assert.True(
            outcome.StartsWith("rejected", StringComparison.Ordinal),
            $"ACE no longer rejects six characters in a TEXT(5) column - it {outcome}. "
            + "RowEncoder.EnsureFitsDeclaredLength should then stop rejecting them too.");

        // The wording LibRed's own rejection is modelled on.
        Assert.Contains("too small", outcome, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Inserts <paramref name="value"/>, reporting what ACE did rather than throwing.</summary>
    private static string TryInsert(OleDbConnection connection, string table, int id, object value)
    {
        try
        {
            using OleDbCommand insert = connection.CreateCommand();
            insert.CommandText = $"INSERT INTO {table} (Id, V) VALUES ({id}, ?)";
            // No explicit Size: the driver derives it from the value, so parameter clipping cannot interfere.
            insert.Parameters.Add(new OleDbParameter { ParameterName = "v", Value = value });
            insert.ExecuteNonQuery();

            using OleDbCommand read = connection.CreateCommand();
            read.CommandText = $"SELECT V FROM {table} WHERE Id = {id}";
            object? stored = read.ExecuteScalar();
            int length = stored switch { byte[] b => b.Length, string s => s.Length, _ => -1 };
            return $"accepted, stored {length} unit(s)";
        }
        catch (OleDbException ex)
        {
            return $"rejected: {ex.Message.Trim()}";
        }
    }

    // Measured separately rather than assumed from the text result: a length check applied to the wrong
    // column kind would be its own bug.
    [Fact]
    public void Ace_variable_binary_column_versus_an_overlong_value()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "collenbin-");

        using OleDbConnection connection = AceTestDatabase.Open(path);
        using (OleDbCommand ddl = connection.CreateCommand())
        {
            ddl.CommandText = "CREATE TABLE LenProbeBin (Id LONG PRIMARY KEY, V VARBINARY(5))";
            ddl.ExecuteNonQuery();
        }

        string control = TryInsert(connection, "LenProbeBin", 1, new byte[] { 1, 2, 3, 4, 5 });
        Assert.True(control.StartsWith("accepted", StringComparison.Ordinal),
            $"A VARBINARY(5) column would not take five bytes ({control}) - the shape is wrong.");

        string overlong = TryInsert(connection, "LenProbeBin", 2, new byte[] { 1, 2, 3, 4, 5, 6 });

        Assert.True(
            overlong.StartsWith("rejected", StringComparison.Ordinal),
            $"ACE no longer rejects six bytes in a VARBINARY(5) column - it {overlong}, where five gave "
            + $"'{control}'. Text is still rejected, so the check would need splitting per column kind.");
    }
}
