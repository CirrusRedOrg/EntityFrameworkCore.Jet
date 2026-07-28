namespace LibRed.Sql.Binding;

/// <summary>Thrown when a statement references a table or column that does not exist.</summary>
public sealed class SqlBindException(string message) : Exception(message);
