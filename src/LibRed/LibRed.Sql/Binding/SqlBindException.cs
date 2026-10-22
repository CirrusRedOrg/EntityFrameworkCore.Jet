namespace LibRed.Sql.Binding;

/// <summary>Thrown when a statement references a table or column that does not exist.</summary>
public sealed class SqlBindException(string message) : Exception(message)
{
    /// <summary>ACE's message for a table or query a statement reads from that does not exist — the same whether it is
    /// named in a FROM, a join, a subquery or an EXEC (verified).</summary>
    public static string InputNotFound(string name) =>
        $"The Microsoft Access database engine cannot find the input table or query '{name}'. Make sure it exists and "
        + "that its name is spelled correctly.";
}
