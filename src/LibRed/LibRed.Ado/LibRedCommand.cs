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

    public override int ExecuteNonQuery() => ExecuteBatch().RecordsAffected;

    public override object? ExecuteScalar()
    {
        using var reader = ExecuteReader();
        return reader.Read() && reader.FieldCount > 0 ? reader.GetValue(0) : null;
    }

    protected override DbParameter CreateDbParameter() => new LibRedParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        // Route through Execute so the reader path also handles DML/DDL: EF Core runs inserts through
        // ExecuteReader and inspects RecordsAffected. A query yields rows (RecordsAffected -1); an
        // INSERT/CREATE runs and yields an empty result carrying its rows-affected count.
        Engine.CommandResult result = ExecuteBatch();
        return new LibRedDataReader(result.Rows, result.RecordsAffected);
    }

    /// <summary>
    /// Runs the command's text as a batch: Jet/ACE (and the LibRed engine) execute one statement at a
    /// time, but EF Core sends multiple statements in a single command — most notably an INSERT followed
    /// by a guarded SELECT that reads the store-generated key back via <c>@@ROWCOUNT</c>/<c>@@IDENTITY</c>.
    /// Each statement runs through the same engine, so its connection-scoped session state (the two system
    /// variables) carries from the INSERT to the SELECT. The batch's result is its <em>last</em> statement's
    /// — the SELECT the caller reads — matching how a real database returns the final result set.
    /// </summary>
    private Engine.CommandResult ExecuteBatch()
    {
        Engine.QueryEngine engine = RequireEngine();
        IReadOnlyDictionary<string, object?> parameters = BuildParameters();

        Engine.CommandResult? last = null;
        foreach (string statement in SplitStatements(CommandText))
            last = engine.Execute(statement, parameters);

        return last ?? new Engine.CommandResult(Engine.Execution.ResultSet.Empty, RecordsAffected: 0);
    }

    /// <summary>
    /// Splits a batch on top-level <c>;</c> separators, ignoring semicolons inside string literals
    /// (<c>'…'</c> / <c>"…"</c>) and quoted identifiers (<c>[…]</c> / <c>`…`</c>). Blank statements
    /// (e.g. a trailing <c>;</c>) are dropped. The single-statement common case returns one item.
    /// </summary>
    public static IEnumerable<string> SplitStatements(string sql)
    {
        int start = 0;
        char quote = '\0'; // the closing delimiter we're inside, or '\0' at top level
        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else if (c is '\'' or '"' or '`') quote = c;
            else if (c == '[') quote = ']';
            else if (c == ';')
            {
                string part = sql[start..i].Trim();
                if (part.Length > 0) yield return part;
                start = i + 1;
            }
        }

        string tail = sql[start..].Trim();
        if (tail.Length > 0) yield return tail;
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
