using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace LibRed.Data;

/// <summary>ADO.NET command that runs SQL through the LibRed engine.</summary>
public sealed class LibRedCommand : DbCommand
{
    private readonly LibRedParameterCollection _parameters = new();

    private string _commandText = string.Empty;

    [AllowNull]
    public override string CommandText
    {
        get => _commandText;
        set => _commandText = value ?? string.Empty;
    }
    public override int CommandTimeout { get; set; } = 30;
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;

    protected override DbConnection? DbConnection { get; set; }
    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }

    public new LibRedConnection? Connection
    {
        get => (LibRedConnection?)DbConnection;
        set => DbConnection = value;
    }

    public override void Cancel() { }

    public override void Prepare() { }

    public override int ExecuteNonQuery() => RequireEngine().ExecuteNonQuery(CommandText, BuildParameters());

    public override object? ExecuteScalar()
    {
        using var reader = ExecuteReader();
        return reader.Read() && reader.FieldCount > 0 ? reader.GetValue(0) : null;
    }

    protected override DbParameter CreateDbParameter() => new LibRedParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        var result = RequireEngine().ExecuteQuery(CommandText, BuildParameters());
        return new LibRedDataReader(result);
    }

    private Engine.QueryEngine RequireEngine() =>
        Connection?.Engine ?? throw new InvalidOperationException("Connection is not open.");

    /// <summary>Snapshots the command's parameters as a name→value map for the engine,
    /// translating <see cref="DBNull"/> to a SQL null.</summary>
    private IReadOnlyDictionary<string, object?> BuildParameters()
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (LibRedParameter parameter in _parameters.Cast<LibRedParameter>())
            map[parameter.ParameterName] = parameter.Value is DBNull ? null : parameter.Value;
        return map;
    }
}
