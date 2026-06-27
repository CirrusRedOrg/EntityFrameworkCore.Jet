namespace LibRed.Sql.Parsing;

/// <summary>Thrown when SQL text cannot be parsed.</summary>
public sealed class SqlParseException(string message, int line = 0, int column = 0)
    : Exception(message)
{
    public int Line { get; } = line;
    public int Column { get; } = column;
}
