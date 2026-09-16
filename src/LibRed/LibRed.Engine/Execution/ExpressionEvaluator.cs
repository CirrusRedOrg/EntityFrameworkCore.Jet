using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EntityFrameworkCore.Jet.Data;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>What ACE takes a result to be, as far as its decimal places go (see
/// <see cref="ExpressionEvaluator.NumberTypeOf"/>).</summary>
internal enum NumberClass
{
    Other,
    Whole,
    Text,
    Date,
    Decimal,
    Currency,
    Double,
}

/// <summary>A result's <see cref="NumberClass"/>, with its places when it is a Decimal.</summary>
internal readonly record struct NumberType(NumberClass Class, int Places = 0);

/// <summary>
/// Evaluates an AST <see cref="Expression"/> against a single row, resolving column
/// references through an <see cref="EvalScope"/> (which chains to outer scopes for
/// correlation). Comparisons coerce numeric operands; SQL nulls propagate (a comparison
/// involving null yields null, treated as "not true" by filters).
/// </summary>
internal sealed class ExpressionEvaluator(
    EvalScope scope,
    IScalarSubqueryRunner subqueries,
    ParameterBag? parameters = null,
    SessionState? session = null)
{
    /// <summary>Rebinds this evaluator's scope to a new row of the same schema and returns the evaluator, so a
    /// hot loop can reuse one evaluator across rows instead of allocating a fresh evaluator + scope per row.</summary>
    public ExpressionEvaluator Rebind(object?[] row)
    {
        scope.Rebind(row);
        return this;
    }

    public object? Evaluate(Expression expression) => expression switch
    {
        LiteralExpression l => l.Value,
        ColumnReference c => scope.TryResolve(c, out object? v) ? v
            : TryNiladicFunction(c, out object? nv) ? nv
            : throw new InvalidOperationException($"Column '{EvalScope.Describe(c)}' was not found."),
        ScalarSubquery s => subqueries.ExecuteScalar(s.Query, scope),
        ExistsExpression e => subqueries.ExecuteExists(e.Query, scope),
        InSubqueryExpression i => EvaluateInSubquery(i),
        InListExpression i => EvaluateInList(i),
        BetweenExpression be => EvaluateBetween(be),
        CaseExpression c => EvaluateCase(c),
        FunctionCall f => EvaluateFunction(f),
        UnaryExpression u => EvaluateUnary(u),
        BinaryExpression b => EvaluateBinary(b),
        ParameterExpression p => parameters is not null
            ? parameters.Resolve(p.Name)
            : throw new InvalidOperationException($"No parameters were supplied for '{p.Name}'."),
        SystemVariableExpression v => ResolveSystemVariable(v.Name),
        _ => throw new NotSupportedException($"Cannot evaluate {expression.GetType().Name}."),
    };

    /// <summary>Access's <c>Now</c> is a niladic function callable without parentheses — so a bare unqualified
    /// identifier that isn't a column but names it evaluates as the current timestamp. Matches ACE, which accepts
    /// e.g. <c>DATETIME DEFAULT Now</c> and <c>SELECT Now</c>. Only tried after column resolution fails, so a real
    /// column named "Now" still wins. Note <c>Date</c>/<c>Time</c> are NOT included: they are reserved type
    /// keywords in Jet SQL and ACE rejects them bare ("Type mismatch") — they require parentheses (<c>Date()</c>,
    /// <c>Time()</c>), which parse as function calls and are handled in <see cref="EvaluateFunction"/>.</summary>
    private static bool TryNiladicFunction(ColumnReference c, out object? value)
    {
        if (c.Table is null && c.Column.Equals("Now", StringComparison.OrdinalIgnoreCase))
        {
            value = DateTime.Now;
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>Resolves a connection-scoped system variable from the session state: <c>@@ROWCOUNT</c>
    /// (rows affected by the previous statement) and <c>@@IDENTITY</c> (the last AutoNumber generated on
    /// this connection, or NULL if none). EF Core's insert round-trip reads both in the SELECT that
    /// follows the INSERT within the same batch.</summary>
    private object? ResolveSystemVariable(string name)
    {
        if (session is null)
            throw new InvalidOperationException($"System variable '@@{name}' is not available in this context.");

        return name.ToUpperInvariant() switch
        {
            "ROWCOUNT" => session.RowCount,
            "IDENTITY" => session.LastIdentity,
            _ => throw new NotSupportedException($"Unknown system variable '@@{name}'."),
        };
    }

    /// <summary><c>x [NOT] IN (subquery)</c> with SQL three-valued semantics: NULL if x is null or (no match
    /// and the subquery yields a null), otherwise the membership result (negated for NOT IN).</summary>
    private object? EvaluateInSubquery(InSubqueryExpression inq)
    {
        object? val = Evaluate(inq.Value);
        if (val is null) return null;

        bool hasNull = false, found = false;
        // A correlated IN is a semi-join: hash the body's values once instead of re-running it for every outer row.
        if (subqueries.ExecuteInSubquery(inq.Query, inq.Value, val, scope) is var (semiFound, semiNull))
        {
            (found, hasNull) = (semiFound, semiNull);
        }
        else
        {
            // An uncorrelated body is hoisted by ExecuteColumn and runs once; what used to cost outer × inner
            // was the membership test over its values, repeated per outer row. Hoisting also builds a hash set,
            // so ask that first and only walk the values when it declines (mixed or non-hashable kinds).
            IEnumerable<object?> items = subqueries.ExecuteColumn(inq.Query, scope);
            if (subqueries.LookupHoistedIn(inq.Query, val) is var (setFound, setNull))
            {
                (found, hasNull) = (setFound, setNull);
            }
            else
            {
                foreach (object? item in items)
                {
                    if (item is null) hasNull = true;
                    else if (Compare(val, item) == 0) { found = true; break; }
                }
            }
        }

        bool? result = found ? true : hasNull ? null : false;
        return inq.Negated ? (result is null ? null : !result) : result;
    }

    /// <summary><c>x [NOT] IN (a, b, …)</c> over a list, evaluated iteratively (not as a recursive OR-tree) so a huge
    /// list can't overflow the stack. Null when x is; otherwise whether an item equals x, with each pair brought to a
    /// common kind as <c>=</c> does (verified vs ACE). Unlike the standard, a Null item is skipped rather than making a
    /// miss Null: <c>5 IN (1, NULL)</c> is False and <c>5 NOT IN (1, NULL)</c> True. A <c>True</c> or <c>False</c> item
    /// is compared as -1 or 0, not as a truth test.</summary>
    private object? EvaluateInList(InListExpression inl)
    {
        object? val = Evaluate(inl.Value);
        if (val is null) return null;

        bool found = false;
        foreach (Expression itemExpr in inl.Items)
        {
            if (Evaluate(itemExpr) is { } item && CompareAsKinds(val, item) == 0) { found = true; break; }
        }
        return found != inl.Negated;
    }

    /// <summary><c>x [NOT] BETWEEN a AND b</c>: whether x lies between the two bounds inclusive, in whichever order
    /// they are written, with each pair brought to a common kind as a comparison does. Null when any of the three is
    /// (verified vs ACE: <c>15 BETWEEN NULL AND 10</c> is Null, not False).</summary>
    private object? EvaluateBetween(BetweenExpression be)
    {
        object? val = Evaluate(be.Value), low = Evaluate(be.Low), high = Evaluate(be.High);
        if (val is null || low is null || high is null) return null;

        int toLow = CompareAsKinds(val, low), toHigh = CompareAsKinds(val, high);
        bool inside = (toLow >= 0 && toHigh <= 0) || (toLow <= 0 && toHigh >= 0);
        return inside != be.Negated;
    }

    /// <summary>Standard SQL <c>CASE</c>. Arms are tested in order and the first whose condition is true wins;
    /// an arm is skipped when its condition is false <em>or</em> NULL, since only true selects. Evaluation
    /// short-circuits — later conditions and every unselected result go unevaluated, which matters because a
    /// result can divide by zero or otherwise throw in a branch the condition exists to avoid. With nothing
    /// matched and no ELSE, the answer is NULL rather than an error, per the standard.</summary>
    private object? EvaluateCase(CaseExpression c)
    {
        foreach (CaseWhen arm in c.WhenClauses)
        {
            if (IsTrue(arm.Condition))
                return Evaluate(arm.Result);
        }

        return c.ElseResult is null ? null : Evaluate(c.ElseResult);
    }

    private object? EvaluateFunction(FunctionCall f)
    {
        // Aggregate calls are precomputed per group and resolved by reference — including an outer
        // aggregate found in an enclosing scope (a correlated subquery referencing MAX(o.Col), etc.).
        if (scope.TryResolveAggregate(f, out object? aggregate))
            return aggregate;

        // VBA "$" variants (Left$, UCase$, Chr$, …) return a String instead of a Variant but compute the same
        // value in the Jet expression service — so a trailing "$" is stripped and dispatched to the base name.
        string name = f.Name.ToUpperInvariant();
        if (name.Length > 1 && name[^1] == '$') name = name[..^1];
        ValidateArity(name, f.Arguments.Count);

        return name switch
        {
            "IIF" => IsTrue(f.Arguments[0]) ? Evaluate(f.Arguments[1])
                : f.Arguments.Count == 3 ? Evaluate(f.Arguments[2]) : null,
            "CHOOSE" => Choose(f),
            "SWITCH" => Switch(f),
            "NULLIF" => NullIf(f),
            "COALESCE" => Coalesce(f),
            "GREATEST" => Extreme(f, greatest: true),
            "LEAST" => Extreme(f, greatest: false),
            "DATEPART" => DatePart(Evaluate(f.Arguments[0]), Evaluate(f.Arguments[1])),
            "ROUND" => Round(f),
            "FIX" => Numeric1(f, Math.Truncate, Math.Truncate),  // toward zero
            "INT" => Numeric1(f, Math.Floor, Math.Floor),        // toward -infinity
            "ABS" => Numeric1(f, Math.Abs, Math.Abs),
            // VBA/Access type-conversion functions. All propagate NULL. CInt/CLng/CByte round half-to-even
            // ("banker's rounding"), which is exactly what Convert.ToInt16/Int32/Byte do. CVar is a no-op
            // passthrough (LibRed has no distinct Variant type).
            // A Boolean argument goes through Numeric() first, so True converts as VARIANT_BOOL -1 rather than
            // .NET's 1 (verified vs ACE: CInt/CLng/CDbl/CSng/CCur(True) are all -1, and CByte(True) overflows
            // because a byte cannot hold -1 — which Convert.ToByte(-1) raises for us).
            "CCUR" => Convert1(f, v => Math.Round(Dec(v), 4)),  // to Currency (decimal, 4 dp)
            "CBOOL" => Convert1(f, v => VbaBool(v)),
            "CBYTE" => Convert1(f, v => Convert.ToByte(Numeric(v), CultureInfo.InvariantCulture)),
            "CINT" => Convert1(f, v => Convert.ToInt16(Numeric(v), CultureInfo.InvariantCulture)),
            "CLNG" => Convert1(f, v => Int(v)),
            "CSNG" => Convert1(f, v => Sng(v)),
            "CDBL" => Convert1(f, v => Dbl(v)),
            // CDec has no ACE equivalent — the Jet Expression Service has no such function — so this is a
            // LibRed extension with no parity contract to honour. CCur is ACE's route to a decimal.
            "CDEC" => Convert1(f, v => Dec(v)),
            "CSTR" => Convert1(f, VbaString),
            "CDATE" => Convert1(f, ToDate),
            "CVAR" => Evaluate(f.Arguments[0]), // passthrough (no Variant type)

            // VBA/Access string functions. All propagate NULL; positions are 1-based. Comparisons default to
            // case-insensitive (Access "Option Compare Database" = Text), overridable by a compare argument.
            // ToText, not ToString: a binary column's value is a UTF-16 STRING to every text function, so
            // Len(0x4100) is 1 (one character) where LenB is 2. Calling ToString() on a byte[] yields the
            // literal "System.Byte[]", which silently produced nonsense — Len returned 13 for every value.
            "LEN" => Convert1(f, v => ToText(v).Length),
            "LCASE" => Convert1(f, v => ToText(v).ToLowerInvariant()),
            "UCASE" => Convert1(f, v => ToText(v).ToUpperInvariant()),
            "TRIM" => Convert1(f, v => ToText(v).Trim(' ')),
            "LTRIM" => Convert1(f, v => ToText(v).TrimStart(' ')),
            "RTRIM" => Convert1(f, v => ToText(v).TrimEnd(' ')),
            "LEFT" => StringInt(f, static (s, n) => n <= 0 ? "" : n >= s.Length ? s : s[..n]),
            "RIGHT" => StringInt(f, static (s, n) => n <= 0 ? "" : n >= s.Length ? s : s[^n..]),
            "MID" => Mid(f),
            "INSTR" => Instr(f),
            "REPLACE" => Replace(f),

            // Date/time functions (VBA/Access). All propagate NULL on a date argument.
            "DATEADD" => DateAdd(f),
            "DATEDIFF" => DateDiff(f),
            "DATESERIAL" => DateParts(f, (y, m, d) => new DateTime(y, 1, 1).AddMonths(m - 1).AddDays(d - 1)),
            "TIMESERIAL" => DateParts(f, (h, m, s) => DateTime.FromOADate(0).AddHours(h).AddMinutes(m).AddSeconds(s)),
            "NOW" => DateTime.Now,
            "DATE" => DateTime.Today,
            "TIME" => DateTime.FromOADate(0).Add(DateTime.Now.TimeOfDay),
            "YEAR" => DatePartOf(f, d => d.Year),
            "MONTH" => DatePartOf(f, d => d.Month),
            "DAY" => DatePartOf(f, d => d.Day),
            "HOUR" => DatePartOf(f, d => d.Hour),
            "MINUTE" => DatePartOf(f, d => d.Minute),
            "SECOND" => DatePartOf(f, d => d.Second),
            "WEEKDAY" => DatePartOf(f, d => (int)d.DayOfWeek + 1), // Access: Sunday = 1
            // DateValue = the date at midnight; TimeValue = the time on the Jet epoch (1899-12-30) — both
            // NULL-propagating and verified against ACE. IsDate is a predicate (true only for a date or a
            // date/time-parseable string; a number, NULL or unparseable string is false — verified vs ACE).
            "DATEVALUE" => Convert1(f, v => ((DateTime)ToDate(v)).Date),
            "TIMEVALUE" => Convert1(f, v => DateTime.FromOADate(0).Add(((DateTime)ToDate(v)).TimeOfDay)),
            "ISDATE" => IsDateValue(Evaluate(f.Arguments[0])),
            // Jet VBA math functions (double precision). SQR = sqrt, ATN = atan, LOG = natural log.
            // Acos/Asin/Atan2/Floor/Ceiling/Log10/Log-base are emitted by EF as expressions built from
            // these plus arithmetic, so they need no dedicated cases.
            "SIN" => UnaryDouble(f, Math.Sin),
            "COS" => UnaryDouble(f, Math.Cos),
            "TAN" => UnaryDouble(f, Math.Tan),
            "ATN" => UnaryDouble(f, Math.Atan),
            "EXP" => UnaryDouble(f, Math.Exp),
            "LOG" => UnaryDouble(f, Math.Log),
            "SQR" => UnaryDouble(f, Math.Sqrt),
            // SGN sits apart from the group above: it takes a double but yields an Integer, both in VBA
            // (Sgn returns Variant/Integer) and in .NET (Math.Sign returns int). Going through UnaryDouble
            // widened that int straight back to a double, which only showed once EF projected the value
            // instead of comparing it - GetInt32 on a boxed Double throws.
            "SGN" => Convert1(f, v => Math.Sign(Convert.ToDouble(v, CultureInfo.InvariantCulture))),

            // More VBA/Access built-ins (verified vs ACE via the function-whitelist sweep). All NULL-propagating
            // via Convert1 unless noted; positions are 1-based.
            "ASC" => Convert1(f, v => (int)v.ToString()![0]),
            "CHR" => Convert1(f, v => ((char)Convert.ToInt32(v, CultureInfo.InvariantCulture)).ToString()),
            "SPACE" => Convert1(f, v => new string(' ', Convert.ToInt32(v, CultureInfo.InvariantCulture))),
            "STRING" => StringOf(f),                         // String(count, char) → char repeated count times
            "STRREVERSE" => Convert1(f, v => new string(v.ToString()!.Reverse().ToArray())),
            "STRCOMP" => StrComp(f),                         // -1/0/1 (case-insensitive, Access "Compare Database")
            "STR" => Convert1(f, VbaStr),                    // number → text with a leading space when non-negative
            "VAL" => Convert1(f, v => VbaVal(v.ToString()!)),// parse the leading numeric portion (Double), else 0
            "HEX" => Convert1(f, v => Convert.ToString(Convert.ToInt64(v, CultureInfo.InvariantCulture), 16).ToUpperInvariant()),
            "OCT" => Convert1(f, v => Convert.ToString(Convert.ToInt64(v, CultureInfo.InvariantCulture), 8)),
            "INSTRREV" => InstrRev(f),                       // last occurrence, 1-based (0 if none)
            // MonthName(month, [abbreviate]). The second argument was accepted by the arity table and then
            // ignored by Convert1, so MonthName(1, True) returned "January" where ACE returns "Jan" — a
            // silently wrong value, and the arity check that would have caught a stray argument is what let
            // it through. WeekdayName next to it always honoured its own.
            "MONTHNAME" => MonthNameOf(f),
            "TIMER" => (DateTime.Now - DateTime.Today).TotalSeconds,
            "RND" => Rnd(f),
            // Predicates / type inspection. IsError is always false — LibRed has no error-value type. ISNULL
            // returns a Boolean (ACE reports it as -1/0); both print as a boolean here.
            "ISNULL" => Evaluate(f.Arguments[0]) is null,
            "ISNUMERIC" => IsNumericValue(Evaluate(f.Arguments[0])),
            "ISERROR" => false,
            "TYPENAME" => TypeNameOf(Evaluate(f.Arguments[0])),
            "VARTYPE" => VarTypeOf(Evaluate(f.Arguments[0])),
            "STRCONV" => StrConv(f),
            "WEEKDAYNAME" => WeekdayNameOf(f),
            "PARTITION" => PartitionOf(f),
            "FORMAT" => FormatValue(f),
            "FORMATCURRENCY" => Convert.ToDecimal(FinArg(f, 0), CultureInfo.InvariantCulture).ToString("C" + FmtDigits(f, 1), CultureInfo.CurrentCulture),
            "FORMATNUMBER" => Convert.ToDouble(FinArg(f, 0), CultureInfo.InvariantCulture).ToString("N" + FmtDigits(f, 1), CultureInfo.CurrentCulture),
            "FORMATPERCENT" => FinArg(f, 0).ToString(FmtDigits(f, 1) > 0 ? "0." + new string('0', FmtDigits(f, 1)) + "%" : "0%", CultureInfo.CurrentCulture),
            "FORMATDATETIME" => FormatDateTimeFn(f),
            "RGB" => (int)(FinArg(f, 0) % 256) + ((int)(FinArg(f, 1) % 256) << 8) + ((int)(FinArg(f, 2) % 256) << 16),
            "QBCOLOR" => QbColor((int)FinArg(f, 0)),
            // Financial functions (verified vs ACE). rate is per period; pv/fv/pmt sign conventions follow VBA.
            "PMT" => Pmt(f),
            "FV" => Fv(f),
            "PV" => Pv(f),
            "NPER" => NPer(f),
            "IPMT" => IPmt(f),
            "PPMT" => PPmt(f),
            "RATE" => Rate(f),
            "SLN" => (FinArg(f, 0) - FinArg(f, 1)) / FinArg(f, 2),
            "SYD" => (FinArg(f, 0) - FinArg(f, 1)) * (FinArg(f, 2) - FinArg(f, 3) + 1) / (FinArg(f, 2) * (FinArg(f, 2) + 1) / 2),
            "DDB" => Ddb(f),

            // Wide (Unicode code-point) variants. AscW = the first char's code point; ChrW = the char for a code
            // point (unlike Chr, not restricted to a byte). Verified vs ACE: ChrW(233) → 'é'.
            "ASCW" => Convert1(f, v => (int)ToText(v)[0]),
            "CHRW" => Convert1(f, v => ((char)Convert.ToInt32(v, CultureInfo.InvariantCulture)).ToString()),
            // Byte variants operate on the UTF-16 byte layout (2 bytes/char): LenB = 2×length, AscB = the low
            // byte of the first char, and Left/Right/Mid/InStr count bytes. Verified vs ACE (LenB('abc')=6,
            // InStrB(1,'abc','b')=3). ChrB is intentionally absent — ACE's expression service has no ChrB.
            "ASCB" => Convert1(f, v => (int)ToBytes(v)[0]),
            "LENB" => Convert1(f, v => ToBytes(v).Length),
            "LEFTB" => ByteLeft(f),
            "RIGHTB" => ByteRight(f),
            "MIDB" => ByteMid(f),
            "INSTRB" => InstrB(f),

            // GenUniqueID(): Access's random-Long generator. Not callable in a SELECT (ACE errors "Undefined
            // function") but valid as a LONG column's DEFAULT, where it yields a random signed Int32 per row —
            // the mechanism behind a "Random" AutoNumber. Accepted on a plain LONG default too (ACE allows it
            // only on a LONG column). AutoNumber columns take their random value in the row inserter instead.
            "GENUNIQUEID" => RandomLong(),
            // GenGUID(): Access's GUID generator, the sibling of GenUniqueID(). Same shape — ACE errors
            // "Undefined function 'GenGUID' in expression" in a SELECT, but it is valid as a GUID column's
            // DEFAULT, where it yields a fresh Guid per row (verified vs ACE). EF Core models it as
            // HasDefaultValueSql("GenGUID()") for store-generated Guid keys.
            "GENGUID" => Guid.NewGuid(),
            _ => throw new NotSupportedException($"Function {f.Name} is not supported."),
        };
    }

    /// <summary>Rejects argument counts verified against ACE. Keep this table evidence-driven: add a function
    /// only after its minimum/maximum have been exercised through ACE, since Jet includes quirks such as IIf's
    /// accepted two-argument form (the omitted false branch is Null).</summary>
    internal static void ValidateArity(string name, int count)
    {
        (int Min, int Max)? range = name switch
        {
            // Conversion, unary numeric/string/date/inspection functions and single-argument aliases.
            "CBOOL" or "CBYTE" or "CINT" or "CLNG" or "CSNG" or "CDBL" or "CCUR" or "CDEC"
                or "CSTR" or "CDATE" or "CVAR"
                or "ABS" or "SGN" or "INT" or "FIX" or "SQR" or "EXP" or "LOG" or "SIN" or "COS"
                or "TAN" or "ATN"
                or "LEN" or "LCASE" or "UCASE" or "TRIM" or "LTRIM" or "RTRIM" or "SPACE"
                or "STRREVERSE" or "STR" or "VAL" or "CHR" or "ASC" or "HEX" or "OCT"
                or "DATEVALUE" or "TIMEVALUE" or "YEAR" or "MONTH" or "DAY" or "HOUR" or "MINUTE"
                or "SECOND" or "ISDATE" or "ISNULL" or "ISNUMERIC" or "ISERROR" or "TYPENAME" or "VARTYPE"
                or "QBCOLOR" or "ASCW" or "CHRW" or "ASCB" or "LENB" => (1, 1),

            "LEFT" or "RIGHT" or "STRING" or "LEFTB" or "RIGHTB" => (2, 2),
            "MID" or "MIDB" => (2, 3),
            "INSTR" or "INSTRREV" or "INSTRB" => (2, 4),
            "STRCOMP" => (2, 3),
            "STRCONV" => (2, 3),
            "IIF" => (2, 3),
            "NULLIF" => (2, 2),
            "CHOOSE" => (2, int.MaxValue),
            "SWITCH" => (2, int.MaxValue),
            // COALESCE(expression [, ...n]). SQL Server insists on two, but one is harmless and the standard's
            // own grammar allows it, so only an empty list is rejected.
            "COALESCE" => (1, int.MaxValue),
            // GREATEST/LEAST(expression [, ...n]), as SQL Server and PostgreSQL take them: one argument or more.
            "GREATEST" or "LEAST" => (1, int.MaxValue),

            "NOW" or "DATE" or "TIME" or "TIMER" or "GENUNIQUEID" or "GENGUID" => (0, 0),
            "DATEADD" => (3, 3),
            "DATEDIFF" => (3, 5),
            "DATEPART" => (2, 4),
            "DATESERIAL" or "TIMESERIAL" => (3, 3),
            "WEEKDAY" or "MONTHNAME" => (1, 2),
            "WEEKDAYNAME" => (1, 3),

            "RGB" => (3, 3),
            "ROUND" => (1, 2),
            "RND" => (0, 1),
            "REPLACE" => (3, 6),
            "FORMAT" => (1, 4),
            "FORMATCURRENCY" or "FORMATNUMBER" or "FORMATPERCENT" => (1, 5),
            "FORMATDATETIME" => (1, 2),
            "PARTITION" => (4, 4),

            "PMT" or "FV" or "PV" or "NPER" => (3, 5),
            "IPMT" or "PPMT" => (4, 6),
            "RATE" => (3, 6),
            "SLN" => (3, 3),
            "SYD" => (4, 4),
            "DDB" => (4, 5),

            "COUNT" or "SUM" or "AVG" or "MIN" or "MAX" or "FIRST" or "LAST" or "STDEV" or "VAR"
                or "STDEVP" or "VARP" or "STDDEV" or "STDDEVP" => (1, 1),
            _ => null,
        };
        bool invalidPairs = name == "SWITCH" && count % 2 != 0;
        if (range is { } valid && (count < valid.Min || count > valid.Max || invalidPairs))
            throw new InvalidOperationException(
                $"Wrong number of arguments used with function {name} (expected " +
                (name == "SWITCH" ? "condition/value pairs" : valid.Min == valid.Max
                    ? valid.Min.ToString(CultureInfo.InvariantCulture)
                    : valid.Max == int.MaxValue ? $"at least {valid.Min}" : $"{valid.Min} to {valid.Max}") + ").");
    }

    /// <summary>Access <c>Choose(index, choice-1, choice-2, …)</c>: returns the 1-based choice at
    /// <paramref name="f"/>'s index, or NULL when the index is out of range (verified vs ACE: <c>Choose(0,…)</c>
    /// and <c>Choose(5,…)</c> on three choices both return Null). A NULL index is an error in ACE ("Data type
    /// mismatch"). Only the selected choice is evaluated.</summary>
    private object? Choose(FunctionCall f)
    {
        object? indexValue = Evaluate(f.Arguments[0]);
        if (indexValue is null)
            throw new InvalidOperationException("Data type mismatch in criteria expression: Choose() index is null.");
        int index = Convert.ToInt32(indexValue, CultureInfo.InvariantCulture);
        int choiceCount = f.Arguments.Count - 1;
        return index < 1 || index > choiceCount ? null : Evaluate(f.Arguments[index]);
    }

    /// <summary>
    /// <c>NULLIF(a, b)</c>: NULL when the two are equal, otherwise <c>a</c>.
    /// </summary>
    /// <remarks>
    /// A deliberate divergence from ACE, which has no such function — it answers "Undefined function 'NULLIF'
    /// in expression" (verified). Access's own spelling of this is <c>IIF(a = b, NULL, a)</c>, and that is what
    /// this evaluates to; the difference is only that LibRed also accepts the name EF Core emits, so a query
    /// using it runs here rather than failing at the engine.
    ///
    /// Equality follows the same comparison as the <c>=</c> operator, which makes the NULL cases fall out
    /// correctly without special-casing: comparing with a NULL is unknown rather than equal, so
    /// <c>NULLIF(x, NULL)</c> is <c>x</c> and <c>NULLIF(NULL, y)</c> is NULL — matching the IIF form, where an
    /// unknown condition takes the false branch.
    ///
    /// Unlike the IIF spelling, <c>a</c> is evaluated once.
    /// </remarks>
    private object? NullIf(FunctionCall f)
    {
        object? left = Evaluate(f.Arguments[0]);
        if (left is null) return null;

        object? right = Evaluate(f.Arguments[1]);
        return right is not null && Compare(left, right) == 0 ? null : left;
    }

    /// <summary>
    /// <c>COALESCE(a, b, …)</c> — the arguments in order, and the value of the first that is not NULL; NULL
    /// when every one of them is. Access/ACE has no COALESCE (its nearest equivalent is <c>Nz</c>, which takes
    /// only two), so this is reachable from LibRed's extended SQL mode and from hand-written SQL.
    /// </summary>
    /// <remarks>
    /// The standard defines COALESCE as shorthand for
    /// <c>CASE WHEN a IS NOT NULL THEN a WHEN b IS NOT NULL THEN b … END</c>, and SQL Server implements it by
    /// literally rewriting to that — which is why its docs warn that arguments are evaluated more than once
    /// and a subquery argument can yield different values between evaluations. Evaluating each argument once
    /// here gives the same answer with none of that, so the shorthand is honoured without inheriting the
    /// rewrite's cost or its instability.
    /// </remarks>
    private object? Coalesce(FunctionCall f)
    {
        foreach (Expression argument in f.Arguments)
        {
            object? value = Evaluate(argument);
            if (value is not null)
                return value;
        }

        return null;
    }

    /// <summary>
    /// <c>GREATEST(a, b, …)</c> and <c>LEAST(a, b, …)</c> — the largest or smallest of the arguments, compared
    /// as the <c>&lt;</c> and <c>&gt;</c> operators compare. Access/ACE has neither, so like COALESCE they are
    /// reachable from LibRed's extended SQL mode and from hand-written SQL.
    /// </summary>
    /// <remarks>
    /// NULL arguments are ignored and the answer is NULL only when every argument is NULL — SQL Server's and
    /// PostgreSQL's rule, and the one EF Core translates <c>Math.Max</c>/<c>Math.Min</c> and a <c>Max()</c>/
    /// <c>Min()</c> over an inline collection against. (MySQL and Oracle instead return NULL when any argument
    /// is NULL.) Every argument is evaluated, each once. Of equal values the first is returned.
    /// </remarks>
    private object? Extreme(FunctionCall f, bool greatest)
    {
        object? result = null;
        foreach (Expression argument in f.Arguments)
        {
            object? value = Evaluate(argument);
            if (value is null)
                continue;
            if (result is null || (greatest ? Compare(value, result) > 0 : Compare(value, result) < 0))
                result = value;
        }

        return result;
    }

    /// <summary>Access <c>Switch(cond-1, value-1, cond-2, value-2, …)</c>: evaluates the conditions left to
    /// right and returns the value paired with the first true one, or NULL if none is true (verified vs ACE).
    /// The argument count must be even (condition/value pairs) — an odd count is an error in ACE ("Wrong number
    /// of arguments"). Only the matched value is evaluated.</summary>
    private object? Switch(FunctionCall f)
    {
        if (f.Arguments.Count % 2 != 0)
            throw new InvalidOperationException(
                "Wrong number of arguments used with function Switch (expects condition/value pairs).");
        for (int i = 0; i < f.Arguments.Count; i += 2)
            if (IsTrue(f.Arguments[i]))
                return Evaluate(f.Arguments[i + 1]);
        return null;
    }

    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Access <c>String(count, character)</c>: <paramref name="f"/>'s first arg repeated. The character
    /// arg may be a string (first char used) or a character code. NULL-propagating.</summary>
    private object? StringOf(FunctionCall f)
    {
        object? countValue = Evaluate(f.Arguments[0]);
        object? charValue = Evaluate(f.Arguments[1]);
        if (countValue is null || charValue is null) return null;
        int count = Convert.ToInt32(countValue, CultureInfo.InvariantCulture);
        char ch = charValue is string s
            ? (s.Length > 0 ? s[0] : ' ')
            : (char)Convert.ToInt32(charValue, CultureInfo.InvariantCulture);
        return new string(ch, count);
    }

    /// <summary>Access <c>StrComp(a, b, [compare])</c>: -1/0/1 (NULL-propagating). compare=0 is a binary
    /// (case-sensitive) comparison; the default and every other mode are textual (case-insensitive, Access
    /// "Option Compare Database" = Text). Verified vs ACE (<c>StrComp('a','A',0)</c>=1, default=0).</summary>
    private object? StrComp(FunctionCall f)
    {
        object? a = Evaluate(f.Arguments[0]);
        object? b = Evaluate(f.Arguments[1]);
        if (a is null || b is null) return null;
        StringComparison cmp = f.Arguments.Count > 2
            && Convert.ToInt32(Evaluate(f.Arguments[2]), CultureInfo.InvariantCulture) == 0
            ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return Math.Sign(string.Compare(a.ToString(), b.ToString(), cmp));
    }

    /// <summary>Access <c>InStrRev(string1, string2, [start=-1], [compare])</c>: the 1-based position of the last
    /// occurrence of string2 in string1 (0 if not found), searching within the first <c>start</c> characters
    /// (the match must end at or before <c>start</c>; <c>start</c>=-1 means the whole string). Case-insensitive
    /// unless compare=0 (binary). Semantics verified vs ACE, including its quirks: an empty needle returns the
    /// effective start position; <c>start</c>=0 (or &lt;-1) → "Invalid procedure call"; and — unlike
    /// <c>InStr</c> — a NULL argument raises "Data type mismatch" rather than propagating NULL.</summary>
    private object? InstrRev(FunctionCall f)
    {
        object? s1v = Evaluate(f.Arguments[0]);
        object? s2v = Evaluate(f.Arguments[1]);
        if (s1v is null || s2v is null)
            throw new InvalidOperationException("Data type mismatch in criteria expression: InStrRev() argument is null.");
        string s1 = s1v.ToString()!, s2 = s2v.ToString()!;

        int start = f.Arguments.Count > 2 ? Convert.ToInt32(Evaluate(f.Arguments[2]), CultureInfo.InvariantCulture) : -1;
        if (start == -1) start = s1.Length;
        else if (start < 1)
            throw new InvalidOperationException("Invalid procedure call: InStrRev() start must be -1 or a positive position.");

        StringComparison cmp = f.Arguments.Count > 3
            && Convert.ToInt32(Evaluate(f.Arguments[3]), CultureInfo.InvariantCulture) == 0
            ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        if (s1.Length == 0) return 0;
        int window = Math.Min(start, s1.Length);            // search within Left(string1, start)
        if (s2.Length == 0) return window;                  // empty needle → the effective start position
        int idx = s1[..window].LastIndexOf(s2, cmp);
        return idx < 0 ? 0 : idx + 1;
    }

    /// <summary>VBA <c>Str(number)</c>: the number as text, with a leading space for non-negative values (VBA
    /// reserves that column for the sign).</summary>
    private static string VbaStr(object v)
    {
        double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
        string s = d.ToString(CultureInfo.InvariantCulture);
        return d >= 0 ? " " + s : s;
    }

    /// <summary>VBA <c>Val(string)</c>: the leading number as a Double (0 if none). VBA first strips ALL
    /// whitespace (so <c>"3 .1 4"</c> → 3.14, <c>"  -  5"</c> → -5), then reads the leading number — recognising
    /// <c>&amp;H</c> hex and <c>&amp;O</c> octal prefixes — and stops at the first character it can't use
    /// (verified vs ACE).</summary>
    private static object VbaVal(string s)
    {
        string t = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (t.Length == 0) return 0.0;

        Match hex = Regex.Match(t, @"^([+-]?)&[Hh]([0-9A-Fa-f]+)");
        if (hex.Success)
        {
            long h = Convert.ToInt64(hex.Groups[2].Value, 16);
            return (double)(hex.Groups[1].Value == "-" ? -h : h);
        }
        Match oct = Regex.Match(t, @"^([+-]?)&[Oo]([0-7]+)");
        if (oct.Success)
        {
            long o = Convert.ToInt64(oct.Groups[2].Value, 8);
            return (double)(oct.Groups[1].Value == "-" ? -o : o);
        }
        Match dec = Regex.Match(t, @"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?");
        return dec.Success && double.TryParse(dec.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? d : 0.0;
    }

    /// <summary>VBA <c>IsNumeric(value)</c>: true for a number or a numeric string.</summary>
    private static bool IsNumericValue(object? v) => v switch
    {
        null or bool => false,
        byte or short or int or long or float or double or decimal => true,
        _ => double.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out _),
    };

    /// <summary>VBA <c>TypeName(value)</c> — the Access type name (verified vs ACE, e.g. an Int32 literal → "Long").</summary>
    private static string TypeNameOf(object? v) => v switch
    {
        null => "Null",
        bool => "Boolean",
        byte => "Byte",
        short => "Integer",
        int or long => "Long",
        float => "Single",
        double => "Double",
        decimal => "Currency",
        DateTime => "Date",
        string => "String",
        // A binary column reports as String, not Byte[] — ACE's expression service sees the value as a
        // UTF-16 string (VarType 8 = VT_BSTR), so TypeName must say so even though LibRed holds a byte[].
        byte[] => "String",
        _ => v.GetType().Name,
    };

    /// <summary>VBA <c>VarType(value)</c> — the Access variant type code (vbLong=3, vbString=8, …).</summary>
    private static int VarTypeOf(object? v) => v switch
    {
        null => 1,          // vbNull
        bool => 11,         // vbBoolean
        byte => 17,         // vbByte
        short => 2,         // vbInteger
        int or long => 3,   // vbLong
        float => 4,         // vbSingle
        double => 5,        // vbDouble
        decimal => 6,       // vbCurrency
        DateTime => 7,      // vbDate
        _ => 8,             // vbString
    };

    /// <summary>Access <c>Format(value[, format])</c>. Named formats (Currency, Percent, Short Date, …) and the
    /// custom numeric/date/string format strings, driven off <see cref="CultureInfo.CurrentCulture"/> — as ACE
    /// drives them off the OS regional settings (so date/currency output is locale-dependent, matching ACE on a
    /// given host). Custom date formats translate VBA tokens to .NET (VBA <c>mm</c>=month/<c>nn</c>=minutes/
    /// <c>hh</c>=hour, plus <c>q</c>=quarter). NULL-propagating; no/empty format → the default string.</summary>
    private object? FormatValue(FunctionCall f)
    {
        object? value = Evaluate(f.Arguments[0]);
        if (value is null) return null;
        string? fmt = f.Arguments.Count > 1 ? Evaluate(f.Arguments[1])?.ToString() : null;
        if (string.IsNullOrEmpty(fmt)) return value.ToString();

        CultureInfo c = CultureInfo.CurrentCulture;
        // Named formats (case-insensitive). Numeric/boolean names first, then date/time names.
        switch (fmt.Trim().ToLowerInvariant())
        {
            case "general number": return Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString(c);
            case "currency": return Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("C", c);
            case "fixed": return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("0.00", c);
            case "standard": return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("#,##0.00", c);
            case "percent": return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("0.00%", c);
            case "scientific": return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("0.00E+00", c);
            case "yes/no": return IsZeroValue(value) ? "No" : "Yes";
            case "true/false": return IsZeroValue(value) ? "False" : "True";
            case "on/off": return IsZeroValue(value) ? "Off" : "On";
            case "general date": return ((DateTime)ToDate(value)).ToString(c);
            case "long date": return ((DateTime)ToDate(value)).ToString("D", c);
            case "medium date": return ((DateTime)ToDate(value)).ToString("dd-MMM-yy", c);
            case "short date": return ((DateTime)ToDate(value)).ToString("d", c);
            case "long time": return ((DateTime)ToDate(value)).ToString("T", c);
            case "medium time": return ((DateTime)ToDate(value)).ToString("hh:mm tt", c);
            case "short time": return ((DateTime)ToDate(value)).ToString("HH:mm", c);
        }

        // Custom format strings. '0'/'#' → numeric (VBA numeric tokens map ~directly to .NET). Otherwise a date
        // token letter → date format (VBA→.NET translation). Otherwise a string format ('>' upper, '<' lower).
        if (fmt.IndexOfAny(['0', '#']) >= 0)
            return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString(fmt, c);
        if (fmt.IndexOfAny(['y', 'Y', 'm', 'M', 'd', 'D', 'h', 'H', 'n', 'N', 's', 'S', 'q', 'Q']) >= 0)
        {
            DateTime dt = (DateTime)ToDate(value);
            return dt.ToString(TranslateVbaDateFormat(fmt, dt), c);
        }
        return fmt switch
        {
            ">" => value.ToString()!.ToUpperInvariant(),
            "<" => value.ToString()!.ToLowerInvariant(),
            _ => value.ToString(),
        };
    }

    /// <summary>True when a value is zero/false — for the Yes/No, True/False, On/Off named formats.</summary>
    private static bool IsZeroValue(object v) =>
        v is bool b ? !b : Convert.ToDouble(v, CultureInfo.InvariantCulture) == 0;

    /// <summary>Translates a VBA date/time format string into the equivalent .NET custom format. VBA differs from
    /// .NET on <c>m</c>=month (vs minutes), <c>n</c>=minutes, and <c>h</c>=24-hour unless AM/PM is present; and it
    /// has <c>q</c>=quarter, which .NET lacks (emitted as an escaped literal digit).</summary>
    private static string TranslateVbaDateFormat(string vba, DateTime dt)
    {
        bool twelveHour = vba.Contains("am/pm", StringComparison.OrdinalIgnoreCase)
            || vba.Contains("a/p", StringComparison.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        for (int i = 0; i < vba.Length;)
        {
            char ch = vba[i];
            // AM/PM tokens.
            if (i + 4 < vba.Length + 1 && vba.AsSpan(i).StartsWith("am/pm", StringComparison.OrdinalIgnoreCase))
            { sb.Append("tt"); i += 5; continue; }
            if (ch is '\\' && i + 1 < vba.Length) { sb.Append('\\').Append(vba[i + 1]); i += 2; continue; }
            if (ch is '"')
            {
                int j = i + 1;
                while (j < vba.Length && vba[j] != '"') { sb.Append('\\').Append(vba[j]); j++; }
                i = j + 1; continue;
            }
            char lower = char.ToLowerInvariant(ch);
            if ("ymdhnsq".IndexOf(lower) >= 0)
            {
                int j = i; while (j < vba.Length && char.ToLowerInvariant(vba[j]) == lower) j++;
                int len = j - i;
                sb.Append(lower switch
                {
                    'y' => new string('y', len is 2 ? 2 : 4),
                    'm' => new string('M', len),                          // VBA m/mm/mmm/mmmm = month
                    'n' => new string('m', Math.Min(len, 2)),             // VBA n/nn = minutes
                    's' => new string('s', Math.Min(len, 2)),
                    'd' => new string('d', len),
                    'h' => new string(twelveHour ? 'h' : 'H', Math.Min(len, 2)),
                    'q' => "\\" + ((dt.Month - 1) / 3 + 1),               // quarter as an escaped literal digit
                    _ => new string(lower, len),
                });
                i = j; continue;
            }
            sb.Append(ch); i++;
        }
        return sb.ToString();
    }

    /// <summary>Access <c>StrConv(string, conversion)</c> (verified vs ACE): 1 = UpperCase, 2 = LowerCase,
    /// 3 = ProperCase (title case); 64 = vbUnicode (reinterpret the UTF-16 bytes as one char each — doubles the
    /// length with null chars); 128 = vbFromUnicode (combine char pairs into single code units). The narrow/wide
    /// and Japanese Kana modes (4/16/32) raise "Invalid procedure call", matching ACE. NULL-propagating.</summary>
    private object? StrConv(FunctionCall f)
    {
        object? sv = Evaluate(f.Arguments[0]);
        object? modeV = Evaluate(f.Arguments[1]);
        if (sv is null || modeV is null) return null;
        int mode = Convert.ToInt32(modeV, CultureInfo.InvariantCulture);

        // vbUnicode (64) on binary widens each byte to one Unicode char — Jet's binary→string conversion.
        // This is the byte-array path EF emits for `byte[].Contains(x)`: INSTR(1, STRCONV(arr, 64), 0xXX, 0).
        if (mode == 64 && sv is byte[] binary)
            return ByteArrayToString(binary);

        string s = sv.ToString()!;
        return mode switch
        {
            1 => s.ToUpperInvariant(),
            2 => s.ToLowerInvariant(),
            3 => EnUs.TextInfo.ToTitleCase(s.ToLowerInvariant()),
            64 => new string(Encoding.Unicode.GetBytes(s).Select(x => (char)x).ToArray()),
            128 => FromUnicodeBytes(s),
            _ => throw new InvalidOperationException("Invalid procedure call: unsupported StrConv conversion mode."),
        };
    }

    /// <summary>Jet coerces a binary value to a string by mapping each byte to a single char (the value it
    /// widens back to via <c>STRCONV(…, 64)</c>). A <c>byte[]</c> reaching a string function — e.g. a
    /// <c>0xNN</c> hex literal used as an <c>INSTR</c> needle — is coerced this way, not via
    /// <c>ToString()</c> (which would yield "System.Byte[]").</summary>
    private static string ByteArrayToString(byte[] bytes) => new(Array.ConvertAll(bytes, b => (char)b));

    private static string ToJetString(object value) => value is byte[] b ? ByteArrayToString(b) : value.ToString()!;

    /// <summary>StrConv vbFromUnicode (128): combine successive character pairs into single UTF-16 code units
    /// (low char = low byte, next char = high byte); a trailing unpaired char is dropped (verified vs ACE).</summary>
    private static string FromUnicodeBytes(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i + 1 < s.Length; i += 2)
            sb.Append((char)(s[i] | (s[i + 1] << 8)));
        return sb.ToString();
    }

    /// <summary>VBA <c>WeekdayName(weekday, [abbreviate=False], [firstDayOfWeek=vbSunday])</c>: the name of the
    /// day at 1-based position <c>weekday</c> in a week starting from <c>firstDayOfWeek</c> (1=Sunday … 7=Saturday).
    /// Verified vs ACE for an explicit first day (<c>WeekdayName(1,,1)</c>→"Sunday", <c>(1,,2)</c>→"Monday"). NOTE:
    /// ACE's *omitted* default follows the OS regional first day; LibRed uses the VBA-documented default of
    /// vbSunday for determinism, so the no-third-arg case may differ from a given ACE host. NULL-propagating.</summary>
    /// <summary>Access <c>MonthName(month, [abbreviate])</c> — the full English month name, or its
    /// abbreviation when the second argument is true. NULL-propagating, like its Weekday sibling below.</summary>
    private object? MonthNameOf(FunctionCall f)
    {
        object? monthValue = Evaluate(f.Arguments[0]);
        if (monthValue is null) return null;
        int month = Convert.ToInt32(monthValue, CultureInfo.InvariantCulture);
        bool abbreviate = f.Arguments.Count > 1 && IsTrue(f.Arguments[1]);
        return abbreviate
            ? EnUs.DateTimeFormat.GetAbbreviatedMonthName(month)
            : EnUs.DateTimeFormat.GetMonthName(month);
    }

    private object? WeekdayNameOf(FunctionCall f)
    {
        object? wdV = Evaluate(f.Arguments[0]);
        if (wdV is null) return null;
        int weekday = Convert.ToInt32(wdV, CultureInfo.InvariantCulture);
        bool abbreviate = f.Arguments.Count > 1 && IsTrue(f.Arguments[1]);
        int firstDay = f.Arguments.Count > 2 ? Convert.ToInt32(Evaluate(f.Arguments[2]), CultureInfo.InvariantCulture) : 1;
        if (firstDay == 0) firstDay = 1;                 // vbUseSystem → treat as vbSunday for determinism
        // Map to a 0..6 index into Sunday..Saturday.
        int index = ((firstDay - 1) + (weekday - 1)) % 7;
        if (index < 0) index += 7;
        var names = abbreviate ? EnUs.DateTimeFormat.AbbreviatedDayNames : EnUs.DateTimeFormat.DayNames;
        return names[index];
    }

    /// <summary>Access <c>Partition(number, start, stop, interval)</c>: a <c>"lower:upper"</c> range label, both
    /// sides right-justified to a fixed width. Below the range → lower blank, upper = <c>start-1</c>; above →
    /// lower = <c>stop+1</c>, upper blank; otherwise the interval bucket (verified vs ACE). NULL-propagating.</summary>
    private object? PartitionOf(FunctionCall f)
    {
        object? nV = Evaluate(f.Arguments[0]);
        if (nV is null) return null;
        long number = Convert.ToInt64(nV, CultureInfo.InvariantCulture);
        long start = Convert.ToInt64(Evaluate(f.Arguments[1]), CultureInfo.InvariantCulture);
        long stop = Convert.ToInt64(Evaluate(f.Arguments[2]), CultureInfo.InvariantCulture);
        long interval = Convert.ToInt64(Evaluate(f.Arguments[3]), CultureInfo.InvariantCulture);

        // Fixed field width = the widest boundary that can appear (the below/above sentinels).
        int width = Math.Max((start - 1).ToString(CultureInfo.InvariantCulture).Length,
                             (stop + 1).ToString(CultureInfo.InvariantCulture).Length);

        string lower, upper;
        if (number < start) { lower = ""; upper = (start - 1).ToString(CultureInfo.InvariantCulture); }
        else if (number > stop) { lower = (stop + 1).ToString(CultureInfo.InvariantCulture); upper = ""; }
        else
        {
            long lo = start + (number - start) / interval * interval;
            long hi = Math.Min(lo + interval - 1, stop);
            lower = lo.ToString(CultureInfo.InvariantCulture);
            upper = hi.ToString(CultureInfo.InvariantCulture);
        }
        return $"{lower.PadLeft(width)}:{upper.PadLeft(width)}";
    }

    /// <summary>VBA <c>LeftB(string, bytes)</c>: the leading <c>bytes</c> bytes of the UTF-16 layout, i.e. the
    /// first <c>bytes/2</c> characters. NULL-propagating.</summary>
    private object? ByteLeft(FunctionCall f)
    {
        object? sv = Evaluate(f.Arguments[0]);
        if (sv is null) return null;
        byte[] b = ToBytes(sv);
        int n = Math.Clamp(Convert.ToInt32(Evaluate(f.Arguments[1]), CultureInfo.InvariantCulture), 0, b.Length);
        return ByteResult(sv, b[..n]);
    }

    /// <summary>VBA <c>RightB(string, bytes)</c>: the trailing <c>bytes</c> bytes. NULL-propagating.</summary>
    private object? ByteRight(FunctionCall f)
    {
        object? sv = Evaluate(f.Arguments[0]);
        if (sv is null) return null;
        byte[] b = ToBytes(sv);
        int n = Math.Clamp(Convert.ToInt32(Evaluate(f.Arguments[1]), CultureInfo.InvariantCulture), 0, b.Length);
        return ByteResult(sv, b[^n..]);
    }

    /// <summary>VBA <c>MidB(string, startByte[, lenBytes])</c>: a 1-based **byte** slice (may start/end
    /// mid-character). NULL-propagating.</summary>
    private object? ByteMid(FunctionCall f)
    {
        object? sv = Evaluate(f.Arguments[0]);
        if (sv is null) return null;
        byte[] b = ToBytes(sv);
        int start = Math.Max(0, Convert.ToInt32(Evaluate(f.Arguments[1]), CultureInfo.InvariantCulture) - 1);
        if (start >= b.Length) return ByteResult(sv, []);
        int len = f.Arguments.Count > 2
            ? Convert.ToInt32(Evaluate(f.Arguments[2]), CultureInfo.InvariantCulture)
            : b.Length - start;
        len = Math.Clamp(len, 0, b.Length - start);
        return ByteResult(sv, b[start..(start + len)]);
    }

    /// <summary>The result of a byte-slice function: a **byte[]** when the input was binary (so a further byte
    /// function like <c>ASCB(RIGHTB(x,1))</c> can read the raw byte — the mechanism EFCore.Jet's ByteArrayLength
    /// relies on), or the decoded string (dropping a trailing odd byte) when the input was text.</summary>
    private static object ByteResult(object input, byte[] slice) => input is byte[] ? slice : FromBytes(slice);

    /// <summary>VBA <c>InStrB([start,] string1, string2)</c>: the 1-based **byte** position of string2's bytes in
    /// string1's bytes (0 if not found). NULL-propagating.</summary>
    private object? InstrB(FunctionCall f)
    {
        int argc = f.Arguments.Count;
        object? s1v = Evaluate(f.Arguments[argc >= 3 ? 1 : 0]);
        object? s2v = Evaluate(f.Arguments[argc >= 3 ? 2 : 1]);
        if (s1v is null || s2v is null) return null;
        int start = argc >= 3 ? Math.Max(0, Convert.ToInt32(Evaluate(f.Arguments[0]), CultureInfo.InvariantCulture) - 1) : 0;
        int idx = IndexOfBytes(ToBytes(s1v), ToBytes(s2v), start);
        return idx < 0 ? 0 : idx + 1;
    }

    private static int IndexOfBytes(byte[] hay, byte[] needle, int start)
    {
        if (needle.Length == 0) return start <= hay.Length ? start : -1;
        for (int i = Math.Max(0, start); i + needle.Length <= hay.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (hay[i + j] != needle[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    /// <summary>The UTF-16LE **byte** representation a byte function operates on: a string's encoded bytes, or a
    /// binary value's raw bytes with an odd trailing byte zero-padded (matching ACE, which reinterprets a binary
    /// column as a UTF-16 string — LenB of a 3-byte value is 4).</summary>
    private static byte[] ToBytes(object v)
    {
        if (v is not byte[] bytes) return System.Text.Encoding.Unicode.GetBytes(v.ToString()!);
        if (bytes.Length % 2 == 0) return bytes;
        var padded = new byte[bytes.Length + 1];
        Array.Copy(bytes, padded, bytes.Length);
        return padded;
    }

    /// <summary>Decodes a byte slice back to a string, dropping a trailing incomplete (odd) byte — matching ACE
    /// (MidB(x, 1, 3) yields one character from three bytes).</summary>
    private static string FromBytes(byte[] b) =>
        System.Text.Encoding.Unicode.GetString(b, 0, b.Length - (b.Length % 2));

    /// <summary>The text a string function operates on: a binary value reinterpreted as a UTF-16LE string (odd
    /// byte zero-padded), or a normal string.</summary>
    private static string ToText(object v) =>
        v is byte[] ? System.Text.Encoding.Unicode.GetString(ToBytes(v)) : v.ToString()!;

    // --- Financial / formatting / colour functions (JES surface) ---

    private double FinArg(FunctionCall f, int i) => Convert.ToDouble(Evaluate(f.Arguments[i]), CultureInfo.InvariantCulture);
    private double FinArgOr(FunctionCall f, int i, double def)
        => f.Arguments.Count > i && Evaluate(f.Arguments[i]) is { } v ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : def;
    /// <summary>Optional decimal-count arg for the FormatX functions — default 2 (also for the VBA "-1" default).</summary>
    private int FmtDigits(FunctionCall f, int i)
        => f.Arguments.Count > i && Evaluate(f.Arguments[i]) is { } v && Convert.ToInt32(v, CultureInfo.InvariantCulture) >= 0
            ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 2;

    private object FormatDateTimeFn(FunctionCall f)
    {
        var dt = (DateTime)ToDate(Evaluate(f.Arguments[0])!);
        int mode = f.Arguments.Count > 1 ? Convert.ToInt32(Evaluate(f.Arguments[1]), CultureInfo.InvariantCulture) : 0;
        CultureInfo c = CultureInfo.CurrentCulture;
        return mode switch
        {
            1 => dt.ToString("D", c),                                       // Long Date
            2 => dt.ToString("d", c),                                       // Short Date
            3 => dt.ToString("T", c),                                       // Long Time
            4 => dt.ToString("t", c),                                       // Short Time
            _ => dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("d", c) : dt.ToString("g", c), // General Date
        };
    }

    // QBColor maps 0..15 to fixed BGR Long values (verified vs ACE: QBColor(4) = 128).
    private static readonly int[] QbColors =
    [
        0x000000, 0x800000, 0x008000, 0x808000, 0x000080, 0x800080, 0x008080, 0xC0C0C0,
        0x808080, 0xFF0000, 0x00FF00, 0xFFFF00, 0x0000FF, 0xFF00FF, 0x00FFFF, 0xFFFFFF,
    ];
    private static int QbColor(int n) => QbColors[((n % 16) + 16) % 16];

    private static double Pow1(double rate, double nper) => Math.Pow(1 + rate, nper);
    private static double AnnuityFactor(double rate, double nper, double type) => (1 + rate * type) * (Pow1(rate, nper) - 1) / rate;

    /// <summary>VBA <c>Pmt(rate, nper, pv, [fv=0], [type=0])</c>: the constant payment for an annuity.</summary>
    private object Pmt(FunctionCall f)
    {
        double rate = FinArg(f, 0), nper = FinArg(f, 1), pv = FinArg(f, 2), fv = FinArgOr(f, 3, 0), type = FinArgOr(f, 4, 0);
        return rate == 0 ? -(pv + fv) / nper : -(pv * Pow1(rate, nper) + fv) / AnnuityFactor(rate, nper, type);
    }

    /// <summary>VBA <c>FV(rate, nper, pmt, [pv=0], [type=0])</c>: the future value of an annuity.</summary>
    private object Fv(FunctionCall f)
    {
        double rate = FinArg(f, 0), nper = FinArg(f, 1), pmt = FinArg(f, 2), pv = FinArgOr(f, 3, 0), type = FinArgOr(f, 4, 0);
        return rate == 0 ? -(pv + pmt * nper) : -(pv * Pow1(rate, nper) + pmt * AnnuityFactor(rate, nper, type));
    }

    /// <summary>VBA <c>PV(rate, nper, pmt, [fv=0], [type=0])</c>: the present value of an annuity.</summary>
    private object Pv(FunctionCall f)
    {
        double rate = FinArg(f, 0), nper = FinArg(f, 1), pmt = FinArg(f, 2), fv = FinArgOr(f, 3, 0), type = FinArgOr(f, 4, 0);
        return rate == 0 ? -(fv + pmt * nper) : -(fv + pmt * AnnuityFactor(rate, nper, type)) / Pow1(rate, nper);
    }

    /// <summary>VBA <c>NPer(rate, pmt, pv, [fv=0], [type=0])</c>: the number of periods for an annuity.</summary>
    private object NPer(FunctionCall f)
    {
        double rate = FinArg(f, 0), pmt = FinArg(f, 1), pv = FinArg(f, 2), fv = FinArgOr(f, 3, 0), type = FinArgOr(f, 4, 0);
        if (rate == 0) return -(pv + fv) / pmt;
        double a = pmt * (1 + rate * type);
        return Math.Log((a - fv * rate) / (a + pv * rate)) / Math.Log(1 + rate);
    }

    /// <summary>VBA <c>IPmt(rate, per, nper, pv, [fv=0], [type=0])</c>: the interest portion of payment <c>per</c>.</summary>
    private object IPmt(FunctionCall f)
    {
        double rate = FinArg(f, 0), per = FinArg(f, 1), nper = FinArg(f, 2), pv = FinArg(f, 3), fv = FinArgOr(f, 4, 0), type = FinArgOr(f, 5, 0);
        double pmt = rate == 0 ? -(pv + fv) / nper : -(pv * Pow1(rate, nper) + fv) / AnnuityFactor(rate, nper, type);
        if (type == 1 && per == 1) return 0.0;
        double balance = pv * Pow1(rate, per - 1) + pmt * (rate == 0 ? per - 1 : AnnuityFactor(rate, per - 1, type));
        double interest = -balance * rate;                 // interest paid is a cash outflow (negative), like Pmt
        return type == 1 ? interest / (1 + rate) : interest;
    }

    /// <summary>VBA <c>PPmt(...)</c>: the principal portion of a payment (= Pmt − IPmt).</summary>
    private object PPmt(FunctionCall f)
    {
        double rate = FinArg(f, 0), nper = FinArg(f, 2), pv = FinArg(f, 3), fv = FinArgOr(f, 4, 0), type = FinArgOr(f, 5, 0);
        double pmt = rate == 0 ? -(pv + fv) / nper : -(pv * Pow1(rate, nper) + fv) / AnnuityFactor(rate, nper, type);
        return pmt - Convert.ToDouble(IPmt(f), CultureInfo.InvariantCulture);
    }

    /// <summary>VBA <c>DDB(cost, salvage, life, period, [factor=2])</c>: double-declining-balance depreciation.</summary>
    private object Ddb(FunctionCall f)
    {
        double cost = FinArg(f, 0), salvage = FinArg(f, 1), life = FinArg(f, 2), period = FinArg(f, 3), factor = FinArgOr(f, 4, 2);
        double rate = factor / life;
        double bookStart = cost * Math.Pow(1 - rate, period - 1);
        double dep = bookStart * rate;
        if (bookStart - dep < salvage) dep = Math.Max(0, bookStart - salvage);
        return dep;
    }

    /// <summary>VBA <c>Rate(nper, pmt, pv, [fv=0], [type=0], [guess=0.1])</c>: the per-period rate, solved by
    /// Newton–Raphson on the annuity equation.</summary>
    private object Rate(FunctionCall f)
    {
        double nper = FinArg(f, 0), pmt = FinArg(f, 1), pv = FinArg(f, 2), fv = FinArgOr(f, 3, 0), type = FinArgOr(f, 4, 0);
        double r = FinArgOr(f, 5, 0.1);
        for (int iter = 0; iter < 100; iter++)
        {
            double v = r == 0 ? pv + pmt * nper + fv
                : pv * Pow1(r, nper) + pmt * (1 + r * type) * (Pow1(r, nper) - 1) / r + fv;
            const double h = 1e-6;
            double vh = (r + h) == 0 ? pv + pmt * nper + fv
                : pv * Pow1(r + h, nper) + pmt * (1 + (r + h) * type) * (Pow1(r + h, nper) - 1) / (r + h) + fv;
            double deriv = (vh - v) / h;
            if (Math.Abs(deriv) < 1e-12) break;
            double next = r - v / deriv;
            if (Math.Abs(next - r) < 1e-10) return next;
            r = next;
        }
        return r;
    }

    private uint _localRandSeed = 0x50000;   // used when no connection-scoped SessionState is available

    /// <summary>VBA <c>Rnd([number])</c> using VBA's own 24-bit LCG, so the sequence matches ACE (verified:
    /// <c>Rnd(-1)</c> = 0.2240070104598999). <c>number &gt; 0</c> (or omitted) advances the generator;
    /// <c>number = 0</c> repeats the last value; <c>number &lt; 0</c> reseeds deterministically from the
    /// argument's Single bit pattern. The seed is connection-scoped (via <see cref="SessionState"/>) since the
    /// JES has no <c>Randomize</c>. Result is a Single widened to Double, matching ACE.</summary>
    private object Rnd(FunctionCall f)
    {
        uint seed = session?.RandSeed ?? _localRandSeed;
        double arg = f.Arguments.Count > 0 && Evaluate(f.Arguments[0]) is { } a
            ? Convert.ToDouble(a, CultureInfo.InvariantCulture) : 1.0;
        if (arg != 0)
        {
            if (arg < 0)
            {
                uint ni = BitConverter.SingleToUInt32Bits((float)arg);
                seed = (ni + (ni >> 24)) & 0xFFFFFF;
            }
            seed = (uint)((seed * 0xFD43FDUL + 0xC39EC3UL) & 0xFFFFFF);
        }
        if (session is not null) session.RandSeed = seed; else _localRandSeed = seed;
        return (double)((float)seed / (float)0x1000000);
    }

    /// <summary>A random non-zero signed Int32 — Access's <c>GenUniqueID()</c>.</summary>
    private static int RandomLong()
    {
        int value;
        do { value = Random.Shared.Next(int.MinValue, int.MaxValue); } while (value == 0);
        return value;
    }

    /// <summary>Access ROUND(number[, digits]): banker's rounding, preserving the operand's type (a double
    /// rounds to a double, a decimal to a decimal — the EF contract). NULL-propagating.</summary>
    private object? Round(FunctionCall f)
    {
        object? value = Evaluate(f.Arguments[0]);
        if (value is null) return null;
        int digits = f.Arguments.Count > 1
            ? Convert.ToInt32(Evaluate(f.Arguments[1]), CultureInfo.InvariantCulture)
            : 0;
        return value switch
        {
            decimal m => Math.Round(m, digits, MidpointRounding.ToEven),
            double d => Math.Round(d, digits, MidpointRounding.ToEven),
            float s => (float)Math.Round((double)s, digits, MidpointRounding.ToEven),
            long l => (long)Math.Round((decimal)l, digits, MidpointRounding.ToEven),
            _ => (int)Math.Round(Convert.ToDecimal(value, CultureInfo.InvariantCulture), digits, MidpointRounding.ToEven),
        };
    }

    /// <summary>Applies a conversion to a single argument, propagating NULL.</summary>
    private object? Convert1(FunctionCall f, Func<object, object?> convert)
    {
        object? value = Evaluate(f.Arguments[0]);
        return value is null ? null : convert(value);
    }

    /// <summary>Access CDate: a date passes through, a string is parsed, a number is an OLE Automation date
    /// (days since 1899-12-30).</summary>
    private static object ToDate(object v) => v switch
    {
        DateTime d => d,
        string s => DateTime.Parse(s, CultureInfo.InvariantCulture),
        _ => DateTime.FromOADate(Convert.ToDouble(v, CultureInfo.InvariantCulture)),
    };

    /// <summary>Access IsDate: true only for a date value or a string that parses as a date/time. A number,
    /// NULL, or an unrecognisable string is false (verified vs ACE — unlike CDate, a bare number is not a
    /// date here).</summary>
    private static bool IsDateValue(object? v) => v switch
    {
        null => false,
        DateTime => true,
        string s => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        _ => false,
    };

    /// <summary>A (string, int) → string function (LEFT/RIGHT), propagating NULL on the string argument.</summary>
    // Left/Right: NULL string propagates; a NULL length raises "Data type mismatch" and a negative length
    // "Invalid procedure call" — matching ACE (which errors rather than clamping). A zero length yields "".
    private object? StringInt(FunctionCall f, Func<string, int, string> op)
    {
        object? s = Evaluate(f.Arguments[0]);
        if (s is null) return null;
        object? nv = Evaluate(f.Arguments[1]);
        if (nv is null)
            throw new InvalidOperationException("Data type mismatch in criteria expression: length argument is null.");
        int n = Convert.ToInt32(nv, CultureInfo.InvariantCulture);
        if (n < 0)
            throw new InvalidOperationException("Invalid procedure call: length cannot be negative.");
        return op(s.ToString()!, n);
    }

    /// <summary>Access MID(string, start[, length]) — a 1-based substring; length omitted means to the end.</summary>
    private object? Mid(FunctionCall f)
    {
        object? sv = Evaluate(f.Arguments[0]);
        if (sv is null) return null;                        // Mid propagates NULL on the string argument
        string s = sv.ToString()!;
        int start = Convert.ToInt32(Evaluate(f.Arguments[1]), CultureInfo.InvariantCulture);
        if (start < 1)                                      // ACE errors rather than clamping
            throw new InvalidOperationException("Invalid procedure call: Mid() start must be >= 1.");
        int from = start - 1;
        if (from >= s.Length) return "";
        int avail = s.Length - from;
        int len = avail;
        if (f.Arguments.Count > 2 && Evaluate(f.Arguments[2]) is { } lenVal)
        {
            int requested = Convert.ToInt32(lenVal, CultureInfo.InvariantCulture);
            if (requested < 0)                              // ACE errors on a negative length
                throw new InvalidOperationException("Invalid procedure call: Mid() length cannot be negative.");
            len = Math.Min(requested, avail);
        }
        return s.Substring(from, len);
    }

    /// <summary>Access INSTR([start,] string1, string2[, compare]) — the 1-based position of string2 in
    /// string1 (0 if not found). start defaults to 1; compare 0 = binary (case-sensitive), else text.</summary>
    private object? Instr(FunctionCall f)
    {
        int argc = f.Arguments.Count;
        // 2 args: (s1, s2); 3+: (start, s1, s2[, compare]).
        int start = argc >= 3 ? Convert.ToInt32(Evaluate(f.Arguments[0]), CultureInfo.InvariantCulture) : 1;
        object? s1v = Evaluate(f.Arguments[argc >= 3 ? 1 : 0]);
        object? s2v = Evaluate(f.Arguments[argc >= 3 ? 2 : 1]);
        if (s1v is null || s2v is null) return null;
        StringComparison cmp = argc >= 4 && Convert.ToInt32(Evaluate(f.Arguments[3]), CultureInfo.InvariantCulture) == 0
            ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        // A byte[] argument (e.g. a 0xNN hex literal needle, or a binary haystack) coerces to a string one
        // char per byte, matching Jet — not "System.Byte[]".
        string s1 = ToJetString(s1v), s2 = ToJetString(s2v);
        if (start < 1) start = 1;
        if (start > s1.Length) return 0;
        int idx = s1.IndexOf(s2, start - 1, cmp);
        return idx < 0 ? 0 : idx + 1;
    }

    /// <summary>Access REPLACE(string, find, replace[, start[, count[, compare]]]) — replaces occurrences of
    /// find (from the 1-based start, at most count times, case-insensitive by default).</summary>
    private object? Replace(FunctionCall f)
    {
        object? sv = Evaluate(f.Arguments[0]), findv = Evaluate(f.Arguments[1]), replv = Evaluate(f.Arguments[2]);
        // ACE raises "Data type mismatch" here (unlike InStr, which propagates NULL), but LibRed propagates
        // instead, matching every other dialect - SQL Server documents REPLACE as returning NULL if any
        // argument is NULL. A deliberate divergence, and a widening: no query that returns a value today
        // changes, only ones that error start returning NULL.
        if (sv is null || findv is null || replv is null)
            return null;
        string s = sv.ToString()!, find = findv.ToString()!, repl = replv.ToString()!;

        int start = f.Arguments.Count > 3 ? Convert.ToInt32(Evaluate(f.Arguments[3]), CultureInfo.InvariantCulture) : 1;
        if (start < 1)                                      // ACE errors rather than clamping
            throw new InvalidOperationException("Invalid procedure call: Replace() start must be >= 1.");
        int count = f.Arguments.Count > 4 ? Convert.ToInt32(Evaluate(f.Arguments[4]), CultureInfo.InvariantCulture) : -1;
        StringComparison cmp = f.Arguments.Count > 5 && Convert.ToInt32(Evaluate(f.Arguments[5]), CultureInfo.InvariantCulture) == 0
            ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        if (start > s.Length) return "";
        s = s[(start - 1)..];
        if (find.Length == 0) return s;

        var sb = new StringBuilder();
        int pos = 0, replaced = 0;
        while (true)
        {
            int j = (count >= 0 && replaced >= count) ? -1 : s.IndexOf(find, pos, cmp);
            if (j < 0) { sb.Append(s.AsSpan(pos)); break; }
            sb.Append(s, pos, j - pos).Append(repl);
            pos = j + find.Length;
            replaced++;
        }
        return sb.ToString();
    }

    /// <summary>Applies a numeric transform to a single argument, propagating NULL.</summary>
    /// <summary>A numeric transform (Fix/Int/Abs) that **preserves the operand's type** (double→double,
    /// single→single, decimal→decimal, int→int, long→long) so it matches EF's Math.* return type. Integer
    /// types use the exact decimal op (no floating round-trip). NULL-propagating.</summary>
    private object? Numeric1(FunctionCall f, Func<double, double> dOp, Func<decimal, decimal> mOp) =>
        Evaluate(f.Arguments[0]) switch
        {
            null => null,
            decimal m => mOp(m),
            double d => dOp(d),
            float s => (float)dOp(s),
            long l => (long)mOp(l),
            var v => (int)mOp(Convert.ToDecimal(v!, CultureInfo.InvariantCulture)),
        };

    /// <summary>Applies a double-precision transform to a single argument, propagating NULL. Used for
    /// the trig/exp/log/sqrt VBA functions, which are inherently floating-point.</summary>
    private object? UnaryDouble(FunctionCall f, Func<double, double> op)
    {
        object? value = Evaluate(f.Arguments[0]);
        return value is null ? null : op(Convert.ToDouble(value, CultureInfo.InvariantCulture));
    }

    /// <summary>Access DATEPART(interval, date): extracts a component of a date as an int.</summary>
    private static object? DatePart(object? interval, object? date)
    {
        if (date is null) return null;
        var d = Convert.ToDateTime(date, CultureInfo.InvariantCulture);
        return (interval?.ToString() ?? "").ToLowerInvariant() switch
        {
            "yyyy" => d.Year,
            "q" => (d.Month + 2) / 3,
            "m" => d.Month,
            "y" => d.DayOfYear,
            "d" => d.Day,
            "w" => (int)d.DayOfWeek + 1,
            "ww" => CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(d, CalendarWeekRule.FirstDay, DayOfWeek.Sunday),
            "h" => d.Hour,
            "n" => d.Minute,
            "s" => d.Second,
            "ms" => d.Millisecond,
            "mcs" => d.Microsecond,
            "ns" => d.Nanosecond,
            _ => throw new NotSupportedException($"DATEPART interval '{interval}' is not supported."),
        };
    }

    /// <summary>
    /// <c>BAND</c>, <c>BOR</c> and <c>BXOR</c> on the operands' bits. When either operand is 16 bits, only the low 16
    /// bits are combined and the rest are the left operand's: its own upper bits, or the sign of the 16-bit result when
    /// it is 16 bits itself (verified vs ACE: 1 BOR TRUE is 65535, CLNG(-1) BAND CINT(1) is -65535, CINT(1) BOR 70000
    /// is 4465, CINT(-2) BOR 1 is -1). Otherwise the result has the wider operand's width.
    /// </summary>
    private static object BitwiseOp(object a, object b, Func<long, long, long> op)
    {
        (long left, int leftWidth) = BitOperand(a);
        (long right, int rightWidth) = BitOperand(b);
        if (leftWidth != 16 && rightWidth != 16)
            return Signed(op(left, right), Math.Max(leftWidth, rightWidth));

        long low = op(left, right) & 0xFFFF;
        return leftWidth == 16 ? Signed(low, 16) : Signed(left & ~0xFFFFL | low, leftWidth);
    }

    /// <summary>
    /// Unary minus, keeping the operand's type as C# does (an Integer, a Byte or a Boolean gives a Long). Text is read as
    /// a number first, and a date negates its serial and stays a date (verified vs ACE: -#2020-01-02# is 1779-12-27). A
    /// result the operand's type cannot hold is an overflow (verified vs ACE: -CINT(-32768) and -CLNG(-2147483648)).
    /// </summary>
    private static object Negate(object v) => NumericOperand(v)! switch
    {
        decimal d => -d,
        double d => -d,
        float f => -f,
        long l => checked(-l),
        ulong u => checked(-(long)u),
        DateTime d => OaDate(-d.ToOADate()),
        short.MinValue => throw new OverflowException("Overflow: an Integer cannot hold 32768."),
        var n => checked(-Int(n)),
    };

    /// <summary><c>BNOT</c>: every bit of the operand flipped.</summary>
    private static object BitNot(object v)
    {
        (long bits, int width) = BitOperand(v);
        return Signed(~bits, width);
    }

    /// <summary>
    /// An operand's bits, unsigned, and how many there are: 16 for a Boolean (True is all 16 set) or an Integer, 32 for a
    /// Byte or a Long, 64 for an Int64. Anything else is first read as a whole number, as <c>\</c> reads it: text as a
    /// number, a date as its serial, then rounded half to even, and it must fit a Long.
    /// </summary>
    private static (long Bits, int Width) BitOperand(object v) => v switch
    {
        bool b => (b ? 0xFFFF : 0, 16),
        short s => ((ushort)s, 16),
        byte b => (b, 32),
        int i => ((uint)i, 32),
        long l => (l, 64),
        ulong u => ((long)u, 64),
        _ => ((uint)Int(Serial(NumericOperand(v)!)), 32),
    };

    /// <summary>The low <paramref name="width"/> bits read as a signed number: an Int32 for 16 or 32 bits, an Int64 for 64.</summary>
    private static object Signed(long bits, int width) => width switch
    {
        // Each arm boxed on its own, or the switch would widen the Int32s to Int64.
        16 => (object)(int)(short)bits,
        32 => (object)(int)bits,
        _ => (object)bits,
    };

    /// <summary>A function of a single date argument (Year/Month/Day/…), NULL-propagating.</summary>
    private object? DatePartOf(FunctionCall f, Func<DateTime, int> part)
    {
        object? v = Evaluate(f.Arguments[0]);
        return v is null ? null : part(Convert.ToDateTime(v, CultureInfo.InvariantCulture));
    }

    /// <summary>DateSerial(y,m,d) / TimeSerial(h,m,s): build a date/time from three integer parts (parts may
    /// be out of range and roll over, matching Access). NULL-propagating.</summary>
    private object? DateParts(FunctionCall f, Func<int, int, int, DateTime> build)
    {
        object? a = Evaluate(f.Arguments[0]), b = Evaluate(f.Arguments[1]), c = Evaluate(f.Arguments[2]);
        if (a is null || b is null || c is null) return null;
        return build(Int(a), Int(b), Int(c));
    }

    /// <summary>Access DateAdd(interval, number, date): add <c>number</c> intervals to a date.</summary>
    private object? DateAdd(FunctionCall f)
    {
        object? intervalV = Evaluate(f.Arguments[0]), numberV = Evaluate(f.Arguments[1]), dateV = Evaluate(f.Arguments[2]);
        if (dateV is null || numberV is null) return null;
        int n = (int)Math.Truncate(Convert.ToDouble(numberV, CultureInfo.InvariantCulture)); // Access truncates
        var d = Convert.ToDateTime(dateV, CultureInfo.InvariantCulture);
        return (intervalV?.ToString() ?? "").ToLowerInvariant() switch
        {
            "yyyy" => d.AddYears(n),
            "q" => d.AddMonths(n * 3),
            "m" => d.AddMonths(n),
            "y" or "d" or "w" => d.AddDays(n),
            "ww" => d.AddDays(n * 7),
            "h" => d.AddHours(n),
            "n" => d.AddMinutes(n),
            "s" => d.AddSeconds(n),
            "ms" => d.AddMilliseconds(n),
            _ => throw new NotSupportedException($"DATEADD interval '{intervalV}' is not supported."),
        };
    }

    /// <summary>Access DateDiff(interval, date1, date2): the number of interval boundaries from date1 to
    /// date2 (a Long Integer). NULL-propagating.</summary>
    private object? DateDiff(FunctionCall f)
    {
        object? intervalV = Evaluate(f.Arguments[0]), d1V = Evaluate(f.Arguments[1]), d2V = Evaluate(f.Arguments[2]);
        if (d1V is null || d2V is null) return null;
        var d1 = Convert.ToDateTime(d1V, CultureInfo.InvariantCulture);
        var d2 = Convert.ToDateTime(d2V, CultureInfo.InvariantCulture);
        string interval = (intervalV?.ToString() ?? "").ToLowerInvariant();

        // "ms" is a LibRed extension — ACE's interval list stops at "s". It is available because LibRed stores
        // the full OA double rather than truncating to whole seconds as ACE does, and it is exact: .NET's OA
        // conversion quantises to whole milliseconds, so nothing below a millisecond survived storage anyway
        // (measured: 12:34:56.123 round-trips with zero tick loss, .1234560 comes back as .123).
        //
        // Handled before the switch, and as Int64 rather than the Long Integer every other interval returns: a
        // millisecond difference overflows Int32 after 25 days, and ToUnixTimeMilliseconds spans decades. A
        // long arm inside the switch would widen every other interval's result type along with it.
        if (interval == "ms")
        {
            return (long)(d2 - d1).TotalMilliseconds;
        }

        return interval switch
        {
            "yyyy" => d2.Year - d1.Year,
            "q" => (d2.Year - d1.Year) * 4 + (d2.Month - 1) / 3 - (d1.Month - 1) / 3,
            "m" => (d2.Year - d1.Year) * 12 + d2.Month - d1.Month,
            "y" or "d" or "w" => (int)(d2.Date - d1.Date).TotalDays,
            "ww" => (int)((d2.Date - d1.Date).TotalDays / 7),
            "h" => (int)(d2 - d1).TotalHours,
            "n" => (int)(d2 - d1).TotalMinutes,
            "s" => (int)(d2 - d1).TotalSeconds,
            // "ms" is handled above, as Int64.
            _ => throw new NotSupportedException($"DATEDIFF interval '{intervalV}' is not supported."),
        };
    }

    // Access truthiness: a filter/logical context treats any non-zero number as true (so a boolean stored
    // as a -1/0 integer — the nullable-bool convention — works as a bare predicate), a null as not-true.
    public bool IsTrue(Expression expression) => AsBool(Evaluate(expression)) is true;

    private object? EvaluateUnary(UnaryExpression u)
    {
        object? v = Evaluate(u.Operand);
        return u.Operator switch
        {
            UnaryOperator.Not => AsBool(v) is bool b ? !b : null, // coerce a -1/0 integer boolean too
            UnaryOperator.Negate => v is null ? null : Negate(v),
            UnaryOperator.BitNot => v is null ? null : BitNot(v),
            UnaryOperator.IsNull => v is null,
            UnaryOperator.IsNotNull => v is not null,
            _ => throw new NotSupportedException($"Unary operator {u.Operator}."),
        };
    }

    private object? EvaluateBinary(BinaryExpression b)
    {
        // AND/OR use Kleene three-valued logic, and short-circuit: `false AND x` is false and `true OR x`
        // is true regardless of x — so the right operand (which may be an expensive correlated subquery) is
        // only evaluated when it can affect the result.
        if (b.Operator is BinaryOperator.And)
        {
            bool? l = AsBool(Evaluate(b.Left));
            return l == false ? false : l & AsBool(Evaluate(b.Right));
        }
        if (b.Operator is BinaryOperator.Or)
        {
            bool? l = AsBool(Evaluate(b.Left));
            return l == true ? true : l | AsBool(Evaluate(b.Right));
        }
        // XOR and EQV are Null when either side is; IMP is True whenever its left side is False or its right side
        // is True, and Null otherwise when a side is Null (the VBA truth tables; verified vs ACE).
        if (b.Operator is BinaryOperator.Xor or BinaryOperator.Eqv or BinaryOperator.Imp)
        {
            bool? l = AsBool(Evaluate(b.Left)), r = AsBool(Evaluate(b.Right));
            return b.Operator switch
            {
                BinaryOperator.Xor => l ^ r,
                BinaryOperator.Eqv => l is null || r is null ? null : l == r,
                _ => l == false || r == true ? true : l is null || r is null ? null : false,
            };
        }

        object? left = Evaluate(b.Left);
        object? right = Evaluate(b.Right);

        // '&' treats a single Null as "" but is Null when both sides are (verified vs ACE).
        if (b.Operator == BinaryOperator.Concat)
            return left is null && right is null
                ? null
                : (left is null ? "" : ConcatText(left)) + (right is null ? "" : ConcatText(right));

        // The arithmetic operators other than '+' read text as a number even when the other side is Null, so text
        // that is not a number, a GUID or a binary value is a type mismatch before Null propagates (verified vs
        // ACE: 'abc' * NULL fails, '1' * NULL is Null).
        if (b.Operator is BinaryOperator.Subtract or BinaryOperator.Multiply or BinaryOperator.Divide
            or BinaryOperator.IntDivide or BinaryOperator.Modulo or BinaryOperator.Power)
        {
            left = NumericOperand(left);
            right = NumericOperand(right);
        }

        // A number written with a decimal point meets a Decimal as the value written, not as the Double it was
        // parsed to (verified vs ACE: a DECIMAL(18,4) 4.5 * 334.90 is 1507.05, not 1507.04999…).
        if (b.Operator is BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply or BinaryOperator.Divide)
        {
            if (right is decimal && WrittenValue(b.Left) is decimal leftWritten) left = leftWritten;
            if (left is decimal && WrittenValue(b.Right) is decimal rightWritten) right = rightWritten;
        }

        // A pattern written as just '%' is False for Null rather than Null, so it reads as IS NOT NULL (verified vs ACE;
        // '%%', or a '%' the pattern is computed to, stays Null).
        if (left is null && b.Operator == BinaryOperator.Like && b.Right is LiteralExpression { Value: "%" })
            return false;

        if (left is null || right is null)
            return null;

        if (b.Operator is BinaryOperator.Equal or BinaryOperator.NotEqual && TruthTest(b, left, right) is bool truth)
            return b.Operator == BinaryOperator.Equal ? truth : !truth;

        return b.Operator switch
        {
            BinaryOperator.Equal => CompareAsKinds(left, right) == 0,
            BinaryOperator.NotEqual => CompareAsKinds(left, right) != 0,
            BinaryOperator.LessThan => CompareAsKinds(left, right) < 0,
            BinaryOperator.LessThanOrEqual => CompareAsKinds(left, right) <= 0,
            BinaryOperator.GreaterThan => CompareAsKinds(left, right) > 0,
            BinaryOperator.GreaterThanOrEqual => CompareAsKinds(left, right) >= 0,
            // LIKE reads any other value as the text CStr gives it (verified vs ACE: TRUE LIKE '-1' is True). A binary
            // value becomes text too, so LIKE is case-insensitive over a binary column even though '=' on the same
            // column is byte-wise: `B LIKE 'A%'` matches both 0x4100 ('A') and 0x6100 ('a').
            BinaryOperator.Like => LikeMatcher.IsMatch(ConcatText(left), ConcatText(right)),
            BinaryOperator.Add => Add(left, right),
            BinaryOperator.Subtract => Arithmetic(left, right, '-'),
            BinaryOperator.Multiply => Arithmetic(left, right, '*'),
            BinaryOperator.Divide => Divide(left, right), // Access '/' is floating division
            BinaryOperator.Modulo => IntegerOp(left, right, '%'),
            BinaryOperator.IntDivide => IntegerOp(left, right, '\\'),
            BinaryOperator.Power => Power(left, right),
            BinaryOperator.BitAnd => BitwiseOp(left, right, (x, y) => x & y),
            BinaryOperator.BitOr => BitwiseOp(left, right, (x, y) => x | y),
            BinaryOperator.BitXor => BitwiseOp(left, right, (x, y) => x ^ y),
            _ => throw new NotSupportedException($"Binary operator {b.Operator}."),
        };
    }

    /// <summary>
    /// <c>=</c> or <c>&lt;&gt;</c> against the literal <c>True</c> or <c>False</c> tests the other side's truth
    /// rather than comparing it (verified vs ACE): a value that reads as 0 is False and anything else is True, so
    /// <c>2 = True</c>, <c>'abc' = True</c> and <c>'' = True</c> are all True, while <c>'0' = False</c> is True.
    /// Null for any other comparison.
    /// </summary>
    private static bool? TruthTest(BinaryExpression b, object left, object right) =>
        b.Right is LiteralExpression { Value: bool rightLiteral } ? IsTruthy(left) == rightLiteral
        : b.Left is LiteralExpression { Value: bool leftLiteral } ? IsTruthy(right) == leftLiteral
        : null;

    private static bool IsTruthy(object value) => value switch
    {
        bool b => b,
        string s => !ReadsAsZero(s),
        char c => !ReadsAsZero(c.ToString()),
        DateTime d => d != OaEpoch,
        Guid or byte[] => true,
        _ => !IsNumeric(value) || Dbl(value) != 0,
    };

    private static bool ReadsAsZero(string text)
    {
        try
        {
            return TextAsDecimal(text) == 0m;
        }
        catch (InvalidCastException)
        {
            return false;   // not a number, so not 0
        }
    }

    /// <summary>
    /// The two sides of a comparison, brought to a kind they compare as (verified vs ACE, except as noted).
    /// <list type="bullet">
    /// <item>Text against a number, Boolean or date: the text reads as a number (<see cref="TextAsNumber"/>), a type
    /// mismatch when it is not one. ACE does this for text a function returns; for a text literal or column it
    /// raises a type mismatch instead, which LibRed does not, as SQL Server compares them numerically too.</item>
    /// <item>A date against a number: its serial.</item>
    /// <item>A GUID or binary value against text, or against the other of the two: both as text (a GUID braced upper
    /// case, binary as UTF-16). Against a number or date: a type mismatch.</item>
    /// </list>
    /// </summary>
    private static (object Left, object Right) Comparable(object left, object right)
    {
        if (left is char leftChar) left = leftChar.ToString();
        if (right is char rightChar) right = rightChar.ToString();
        bool leftText = left is string, rightText = right is string;
        bool leftBlob = left is Guid or byte[], rightBlob = right is Guid or byte[];
        if ((leftText && rightBlob) || (leftBlob && rightText) || (leftBlob && rightBlob && left.GetType() != right.GetType()))
            return (ConcatText(left), ConcatText(right));
        if (leftBlob || rightBlob)
            return leftBlob && rightBlob
                ? (left, right)
                : throw new InvalidCastException("Type mismatch: a GUID or binary value cannot be compared with a number or date.");
        if (leftText == rightText)
            return left is DateTime ^ right is DateTime ? (Serial(left), Serial(right)) : (left, right);
        return (leftText ? TextAsNumber((string)left) : Serial(left), rightText ? TextAsNumber((string)right) : Serial(right));
    }

    /// <summary>The order of two values once <see cref="Comparable"/> has brought them to a common kind.</summary>
    private static int CompareAsKinds(object left, object right)
    {
        (object l, object r) = Comparable(left, right);
        return Compare(l, r);
    }

    private static object Serial(object value) => value is DateTime d ? d.ToOADate() : value;

    /// <summary>A value as a condition — for <c>NOT</c>, <c>AND</c>, <c>OR</c>, <c>XOR</c>, <c>EQV</c>, <c>IMP</c> and a
    /// <c>WHERE</c>: False when it reads as 0, True otherwise, Null when Null (verified vs ACE: <c>'0'</c> is False,
    /// <c>'abc'</c>, <c>''</c>, a date, a GUID and a binary value are True).</summary>
    private static bool? AsBool(object? v) => v is null ? null : IsTruthy(v);

    /// <summary><c>+ - *</c> with C# widest-operand type promotion, so the result CLR type matches what EF
    /// expects (int+int→int, …): decimal &gt; double &gt; single &gt; long &gt; int. (Contract: like
    /// <c>Enumerable</c> arithmetic — LibRed emits the operand type, not an ACE-widened one.)</summary>
    private static object Arithmetic(object left, object right, char op)
    {
        // Date/time arithmetic operates on the OLE Automation serial (days since 1899-12-30; the fractional
        // part is the time), verified vs ACE: date+time and date±N days yield a DateTime, but date−date yields
        // a plain day count (Double). A number operand is taken as a count of days. The result is rounded to a
        // whole second (Jet has no sub-second) to shed the tiny floating-point drift the serial round-trip adds.
        if (left is DateTime || right is DateTime)
        {
            double a = Oa(left), b = Oa(right);
            bool bothDates = left is DateTime && right is DateTime;
            return op switch
            {
                '-' when bothDates => a - b,                       // date − date → number of days
                '+' => OaDate(a + b),
                '-' => OaDate(a - b),
                _ => Finite(a * b),                                // date × n has no date meaning → numeric
            };
        }
        // A result past its type is an error, not a wrapped or infinite value (verified vs ACE: 2147483647 + 1,
        // 2147483647 * 2, -2147483647 - 2 and 1E300 * 1E300 all fail).
        if (left is decimal || right is decimal) { decimal a = ArithmeticDecimal(left), b = ArithmeticDecimal(right); return op == '+' ? a + b : op == '-' ? a - b : a * b; }
        if (left is double || right is double) { double a = Dbl(left), b = Dbl(right); return op == '+' ? a + b : Finite(op == '-' ? a - b : a * b); }
        if (left is float || right is float) { float a = (float)Dbl(left), b = (float)Dbl(right); return op == '+' ? a + b : Finite(op == '-' ? a - b : a * b); }
        if (left is long or ulong || right is long or ulong) { long a = Lng(left), b = Lng(right); return checked(op == '+' ? a + b : op == '-' ? a - b : a * b); }
        int x = Int(left), y = Int(right); return checked(op == '+' ? x + y : op == '-' ? x - y : x * y);
    }

    /// <summary>The exact value of a number written with a decimal point (negated as written), or null.</summary>
    private static decimal? WrittenValue(Expression expression)
    {
        bool negate = false;
        while (expression is UnaryExpression { Operator: UnaryOperator.Negate } negation)
        {
            negate = !negate;
            expression = negation.Operand;
        }
        return expression is LiteralExpression { Written: decimal written } ? (negate ? -written : written) : null;
    }

    /// <summary>
    /// The type ACE gives a result column before reading any row, as far as its places go (verified vs ACE). ACE
    /// works the expression out in full and cuts the value only as it goes into a Decimal result column — so
    /// <c>Pmt(0.05 / 12, …)</c> uses the whole rate while <c>SELECT 1 / 1.5</c> is 0.6.
    /// <list type="bullet">
    /// <item>A number written with a decimal point is a Decimal of the places written less trailing zeros
    /// (<c>334.90</c> has one; <c>3.0</c> is a whole number); a Decimal column has its scale; Currency counts as
    /// four places.</item>
    /// <item><c>*</c> and <c>/</c>: a Decimal with whole numbers, dates, Booleans or text stays that Decimal
    /// (DECIMAL(18,4) 4.5 / 7 is 0.6428; 1.5 / '2.5' is 0.6); two Decimals of the same places stay it (1.5 * 1.5 is
    /// 2.2); two of different places, a Double or Single, or a Currency divided give a Double (1.5 * 1.25 is
    /// 1.875; 1.5 / DECIMAL 4.5 is 0.333…).</item>
    /// <item><c>+</c> and <c>-</c> keep the same rule, except that Currency with a Decimal is a Decimal of the
    /// larger places (1.5 - Currency 3.25 is -1.7500).</item>
    /// <item>A function other than <c>CCur</c> and <c>Sum</c>/<c>Min</c>/<c>Max</c>/<c>First</c>/<c>Last</c>, a
    /// parameter or anything else is not a Decimal; a function returning text or a date counts as text or a date
    /// (1.5 / Left('12', 2) is 0.1), as <paramref name="declaredType"/> says.</item>
    /// </list>
    /// </summary>
    internal static NumberType NumberTypeOf(
        Expression expression, IReadOnlyList<OutputColumn> columns, Func<Expression, Type?> declaredType)
    {
        NumberType Of(Expression e) => NumberTypeOf(e, columns, declaredType);

        switch (expression)
        {
            case LiteralExpression { Written: decimal written }:
            {
                int places = (written / 1.0000000000000000000000000000m).Scale;   // trailing zeros dropped
                return places == 0 ? new(NumberClass.Whole) : new(NumberClass.Decimal, places);
            }
            case LiteralExpression literal:
                return literal.Value switch
                {
                    int or long or short or byte or bool => new(NumberClass.Whole),
                    double or float => new(NumberClass.Double),
                    string => new(NumberClass.Text),
                    DateTime => new(NumberClass.Date),
                    _ => new(NumberClass.Other),
                };
            case ColumnReference reference:
                if (OutputColumn.Find(columns, reference) is not { } column)
                    return new(NumberClass.Other);
                if (column.Currency)
                    return new(NumberClass.Currency);
                if (column.Scale is int scale)
                    return new(NumberClass.Decimal, scale);
                Type? type = column.ClrType;
                return type == typeof(double) || type == typeof(float) ? new(NumberClass.Double)
                    : type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
                        || type == typeof(bool) ? new(NumberClass.Whole)
                    : type == typeof(string) ? new(NumberClass.Text)
                    : type == typeof(DateTime) ? new(NumberClass.Date)
                    : new(NumberClass.Other);
            case UnaryExpression { Operator: UnaryOperator.Negate } negation:
                return Of(negation.Operand);
            case BinaryExpression { Operator: BinaryOperator.Multiply or BinaryOperator.Divide } product:
                return Product(Of(product.Left), Of(product.Right), product.Operator == BinaryOperator.Divide);
            case BinaryExpression { Operator: BinaryOperator.Add or BinaryOperator.Subtract } sum:
                return Sum(Of(sum.Left), Of(sum.Right), sum.Operator == BinaryOperator.Add);
            case BinaryExpression { Operator: BinaryOperator.IntDivide or BinaryOperator.Modulo }:
                return new(NumberClass.Whole);
            case BinaryExpression { Operator: BinaryOperator.Power }:
                return new(NumberClass.Double);
            case FunctionCall function when function.Name.Equals("CCUR", StringComparison.OrdinalIgnoreCase):
                return new(NumberClass.Currency);
            case FunctionCall { Arguments: [var argument] } function
                when function.Name.ToUpperInvariant() is "SUM" or "MIN" or "MAX" or "FIRST" or "LAST":
                return Of(argument);
            case FunctionCall function:
                Type? returns = declaredType(function);
                return returns == typeof(string) ? new(NumberClass.Text)
                    : returns == typeof(DateTime) ? new(NumberClass.Date)
                    : new(NumberClass.Other);
            default:
                return new(NumberClass.Other);
        }
    }

    private static bool IsWhole(NumberType type) => type.Class is NumberClass.Whole or NumberClass.Text or NumberClass.Date;

    private static NumberType Product(NumberType left, NumberType right, bool divide)
    {
        if (left.Class == NumberClass.Other || right.Class == NumberClass.Other)
            return new(NumberClass.Other);
        if (left.Class == NumberClass.Double || right.Class == NumberClass.Double
            || divide && (left.Class == NumberClass.Currency || right.Class == NumberClass.Currency))
            return new(NumberClass.Double);
        if (IsWhole(left) && IsWhole(right))
            return divide ? new(NumberClass.Double) : new(NumberClass.Whole);
        if (IsWhole(left)) return right;
        if (IsWhole(right)) return left;
        if (left.Class == NumberClass.Currency && right.Class == NumberClass.Currency)
            return new(NumberClass.Currency);
        int leftPlaces = left.Class == NumberClass.Currency ? 4 : left.Places;
        int rightPlaces = right.Class == NumberClass.Currency ? 4 : right.Places;
        return leftPlaces == rightPlaces ? new(NumberClass.Decimal, leftPlaces) : new(NumberClass.Double);
    }

    private static NumberType Sum(NumberType left, NumberType right, bool add)
    {
        if (left.Class is NumberClass.Other or NumberClass.Date || right.Class is NumberClass.Other or NumberClass.Date
            || add && left.Class == NumberClass.Text && right.Class == NumberClass.Text)
            return new(NumberClass.Other);
        if (left.Class == NumberClass.Double || right.Class == NumberClass.Double)
            return new(NumberClass.Double);
        if (IsWhole(left) && IsWhole(right))
            return new(NumberClass.Whole);
        if (IsWhole(left)) return right;
        if (IsWhole(right)) return left;
        if (left.Class == NumberClass.Currency && right.Class == NumberClass.Currency)
            return new(NumberClass.Currency);
        if (left.Class == NumberClass.Currency || right.Class == NumberClass.Currency)
            return new(NumberClass.Decimal, Math.Max(4, Math.Max(left.Places, right.Places)));
        return left.Places == right.Places ? left : new(NumberClass.Double);
    }

    /// <summary>
    /// A value as it goes into a result column of <paramref name="type"/>: a Decimal column keeps only its places,
    /// the rest cut off. A Double or Single — LibRed's type for a written decimal literal — becomes a Decimal the
    /// way OLE Automation converts it (<see cref="JetDecimalConverter"/>), so binary rounding (0.6 held as
    /// 0.5999…) cannot drop a digit.
    /// </summary>
    internal static object? ToResultPlaces(object? value, NumberType type) =>
        type.Class != NumberClass.Decimal ? value : value switch
        {
            decimal d => decimal.Round(d, type.Places, MidpointRounding.ToZero),
            double or float => CutFloating(value, type.Places),
            _ => value,
        };

    private static object CutFloating(object value, int places)
    {
        try
        {
            decimal exact = value is float f ? JetDecimalConverter.FromSingle(f) : JetDecimalConverter.FromDouble((double)value);
            decimal cut = decimal.Round(exact, places, MidpointRounding.ToZero);
            return value is float ? (float)cut : (double)cut;
        }
        catch (OverflowException)
        {
            return value;   // past a Decimal: ACE could not have held it as one either
        }
    }

    /// <summary>A number as a Decimal for arithmetic with a Decimal: a Double or Single converted the OLE Automation
    /// way (<see cref="JetDecimalConverter"/>), a date as its serial.</summary>
    private static decimal ArithmeticDecimal(object v) => v switch
    {
        double d => JetDecimalConverter.FromDouble(d),
        float f => JetDecimalConverter.FromSingle(f),
        DateTime date => SerialDecimal(date),
        _ => Dec(v),
    };

    private static double Finite(double value) =>
        double.IsFinite(value) ? value : throw new OverflowException("Overflow: the result is too large for a number.");

    private static float Finite(float value) =>
        float.IsFinite(value) ? value : throw new OverflowException("Overflow: the result is too large for a number.");

    /// <summary>The date at an OLE Automation serial; a serial outside 100-01-01 … 9999-12-31 is an overflow.</summary>
    private static DateTime OaDate(double serial)
    {
        try
        {
            return RoundToSecond(DateTime.FromOADate(serial));
        }
        catch (ArgumentException)
        {
            throw new OverflowException($"Overflow: {serial} is outside the range of a date.");
        }
    }

    /// <summary>A date's day number as a Decimal, built from its parts rather than converted from the Double serial
    /// (before the epoch the time fraction still counts away from zero, as in the OLE Automation serial).</summary>
    private static decimal SerialDecimal(DateTime d)
    {
        decimal days = (d.Date - OaEpoch).Days;
        decimal time = d.TimeOfDay.Ticks / (decimal)TimeSpan.TicksPerDay;
        return days >= 0 ? days + time : days - time;
    }


    /// <summary>
    /// Access <c>^</c> (verified vs ACE): Double, a date read as its serial. A negative base with a fractional
    /// exponent is an invalid procedure call, zero to a negative power a division by zero, and a result past a
    /// Double an overflow.
    /// </summary>
    private static double Power(object left, object right)
    {
        double x = Oa(left), y = Oa(right);
        double result = Math.Pow(x, y);
        if (double.IsNaN(result))
            throw new ArgumentException($"Invalid procedure call: {x} cannot be raised to the power {y}.");
        if (double.IsInfinity(result))
            throw x == 0 ? new DivideByZeroException("Division by zero: zero raised to a negative power.") : new OverflowException("Overflow: the result is too large for a number.");
        return result;
    }

    /// <summary>
    /// Access <c>+</c> (verified vs ACE). Two text operands concatenate — a GUID or binary value counts as text.
    /// Otherwise the operands add, text read as a number (<see cref="TextAsNumber"/>); so <c>'1' + 1</c> is 2 and
    /// <c>'1' + #2020-01-02#</c> is the next day. A GUID or binary value with anything but text is a type
    /// mismatch. Null has already propagated.
    /// </summary>
    private static object Add(object left, object right) =>
        IsConcatText(left) && IsConcatText(right)
            ? ConcatText(left) + ConcatText(right)
            : Arithmetic(NumericOperand(left)!, NumericOperand(right)!, '+');

    private static bool IsConcatText(object v) => v is string or char or Guid or byte[];

    /// <summary>An arithmetic operand: text read as a number (<see cref="TextAsNumber"/>); a GUID or binary value
    /// is a type mismatch; anything else, Null included, as it is. A <see cref="char"/> (a parameter can carry one)
    /// is one character of text.</summary>
    private static object? NumericOperand(object? v) => v switch
    {
        string s => TextAsNumber(s),
        char c => TextAsNumber(c.ToString()),
        Guid or byte[] => throw new InvalidCastException("Type mismatch: a GUID or binary value is not a number."),
        _ => v,
    };

    /// <summary>
    /// A value as <c>&amp;</c> (and <c>+</c> between two texts) writes it (verified vs ACE). A Boolean is its
    /// VARIANT_BOOL number; a Double has 15 significant digits and a Single 7; a Decimal drops trailing zeros; a
    /// date is written in the regional short date and long time, without the time at midnight and without the
    /// date on 1899-12-30; a GUID is braced upper case; a binary value is read as UTF-16 text.
    /// </summary>
    private static string ConcatText(object v) => v switch
    {
        string s => s,
        bool b => b ? "-1" : "0",
        double d => FloatingText(d, 15),
        float f => FloatingText(f, 7),
        decimal m => m.ToString("0.############################", CultureInfo.CurrentCulture),
        DateTime d => DateText(d),
        Guid g => g.ToString("B").ToUpperInvariant(),
        byte[] => ToText(v),
        _ => Convert.ToString(v, CultureInfo.CurrentCulture)!,
    };

    /// <summary>
    /// A floating value rounded to <paramref name="digits"/> significant digits, trailing zeros dropped. Written
    /// in E notation when its exponent is at least <paramref name="digits"/>, or when fixed notation would need
    /// more than <paramref name="digits"/> decimals (verified vs ACE: <c>1/3</c> is 0.333333333333333, a Single
    /// 1E7 is 1E+07, a Single 1E-5 is 0.00001, 1E300 is 1E+300).
    /// </summary>
    private static string FloatingText(double value, int digits)
    {
        if (value == 0) return "0";
        if (!double.IsFinite(value)) return value.ToString(CultureInfo.InvariantCulture);

        NumberFormatInfo format = CultureInfo.CurrentCulture.NumberFormat;
        string scientific = value.ToString("E" + (digits - 1), CultureInfo.InvariantCulture);  // -d.dddE+ddd
        int mark = scientific.IndexOf('E');
        int exponent = int.Parse(scientific[(mark + 1)..], CultureInfo.InvariantCulture);
        bool negative = scientific[0] == '-';
        string significant = scientific[(negative ? 1 : 0)..mark].Replace(".", "").TrimEnd('0');
        string sign = negative ? format.NegativeSign : "";
        string point = format.NumberDecimalSeparator;

        int decimals = significant.Length - 1 - exponent;
        if (exponent >= digits || decimals > digits)
        {
            string mantissa = significant.Length == 1 ? significant : significant[0] + point + significant[1..];
            return $"{sign}{mantissa}E{(exponent < 0 ? "-" : "+")}{Math.Abs(exponent):00}";
        }
        if (exponent < 0)
            return sign + "0" + point + new string('0', -exponent - 1) + significant;
        if (significant.Length <= exponent + 1)
            return sign + significant + new string('0', exponent + 1 - significant.Length);
        return sign + significant[..(exponent + 1)] + point + significant[(exponent + 1)..];
    }

    private static readonly DateTime OaEpoch = new(1899, 12, 30);

    /// <summary>A date as <see cref="ConcatText"/> writes it; the year is not zero-padded (year 100 is "100").</summary>
    private static string DateText(DateTime d)
    {
        DateTimeFormatInfo format = CultureInfo.CurrentCulture.DateTimeFormat;
        string time = d.ToString(format.LongTimePattern, CultureInfo.CurrentCulture);
        if (d.Date == OaEpoch) return time;
        string datePattern = format.ShortDatePattern.Replace("yyyy", "'" + d.Year.ToString(CultureInfo.InvariantCulture) + "'");
        string date = d.ToString(datePattern, CultureInfo.CurrentCulture);
        return d.TimeOfDay == TimeSpan.Zero ? date : date + " " + time;
    }

    /// <summary>
    /// Text as <c>+</c> reads it as a number, in the regional separators and currency symbol (verified vs ACE).
    /// Surrounding whitespace is skipped. One sign may come before or after the number, spaced from it; brackets
    /// make it negative but cannot be combined with a sign; one currency symbol may come on either side. Group
    /// separators are ignored once a digit has been read, even after the decimal point. An <c>e</c> or <c>d</c>
    /// exponent needs at least one digit. <c>&amp;H</c>/<c>&amp;O</c> text is a whole number, read as a Long when
    /// it fits 32 bits (<c>&amp;HFFFFFFFF</c> is -1). Anything else is a type mismatch, and a value past a Double
    /// is an overflow.
    /// </summary>
    private static double TextAsNumber(string text)
    {
        double value = double.Parse(NumberText(text), NumberStyles.Float, CultureInfo.InvariantCulture);
        if (double.IsInfinity(value))
            throw new OverflowException($"Overflow: '{text}' is too large for a number.");
        return value;
    }

    /// <summary>Text read as <see cref="TextAsNumber"/> reads it, but exactly; null when it is past a Decimal.</summary>
    private static decimal? TextAsDecimal(string text) =>
        decimal.TryParse(NumberText(text), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) ? value : null;

    /// <summary>The number <see cref="TextAsNumber"/> reads, written out in invariant form.</summary>
    private static string NumberText(string text)
    {
        NumberFormatInfo format = CultureInfo.CurrentCulture.NumberFormat;
        string s = text.Trim();
        if (s.Length > 1 && s[0] == '&' && s[1] is 'H' or 'h' or 'O' or 'o')
            return RadixNumber(text, s[2..], s[1] is 'H' or 'h' ? 16 : 8).ToString(CultureInfo.InvariantCulture);

        int i = 0;
        bool negative = false, signed = false, currency = false, open = false, closed = false;
        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s[i])) i++;
            else if (s[i] is '+' or '-' && !signed) { signed = true; negative = s[i] == '-'; i++; }
            else if (s[i] == '(' && !open) { open = true; i++; }
            else if (!currency && At(s, i, format.CurrencySymbol)) { currency = true; i += format.CurrencySymbol.Length; }
            else break;
        }

        var number = new StringBuilder();
        bool digits = false, point = false;
        while (i < s.Length)
        {
            if (s[i] is >= '0' and <= '9') { number.Append(s[i]); digits = true; i++; }
            else if (!point && At(s, i, format.NumberDecimalSeparator)) { number.Append('.'); point = true; i += format.NumberDecimalSeparator.Length; }
            else if (digits && At(s, i, format.NumberGroupSeparator)) i += format.NumberGroupSeparator.Length;
            else break;
        }
        if (!digits) throw NotANumber(text);
        if (point && number[^1] == '.') number.Append('0');

        if (i < s.Length && s[i] is 'e' or 'E' or 'd' or 'D')
        {
            number.Append('E');
            i++;
            if (i < s.Length && s[i] is '+' or '-') number.Append(s[i++]);
            int start = i;
            while (i < s.Length && s[i] is >= '0' and <= '9') number.Append(s[i++]);
            if (i == start) throw NotANumber(text);
        }

        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s[i])) i++;
            else if (s[i] is '+' or '-' && !signed) { signed = true; negative = s[i] == '-'; i++; }
            else if (s[i] == ')' && open && !closed) { closed = true; i++; }
            else if (!currency && At(s, i, format.CurrencySymbol)) { currency = true; i += format.CurrencySymbol.Length; }
            else throw NotANumber(text);
        }
        if (open != closed || (open && signed)) throw NotANumber(text);

        return negative || open ? "-" + number : number.ToString();
    }

    private static long RadixNumber(string text, string digits, int radix)
    {
        if (digits.Length == 0) throw NotANumber(text);
        ulong value = 0;
        foreach (char c in digits)
        {
            int digit = c is >= '0' and <= '9' ? c - '0'
                : c is >= 'A' and <= 'F' ? c - 'A' + 10
                : c is >= 'a' and <= 'f' ? c - 'a' + 10
                : int.MaxValue;
            if (digit >= radix) throw NotANumber(text);
            value = checked(value * (ulong)radix + (ulong)digit);
        }
        return value <= uint.MaxValue ? (int)(uint)value : checked((long)value);
    }

    private static bool At(string s, int index, string symbol) =>
        symbol.Length > 0 && string.CompareOrdinal(s, index, symbol, 0, symbol.Length) == 0;

    private static InvalidCastException NotANumber(string text) =>
        new($"Type mismatch: '{text}' cannot be read as a number.");

    /// <summary>Access <c>/</c> is floating division — Decimal when either operand is Decimal/Currency,
    /// otherwise Double (never integer division; that is <c>\</c>). A date divides as its serial; dividing by
    /// zero is an error and so is a result past a Double (verified vs ACE: 1 / 0 and 0 / 0 fail).</summary>
    private static object Divide(object left, object right)
    {
        if (left is decimal || right is decimal)
            return ArithmeticDecimal(left) / ArithmeticDecimal(right);
        // A Single divided with only Singles, Integers or Booleans divides in single precision (verified vs
        // ACE: TRUE / a Single 1.5 is -0.6666666865348816), though the result is still a Double.
        if ((left is float || right is float) && IsSingleWidth(left) && IsSingleWidth(right))
        {
            float singleDivisor = Sng(right);
            if (singleDivisor == 0)
                throw new DivideByZeroException("Division by zero.");
            return (double)Finite(Sng(left) / singleDivisor);
        }
        double divisor = Oa(right);
        if (divisor == 0)
            throw new DivideByZeroException("Division by zero.");
        return Finite(Oa(left) / divisor);
    }

    // Not Byte: ACE treats a Byte column as a Long here (verified: a Byte 1 / a Single 1.5 is 0.6666666666666666).
    private static bool IsSingleWidth(object v) => v is float or short or bool;

    /// <summary>Access integer operators <c>\</c> (int division) and <c>MOD</c>: operands round to an
    /// integer (half to even), a date as its serial, and the result keeps the operand's integer type (int, or
    /// long if either is Int64) — so <c>int \ int</c> is Int32, matching the EF contract. Anything MOD -1 is 0,
    /// even the smallest Long (verified vs ACE).</summary>
    private static object IntegerOp(object left, object right, char op)
    {
        left = Serial(left);
        right = Serial(right);
        if (left is long or ulong || right is long or ulong)
        { long a = Lng(left), b = Lng(right); return op == '%' ? (b == -1 ? 0L : a % b) : a / b; }
        int x = Int(left), y = Int(right); return op == '%' ? (y == -1 ? 0 : x % y) : x / y;
    }

    /// <summary>VBA <c>CStr</c>. A Double renders at 15 significant digits and a Single at 7 — the OA/VB
    /// convention, not .NET Core 3.0+'s shortest-round-trippable form, which would turn <c>0.1+0.2</c> into
    /// "0.30000000000000004" (verified vs ACE: "0.3", and <c>CStr(CSng(1/3))</c> is "0.3333333"). A Boolean
    /// renders as its VARIANT_BOOL number, "-1" — note that is the Jet Expression Service's behaviour and
    /// differs from the VBA runtime proper, which renders "True".</summary>
    private static string VbaString(object v) => v switch
    {
        bool b => b ? "-1" : "0",
        double d => d.ToString("G15", CultureInfo.InvariantCulture),
        float f => f.ToString("G7", CultureInfo.InvariantCulture),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture)!,
    };

    /// <summary>VBA <c>CBool</c>: any non-zero number is True (so 0.5 is True), and a string may hold a number
    /// ("-1") as well as "True"/"False". <see cref="Convert.ToBoolean(object)"/> rejects the numeric-string form
    /// with a FormatException, so ACE accepts input LibRed used to refuse (verified vs ACE).</summary>
    private static bool VbaBool(object v) => v switch
    {
        bool b => b,
        string s => bool.TryParse(s, out var parsed)
            ? parsed
            : double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)
                ? n != 0
                : throw new InvalidOperationException($"Type mismatch: '{s}' cannot be converted to Boolean."),
        _ => Dbl(v) != 0,
    };

    // Jet's boolean convention (true = -1, false = 0) so a bool matches the numeric column it is stored in.
    private static object Numeric(object v) => v is bool b ? (b ? -1 : 0) : v;
    private static decimal Dec(object v) => Convert.ToDecimal(Numeric(v), CultureInfo.InvariantCulture);
    private static double Dbl(object v) => Convert.ToDouble(Numeric(v), CultureInfo.InvariantCulture);
    // Narrow to single precision (the cast yields ±Infinity for an out-of-range double rather than throwing).
    private static float Sng(object v) => (float)Dbl(v);
    private static long Lng(object v) => Convert.ToInt64(Numeric(v), CultureInfo.InvariantCulture);
    private static int Int(object v) => Convert.ToInt32(Numeric(v), CultureInfo.InvariantCulture);

    // For date arithmetic: a DateTime becomes its OLE Automation serial; a number is taken verbatim (as days).
    private static double Oa(object v) => v is DateTime d ? d.ToOADate() : Dbl(v);

    // Rounds to the nearest whole second — Jet stores no sub-second, and the OA-serial round-trip can leave
    // a value a few ticks shy of/past an exact second (e.g. 21:05:18.9999999).
    private static DateTime RoundToSecond(DateTime d)
    {
        long rem = d.Ticks % TimeSpan.TicksPerSecond;
        return rem >= TimeSpan.TicksPerSecond - rem
            ? d.AddTicks(TimeSpan.TicksPerSecond - rem)
            : d.AddTicks(-rem);
    }

    private static int Compare(object left, object right)
    {
        if (IsNumeric(left) && IsNumeric(right))
        {
            // A single-precision operand (a Single column value, a CSNG result, a SUM of singles) compares in
            // single precision — narrow the other side to Single too. Widening the single to double instead
            // exposes its rounding (a stored -1.234f is -1.2339999675… as a double) and breaks equality against
            // a double literal, whereas ACE compares the literal in the column's single precision (no CSNG in
            // the SQL). Only a Single present, no genuine double column, triggers this.
            if (left is float || right is float)
                return Sng(left).CompareTo(Sng(right));

            // Otherwise compare in double when either side is floating point: a double can exceed decimal's
            // range (e.g. EXP of a large value) and coercing it to decimal overflows. For integer/decimal
            // operands keep decimal, which holds 64-bit integers and exact decimals without the precision loss
            // double would introduce.
            return left is double || right is double
                ? Dbl(left).CompareTo(Dbl(right))
                : Dec(left).CompareTo(Dec(right));
        }

        // Binary (byte[]) columns: structural, length-sensitive byte compare — lexicographic then by
        // length, so a shorter value sorts before a longer one sharing its prefix (Jet's binary order,
        // matching IndexKeyEncoder). Without this, byte[] falls through to ToString() ("System.Byte[]"
        // for every array) and all binaries compare *equal* — so `WHERE binKey = @p` matches every row.
        if (left is byte[] lb && right is byte[] rb)
            return CompareBytes(lb, rb);

        if (left is string || right is string)
            return CompareText(left.ToString()!, right.ToString()!);

        // Dates compare by their OLE Automation serial rather than chronologically. Below the epoch
        // (1899-12-30) the day count is negative while the time fraction stays positive, so 1899-12-29 06:00 is
        // -1.25 and 18:00 is -1.75 — later in the day is the SMALLER serial. ACE compares and orders on that raw
        // serial and therefore puts later pre-epoch times first (verified in
        // LibRed.Core.Tests.AcePreEpochDateProbeTest: `06:00 < 18:00` is False, ORDER BY gives 1,3,2,4,5,6).
        //
        // Matching it is not only about ACE parity: IndexKeyEncoder writes this same serial as the index key,
        // and that encoding cannot change because ACE writes those keys too. Comparing chronologically here
        // while the index compares by serial made an index seek and a table scan return DIFFERENT rows for a
        // pre-epoch range (see PreEpochDateOrderingTests). From the epoch onward the two orders are identical,
        // so this only affects pre-1899 dates.
        if (left is DateTime leftDate && right is DateTime rightDate)
            return leftDate.ToOADate().CompareTo(rightDate.ToOADate());

        if (left is IComparable c && left.GetType() == right.GetType())
            return c.CompareTo(right);

        return CompareText(left.ToString()!, right.ToString()!);
    }

    /// <summary>Whether two non-null values are equal under the same coercions as <c>=</c> (used by the hash
    /// join to re-check a bucket candidate). Only meaningful within one type kind — see <see cref="KeyHash"/>.</summary>
    public static bool KeyEqual(object a, object b) => Compare(a, b) == 0;

    /// <summary>A hash for a non-null join key that agrees with <see cref="KeyEqual"/> within a type kind: values
    /// the evaluator treats as equal hash the same (numeric via double, text via Access's case-insensitive/
    /// trailing-space-trimmed collation, binary structurally). The planner only builds a hash join over
    /// same-kind key columns, so this is total over the keys it actually sees.</summary>
    public static int KeyHash(object v) => v switch
    {
        byte[] b => BinaryHash(b),
        string s => System.Globalization.CultureInfo.InvariantCulture.CompareInfo
            .GetHashCode(s.TrimEnd(' '), System.Globalization.CompareOptions.IgnoreCase),
        _ when IsNumeric(v) => Dbl(v).GetHashCode(),
        _ => v.GetHashCode(),
    };

    private static int BinaryHash(byte[] b)
    {
        var h = new HashCode();
        h.AddBytes(b);
        return h.ToHashCode();
    }

    /// <summary>Lexicographic byte comparison, then by length (shorter prefix sorts first).</summary>
    private static int CompareBytes(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        return a.Length.CompareTo(b.Length);
    }

    /// <summary>Access text comparison: **case-insensitive**, **trailing spaces ignored**, and
    /// **accent-aware ordering** (Access "General" collation). Uses invariant-culture ignore-case, which
    /// matches ACE where ordinal doesn't — an accented letter sorts next to its base letter (verified vs
    /// ACE: <c>'é' &lt; 'f'</c>, <c>'café' &lt; 'cafz'</c>) while accents stay significant for equality
    /// (<c>'café' ≠ 'cafe'</c>). (Ignorable apostrophe/hyphen ordering is not reproduced.)</summary>
    private static int CompareText(string a, string b) =>
        string.Compare(a.TrimEnd(' '), b.TrimEnd(' '), StringComparison.InvariantCultureIgnoreCase);

    /// <summary>Orders two values for SORT (nulls first), using the same coercion as comparisons.</summary>
    public static int CompareForSort(object? a, object? b) => (a, b) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        _ => Compare(a, b),
    };

    // Booleans count as numeric for comparison: EF maps CLR bool to a numeric (smallint) column, and
    // a boolean predicate (e.g. IS NOT NULL) must compare equal to that stored value. The comparison
    // coercions (Dec/Dbl) use Jet's convention (false = 0, true = -1) so a bool matches the numeric value
    // it is stored as.
    internal static bool IsNumeric(object v) =>
        v is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
}
