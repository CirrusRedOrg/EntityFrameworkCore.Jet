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
        ValidateTransaction();
        Engine.QueryEngine engine = RequireEngine();
        IReadOnlyDictionary<string, object?> parameters = BuildParameters();

        Engine.CommandResult? last = null;
        foreach (string statement in SplitStatements(CommandText))
        {
            // A fragment holding no statement (only comments) is skipped rather than run: it must not become
            // the batch's last result, or `INSERT …; -- done` would report the comment's zero rows instead of
            // the insert's. A batch that is entirely comments falls through to the empty result below.
            if (engine.IsStatementless(statement)) continue;

            try
            {
                last = engine.Execute(statement, parameters);
                Connection?.ReconcileSqlTransactionControl();
            }
            catch (LibRed.ConstraintViolationException e)
            {
                // ADO.NET callers expect a DbException for a database-operation error, and provider code
                // has to be able to recognise a duplicate key without reading the message: EF Core's
                // migration lock treats losing the INSERT race as the normal path and retries, so an
                // unrecognised constraint failure there turns contention into a hard failure.
                throw new LibRedException(e.Message, LibRedException.DuplicateKey, e);
            }
            catch (LibRed.SchemaObjectExistsException e)
            {
                // Same contract for DDL name collisions. EF Core's migration lock creates its lock table
                // behind an exists-then-create check that several connections can pass at once, and catches
                // the losers' "already exists" as DbException. Left untranslated this escapes that guard and
                // fails the migration outright — ACE raises OleDbException there, so translating is what
                // makes LibRed behave like the engine it stands in for.
                throw new LibRedException(e.Message, LibRedException.ObjectAlreadyExists, e);
            }
        }

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

    /// <summary>Rejects executing under a transaction that isn't the one active on this command's connection —
    /// one from another connection, or one already committed/rolled back (after which the connection no longer
    /// holds it). A command with no transaction assigned runs directly on the connection (autocommit), which the
    /// engine still makes atomic per statement.</summary>
    private void ValidateTransaction()
    {
        if (DbTransaction is null) return;
        if (!ReferenceEquals(DbTransaction, Connection?.CurrentTransaction))
            throw new InvalidOperationException(
                "The transaction assigned to this command is not active on its connection — it belongs to another " +
                "connection or has already been committed or rolled back.");
    }

    /// <summary>Snapshots the command's parameters as a name→value map for the engine,
    /// translating <see cref="DBNull"/> to a SQL null.</summary>
    private IReadOnlyDictionary<string, object?> BuildParameters()
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (LibRedParameter parameter in _parameters.Cast<LibRedParameter>())
            map[parameter.ParameterName] = Normalize(parameter.Value);
        return map;
    }

    /// <summary>The OLE epoch (1899-12-30): Jet stores every temporal as a DateTime relative to it — a time as
    /// the epoch date + time-of-day, a date at midnight.</summary>
    private static readonly DateTime OleEpoch = new(1899, 12, 30);

    /// <summary>Coerces a parameter value to what the engine should see. Jet/ACE has no native TimeSpan, TimeOnly,
    /// DateOnly or DateTimeOffset — they are all stored as a <see cref="DateTime"/> on the 1899-12-30 epoch — so
    /// this boundary (the single point EF parameters enter the engine) converts each to that DateTime, exactly as
    /// the literal path does (a TimeSpan literal renders as a <c>#…#</c>/TIMEVALUE DateTime). The engine then only
    /// ever handles DateTime for temporals, and the reader converts back on the way out. Sub-seconds are stripped
    /// (Jet has 1-second resolution) so a <c>WHERE d = @p</c> comparison matches the seconds-only stored value.</summary>
    private static object? Normalize(object? value) => value switch
    {
        DBNull => null,
        DateTime d => Seconds(d),
        // DateTimeOffset is read back at offset zero, so store its UTC instant.
        DateTimeOffset dto => Seconds(dto.UtcDateTime),
        TimeSpan t => OleEpoch + Seconds(t),
        TimeOnly to => OleEpoch + Seconds(to.ToTimeSpan()),
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        _ => value,
    };

    private static DateTime Seconds(DateTime d) => d.AddTicks(-(d.Ticks % TimeSpan.TicksPerSecond));
    private static TimeSpan Seconds(TimeSpan t) => TimeSpan.FromTicks(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond);
}
