using System.Data.OleDb;
using Xunit;

namespace LibRed.Engine.Tests;

// ACE clips a parameter to its declared DbParameter.Size; LibRedParameter.EffectiveValue matches that in
// both SQL modes. These are the measurements it holds to.
//
// Each case proves the clip positively - an over-long value whose prefix IS a stored value must still
// match it - rather than inferring it from a query that found nothing, which a rejected parameter or a
// malformed predicate would produce just as well.
[Collection(AceCollection.Name)]
public class ParameterSizeAccessTests : TempDatabaseTest
{
    /// <summary>Rows in Customers whose City equals <paramref name="value"/>, passed as a parameter carrying
    /// <paramref name="size"/> (0 = leave Size unset, so the driver derives it from the value).</summary>
    private static int CountByCity(OleDbConnection connection, string value, int size)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Customers WHERE City = ?";

        var parameter = new OleDbParameter { ParameterName = "city", Value = value };
        if (size != 0) parameter.Size = size;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    [Fact]
    public void Ace_string_parameter_versus_declared_size()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "paramsize-");

        using OleDbConnection connection = AceTestDatabase.Open(path);

        // Controls, so a surprising answer below cannot be blamed on the fixture or the placeholder syntax.
        int seattle = CountByCity(connection, "Seattle", size: 0);
        int sea = CountByCity(connection, "Sea", size: 0);
        Assert.True(seattle > 0, $"Northwind should have a Seattle customer; found {seattle}.");
        Assert.Equal(0, sea);

        int exact = CountByCity(connection, "Seattle", size: 7);
        int undersized = CountByCity(connection, "Seattle", size: 3);

        Assert.True(
            undersized == sea,
            $"ACE no longer clips a string parameter to Size: 'Seattle' with Size=3 matched {undersized} row(s), "
            + $"where the truncated literal 'Sea' matches {sea}. (Size=7 matched {exact}, Size unset {seattle}.) "
            + "If ACE really has changed, LibRedParameter.EffectiveValue should stop clipping too.");

        Assert.Equal(seattle, exact);

        // Ten characters whose first seven are a real city: only a genuine clip matches its rows.
        int overlong = CountByCity(connection, "SeattleXYZ", size: 7);
        Assert.True(
            overlong == seattle,
            $"'SeattleXYZ' with Size=7 matched {overlong} row(s), not the {seattle} that 'Seattle' matches - "
            + "so the zero above was not truncation.");
    }

    // The other direction: a Size LARGER than the value is a maximum, not a width. If ACE padded instead,
    // EffectiveValue - which only clips - would be wrong for every parameter EF sizes from a column's max
    // length. ADO.NET's -1 ("max") must behave the same way.
    [Fact]
    public void Ace_does_not_pad_a_string_parameter_up_to_an_oversized_size()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "paramsizemax-");

        using OleDbConnection connection = AceTestDatabase.Open(path);

        int seattle = CountByCity(connection, "Seattle", size: 0);
        Assert.True(seattle > 0, $"Northwind should have a Seattle customer; found {seattle}.");

        Assert.Equal(seattle, CountByCity(connection, "Seattle", size: 20));
        Assert.Equal(seattle, CountByCity(connection, "Seattle", size: -1));
    }

    private static int CountByValue(OleDbConnection connection, byte[] value, int size)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ParamSizeProbe WHERE V = ?";

        var parameter = new OleDbParameter { ParameterName = "v", Value = value };
        if (size != 0) parameter.Size = size;
        command.Parameters.Add(parameter);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    // The same question for a binary parameter. Access has no variable-length binary type that can be compared
    // (its only one is OLE Object, which cannot appear in a predicate), so this uses a fixed BINARY column and
    // establishes the comparison shape with controls before asking anything about Size - a fixed column
    // zero-pads, so "does an exact-length value even match" has to be answered first, not assumed.
    [Fact]
    public void Ace_binary_parameter_versus_declared_size()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "paramsizebin-");

        using OleDbConnection connection = AceTestDatabase.Open(path);
        using (OleDbCommand ddl = connection.CreateCommand())
        {
            ddl.CommandText = "CREATE TABLE ParamSizeProbe (Id LONG PRIMARY KEY, V BINARY(5))";
            ddl.ExecuteNonQuery();
        }

        byte[] stored = [1, 2, 3, 4, 5];
        using (OleDbCommand insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO ParamSizeProbe (Id, V) VALUES (1, ?)";
            insert.Parameters.Add(new OleDbParameter { ParameterName = "v", Value = stored });
            insert.ExecuteNonQuery();
        }

        int exact = CountByValue(connection, stored, size: 0);
        Assert.True(exact == 1, $"A BINARY(5) column did not match its own 5-byte value ({exact} row(s)) - the "
            + "comparison shape is wrong, so nothing below would mean anything.");

        int prefix = CountByValue(connection, [1, 2, 3], size: 0);
        int undersized = CountByValue(connection, stored, size: 3);

        Assert.True(
            undersized == prefix,
            $"ACE now treats a binary parameter's Size differently from a string's: the full 5 bytes with Size=3 "
            + $"matched {undersized} row(s) where the literal 3-byte prefix matches {prefix}. If binary has "
            + "diverged, EffectiveValue must stop clipping byte[] while still clipping string.");

        // Eight bytes whose first five are the stored value: again, only a genuine clip matches.
        int overlong = CountByValue(connection, [1, 2, 3, 4, 5, 9, 9, 9], size: 5);
        Assert.True(
            overlong == exact,
            $"An 8-byte value with Size=5 matched {overlong} row(s), not the {exact} that its 5-byte prefix "
            + "matches - so ACE does not clip a binary parameter to Size, whatever it does for text.");
    }
}
