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
        // SchemaOnly asks what the command WOULD return: the reader carries the columns and no rows, and
        // nothing runs — not the INSERT in a batch, and not the stored procedure a name stands for.
        if (behavior.HasFlag(CommandBehavior.SchemaOnly))
            return new LibRedDataReader(DescribeBatch(), recordsAffected: -1, behavior, Connection);

        // Route through Execute so the reader path also handles DML/DDL: EF Core runs inserts through
        // ExecuteReader and inspects RecordsAffected. A query yields rows (RecordsAffected -1); an
        // INSERT/CREATE runs and yields an empty result carrying its rows-affected count.
        Engine.CommandResult result = ExecuteBatch();
        return new LibRedDataReader(result.Rows, result.RecordsAffected, behavior, Connection);
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
        foreach (string statement in SplitStatements(StatementText()))
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
    /// The shape the command's batch would return, running none of it. As in <see cref="ExecuteBatch"/> the
    /// batch's result is its <em>last</em> statement's — and since nothing runs, that is the only one worth
    /// describing.
    /// </summary>
    private Engine.Execution.ResultSet DescribeBatch()
    {
        ValidateTransaction();
        Engine.QueryEngine engine = RequireEngine();

        string? last = SplitStatements(StatementText()).LastOrDefault(s => !engine.IsStatementless(s));
        return last is null
            ? Engine.Execution.ResultSet.Empty
            : engine.Describe(last, BuildParameters());
    }

    /// <summary>
    /// The SQL this command runs, which for the two non-text command types is built from the name in
    /// <see cref="CommandText"/>: a stored procedure — an Access stored query — is executed by name with each
    /// of the command's parameters bound to the procedure's parameter of the same name, and a table is read
    /// whole. A name is bracket-quoted, so one containing spaces works as it does in Access.
    /// </summary>
    private string StatementText() => CommandType switch
    {
        CommandType.Text => CommandText,
        CommandType.TableDirect => $"SELECT * FROM {Quote(CommandText)}",
        CommandType.StoredProcedure => BuildExecute(),
        _ => throw new NotSupportedException($"CommandType.{CommandType} is not supported."),
    };

    /// <summary>An <c>EXECUTE</c> for the named stored query, naming each parameter so the order the caller
    /// added them in does not matter. A procedure taking none runs bare.</summary>
    private string BuildExecute()
    {
        var arguments = _parameters.Cast<LibRedParameter>()
            .Where(p => p.Direction is ParameterDirection.Input or ParameterDirection.InputOutput)
            .Select(p => $"{Quote(p.ParameterName.TrimStart('@'))} = @{p.ParameterName.TrimStart('@')}")
            .ToList();

        return arguments.Count == 0
            ? $"EXECUTE {Quote(CommandText)}"
            : $"EXECUTE {Quote(CommandText)} {string.Join(", ", arguments)}";
    }

    private static string Quote(string name) => $"[{name.Trim().Trim('[', ']')}]";

    /// <summary>
    /// Splits a batch on top-level <c>;</c> separators, ignoring semicolons inside string literals
    /// (<c>'…'</c> / <c>"…"</c>) and quoted identifiers (<c>[…]</c> / <c>`…`</c>). Blank statements
    /// (e.g. a trailing <c>;</c>) are dropped. The single-statement common case returns one item.
    /// <para>An Access <c>PARAMETERS …;</c> clause is <em>not</em> a statement of its own: its semicolon
    /// ends the clause, and the query it declares for follows. Such a fragment is carried onto the next one
    /// so the pair reaches the engine as the one statement it is — the form every stored parameterized
    /// query reads back as.</para>
    /// </summary>
    public static IEnumerable<string> SplitStatements(string sql)
    {
        int start = 0;
        char quote = '\0'; // the closing delimiter we're inside, or '\0' at top level
        string prefix = string.Empty; // a PARAMETERS clause awaiting its query
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
                if (part.Length > 0)
                {
                    if (IsParametersClause(part)) prefix += part + "; ";
                    else { yield return prefix + part; prefix = string.Empty; }
                }
                start = i + 1;
            }
        }

        string tail = sql[start..].Trim();
        // A clause with nothing after it is yielded as-is, so the engine reports it rather than the batch
        // silently running nothing.
        if (tail.Length > 0) yield return prefix + tail;
        else if (prefix.Length > 0) yield return prefix.TrimEnd(' ', ';');
    }

    private static bool IsParametersClause(string statement) =>
        statement.StartsWith("PARAMETERS", StringComparison.OrdinalIgnoreCase)
        && (statement.Length == "PARAMETERS".Length || char.IsWhiteSpace(statement["PARAMETERS".Length]));

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

    /// <summary>Snapshots the command's parameters as a name→value map for the engine, clipping each to its
    /// declared size (see <see cref="LibRedParameter.EffectiveValue"/>) and translating <see cref="DBNull"/> to
    /// a SQL null.</summary>
    private IReadOnlyDictionary<string, object?> BuildParameters()
    {
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (LibRedParameter parameter in _parameters.Cast<LibRedParameter>())
            map[parameter.ParameterName] = Normalize(parameter.EffectiveValue, parameter.DbType);
        return map;
    }

    /// <summary>
    /// Coerces a parameter value to what the engine should see. Jet/ACE has no native TimeSpan, TimeOnly,
    /// DateOnly or DateTimeOffset — they are all stored as a <see cref="DateTime"/> — so this boundary (the single
    /// point EF parameters enter the engine) converts DateOnly and DateTimeOffset to that DateTime, and the reader
    /// converts back on the way out. A TimeSpan or TimeOnly goes in as itself: the engine's parameter bag turns it
    /// into the time on the 1899-12-30 epoch wherever it is read — saved, compared, passed to a function — exactly
    /// as the literal path does, but first remembers it was a span, because a date less a span is a date where a
    /// date less a date is a day count.
    /// </summary>
    /// <remarks>
    /// Values are truncated to whole MILLISECONDS, not whole seconds. ACE has one-second resolution, but that is
    /// ACE truncating on write — LibRed stores the full OA double, and a millisecond survives it exactly
    /// (measured: 12:34:56.123 round-trips with zero tick loss). Below a millisecond nothing survives whatever
    /// this does, because .NET's ToOADate/FromOADate quantise there; truncating to the same boundary the store
    /// uses is what keeps <c>WHERE d = @p</c> matching, which is the reason this truncates at all.
    /// <para>A <see cref="DbType.DateTime2"/> parameter is the exception, as it is for SqlClient: a DATETIME2 column
    /// stores the value's 100-ns ticks rather than the OA double, so its sub-millisecond part is kept. The
    /// parameter has to say so — a DateTime value alone infers <see cref="DbType.DateTime"/> — because this
    /// boundary cannot see which column the value is for.</para>
    /// </remarks>
    private static object? Normalize(object? value, DbType dbType) => value switch
    {
        DBNull => null,
        DateTime d when dbType == DbType.DateTime2 => d,
        DateTime d => Milliseconds(d),
        // DateTimeOffset is read back at offset zero, so store its UTC instant.
        DateTimeOffset dto => Milliseconds(dto.UtcDateTime),
        TimeSpan t => Milliseconds(t),
        TimeOnly to => TimeOnly.FromTimeSpan(Milliseconds(to.ToTimeSpan())),
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        _ => value,
    };

    private static DateTime Milliseconds(DateTime d) => d.AddTicks(-(d.Ticks % TimeSpan.TicksPerMillisecond));

    private static TimeSpan Milliseconds(TimeSpan t) =>
        TimeSpan.FromTicks(t.Ticks - t.Ticks % TimeSpan.TicksPerMillisecond);
}
