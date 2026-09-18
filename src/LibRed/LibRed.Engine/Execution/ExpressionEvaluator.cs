using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EntityFrameworkCore.Jet.Data;
using LibRed.Sql.Ast;
using LibRed.Storage;

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
internal sealed partial class ExpressionEvaluator(
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
        OutputColumnPosition p => scope.At(p.Position),
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
    /// common kind as <c>=</c> does (verified vs ACE). A miss is Null when an item is Null, as the standard has it:
    /// <c>5 IN (1, NULL)</c> and <c>5 NOT IN (1, NULL)</c> are both Null. This departs from ACE, which skips a Null
    /// item and makes them False and True. A <c>True</c> or <c>False</c> item is compared as -1 or 0, not as a truth
    /// test.</summary>
    private object? EvaluateInList(InListExpression inl)
    {
        object? val = Evaluate(inl.Value);
        if (val is null) return null;

        bool found = false, hasNull = false;
        foreach (Expression itemExpr in inl.Items)
        {
            if (Evaluate(itemExpr) is not { } item) hasNull = true;
            else if (CompareAsKinds(val, item) == 0) { found = true; break; }
        }
        return !found && hasNull ? null : found != inl.Negated;
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

    // What Trim, LTrim and RTrim strip (verified vs ACE): the space and the ideographic space U+3000, in any mixture —
    // not a tab, CR, LF, no-break space, the other Unicode spaces or a zero-width one.
    private static readonly char[] TrimmedSpaces = [' ', '　'];

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
        if (f.Filter is not null && !Planning.QueryPlanner.IsAggregate(name))
            throw new InvalidOperationException($"{f.Name} takes no FILTER; only an aggregate does.");
        ValidateArity(name, f.Arguments.Count);

        return name switch
        {
            "IIF" => Evaluate(f.Arguments[0]) is { } condition && IifCondition(condition) ? Evaluate(f.Arguments[1])
                : f.Arguments.Count == 3 ? Evaluate(f.Arguments[2]) : null,
            "CHOOSE" => Choose(f),
            "SWITCH" => Switch(f),
            "NULLIF" => NullIf(f),
            "COALESCE" => Coalesce(f),
            "GREATEST" => Extreme(f, greatest: true),
            "LEAST" => Extreme(f, greatest: false),
            "DATEPART" => DatePart(f),
            "ROUND" => Round(f),
            "FIX" => Numeric1(f, Math.Truncate, Math.Truncate, keepsDate: true),  // toward zero
            "INT" => Numeric1(f, Math.Floor, Math.Floor, keepsDate: true),        // toward -infinity
            "ABS" => Numeric1(f, Math.Abs, Math.Abs, keepsDate: false),
            // VBA/Access type-conversion functions (verified vs ACE). Each reads its argument as ConversionNumber
            // does: text as a number, a date as its serial, True as -1 — so CByte(True) overflows. CInt/CLng/CByte
            // round half to even, as Convert.ToInt16/Int32/Byte do, and a value past the type is an overflow. ACE
            // raises "Invalid use of Null" for a Null argument; LibRed returns Null. CVar passes its argument
            // through (LibRed has no Variant type; ACE hands the value back as text).
            "CCUR" => DecimalArgument(f, ToCurrency),
            "CBOOL" => Convert1(f, v => VbaBool(v)),
            "CBYTE" => Convert1(f, v => Convert.ToByte(ConversionNumber(v), CultureInfo.InvariantCulture)),
            "CINT" => Convert1(f, v => (short)AsInteger(v)),
            "CLNG" => Convert1(f, v => AsLong(v)),
            // CLngLng is VBA's LongLong conversion, which the Jet Expression Service does not have (verified: ACE
            // reports it undefined), so a LibRed extension. It reads its argument as CLng does, into an Int64.
            "CLNGLNG" => Convert1(f, v => AsLongLong(v)),
            "CSNG" => Convert1(f, v => Finite(Sng(ConversionNumber(v)))),
            "CDBL" => Convert1(f, v => Dbl(ConversionNumber(v))),
            // CDec has no ACE equivalent — the Jet Expression Service has no such function — so this is a
            // LibRed extension with no parity contract to honour. CCur is ACE's route to a decimal.
            "CDEC" => DecimalArgument(f, number => number),
            "CSTR" => Convert1(f, ConcatText),
            "CDATE" => Convert1(f, v => ToDate(v)),
            "CVAR" => Evaluate(f.Arguments[0]),

            // VBA/Access string functions (verified vs ACE). A value that is not text is read as CStr writes it
            // (ConcatText): True is "-1", a date in the regional format, a GUID braced, and a binary value as
            // UTF-16 text, so Len(0x4100) is 1 where LenB is 2. Positions are 1-based and read as the conversion
            // functions read numbers; a Null argument gives Null. Text compares in the database sort order unless
            // a compare argument of 0 asks for a binary comparison.
            "LEN" => Convert1(f, v => ConcatText(v).Length),
            "LCASE" => Convert1(f, v => ConcatText(v).ToLowerInvariant()),
            "UCASE" => Convert1(f, v => ConcatText(v).ToUpperInvariant()),
            "TRIM" => Convert1(f, v => ConcatText(v).Trim(TrimmedSpaces)),
            "LTRIM" => Convert1(f, v => ConcatText(v).TrimStart(TrimmedSpaces)),
            "RTRIM" => Convert1(f, v => ConcatText(v).TrimEnd(TrimmedSpaces)),
            "LEFT" => StringInt(f, static (s, n) => n <= 0 ? "" : n >= s.Length ? s : s[..n]),
            "RIGHT" => StringInt(f, static (s, n) => n <= 0 ? "" : n >= s.Length ? s : s[^n..]),
            "MID" => Mid(f),
            "INSTR" => Instr(f),
            "REPLACE" => Replace(f),

            // Date/time functions (verified vs ACE). A date argument is read as CDate reads it: text as a date in
            // the regional format, otherwise as a number, and a number as the date at that serial. Settings and
            // counts are read as CInt reads them. A Null argument gives Null. LibRed keeps milliseconds where ACE
            // rounds to the second, so Second(0.00001) is 0 here and 1 in ACE.
            "DATEADD" => DateAdd(f),
            "DATEDIFF" => DateDiff(f),
            "DATESERIAL" => DateParts(f, DateSerial),
            "TIMESERIAL" => DateParts(f, static (h, m, s) => OaDate((h * 3600 + m * 60 + s) / 86400.0)),
            "NOW" => DateTime.Now,
            "DATE" => DateTime.Today,
            "TIME" => DateTime.FromOADate(0).Add(DateTime.Now.TimeOfDay),
            "YEAR" => Convert1(f, v => ToDate(v).Year),
            "MONTH" => Convert1(f, v => ToDate(v).Month),
            "DAY" => Convert1(f, v => ToDate(v).Day),
            "HOUR" => Convert1(f, v => ToDate(v).Hour),
            "MINUTE" => Convert1(f, v => ToDate(v).Minute),
            "SECOND" => Convert1(f, v => ToDate(v).Second),
            "WEEKDAY" => Weekday(f),
            // DateValue is the date at midnight and TimeValue the time on 1899-12-30. Unlike CDate they take only a
            // date or text that reads as one; a number is a type mismatch. IsDate is true for exactly those.
            "DATEVALUE" => Convert1(f, v => DateValueArgument(v).Date),
            "TIMEVALUE" => Convert1(f, v => DateTime.FromOADate(0).Add(DateValueArgument(v).TimeOfDay)),
            "ISDATE" => TryDateText(Evaluate(f.Arguments[0]), out _),
            // Jet VBA math functions (double precision). SQR = sqrt, ATN = atan, LOG = natural log.
            // Acos/Asin/Atan2/Floor/Ceiling/Log10/Log-base are emitted by EF as expressions built from
            // these plus arithmetic, so they need no dedicated cases.
            // ACE's Tan is its Sin over its Cos, which differs from a direct tangent in the last digit.
            "SIN" => UnaryDouble(f, Trigonometric(Math.Sin)),
            "COS" => UnaryDouble(f, Trigonometric(Math.Cos)),
            "TAN" => UnaryDouble(f, Trigonometric(x => Math.Sin(x) / Math.Cos(x))),
            "ATN" => UnaryDouble(f, Math.Atan),
            "EXP" => UnaryDouble(f, Math.Exp),
            "LOG" => UnaryDouble(f, Math.Log),
            "SQR" => UnaryDouble(f, Math.Sqrt),
            // SGN sits apart from the group above: it takes a double but yields an Integer, both in VBA
            // (Sgn returns Variant/Integer) and in .NET (Math.Sign returns int). Going through UnaryDouble
            // widened that int straight back to a double, which only showed once EF projected the value
            // instead of comparing it - GetInt32 on a boxed Double throws.
            "SGN" => Convert1(f, v => Math.Sign(Dbl(ConversionNumber(v)))),

            // More VBA/Access built-ins (verified vs ACE via the function-whitelist sweep). All NULL-propagating
            // via Convert1 unless noted; positions are 1-based.
            // Asc and Chr work in the system ANSI code page (Chr takes 0-255; Chr(128) is '€', Asc('Ā') is 65 by
            // best fit); AscW and ChrW in UTF-16 code units, AscW signed and ChrW taking -32768 to 65535.
            "ASC" => Convert1(f, v => (int)Ansi.GetBytes(FirstCharacter(v))[0]),
            "CHR" => Convert1(f, v => AnsiCharacter(InRange(AsLong(v), 0, 255)).ToString()),
            "SPACE" => Convert1(f, v => new string(' ', Count(v))),
            "STRING" => StringOf(f),                         // String(count, char) → char repeated count times
            "STRREVERSE" => Convert1(f, v => new string(ConcatText(v).Reverse().ToArray())),
            "STRCOMP" => StrComp(f),                         // -1/0/1 (case-insensitive, Access "Compare Database")
            "STR" => Convert1(f, VbaStr),                    // number → text with a leading space when non-negative
            "VAL" => Convert1(f, v => VbaVal(ConcatText(v))),
            "HEX" => Convert1(f, v => RadixText(v, 16)),
            "OCT" => Convert1(f, v => RadixText(v, 8)),
            "INSTRREV" => InstrRev(f),                       // last occurrence, 1-based (0 if none)
            // MonthName(month, [abbreviate]). The second argument was accepted by the arity table and then
            // ignored by Convert1, so MonthName(1, True) returned "January" where ACE returns "Jan" — a
            // silently wrong value, and the arity check that would have caught a stray argument is what let
            // it through. WeekdayName next to it always honoured its own.
            "MONTHNAME" => MonthNameOf(f),
            "TIMER" => (DateTime.Now - DateTime.Today).TotalSeconds,
            "RND" => Rnd(f),
            // Predicates / type inspection. IsError is always false — LibRed has no error-value type — but its argument
            // is still evaluated, so an error in it is raised (verified vs ACE: IsError(1/0) fails). ISNULL returns a
            // Boolean (ACE reports it as -1/0); both print as a boolean here.
            "ISNULL" => Evaluate(f.Arguments[0]) is null,
            "ISNUMERIC" => IsNumericValue(Evaluate(f.Arguments[0])),
            "ISERROR" => IsError(f),
            "TYPENAME" => TypeNameOf(Evaluate(f.Arguments[0]), f.Arguments[0]),
            "VARTYPE" => VarTypeOf(Evaluate(f.Arguments[0]), f.Arguments[0]),
            "STRCONV" => StrConv(f),
            "WEEKDAYNAME" => WeekdayNameOf(f),
            "PARTITION" => PartitionOf(f),
            "FORMAT" => FormatValue(f),
            "FORMATCURRENCY" => FormatStyled(f, NumberStyle.Currency),
            "FORMATNUMBER" => FormatStyled(f, NumberStyle.Number),
            "FORMATPERCENT" => FormatStyled(f, NumberStyle.Percent),
            "FORMATDATETIME" => FormatDateTime(f),
            "RGB" => Rgb(f),
            "QBCOLOR" => Convert1(f, v => QbColors[Setting(v, 0, 15)]),
            // Financial functions (verified vs ACE). rate is per period; pv/fv/pmt sign conventions follow VBA.
            "PMT" => Financial(f, a => Pmt(a[0], a[1], a[2], a[3], Due(a[4]))),
            "FV" => Financial(f, a => Fv(a[0], a[1], a[2], a[3], Due(a[4]))),
            "PV" => Financial(f, a => Pv(a[0], a[1], a[2], a[3], Due(a[4]))),
            "NPER" => Financial(f, a => NPer(a[0], a[1], a[2], a[3], Due(a[4]))),
            "IPMT" => Financial(f, a => IPmt(a[0], a[1], a[2], a[3], a[4], Due(a[5]))),
            "PPMT" => Financial(f, a => PPmt(a[0], a[1], a[2], a[3], a[4], Due(a[5]))),
            "RATE" => Financial(f, a => Rate(a[0], a[1], a[2], a[3], Due(a[4]), f.Arguments.Count > 5 ? a[5] : 0.1)),
            "SLN" => Financial(f, a => Sln(a[0], a[1], a[2])),
            "SYD" => Financial(f, a => Syd(a[0], a[1], a[2], a[3])),
            "DDB" => Financial(f, a => Ddb(a[0], a[1], a[2], a[3], f.Arguments.Count > 4 ? a[4] : 2)),

            "ASCW" => Convert1(f, v => (int)(short)FirstCharacter(v)[0]),
            "CHRW" => Convert1(f, v => ((char)(InRange(AsLong(v), -32768, 65535) & 0xFFFF)).ToString()),
            // Byte variants operate on the UTF-16 byte layout (2 bytes/char): LenB = 2×length, AscB = the low
            // byte of the first char, and Left/Right/Mid/InStr count bytes. Verified vs ACE (LenB('abc')=6,
            // InStrB(1,'abc','b')=3). ChrB is intentionally absent — ACE's expression service has no ChrB.
            "ASCB" => Convert1(f, v => (int)ToBytes(FirstCharacter(v))[0]),
            "LENB" => Convert1(f, v => ToBytes(v).Length),
            "DATALENGTH" => Convert1(f, v => DataLength(v, IsCurrency(f.Arguments[0]))),
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
            "CBOOL" or "CBYTE" or "CINT" or "CLNG" or "CLNGLNG" or "CSNG" or "CDBL" or "CCUR" or "CDEC"
                or "CSTR" or "CDATE" or "CVAR"
                or "ABS" or "SGN" or "INT" or "FIX" or "SQR" or "EXP" or "LOG" or "SIN" or "COS"
                or "TAN" or "ATN"
                or "LEN" or "LCASE" or "UCASE" or "TRIM" or "LTRIM" or "RTRIM" or "SPACE"
                or "STRREVERSE" or "STR" or "VAL" or "CHR" or "ASC" or "HEX" or "OCT"
                or "DATEVALUE" or "TIMEVALUE" or "YEAR" or "MONTH" or "DAY" or "HOUR" or "MINUTE"
                or "SECOND" or "ISDATE" or "ISNULL" or "ISNUMERIC" or "ISERROR" or "TYPENAME" or "VARTYPE"
                or "QBCOLOR" or "ASCW" or "CHRW" or "ASCB" or "LENB" or "DATALENGTH" => (1, 1),

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

            "FIRST" or "LAST" => (1, 1),
            _ when RunningAggregate.Supports(name) => RunningAggregate.IsPair(name) ? (2, 2) : (1, 1),
            // The fraction and the WITHIN GROUP key, which the parser appends; it has checked the call's shape.
            "PERCENTILE_CONT" or "PERCENTILE_DISC" => (2, 2),
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

    /// <summary>Access <c>Choose(index, choice-1, choice-2, …)</c> (verified vs ACE): the choice at the index, read as
    /// a number and truncated (1.5 and 1.9 are 1, True is -1), or Null when there is no such choice. Every choice is
    /// evaluated, so an error in any of them is raised. A Null index is a type mismatch.</summary>
    private object? Choose(FunctionCall f)
    {
        object? indexValue = Evaluate(f.Arguments[0]);
        if (indexValue is null)
            throw new InvalidOperationException("Data type mismatch in criteria expression: Choose() index is null.");
        double index = Math.Truncate(Dbl(ConversionNumber(indexValue)));
        object?[] choices = f.Arguments.Skip(1).Select(Evaluate).ToArray();
        return index < 1 || index > choices.Length ? null : choices[(int)index - 1];
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

    /// <summary>Access <c>Switch(cond-1, value-1, cond-2, value-2, …)</c> (verified vs ACE): the value paired with the
    /// first true condition, or Null if none is. A condition is read as CBool reads it, so 'False' is False and text
    /// that is neither a Boolean nor a number is a type mismatch; Null is False. Every argument is evaluated, so an
    /// error in any of them is raised. An odd number of arguments is an error ("Wrong number of arguments").</summary>
    private object? Switch(FunctionCall f)
    {
        if (f.Arguments.Count % 2 != 0)
            throw new InvalidOperationException(
                "Wrong number of arguments used with function Switch (expects condition/value pairs).");
        object?[] values = f.Arguments.Select(Evaluate).ToArray();
        for (int i = 0; i < values.Length; i += 2)
            if (values[i] is { } condition && VbaBool(condition))
                return values[i + 1];
        return null;
    }

    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    /// <summary>The system ANSI code page, which Asc and Chr work in; unmappable characters take their best fit.</summary>
    private static Encoding Ansi =>
        CodePagesEncodingProvider.Instance.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage) ?? Encoding.Latin1;

    private static char AnsiCharacter(int code) => Ansi.GetString([(byte)code])[0];

    /// <summary>The first character of a value as text; an empty text is an invalid procedure call.</summary>
    private static string FirstCharacter(object v)
    {
        string text = ConcatText(v);
        return text.Length > 0
            ? text[..1]
            : throw new ArgumentException("Invalid procedure call: the text is empty.");
    }

    /// <summary>A value read as CInt reads it: half to even, and past an Integer is an overflow.</summary>
    private static int AsInteger(object v) => Convert.ToInt16(ConversionNumber(v), CultureInfo.InvariantCulture);

    /// <summary>A value read as CLng reads it: half to even, and past a Long is an overflow.</summary>
    private static int AsLong(object v) => Int(ConversionNumber(v));

    /// <summary>A value read as CLngLng reads it: as CLng, half to even, and past an Int64 an overflow. Text that
    /// reads as a number is read exactly, not through a Double, so '9223372036854775807' loses no digit.</summary>
    private static long AsLongLong(object v) =>
        v is string or char && TextAsDecimal(v.ToString()!) is decimal exact
            ? Convert.ToInt64(exact, CultureInfo.InvariantCulture)
            : Lng(ConversionNumber(v));

    /// <summary>A whole-number argument, where outside <paramref name="least"/>-<paramref name="most"/> is an invalid
    /// procedure call.</summary>
    private static int InRange(int n, int least, int most = int.MaxValue) =>
        n >= least && n <= most ? n : throw new ArgumentException($"Invalid procedure call: {n} is out of range.");

    /// <summary>A count, length or position read as CLng reads it; below <paramref name="least"/> is an invalid
    /// procedure call.</summary>
    private static int Count(object v, int least = 0) => InRange(AsLong(v), least);

    /// <summary>
    /// A function's argument at <paramref name="index"/>, read by <paramref name="read"/>: <paramref name="absent"/>
    /// when the call leaves it out, and null — the call gives Null — when it is Null.
    /// </summary>
    private T? Optional<T>(FunctionCall f, int index, T absent, Func<object, T> read) where T : struct =>
        f.Arguments.Count <= index ? absent
        : Evaluate(f.Arguments[index]) is { } v ? read(v)
        : null;

    /// <summary>A count, length or position argument (<see cref="Count"/>), or null when the argument is Null.</summary>
    private int? CountArgument(FunctionCall f, int index, int least = 0, int absent = 0) =>
        Optional(f, index, absent, v => Count(v, least));

    /// <summary>
    /// Whether a compare argument asks for a binary comparison: 0 is binary, and 1 (the default when the argument is
    /// absent) or a locale ID is textual. Anything else is an invalid procedure call — the documented
    /// vbUseCompareOption (-1) and database comparison (2) included (verified vs ACE: 1031, 1033 and the neutral IDs
    /// 3 and 4 are accepted, 20000 is not). Null when the argument is Null.
    /// <para>ACE compares text under a locale ID with that locale's Windows rules; LibRed uses the database order for
    /// every locale. They were measured to agree on case, accents and hyphens, and to differ only in whether ß
    /// matches ss: ACE expands it for 1031, 1033, 1036, 2057 and 3082 but not for 1027, 1041 or the neutral IDs.</para>
    /// </summary>
    private bool? BinaryCompare(FunctionCall f, int index) =>
        Optional(f, index, false, v => AsLong(v) switch
        {
            0 => true,
            1 => false,
            var mode when mode > 2 && IsLocaleId(mode) => false,
            var mode => throw new ArgumentException($"Invalid procedure call: {mode} is not a comparison."),
        });

    private static bool IsLocaleId(int lcid)
    {
        try
        {
            return lcid > 0 && CultureInfo.GetCultureInfo(lcid).LCID == lcid;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// The first match of <paramref name="find"/> in <paramref name="text"/> at or after <paramref name="start"/>, as
    /// a position and length, or (-1, 0). A textual match compares in the database sort order, so 'SS' finds 'ß' and
    /// the matched length can differ from <paramref name="find"/>'s.
    /// </summary>
    private static (int Index, int Length) FindText(string text, string find, int start, bool binary)
    {
        if (binary || IsPlainText(text) && IsPlainText(find))
        {
            int index = text.IndexOf(find, start, binary ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            return (index, find.Length);
        }
        int shortest = Math.Max(1, (find.Length + 1) / 2);
        for (int i = start; i < text.Length; i++)
        {
            for (int length = shortest; length <= Math.Min(text.Length - i, find.Length * 2); length++)
            {
                if (CompareText(text.Substring(i, length), find) == 0)
                    return (i, length);
            }
        }
        return (-1, 0);
    }

    /// <summary>Text whose database order is plain case-insensitive order: ASCII with no hyphen or apostrophe,
    /// which the order weighs apart, and no trailing space, which it ignores.</summary>
    private static bool IsPlainText(string text) =>
        text.All(c => c < 0x80 && c is not ('-' or '\'')) && !text.EndsWith(' ');

    /// <summary>
    /// Access <c>String(count, character)</c>: the character repeated. A text gives its first character (an empty
    /// one is an invalid procedure call); a number is a character code in the ANSI code page, taken modulo 256 (verified
    /// vs ACE: String(3, 321) is 'AAA', String(3, True) 'ÿÿÿ').
    /// </summary>
    private object? StringOf(FunctionCall f)
    {
        if (CountArgument(f, 0) is not { } count || Evaluate(f.Arguments[1]) is not { } charValue)
            return null;
        char ch = charValue is string or Guid or byte[]
            ? FirstCharacter(charValue)[0]
            : AnsiCharacter(AsLong(charValue) & 0xFF);
        return new string(ch, count);
    }

    /// <summary>
    /// Access <c>StrComp(a, b, [compare])</c>: -1, 0 or 1. A binary comparison orders the UTF-16 code units; a textual
    /// one the database sort order, with trailing spaces breaking a tie (verified vs ACE: StrComp('ß', 'ss') is 0,
    /// StrComp('a-b', 'ab') 1, StrComp('a', 'a ') -1).
    /// </summary>
    private object? StrComp(FunctionCall f)
    {
        object? a = Evaluate(f.Arguments[0]);
        object? b = Evaluate(f.Arguments[1]);
        if (a is null || b is null || BinaryCompare(f, 2) is not { } binary)
            return null;
        string left = ConcatText(a), right = ConcatText(b);
        if (binary)
            return Math.Sign(string.CompareOrdinal(left, right));
        int order = CompareText(left, right);
        return order != 0 ? order : Math.Sign(TrailingSpaces(left) - TrailingSpaces(right));

        static int TrailingSpaces(string s) => s.Length - s.TrimEnd(' ').Length;
    }

    /// <summary>Access <c>InStrRev(string1, string2, [start=-1], [compare])</c>: the 1-based position of the last
    /// occurrence of string2 in string1 (0 if not found), searching within the first <c>start</c> characters
    /// (the match must end at or before <c>start</c>; <c>start</c>=-1 means the whole string). Case-insensitive
    /// unless compare=0 (binary). Semantics verified vs ACE, including its quirks: an empty needle returns the
    /// effective start position; <c>start</c>=0 (or &lt;-1) → "Invalid procedure call", and one past the end of
    /// string1 gives 0; and — unlike <c>InStr</c> — a NULL string raises "Data type mismatch" rather than propagating
    /// NULL.</summary>
    private object? InstrRev(FunctionCall f)
    {
        object? s1v = Evaluate(f.Arguments[0]);
        object? s2v = Evaluate(f.Arguments[1]);
        if (s1v is null || s2v is null)
            throw new InvalidOperationException("Data type mismatch in criteria expression: InStrRev() argument is null.");
        string s1 = ConcatText(s1v), s2 = ConcatText(s2v);

        if (CountArgument(f, 2, least: -1, absent: -1) is not { } given)
            return null;
        if (given == 0)
            throw new ArgumentException("Invalid procedure call: InStrRev() start must be -1 or a positive position.");
        if (given > s1.Length)
            return 0;
        if (BinaryCompare(f, 3) is not { } binary)
            return null;
        int start = given > 0 ? given : s1.Length;

        if (s1.Length == 0) return 0;
        string window = s1[..start];                        // search within Left(string1, start)
        if (s2.Length == 0) return start;                   // empty needle → the effective start position
        int last = -1;
        for ((int index, int _) = FindText(window, s2, 0, binary); index >= 0; (index, _) = FindText(window, s2, index + 1, binary))
            last = index;
        return last + 1;
    }

    /// <summary>
    /// Access <c>Str(number)</c> (verified vs ACE): the number read as the conversion functions read it and written
    /// as CStr writes it, but always with a period and no zero before it, and with a space where a positive number's
    /// sign would go (<c>Str(0.5)</c> is " .5", <c>Str(True)</c> is "-1"). A date is written as CStr writes it.
    /// </summary>
    private static string VbaStr(object v)
    {
        if (v is DateTime date)
            return ConcatText(date);
        NumberFormatInfo invariant = NumberFormatInfo.InvariantInfo;
        string text = NumericOperand(v)! switch
        {
            bool b => b ? "-1" : "0",
            double d => FloatingText(d, 15, invariant),
            float f => FloatingText(f, 7, invariant),
            decimal m => m.ToString("0.############################", invariant),
            var n => Convert.ToString(n, invariant)!,
        };
        if (text.StartsWith("0.", StringComparison.Ordinal))
            text = text[1..];
        else if (text.StartsWith("-0.", StringComparison.Ordinal))
            text = "-" + text[2..];
        return text[0] == '-' ? text : " " + text;
    }

    /// <summary>
    /// Access <c>Val(string)</c> (verified vs ACE): the number at the start of the text, as a Double, or 0. Spaces,
    /// tabs, carriage returns and line feeds are removed first, wherever they are (other whitespace is not). The
    /// number may have a sign, a decimal point and an <c>e</c> or <c>d</c> exponent. <c>&amp;H</c> hex and
    /// <c>&amp;O</c> octal take no sign and keep their low 32 bits, read as an Integer when they fit 16 bits
    /// (<c>&amp;HFFFF</c> is -1) and otherwise as a Long. A number past a Double is an overflow.
    /// </summary>
    private static double VbaVal(string s)
    {
        string t = string.Concat(s.Where(c => c is not (' ' or '\t' or '\r' or '\n')));
        if (t.Length > 1 && t[0] == '&' && (char.ToUpperInvariant(t[1]) is var prefix && prefix is 'H' or 'O'))
        {
            int radix = prefix == 'H' ? 16 : 8;
            ulong bits = 0;
            foreach (char c in t[2..])
            {
                int digit = char.IsAsciiDigit(c) ? c - '0' : char.IsAsciiHexDigit(c) ? char.ToUpperInvariant(c) - 'A' + 10 : radix;
                if (digit >= radix)
                    break;
                bits = unchecked(bits * (ulong)radix + (ulong)digit);
            }
            bits &= 0xFFFF_FFFF;
            return bits <= 0xFFFF ? (short)bits : (int)bits;
        }

        Match number = Regex.Match(t, @"^[+-]?(\d+\.?\d*|\.\d+)([eEdD][+-]?\d+)?");
        if (!number.Success)
            return 0;
        double value = double.Parse(number.Value.Replace('d', 'e').Replace('D', 'e'), NumberStyles.Float, CultureInfo.InvariantCulture);
        return double.IsFinite(value) ? value : throw new OverflowException($"Overflow: '{s}' is too large for a number.");
    }

    /// <summary>
    /// Access <c>Hex</c> and <c>Oct</c> (verified vs ACE): the bits of the number read as the conversion functions
    /// read it — 16 of them for an Integer or a Boolean (<c>Hex(True)</c> is FFFF), 32 for a Long, and otherwise the
    /// value rounded half to even as a 64-bit whole number (<c>Hex(-1.5)</c> is FFFFFFFFFFFFFFFE).
    /// </summary>
    private static string RadixText(object v, int radix)
    {
        long bits = NumericOperand(v)! switch
        {
            bool b => b ? 0xFFFF : 0,
            short s => (ushort)s,
            int i => (uint)i,
            var n => Lng(Serial(n)),
        };
        return Convert.ToString(bits, radix).ToUpperInvariant();
    }

    private bool IsError(FunctionCall f)
    {
        Evaluate(f.Arguments[0]);
        return false;
    }

    /// <summary>Access <c>IsNumeric(value)</c> (verified vs ACE): true for a number or a Boolean, and for text that
    /// reads as a number the way <c>+</c> reads it ('$5', '&amp;HFF', '(1)' and '1d2' do; '1e400' does not). A date, a
    /// GUID, a binary value and Null are not numeric.</summary>
    private static bool IsNumericValue(object? v) => v switch
    {
        null or DateTime or Guid or byte[] => false,
        string or char => TryTextAsNumber(v.ToString()!) is not null,
        _ => true,
    };

    /// <summary>VBA <c>TypeName(value)</c> — the Access type name of the value's type (e.g. an Int32 literal → "Long").
    /// A Boolean is "Boolean" and a Byte "Byte", as LibRed holds them; ACE reports both as integers. Currency and
    /// Decimal share <see cref="decimal"/>, so which one a value is comes from its <paramref name="expression"/>.</summary>
    private string TypeNameOf(object? v, Expression expression) => v switch
    {
        decimal when IsCurrency(expression) => "Currency",
        decimal => "Decimal",
        null => "Null",
        bool => "Boolean",
        byte => "Byte",
        short => "Integer",
        int => "Long",
        long or ulong => "LongLong",
        float => "Single",
        double => "Double",
        DateTime => "Date",
        string => "String",
        // A binary column reports as String, not Byte[] — ACE's expression service sees the value as a
        // UTF-16 string (VarType 8 = VT_BSTR), so TypeName must say so even though LibRed holds a byte[].
        byte[] or Guid or char => "String",
        _ => v.GetType().Name,
    };

    /// <summary>VBA <c>VarType(value)</c> — the Access variant type code (vbLong=3, vbString=8, …), with the same
    /// mapping as <see cref="TypeNameOf"/>.</summary>
    private int VarTypeOf(object? v, Expression expression) => v switch
    {
        decimal when IsCurrency(expression) => 6, // vbCurrency
        decimal => 14,      // vbDecimal
        null => 1,          // vbNull
        bool => 11,         // vbBoolean
        byte => 17,         // vbByte
        short => 2,         // vbInteger
        int => 3,           // vbLong
        long or ulong => 20, // vbLongLong
        float => 4,         // vbSingle
        double => 5,        // vbDouble
        DateTime => 7,      // vbDate
        _ => 8,             // vbString
    };

    /// <summary>Whether an expression is a Currency: a Currency column, CCur, or arithmetic that keeps one.</summary>
    private bool IsCurrency(Expression expression) =>
        NumberTypeOf(expression, scope.AllColumns(), _ => null).Class == NumberClass.Currency;

    /// <summary>
    /// Access <c>StrConv(string, conversion, [LCID])</c> (verified vs ACE). 0 leaves the text as it is. 1, 2 and 3
    /// work in the ANSI code page, so a character it lacks becomes its best fit or '?': 1 is upper case, 2 lower case,
    /// and 3 upper-cases the first letter and each letter after a space, tab, line break, form feed or NUL and
    /// lower-cases the rest. 64 (vbUnicode) reads the text's UTF-16 bytes as ANSI characters, and 128 (vbFromUnicode)
    /// packs the text's ANSI bytes two to a character. The East Asian conversions (4 to 32) and other combinations are
    /// an invalid procedure call, but give Null for Null text; a conversion outside 0-255, or an LCID that is not a
    /// locale, is an invalid procedure call even then. ACE keeps an odd trailing byte from 128 inside an expression
    /// (<c>LenB(StrConv("abc", 128))</c> is 3); LibRed text has no odd bytes, so it drops it.
    /// </summary>
    private object? StrConv(FunctionCall f)
    {
        object? sv = Evaluate(f.Arguments[0]);
        if (Evaluate(f.Arguments[1]) is not { } modeV)
            return null;
        int mode = InRange(AsLong(modeV), 0, 255);
        if (Optional(f, 2, 0, AsLong) is not { } locale)
            return null;
        if (locale != 0 && !IsLocaleId(locale))
            throw new ArgumentException($"Invalid procedure call: {locale} is not a locale.");
        if (sv is null)
            return null;
        if (mode is not (0 or 1 or 2 or 3 or 64 or 128))
            throw new ArgumentException($"Invalid procedure call: {mode} is not a StrConv conversion.");

        // vbUnicode (64) on binary widens each byte to one Unicode char — Jet's binary→string conversion.
        // This is the byte-array path EF emits for `byte[].Contains(x)`: INSTR(1, STRCONV(arr, 64), 0xXX, 0).
        if (mode == 64 && sv is byte[] binary)
            return ByteArrayToString(binary);

        string s = ConcatText(sv);
        Encoding ansi = Ansi;
        return mode switch
        {
            0 => s,
            1 => ansi.GetString(ansi.GetBytes(s)).ToUpperInvariant(),
            2 => ansi.GetString(ansi.GetBytes(s)).ToLowerInvariant(),
            3 => ProperCase(ansi.GetString(ansi.GetBytes(s))),
            64 => ansi.GetString(Encoding.Unicode.GetBytes(s)),
            _ => FromBytes(ansi.GetBytes(s)),
        };
    }

    private static string ProperCase(string s)
    {
        var chars = new char[s.Length];
        bool wordStart = true;
        for (int i = 0; i < s.Length; i++)
        {
            chars[i] = wordStart ? char.ToUpperInvariant(s[i]) : char.ToLowerInvariant(s[i]);
            wordStart = s[i] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r' or '\0';
        }
        return new string(chars);
    }

    /// <summary>Jet coerces a binary value to a string by mapping each byte to a single char (the value it
    /// widens back to via <c>STRCONV(…, 64)</c>). A <c>byte[]</c> reaching a string function — e.g. a
    /// <c>0xNN</c> hex literal used as an <c>INSTR</c> needle — is coerced this way, not via
    /// <c>ToString()</c> (which would yield "System.Byte[]").</summary>
    private static string ByteArrayToString(byte[] bytes) => new(Array.ConvertAll(bytes, b => (char)b));

    private static string ToJetString(object value) => value is byte[] b ? ByteArrayToString(b) : ConcatText(value);

    /// <summary>Access <c>MonthName(month, [abbreviate])</c>: the English month name, abbreviated when asked. A month
    /// outside 1-12 is an invalid procedure call (verified vs ACE, True included).</summary>
    private object? MonthNameOf(FunctionCall f)
    {
        if (Evaluate(f.Arguments[0]) is not { } month || Abbreviate(f) is not { } abbreviate)
            return null;
        int number = Setting(month, 1, 12);
        return abbreviate
            ? EnUs.DateTimeFormat.GetAbbreviatedMonthName(number)
            : EnUs.DateTimeFormat.GetMonthName(number);
    }

    /// <summary>Access <c>WeekdayName(weekday, [abbreviate], [firstdayofweek])</c>: the English name of the day at
    /// 1-based position <c>weekday</c> in a week starting on <c>firstdayofweek</c>, which defaults to the system's
    /// first day (verified vs ACE: WeekdayName(1) is Monday under en-AU). A weekday outside 1-7 is an invalid
    /// procedure call.</summary>
    private object? WeekdayNameOf(FunctionCall f)
    {
        if (Evaluate(f.Arguments[0]) is not { } weekday || Abbreviate(f) is not { } abbreviate
            || FirstDayOfWeek(f, 2, absent: 0) is not { } first)
            return null;
        int index = ((int)first + Setting(weekday, 1, 7) - 1) % 7;
        return (abbreviate ? EnUs.DateTimeFormat.AbbreviatedDayNames : EnUs.DateTimeFormat.DayNames)[index];
    }

    /// <summary>MonthName's and WeekdayName's abbreviate argument, read as CBool reads it; false when absent.</summary>
    private bool? Abbreviate(FunctionCall f) => Optional(f, 1, false, VbaBool);

    /// <summary>Access <c>Partition(number, start, stop, interval)</c>: a <c>"lower:upper"</c> range label, both
    /// sides right-justified to a fixed width. Below the range → lower blank, upper = <c>start-1</c>; above →
    /// lower = <c>stop+1</c>, upper blank; otherwise the interval bucket (verified vs ACE). The arguments are read as
    /// the conversion functions read them (a date as its serial); a negative start, a stop not after the start or an
    /// interval below 1 is an invalid procedure call. NULL-propagating.</summary>
    private object? PartitionOf(FunctionCall f)
    {
        object?[] args = f.Arguments.Select(Evaluate).ToArray();
        if (args.Any(a => a is null)) return null;
        long number = Lng(ConversionNumber(args[0]!));
        long start = Lng(ConversionNumber(args[1]!));
        long stop = Lng(ConversionNumber(args[2]!));
        long interval = Lng(ConversionNumber(args[3]!));
        if (start < 0 || stop <= start || interval < 1)
            throw new ArgumentException("Invalid procedure call: Partition needs 0 <= start < stop and an interval of at least 1.");

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
        if (sv is null || CountArgument(f, 1) is not { } count) return null;
        byte[] b = ToBytes(sv);
        return ByteResult(sv, b[..Math.Min(count, b.Length)]);
    }

    /// <summary>VBA <c>RightB(string, bytes)</c>: the trailing <c>bytes</c> bytes. NULL-propagating.</summary>
    private object? ByteRight(FunctionCall f)
    {
        object? sv = Evaluate(f.Arguments[0]);
        if (sv is null || CountArgument(f, 1) is not { } count) return null;
        byte[] b = ToBytes(sv);
        return ByteResult(sv, b[^Math.Min(count, b.Length)..]);
    }

    /// <summary>VBA <c>MidB(string, startByte[, lenBytes])</c>: a 1-based **byte** slice (may start/end
    /// mid-character). NULL-propagating; a start below 1 or a negative length is an invalid procedure call.</summary>
    private object? ByteMid(FunctionCall f)
    {
        object? sv = Evaluate(f.Arguments[0]);
        if (sv is null || CountArgument(f, 1, least: 1) is not { } first
            || CountArgument(f, 2, absent: int.MaxValue) is not { } length)
            return null;
        byte[] b = ToBytes(sv);
        int start = first - 1;
        if (start >= b.Length) return ByteResult(sv, []);
        int len = Math.Min(length, b.Length - start);
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
        if (s1v is null || s2v is null || (argc >= 3 ? CountArgument(f, 0, least: 1) : 1) is not { } start)
            return null;
        int idx = IndexOfBytes(ToBytes(s1v), ToBytes(s2v), start - 1);
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
        if (v is not byte[] bytes) return Encoding.Unicode.GetBytes(ConcatText(v));
        if (bytes.Length % 2 == 0) return bytes;
        var padded = new byte[bytes.Length + 1];
        Array.Copy(bytes, padded, bytes.Length);
        return padded;
    }

    /// <summary>
    /// SQL Server's <c>DATALENGTH</c>, a LibRed extension (Access has none): the bytes a value takes as Access stores
    /// it. Text is two per character — Access text is UTF-16, as <c>nvarchar</c> is — with trailing spaces counted and
    /// no account of the on-disk Unicode compression; a binary value is its length, unpadded (LenB pads an odd one).
    /// A Byte is 1, an Integer 2, a Long and a Single 4, a Double, a Currency, a date and a BIGINT 8, a GUID 16 and a
    /// Decimal 17 — its sign byte and 16-byte magnitude. A Boolean is stored as a bit of the row's null bitmap and so
    /// takes no byte of its own; it counts 1, as SQL Server counts a <c>bit</c>.
    /// </summary>
    private static int DataLength(object value, bool currency) => value switch
    {
        string s => checked(s.Length * 2),
        char => 2,
        byte[] bytes => bytes.Length,
        bool or byte or sbyte => 1,
        short or ushort => 2,
        int or uint or float => 4,
        long or ulong or double or DateTime => 8,
        decimal => currency ? 8 : 17,
        Guid => 16,
        _ => checked(ConcatText(value).Length * 2),
    };

    /// <summary>Successive byte pairs as UTF-16 code units (low byte first), dropping a trailing odd byte — matching
    /// ACE (MidB(x, 1, 3) yields one character from three bytes). A lone surrogate is kept as it is.</summary>
    private static string FromBytes(byte[] bytes)
    {
        var chars = new char[bytes.Length / 2];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = (char)(bytes[2 * i] | (bytes[2 * i + 1] << 8));
        return new string(chars);
    }

    // --- Colour functions ---

    // QBColor maps 0..15 to fixed BGR Long values (verified vs ACE: QBColor(4) = 128).
    private static readonly int[] QbColors =
    [
        0x000000, 0x800000, 0x008000, 0x808000, 0x000080, 0x800080, 0x008080, 0xC0C0C0,
        0x808080, 0xFF0000, 0x00FF00, 0xFFFF00, 0x0000FF, 0xFF00FF, 0x00FFFF, 0xFFFFFF,
    ];

    /// <summary>
    /// Access <c>RGB(red, green, blue)</c>: the colour as a Long, red in the low byte. NULL-propagating. Each part is a
    /// setting (<see cref="Setting"/>) where above 255 is taken as 255 (verified vs ACE: RGB(256, 0, 0) is 255, and
    /// RGB(-0.6, 0, 0) fails).
    /// </summary>
    private object? Rgb(FunctionCall f)
    {
        if (Evaluate(f.Arguments[0]) is not { } red || Evaluate(f.Arguments[1]) is not { } green
            || Evaluate(f.Arguments[2]) is not { } blue)
            return null;
        return Part(red) | Part(green) << 8 | Part(blue) << 16;

        static int Part(object v) => Math.Min(Setting(v, 0, short.MaxValue), 255);
    }

    // --- Financial functions ---

    /// <summary>
    /// A financial function (verified vs ACE, to the last bit). The arguments are read as the conversion functions read
    /// them (text as a number, a date as its serial, True as -1); an omitted one is 0, except Rate's guess and DDB's
    /// factor. A "type" argument means payment at the start of each period whenever it is not 0. Arguments out of range
    /// are an invalid procedure call; a result past a Double comes back as infinity or NaN, as ACE returns it. A Null
    /// argument gives Null where ACE raises an error.
    /// <para>The algorithms and the order of their arithmetic are the VBA runtime's, as Microsoft.VisualBasic's
    /// Financial module carries them; the order decides the last digit.</para>
    /// </summary>
    private object? Financial(FunctionCall f, Func<double[], double> compute)
    {
        var arguments = new double[6];
        for (int i = 0; i < f.Arguments.Count; i++)
        {
            if (Evaluate(f.Arguments[i]) is not { } value)
                return null;
            arguments[i] = Dbl(ConversionNumber(value));
        }
        return compute(arguments);
    }

    /// <summary>A "type" argument: payment at the start of each period when it rounds (half to even) to anything but 0
    /// (verified vs ACE: 0.5 is the end, 1.5, 2 and -1 the start).</summary>
    private static bool Due(double type) => Math.Round(type, MidpointRounding.ToEven) != 0;

    private static ArgumentException OutOfRange(string argument) =>
        new($"Invalid procedure call: {argument} is out of range.");

    /// <summary>Access <c>Pmt(rate, nper, pv, [fv], [type])</c>: the payment each period. An nper of 0 is out of range.</summary>
    private static double Pmt(double rate, double nper, double pv, double fv, bool due)
    {
        if (nper == 0)
            throw OutOfRange("NPer");
        if (rate == 0)
            return (-fv - pv) / nper;
        double start = due ? 1 + rate : 1;
        double growth = Math.Pow(rate + 1, nper);
        return (-fv - pv * growth) / (start * (growth - 1)) * rate;
    }

    /// <summary>Access <c>FV(rate, nper, pmt, [pv], [type])</c>: the value after the last payment.</summary>
    private static double Fv(double rate, double nper, double pmt, double pv, bool due)
    {
        if (rate == 0)
            return -pv - pmt * nper;
        double start = due ? 1 + rate : 1;
        double growth = Math.Pow(1 + rate, nper);
        return -(pv * growth + pmt * start * (growth - 1) / rate);
    }

    /// <summary>Access <c>PV(rate, nper, pmt, [fv], [type])</c>: the value before the first payment.</summary>
    private static double Pv(double rate, double nper, double pmt, double fv, bool due)
    {
        if (rate == 0)
            return -fv - pmt * nper;
        double start = due ? 1 + rate : 1;
        double growth = Math.Pow(1 + rate, nper);
        return -(fv + pmt * start * (growth - 1) / rate) / growth;
    }

    /// <summary>Access <c>NPer(rate, pmt, pv, [fv], [type])</c>: the number of payments. A rate of -1 or less, no
    /// payment at a rate of 0, or payments that never reach the future value are out of range.</summary>
    private static double NPer(double rate, double pmt, double pv, double fv, bool due)
    {
        if (rate <= -1)
            throw OutOfRange("Rate");
        if (rate == 0)
            return pmt == 0 ? throw OutOfRange("Pmt") : -(pv + fv) / pmt;
        double payment = due ? pmt * (1 + rate) / rate : pmt / rate;
        double future = -fv + payment, present = pv + payment;
        if (future < 0 && present < 0)
            (future, present) = (-future, -present);
        else if (future <= 0 || present <= 0)
            throw OutOfRange("Pmt");
        return (Math.Log(future) - Math.Log(present)) / Math.Log(rate + 1);
    }

    /// <summary>Access <c>IPmt(rate, per, nper, pv, [fv], [type])</c>: the interest in payment <c>per</c>, which must be
    /// above 0 and below nper + 1.</summary>
    private static double IPmt(double rate, double per, double nper, double pv, double fv, bool due)
    {
        if (per <= 0 || per >= nper + 1)
            throw OutOfRange("Per");
        if (due && per == 1)
            return 0;
        double pmt = Pmt(rate, nper, pv, fv, due);
        if (due)
            pv += pmt;
        return Fv(rate, per - (due ? 2 : 1), pmt, pv, false) * rate;
    }

    /// <summary>Access <c>PPmt(rate, per, nper, pv, [fv], [type])</c>: the principal in payment <c>per</c>.</summary>
    private static double PPmt(double rate, double per, double nper, double pv, double fv, bool due)
    {
        if (per <= 0 || per >= nper + 1)
            throw OutOfRange("Per");
        return Pmt(rate, nper, pv, fv, due) - IPmt(rate, per, nper, pv, fv, due);
    }

    /// <summary>
    /// Access <c>Rate(nper, pmt, pv, [fv], [type], [guess])</c>: the rate per period, found by the secant method from the
    /// guess (0.1 when omitted). An nper of 0 or less, or no answer within 40 steps, is out of range.
    /// </summary>
    private static double Rate(double nper, double pmt, double pv, double fv, bool due, double guess)
    {
        const double step = 0.00001, epsilon = 0.0000001;
        if (nper <= 0)
            throw OutOfRange("NPer");
        double Error(double rate)
        {
            if (rate == 0)
                return pv + pmt * nper + fv;
            double growth = Math.Pow(rate + 1, nper);
            double start = due ? 1 + rate : 1;
            return pv * growth + pmt * start * (growth - 1) / rate + fv;
        }

        double rate0 = guess, error0 = Error(rate0);
        double rate1 = error0 > 0 ? rate0 / 2 : rate0 * 2, error1 = Error(rate1);
        for (int i = 0; i < 40; i++)
        {
            if (error1 == error0)
            {
                rate0 = rate1 > rate0 ? rate0 - step : rate0 + step;
                error0 = Error(rate0);
                if (error1 == error0)
                    throw OutOfRange("Rate");
            }
            rate0 = rate1 - (rate1 - rate0) * error1 / (error1 - error0);
            error0 = Error(rate0);
            if (Math.Abs(error0) < epsilon)
                return rate0;
            (error0, error1) = (error1, error0);
            (rate0, rate1) = (rate1, rate0);
        }
        throw OutOfRange("Rate");
    }

    /// <summary>Access <c>SLN(cost, salvage, life)</c>: straight-line depreciation. A life of 0 is out of range.</summary>
    private static double Sln(double cost, double salvage, double life) =>
        life == 0 ? throw OutOfRange("Life") : (cost - salvage) / life;

    /// <summary>Access <c>SYD(cost, salvage, life, period)</c>: sum-of-years'-digits depreciation. A negative salvage, or a
    /// period of 0 or less or past the life, is out of range.</summary>
    private static double Syd(double cost, double salvage, double life, double period)
    {
        if (salvage < 0 || period > life || period <= 0)
            throw OutOfRange("Period");
        return (cost - salvage) * (life - period + 1) / (life * (life + 1) / 2);
    }

    /// <summary>
    /// Access <c>DDB(cost, salvage, life, period, [factor])</c>: declining-balance depreciation at <c>factor</c> (2 when
    /// omitted) over the life. A factor of 0 or less, a negative salvage, or a period of 0 or less or past the life is
    /// out of range. A cost of 0 or less depreciates nothing, and a period's depreciation never takes the value below
    /// the salvage.
    /// </summary>
    private static double Ddb(double cost, double salvage, double life, double period, double factor)
    {
        if (factor <= 0 || salvage < 0 || period <= 0 || period > life)
            throw OutOfRange("Period");
        if (cost <= 0)
            return 0;
        if (life < 2)
            return cost - salvage;
        if (life == 2)
            return period > 1 ? 0 : cost - salvage;
        if (period <= 1)
        {
            double first = cost * factor / life, most = cost - salvage;
            return first > most ? most : first;
        }
        double remaining = (life - factor) / life;
        double depreciation = factor * cost / life * Math.Pow(remaining, period - 1);
        double excess = cost * (1 - Math.Pow(remaining, period)) - cost + salvage;
        if (excess > 0)
            depreciation -= excess;
        return depreciation >= 0 ? depreciation : 0;
    }

    private uint _localRandSeed = 0x50000;   // used when no connection-scoped SessionState is available

    /// <summary>VBA <c>Rnd([number])</c> using VBA's own 24-bit LCG, so the sequence matches ACE (verified:
    /// <c>Rnd(-1)</c> = 0.2240070104598999). <c>number &gt; 0</c> (or omitted) advances the generator;
    /// <c>number = 0</c> repeats the last value; <c>number &lt; 0</c> reseeds deterministically from the
    /// argument's Single bit pattern. The seed is connection-scoped (via <see cref="SessionState"/>) since the
    /// JES has no <c>Randomize</c>. Result is a Single widened to Double, matching ACE.</summary>
    private object? Rnd(FunctionCall f)
    {
        uint seed = session?.RandSeed ?? _localRandSeed;
        // The argument is read as a Single, as conversion functions read it; Null gives Null (ACE raises
        // "Invalid use of Null").
        if (Optional(f, 0, 1.0, a => Finite(Sng(ConversionNumber(a)))) is not { } arg)
            return null;
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

    /// <summary>
    /// Access <c>Round(number[, places])</c> (verified vs ACE): half to even on the number's decimal form — a Double or
    /// Single as OLE Automation writes it, so <c>Round(2.675, 2)</c> is 2.68 and <c>Round('2.55', 1)</c> 2.6 — and the
    /// number unchanged when nothing is cut. The operand's type is kept (the EF contract); text and dates give a
    /// Double and True is -1; a number written with a decimal point rounds as written. Places are read as a number and
    /// rounded; Null places give Null, fewer than 0 are an invalid procedure call, and more than a Decimal holds leave
    /// the number as it is.
    /// </summary>
    private object? Round(FunctionCall f)
    {
        if (Evaluate(f.Arguments[0]) is not { } value || Optional(f, 1, 0, v => Count(v)) is not { } places)
            return null;
        places = Math.Min(places, 28);

        return value switch
        {
            decimal m => decimal.Round(m, places, MidpointRounding.ToEven),
            double d when WrittenValue(f.Arguments[0]) is decimal written =>
                RoundWritten(d, places, () => written, r => (double)r),
            double d => RoundDouble(d, places),
            float s => RoundWritten(s, places, () => JetDecimalConverter.FromSingle(s), r => (float)r),
            long or ulong => Lng(value),
            int or short or byte or bool => Int(value),
            _ => RoundDouble(Dbl(Serial(NumericOperand(value)!)), places),
        };

        static object RoundDouble(double d, int places) =>
            RoundWritten(d, places, () => JetDecimalConverter.FromDouble(d), r => (double)r);

        // A number past a Decimal has no places left to cut.
        static object RoundWritten<T>(T original, int places, Func<decimal> written, Func<decimal, T> back) where T : notnull
        {
            decimal exact;
            try
            {
                exact = written();
            }
            catch (OverflowException)
            {
                return original;
            }
            decimal rounded = decimal.Round(exact, places, MidpointRounding.ToEven);
            return rounded == exact ? original : back(rounded);
        }
    }

    /// <summary>Applies a conversion to a single argument, propagating NULL.</summary>
    private object? Convert1(FunctionCall f, Func<object, object?> convert)
    {
        object? value = Evaluate(f.Arguments[0]);
        return value is null ? null : convert(value);
    }

    /// <summary>
    /// Access <c>CDate</c> (verified vs ACE): a date passes through; text is read as a date and time the way OLE
    /// Automation reads it (<see cref="VbaDateText"/>), and otherwise as a number; a number is the date at that
    /// serial (True is -1, 1899-12-29).
    /// </summary>
    private static DateTime ToDate(object v) => v switch
    {
        DateTime d => d,
        _ when TryDateText(v, out DateTime parsed) => parsed,
        _ => OaDate(Dbl(ConversionNumber(v))),
    };

    /// <summary>A conversion function's argument as a number: text read as one (<see cref="TextAsNumber"/>), a date
    /// as its serial and a Boolean as -1 or 0; a GUID or binary value is a type mismatch.</summary>
    internal static object ConversionNumber(object v) => Numeric(Serial(NumericOperand(v)!));

    /// <summary>
    /// A <c>CCur</c> or <c>CDec</c> argument as a Decimal, exactly where it can be: a number written with a decimal
    /// point as written, and text that reads as a number within a Decimal's range as it reads (verified vs ACE:
    /// CCur('12345678901234.5678') and CCur(12345678901234.5678) keep every place). Anything else is read as the
    /// conversion functions read it. Null for a Null argument.
    /// </summary>
    private object? DecimalArgument(FunctionCall f, Func<decimal, decimal> convert)
    {
        if (WrittenValue(f.Arguments[0]) is decimal written)
            return convert(written);
        return Evaluate(f.Arguments[0]) switch
        {
            null => null,
            (string or char) and var text when TextAsDecimal(text.ToString()!) is decimal exact => convert(exact),
            var value => convert(ArithmeticDecimal(ConversionNumber(value))),
        };
    }

    /// <summary>Access <c>CCur</c>: the value to four places, half to even; past a Currency is an overflow.</summary>
    private static decimal ToCurrency(decimal number)
    {
        decimal value = decimal.Round(number, 4, MidpointRounding.ToEven);
        return Math.Abs(value) <= 922337203685477.5807m || value == -922337203685477.5808m
            ? value
            : throw new OverflowException($"Overflow: {value} is outside the range of a Currency.");
    }

    /// <summary>A date, or text that reads as one (<see cref="VbaDateText"/>); a number or Null is not.</summary>
    private static bool TryDateText(object? v, out DateTime value)
    {
        value = v as DateTime? ?? default;
        return v is DateTime || v is string text && VbaDateText.TryParse(text, CultureInfo.CurrentCulture, out value);
    }

    /// <summary>A DateValue or TimeValue argument: a date or text that reads as one, else a type mismatch.</summary>
    private static DateTime DateValueArgument(object v) =>
        TryDateText(v, out DateTime value) ? value : throw new InvalidCastException($"Type mismatch: '{ConcatText(v)}' is not a date.");

    /// <summary>Left and Right: the text's first or last characters. A Null length raises "Data type mismatch" as
    /// ACE does, and a negative one is an invalid procedure call.</summary>
    private object? StringInt(FunctionCall f, Func<string, int, string> op)
    {
        object? s = Evaluate(f.Arguments[0]);
        if (s is null) return null;
        object? nv = Evaluate(f.Arguments[1]);
        if (nv is null)
            throw new InvalidOperationException("Data type mismatch in criteria expression: length argument is null.");
        return op(ConcatText(s), Count(nv));
    }

    /// <summary>Access MID(string, start[, length]) — a 1-based substring; length omitted means to the end. A start
    /// below 1 or a negative length is an invalid procedure call.</summary>
    private object? Mid(FunctionCall f)
    {
        object? sv = Evaluate(f.Arguments[0]);
        if (sv is null || CountArgument(f, 1, least: 1) is not { } start
            || CountArgument(f, 2, absent: int.MaxValue) is not { } length)
            return null;
        string s = ConcatText(sv);
        int from = start - 1;
        if (from >= s.Length) return "";
        return s.Substring(from, Math.Min(length, s.Length - from));
    }

    /// <summary>Access INSTR([start,] string1, string2[, compare]) — the 1-based position of string2 in string1, 0 if
    /// it is not there. An empty string2 is found at start, wherever that is, unless string1 is empty; a start below 1
    /// is an invalid procedure call.</summary>
    private object? Instr(FunctionCall f)
    {
        int argc = f.Arguments.Count;
        // 2 args: (s1, s2); 3+: (start, s1, s2[, compare]).
        if ((argc >= 3 ? CountArgument(f, 0, least: 1) : 1) is not { } start)
            return null;
        object? s1v = Evaluate(f.Arguments[argc >= 3 ? 1 : 0]);
        object? s2v = Evaluate(f.Arguments[argc >= 3 ? 2 : 1]);
        if (s1v is null || s2v is null || BinaryCompare(f, 3) is not { } binary)
            return null;

        // A byte[] argument (e.g. a 0xNN hex literal needle, or a binary haystack) coerces to a string one
        // char per byte, matching Jet — not "System.Byte[]".
        string s1 = ToJetString(s1v), s2 = ToJetString(s2v);
        if (s1.Length == 0) return 0;
        if (s2.Length == 0) return start;
        if (start > s1.Length) return 0;
        return FindText(s1, s2, start - 1, binary).Index + 1;
    }

    /// <summary>Access REPLACE(string, find, replace[, start[, count[, compare]]]) — the text from start on, with
    /// find replaced at most count times (all when count is -1). A start below 1 is an invalid procedure call.</summary>
    private object? Replace(FunctionCall f)
    {
        object? sv = Evaluate(f.Arguments[0]), findv = Evaluate(f.Arguments[1]), replv = Evaluate(f.Arguments[2]);
        // ACE raises "Data type mismatch" here (unlike InStr, which propagates NULL), but LibRed propagates
        // instead, matching every other dialect - SQL Server documents REPLACE as returning NULL if any
        // argument is NULL. A deliberate divergence, and a widening: no query that returns a value today
        // changes, only ones that error start returning NULL.
        if (sv is null || findv is null || replv is null)
            return null;
        string s = ConcatText(sv), find = ConcatText(findv), repl = ConcatText(replv);

        if (CountArgument(f, 3, least: 1, absent: 1) is not { } start
            || CountArgument(f, 4, least: -1, absent: -1) is not { } count
            || BinaryCompare(f, 5) is not { } binary)
            return null;

        if (start > s.Length) return "";
        s = s[(start - 1)..];
        if (find.Length == 0) return s;

        var sb = new StringBuilder();
        int pos = 0, replaced = 0;
        while (true)
        {
            (int j, int length) = count >= 0 && replaced >= count ? (-1, 0) : FindText(s, find, pos, binary);
            if (j < 0) { sb.Append(s.AsSpan(pos)); break; }
            sb.Append(s, pos, j - pos).Append(repl);
            pos = j + length;
            replaced++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Fix, Int and Abs, keeping the operand's type (double→double, single→single, decimal→decimal, long→long, and
    /// the narrower integers and Booleans→int) so it matches EF's Math.* return type; NULL-propagating. Text is read
    /// as a number and gives a Double, True is -1, and an Integer result past a Long widens to a Double (verified vs
    /// ACE: Abs(-2147483648) is 2147483648). A date is its serial: Int and Fix give a date back
    /// (<paramref name="keepsDate"/>), Abs a Double.
    /// </summary>
    private object? Numeric1(FunctionCall f, Func<double, double> dOp, Func<decimal, decimal> mOp, bool keepsDate) =>
        Evaluate(f.Arguments[0]) switch
        {
            null => null,
            decimal m => mOp(m),
            double d => dOp(d),
            float s => (float)dOp(s),
            long l => checked((long)mOp(l)),
            DateTime d => keepsDate ? OaDate(dOp(d.ToOADate())) : dOp(d.ToOADate()),
            var v when v is string or char or Guid or byte[] => dOp(Dbl(NumericOperand(v)!)),
            var v => mOp(Dec(v)) is var r && r >= int.MinValue && r <= int.MaxValue ? (object)(int)r : (double)r,
        };

    /// <summary>
    /// Sqr, Exp, Log, Sin, Cos, Tan and Atn, on a Double read as the conversion functions read it; NULL-propagating.
    /// A result that is not a number — the square root or log of a number below its domain, or a trigonometric
    /// function of a number too large to reduce — is an invalid procedure call, and a result past a Double an
    /// overflow (verified vs ACE).
    /// </summary>
    private object? UnaryDouble(FunctionCall f, Func<double, double> op)
    {
        if (Evaluate(f.Arguments[0]) is not { } value)
            return null;
        double x = Dbl(ConversionNumber(value));
        double result = op(x);
        return double.IsNaN(result) || double.IsNegativeInfinity(result) && x == 0
            ? throw new ArgumentException($"Invalid procedure call: the function is not defined at {x}.")
            : Finite(result);
    }

    /// <summary>A trigonometric function, which ACE refuses for an argument of 9.223372E18 or more (verified).</summary>
    private static Func<double, double> Trigonometric(Func<double, double> op) =>
        x => Math.Abs(x) >= 9.223372E18 ? double.NaN : op(x);

    /// <summary>Access <c>DatePart(interval, date, [firstdayofweek], [firstweekofyear])</c>: a component of a date.
    /// "ms", "mcs" and "ns" are LibRed extensions.</summary>
    private object? DatePart(FunctionCall f)
    {
        if (Evaluate(f.Arguments[0]) is not { } interval || Evaluate(f.Arguments[1]) is not { } date
            || FirstDayOfWeek(f, 2) is not { } first || FirstWeekOfYear(f, 3) is not { } rule)
            return null;
        DateTime d = ToDate(date);
        return ConcatText(interval).ToLowerInvariant() switch
        {
            "yyyy" => d.Year,
            "q" => (d.Month + 2) / 3,
            "m" => d.Month,
            "y" => d.DayOfYear,
            "d" => d.Day,
            "w" => DaysIntoWeek(d, first) + 1,
            "ww" => WeekOfYear(d, first, rule),
            "h" => d.Hour,
            "n" => d.Minute,
            "s" => d.Second,
            "ms" => d.Millisecond,
            "mcs" => d.Microsecond,
            "ns" => d.Nanosecond,
            _ => throw UnknownInterval(interval),
        };
    }

    private static ArgumentException UnknownInterval(object interval) =>
        new($"Invalid procedure call: '{interval}' is not a date interval.");

    /// <summary>Access <c>Weekday(date, [firstdayofweek])</c>: the day's 1-based position in the week.</summary>
    private object? Weekday(FunctionCall f) =>
        Evaluate(f.Arguments[0]) is { } date && FirstDayOfWeek(f, 1) is { } first
            ? DaysIntoWeek(ToDate(date), first) + 1
            : null;

    /// <summary>
    /// A firstdayofweek argument: 1 (Sunday) to 7 (Saturday), or 0 for the system's first day; anything else is an
    /// invalid procedure call (verified vs ACE: 0 is Monday under en-AU, and 8 and -1 are refused). Null when the
    /// argument is Null.
    /// </summary>
    private DayOfWeek? FirstDayOfWeek(FunctionCall f, int index, int absent = 1) =>
        Optional(f, index, absent, v => Setting(v, 0, 7)) switch
        {
            null => null,
            0 => CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek,
            var day => (DayOfWeek)(day - 1),
        };

    /// <summary>A firstweekofyear argument: 1 (the week of January 1), 2 (the first week with four days), 3 (the first
    /// full week), or 0 for the system's rule; anything else is an invalid procedure call. Null when it is Null.</summary>
    private CalendarWeekRule? FirstWeekOfYear(FunctionCall f, int index) =>
        Optional(f, index, 1, v => Setting(v, 0, 3)) switch
        {
            null => null,
            0 => CultureInfo.CurrentCulture.DateTimeFormat.CalendarWeekRule,
            1 => CalendarWeekRule.FirstDay,
            2 => CalendarWeekRule.FirstFourDayWeek,
            _ => CalendarWeekRule.FirstFullWeek,
        };

    /// <summary>A setting read as CInt reads it (half to even; verified vs ACE: '2' and 1.5 are 2); outside
    /// <paramref name="lowest"/>-<paramref name="highest"/> is an invalid procedure call.</summary>
    private static int Setting(object v, int lowest, int highest) => InRange(AsInteger(v), lowest, highest);

    /// <summary>How many days of the week come before the date's day, for a week starting on <paramref name="first"/>.</summary>
    private static int DaysIntoWeek(DateTime date, DayOfWeek first) => ((int)date.DayOfWeek - (int)first + 7) % 7;

    /// <summary>
    /// The date's week number (verified vs ACE). Days before the year's first week belong to the last week of the year
    /// before, and under the four-day rule a week with four days in the next year is that year's week 1.
    /// </summary>
    private static int WeekOfYear(DateTime date, DayOfWeek first, CalendarWeekRule rule)
    {
        date = date.Date;
        if (date < WeekOne(date.Year))
            return (date - WeekOne(date.Year - 1)).Days / 7 + 1;
        if (rule == CalendarWeekRule.FirstFourDayWeek && date.Year < 9999 && date >= WeekOne(date.Year + 1))
            return 1;
        return (date - WeekOne(date.Year)).Days / 7 + 1;

        DateTime WeekOne(int year)
        {
            var january1 = new DateTime(year, 1, 1);
            int before = DaysIntoWeek(january1, first);
            return january1.AddDays(rule switch
            {
                CalendarWeekRule.FirstDay => -before,
                CalendarWeekRule.FirstFourDayWeek => before <= 3 ? -before : 7 - before,
                _ => before == 0 ? 0 : 7 - before,
            });
        }
    }

    /// <summary>
    /// <c>BAND</c>, <c>BOR</c> and <c>BXOR</c> on the operands' bits. When either operand is 16 bits, only the low 16
    /// bits are combined and the rest are the left operand's: its own upper bits, or the sign of the 16-bit result when
    /// it is 16 bits itself (verified vs ACE: 1 BOR TRUE is 65535, CLNG(-1) BAND CINT(1) is -65535, CINT(1) BOR 70000
    /// is 4465, CINT(-2) BOR 1 is -1). Otherwise the result has the wider operand's width. The result is an Integer
    /// only when both operands are 16 bits, and otherwise a Long or an Int64 (verified vs ACE: TRUE BAND CINT(-2) is
    /// an Integer, CINT(-2) BAND 70000 and a Byte BAND a Byte are Longs).
    /// </summary>
    private static object BitwiseOp(object a, object b, Func<long, long, long> op)
    {
        (long left, int leftWidth) = BitOperand(a);
        (long right, int rightWidth) = BitOperand(b);
        if (leftWidth != 16 && rightWidth != 16)
            return Signed(op(left, right), Math.Max(leftWidth, rightWidth));

        long low = op(left, right) & 0xFFFF;
        return leftWidth != 16 ? Signed(left & ~0xFFFFL | low, leftWidth)
            : rightWidth == 16 ? Signed(low, 16)
            : Signed((short)low, 32);
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

    /// <summary><c>BNOT</c>: every bit of the operand flipped, an Integer for a Boolean or an Integer (verified vs ACE:
    /// BNOT of a Yes/No column is an Integer, of a Byte a Long).</summary>
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

    /// <summary>The low <paramref name="width"/> bits read as a signed number: an Int16 for 16 bits, an Int32 for 32,
    /// an Int64 for 64.</summary>
    private static object Signed(long bits, int width) => width switch
    {
        // Each arm boxed on its own, or the switch would widen them all to Int64.
        16 => (object)(short)bits,
        32 => (object)(int)bits,
        _ => (object)bits,
    };

    /// <summary>DateSerial and TimeSerial: a date or time from three Integer parts, read as CInt reads them (verified vs
    /// ACE: 32768 is an overflow). Parts out of their range carry into the next.</summary>
    private object? DateParts(FunctionCall f, Func<int, int, int, DateTime> build)
    {
        if (Evaluate(f.Arguments[0]) is not { } a || Evaluate(f.Arguments[1]) is not { } b || Evaluate(f.Arguments[2]) is not { } c)
            return null;
        return build(AsInteger(a), AsInteger(b), AsInteger(c));
    }

    /// <summary>
    /// Access <c>DateSerial</c> (verified vs ACE): the month carries into the year first, then a year below 100 takes
    /// its century from the calendar's two-digit year window (so 49 is 2049, 50 is 1950, -1 is 1999 and
    /// DateSerial(100, 0, 1) is 1999-12-01), then the day carries. A year outside 100-9999 is an invalid procedure call.
    /// </summary>
    private static DateTime DateSerial(int year, int month, int day)
    {
        int months = year * 12 + month - 1;
        year = (int)Math.Floor(months / 12.0);
        month = months - year * 12 + 1;
        if (year < 100)
        {
            int latest = CultureInfo.CurrentCulture.Calendar.TwoDigitYearMax;
            int century = latest - latest % 100;
            year += year <= latest % 100 ? century : century - 100;
        }
        return InDateRange(() => new DateTime(year, month, 1).AddDays(day - 1));
    }

    /// <summary>A computed date, or an invalid procedure call when it falls outside 100-01-01 … 9999-12-31.</summary>
    private static DateTime InDateRange(Func<DateTime> compute)
    {
        try
        {
            DateTime date = compute();
            if (date.Year >= 100)
                return date;
        }
        catch (ArgumentOutOfRangeException)
        {
        }
        throw new ArgumentException("Invalid procedure call: the date is out of range.");
    }

    /// <summary>
    /// Access <c>DateAdd(interval, number, date)</c> (verified vs ACE): the number is truncated (True is -1), a month
    /// step keeps the day where the month has it and takes the month's last day otherwise, and a result outside
    /// 100-9999 is an invalid procedure call. "ms" is a LibRed extension.
    /// </summary>
    private object? DateAdd(FunctionCall f)
    {
        if (Evaluate(f.Arguments[0]) is not { } interval || Evaluate(f.Arguments[1]) is not { } number
            || Evaluate(f.Arguments[2]) is not { } date)
            return null;
        double n = Math.Truncate(Dbl(ConversionNumber(number)));
        DateTime d = ToDate(date);
        Func<DateTime> add = ConcatText(interval).ToLowerInvariant() switch
        {
            "yyyy" => () => d.AddYears(Int(n)),
            "q" => () => d.AddMonths(checked(Int(n) * 3)),
            "m" => () => d.AddMonths(Int(n)),
            "y" or "d" or "w" => () => d.AddDays(n),
            "ww" => () => d.AddDays(n * 7),
            "h" => () => d.AddHours(n),
            "n" => () => d.AddMinutes(n),
            "s" => () => d.AddSeconds(n),
            "ms" => () => d.AddMilliseconds(n),
            _ => throw UnknownInterval(interval),
        };
        return InDateRange(add);
    }

    /// <summary>
    /// Access <c>DateDiff(interval, date1, date2, [firstdayofweek], [firstweekofyear])</c> (verified vs ACE): the
    /// number of interval boundaries from date1 to date2, as a Long Integer. "w" is whole weeks of days, "ww" counts
    /// the weeks' first days, and "h", "n" and "s" count hour, minute and second boundaries; a count past a Long
    /// Integer is an overflow. The first day of the week matters only to "ww", and the first week of the year to none.
    /// </summary>
    private object? DateDiff(FunctionCall f)
    {
        if (Evaluate(f.Arguments[0]) is not { } intervalV || Evaluate(f.Arguments[1]) is not { } d1V
            || Evaluate(f.Arguments[2]) is not { } d2V)
            return null;
        DateTime d1 = ToDate(d1V), d2 = ToDate(d2V);
        string interval = ConcatText(intervalV).ToLowerInvariant();

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

        if (interval == "ww")
        {
            if (FirstDayOfWeek(f, 3) is not { } first)
                return null;
            return (d2.Date.AddDays(-DaysIntoWeek(d2, first)) - d1.Date.AddDays(-DaysIntoWeek(d1, first))).Days / 7;
        }

        return interval switch
        {
            "yyyy" => d2.Year - d1.Year,
            "q" => (d2.Year - d1.Year) * 4 + (d2.Month - 1) / 3 - (d1.Month - 1) / 3,
            "m" => (d2.Year - d1.Year) * 12 + d2.Month - d1.Month,
            "y" or "d" => (d2.Date - d1.Date).Days,
            "w" => (d2.Date - d1.Date).Days / 7,
            "h" => Boundaries(TimeSpan.TicksPerHour),
            "n" => Boundaries(TimeSpan.TicksPerMinute),
            "s" => Boundaries(TimeSpan.TicksPerSecond),
            // "ms" is handled above, as Int64.
            _ => throw UnknownInterval(intervalV),
        };

        int Boundaries(long unit) => checked((int)(d2.Ticks / unit - d1.Ticks / unit));
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

        // A date plus or less a span (IsSpan) is the date moved by it. Which side is the span is settled before
        // either is evaluated, so neither side is worked out twice; beside anything but a date, the span side is
        // evaluated as it always is — as the time on the epoch.
        object? left, right;
        if (SpanSide(b) is { } spanOnRight)
        {
            object? other = Evaluate(spanOnRight ? b.Left : b.Right);
            if (other is DateTime date)
                return SpanOf(spanOnRight ? b.Right : b.Left) is { } span
                    ? Shift(date, spanOnRight && b.Operator == BinaryOperator.Subtract ? -span : span)
                    : null;
            (left, right) = spanOnRight ? (other, Evaluate(b.Right)) : (Evaluate(b.Left), other);
        }
        else
        {
            left = Evaluate(b.Left);
            right = Evaluate(b.Right);
        }

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

        // A result that is a Currency (NumberTypeOf) is one at every step, not only in the result column: its four
        // places and its range apply to it where it is worked out, as CCur applies them (verified vs ACE: Currency
        // 1.2345 * 1.2345 is 1.524, 0.0003 * 1.2345 is 0.0004, and the largest Currency + 1 or * 3 is an
        // overflow). A Currency with a written decimal or a Double is not one, so those are left as they are.
        if (b.Operator is BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply)
        {
            object result = b.Operator switch
            {
                BinaryOperator.Add => Add(left, right),
                BinaryOperator.Subtract => Arithmetic(left, right, '-'),
                _ => Arithmetic(left, right, '*'),
            };
            return result is decimal money && IsCurrency(b) ? ToCurrency(money) : result;
        }

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
    /// Whether <paramref name="expression"/> is a span — known before any row is read, as a Currency result is.
    /// A span starts from a TimeSpan or TimeOnly parameter (<paramref name="duration"/>); only a parameter can say it
    /// is one, since a time column or a time written into the SQL is a date on the epoch, as Jet stores it. A span
    /// stays one under negation, added to or less another span, times or divided by something that is not a span,
    /// and chosen by IIF, COALESCE or CASE from spans and Nulls. A date plus or less a span is the date moved by it
    /// (<see cref="SpanOf"/>) — a date, where a date less a date is a day count.
    /// </summary>
    internal static bool IsSpan(Expression expression, Func<string, TimeSpan?> duration)
    {
        bool Span(Expression e) => IsSpan(e, duration);
        bool SpanOrNull(Expression e) => e is LiteralExpression { Value: null } || Span(e);
        bool Choice(IEnumerable<Expression> results) => results.All(SpanOrNull) && results.Any(Span);

        return expression switch
        {
            ParameterExpression parameter => duration(parameter.Name) is not null,
            UnaryExpression { Operator: UnaryOperator.Negate } negation => Span(negation.Operand),
            BinaryExpression { Operator: BinaryOperator.Add or BinaryOperator.Subtract } sum => Span(sum.Left) && Span(sum.Right),
            BinaryExpression { Operator: BinaryOperator.Multiply } product => Span(product.Left) != Span(product.Right),
            BinaryExpression { Operator: BinaryOperator.Divide } quotient => Span(quotient.Left) && !Span(quotient.Right),
            FunctionCall call when call.Name.Equals("IIF", StringComparison.OrdinalIgnoreCase) && call.Arguments.Count >= 2 =>
                Choice(call.Arguments.Skip(1)),
            FunctionCall call when call.Name.Equals("COALESCE", StringComparison.OrdinalIgnoreCase) => Choice(call.Arguments),
            CaseExpression @case => Choice(@case.WhenClauses.Select(w => w.Result)
                .Concat(@case.ElseResult is { } otherwise ? [otherwise] : [])),
            _ => false,
        };
    }

    /// <summary>Which side of a <c>+</c> or <c>-</c> is a span that a date beside it would move by: true for the right,
    /// false for the left (only for <c>+</c>, since a span less a date is no date), null when neither.</summary>
    private bool? SpanSide(BinaryExpression b)
    {
        if (parameters is null || b.Operator is not (BinaryOperator.Add or BinaryOperator.Subtract))
            return null;
        if (IsSpan(b.Right, parameters.Duration))
            return true;
        return b.Operator == BinaryOperator.Add && IsSpan(b.Left, parameters.Duration) ? false : null;
    }

    /// <summary>The value of a span (<see cref="IsSpan"/>), to the tick; null when it is Null. A factor or divisor is read
    /// as a number as the arithmetic operators read one.</summary>
    private TimeSpan? SpanOf(Expression expression)
    {
        switch (expression)
        {
            case ParameterExpression parameter:
                return parameters!.Duration(parameter.Name);
            case LiteralExpression { Value: null }:
                return null;
            case UnaryExpression negation:
                return -SpanOf(negation.Operand);
            case BinaryExpression { Operator: BinaryOperator.Add or BinaryOperator.Subtract } sum:
                return SpanOf(sum.Left) is { } a && SpanOf(sum.Right) is { } b
                    ? checked(sum.Operator == BinaryOperator.Add ? a + b : a - b)
                    : null;
            case BinaryExpression product:
            {
                bool spanOnLeft = product.Operator == BinaryOperator.Divide || IsSpan(product.Left, parameters!.Duration);
                if (SpanOf(spanOnLeft ? product.Left : product.Right) is not { } span
                    || Evaluate(spanOnLeft ? product.Right : product.Left) is not { } by)
                    return null;
                double factor = Dbl(ConversionNumber(by));
                if (product.Operator == BinaryOperator.Divide)
                    factor = factor != 0 ? 1 / factor : throw new DivideByZeroException("Division by zero.");
                return TimeSpan.FromTicks(checked((long)Math.Round(span.Ticks * factor)));
            }
            case FunctionCall call when call.Name.Equals("IIF", StringComparison.OrdinalIgnoreCase):
                return Evaluate(call.Arguments[0]) is { } condition && IifCondition(condition)
                    ? SpanOf(call.Arguments[1])
                    : call.Arguments.Count == 3 ? SpanOf(call.Arguments[2]) : null;
            case FunctionCall coalesce:
                foreach (Expression argument in coalesce.Arguments)
                    if (SpanOf(argument) is { } first)
                        return first;
                return null;
            case CaseExpression @case:
                foreach (CaseWhen when in @case.WhenClauses)
                    if (IsTrue(when.Condition))
                        return SpanOf(when.Result);
                return @case.ElseResult is { } otherwise ? SpanOf(otherwise) : null;
            default:
                throw new InvalidOperationException($"{expression.GetType().Name} is not a span.");
        }
    }

    /// <summary>A date moved by a span, to the tick; outside the dates Jet holds, an overflow.</summary>
    private static DateTime Shift(DateTime date, TimeSpan by)
    {
        long ticks = date.Ticks + by.Ticks;
        return ticks >= new DateTime(100, 1, 1).Ticks && ticks <= DateTime.MaxValue.Ticks
            ? new DateTime(ticks)
            : throw new OverflowException($"Overflow: {date} moved by {by} is outside the range of a date.");
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

    /// <summary>
    /// IIf's condition, which unlike the others is rounded to a whole number first, half to even (verified vs ACE:
    /// 0.4, 0.5, '0.4' and a date at 06:00 on 1899-12-30 are False, 0.6 and 1E+20 are True). Text that is not a
    /// number is True, as <see cref="IsTruthy"/> has it.
    /// </summary>
    private static bool IifCondition(object value) => value switch
    {
        bool b => b,
        string or char => !(TryTextAsNumber(value.ToString()!) is { } number && Math.Round(number, MidpointRounding.ToEven) == 0),
        DateTime d => Math.Round(d.ToOADate(), MidpointRounding.ToEven) != 0,
        Guid or byte[] => true,
        _ => !IsNumeric(value) || Math.Round(Dbl(value), MidpointRounding.ToEven) != 0,
    };

    /// <summary>Text read as <see cref="TextAsNumber"/> reads it, or null when it is not a number or is past a Double.</summary>
    private static double? TryTextAsNumber(string text)
    {
        try
        {
            return TextAsNumber(text);
        }
        catch (Exception e) when (e is InvalidCastException or OverflowException)
        {
            return null;
        }
    }

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
    /// (<c>334.90</c> has one; <c>3.0</c> is a whole number), and so is a whole number too big for a Long, with
    /// none; a Decimal column has its scale; Currency counts as four places.</item>
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
                    // A whole number too big for a Long is a Decimal to ACE, so Currency * 864000000000 is a Double
                    // there rather than an overflowing Currency (verified vs ACE); LibRed keeps the value an Int64.
                    long => new(NumberClass.Decimal, 0),
                    int or short or byte or bool => new(NumberClass.Whole),
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
            case FunctionCall { WithinGroup: not null, Arguments: [_, var key] } function
                when function.Name.Equals("PERCENTILE_DISC", StringComparison.OrdinalIgnoreCase):
                return Of(key);
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

    /// <summary>
    /// A value converted to the type its result column declares, when it has another: a number to a wider number
    /// (a Boolean as -1 or 0, a Double into a Decimal the OLE Automation way), anything to text as <c>&amp;</c> writes
    /// it, and anything to binary as its bytes (<see cref="ColumnBytes"/>). Null, or no <paramref name="type"/>, leaves
    /// the value as it is.
    /// </summary>
    internal static object? AsColumnType(object? value, Type? type, bool currency)
    {
        if (value is null || type is null || value.GetType() == type)
            return value;
        if (type == typeof(string)) return ConcatText(value);
        if (type == typeof(byte[])) return ColumnBytes(value, currency);
        if (type == typeof(decimal)) return Dec(value);
        if (type == typeof(double)) return Dbl(value);
        return Convert.ChangeType(Numeric(value), type, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A value in a binary result column (verified vs ACE, which writes it so where a UNION mixes binary or GUID
    /// values with others): text as UTF-16, a GUID as its 16 bytes, a Boolean as a 16-bit -1 or 0, a Byte or a Long
    /// as 4 bytes, an Integer as 2, a Single as 4 and a Double or a date's serial as 8, Currency as its 8-byte scaled
    /// integer, and a Decimal or a Large Number as its text. ACE cuts a Large Number's text to 8 bytes, which LibRed
    /// does not.
    /// </summary>
    private static byte[] ColumnBytes(object value, bool currency) => value switch
    {
        byte[] bytes => bytes,
        string text => Encoding.Unicode.GetBytes(text),
        Guid guid => guid.ToByteArray(),
        bool b => BitConverter.GetBytes((short)(b ? -1 : 0)),
        byte b => BitConverter.GetBytes((int)b),
        short s => BitConverter.GetBytes(s),
        int i => BitConverter.GetBytes(i),
        float f => BitConverter.GetBytes(f),
        double d => BitConverter.GetBytes(d),
        DateTime date => BitConverter.GetBytes(date.ToOADate()),
        decimal m when currency => BitConverter.GetBytes(decimal.ToOACurrency(m)),
        _ => Encoding.Unicode.GetBytes(ConcatText(value)),
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
    private static decimal ArithmeticDecimal(object v) => v is DateTime date ? SerialDecimal(date) : Dec(v);

    private static double Finite(double value) =>
        double.IsFinite(value) ? value : throw new OverflowException("Overflow: the result is too large for a number.");

    private static float Finite(float value) =>
        float.IsFinite(value) ? value : throw new OverflowException("Overflow: the result is too large for a number.");

    /// <summary>The date at an OLE Automation serial; a serial outside 100-01-01 … 9999-12-31 is an overflow.</summary>
    private static DateTime OaDate(double serial)
    {
        try
        {
            return DateTime.FromOADate(serial);
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
    internal static string ConcatText(object v) => v switch
    {
        string s => s,
        bool b => b ? "-1" : "0",
        double d => FloatingText(d, 15),
        float f => FloatingText(f, 7),
        decimal m => m.ToString("0.############################", CultureInfo.CurrentCulture),
        DateTime d => DateText(d),
        Guid g => g.ToString("B").ToUpperInvariant(),
        byte[] => Encoding.Unicode.GetString(ToBytes(v)),
        _ => Convert.ToString(v, CultureInfo.CurrentCulture)!,
    };

    /// <summary>
    /// A floating value rounded to <paramref name="digits"/> significant digits, trailing zeros dropped. Written
    /// in E notation when its exponent is at least <paramref name="digits"/>, or when fixed notation would need
    /// more than <paramref name="digits"/> decimals (verified vs ACE: <c>1/3</c> is 0.333333333333333, a Single
    /// 1E7 is 1E+07, a Single 1E-5 is 0.00001, 1E300 is 1E+300).
    /// </summary>
    private static string FloatingText(double value, int digits, NumberFormatInfo? format = null)
    {
        if (value == 0) return "0";
        if (!double.IsFinite(value)) return value.ToString(CultureInfo.InvariantCulture);

        format ??= CultureInfo.CurrentCulture.NumberFormat;
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

    /// <summary>The culture's long time pattern, with the narrow no-break space newer ICU data puts before AM/PM
    /// (U+202F, on Linux and macOS) written as the plain space of Windows' regional settings, and so of ACE. A date
    /// then writes the same text on every platform.</summary>
    private static string LongTimePattern(DateTimeFormatInfo format) => format.LongTimePattern.Replace(' ', ' ');

    /// <summary>A date as <see cref="ConcatText"/> writes it; the year is not zero-padded (year 100 is "100") unless
    /// <paramref name="padYear"/> asks for it, as Format does.</summary>
    private static string DateText(DateTime d, bool padYear = false)
    {
        DateTimeFormatInfo format = CultureInfo.CurrentCulture.DateTimeFormat;
        string time = d.ToString(LongTimePattern(format), CultureInfo.CurrentCulture);
        if (d.Date == OaEpoch) return time;
        string datePattern = padYear
            ? format.ShortDatePattern
            : format.ShortDatePattern.Replace("yyyy", "'" + d.Year.ToString(CultureInfo.InvariantCulture) + "'");
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
        // A Single divided with only Singles, Integers or Booleans is a Single (verified vs ACE: TRUE / a Single 1.5
        // is -0.6666667, and Str(CSng(1) / CSng(10)) is " .1"). OLE DB reports it as a Double, as it does CSng.
        if ((left is float || right is float) && IsSingleWidth(left) && IsSingleWidth(right))
        {
            float singleDivisor = Sng(right);
            if (singleDivisor == 0)
                throw new DivideByZeroException("Division by zero.");
            return Finite(Sng(left) / singleDivisor);
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
    /// <remarks>An Int32 result is worked out in Int64 all the same, and only the result has to fit: ACE squeezes
    /// each operand into a Long first, so a Double, Decimal or Currency past one overflows even where the answer
    /// would not — <c>1E12 MOD 7</c>, whose remainder is below 7. Here that is 1, and a quotient past a Long is still
    /// an overflow, since the column's type is settled before any value is seen. A LibRed extension.</remarks>
    private static object IntegerOp(object left, object right, char op)
    {
        left = Serial(left);
        right = Serial(right);
        long a = Lng(left), b = Lng(right);
        long result = op == '%' ? (b == -1 ? 0L : a % b) : a / b;
        // Each branch boxed on its own: a bare `? result : (int)result` is a long, and would widen the Int32 back.
        return left is long or ulong || right is long or ulong ? (object)result : checked((int)result);
    }

    /// <summary>VBA <c>CBool</c> (verified vs ACE): "True" and "False" as written, otherwise whether the value
    /// read as a number (<see cref="ConversionNumber"/>) is non-zero, so 0.5, '$5' and a date are True.</summary>
    private static bool VbaBool(object v) =>
        v is bool b ? b
        : v is string s && bool.TryParse(s, out bool parsed) ? parsed
        : Dbl(ConversionNumber(v)) != 0;

    // Jet's boolean convention (true = -1, false = 0) so a bool matches the numeric column it is stored in.
    private static object Numeric(object v) => v is bool b ? (b ? -1 : 0) : v;
    private static decimal Dec(object v) => JetDecimalConverter.ToDecimal(Numeric(v), CultureInfo.InvariantCulture);
    private static double Dbl(object v) => Convert.ToDouble(Numeric(v), CultureInfo.InvariantCulture);
    // Narrow to single precision (the cast yields ±Infinity for an out-of-range double rather than throwing).
    private static float Sng(object v) => (float)Dbl(v);
    private static long Lng(object v) => Convert.ToInt64(Numeric(v), CultureInfo.InvariantCulture);
    private static int Int(object v) => Convert.ToInt32(Numeric(v), CultureInfo.InvariantCulture);

    // For date arithmetic: a DateTime becomes its OLE Automation serial; a number is taken verbatim (as days).
    private static double Oa(object v) => v is DateTime d ? d.ToOADate() : Dbl(v);

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
        //
        // ToOADate keeps whole milliseconds, which is all a Date/Time column holds, but a DATETIME2 keeps 100-ns
        // ticks: two of those in the same millisecond are settled by their ticks, the way the serial would run —
        // later first below the epoch — so they neither compare equal nor sort as a tie.
        if (left is DateTime leftDate && right is DateTime rightDate)
        {
            int bySerial = leftDate.ToOADate().CompareTo(rightDate.ToOADate());
            return bySerial != 0 ? bySerial
                : leftDate.Ticks.CompareTo(rightDate.Ticks) * (leftDate < OaEpoch ? -1 : 1);
        }

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

    /// <summary>Access text comparison, in the database sort order (<see cref="JetTextComparer"/>): case-insensitive,
    /// trailing spaces ignored, an accented letter beside its base letter but not equal to it (verified vs ACE:
    /// <c>'é' &lt; 'f'</c>, <c>'café' ≠ 'cafe'</c>), <c>'ß' = 'ss'</c>, and a hyphen weighed after the letters. A
    /// character that order does not cover compares case-insensitively.</summary>
    private static int CompareText(string a, string b) =>
        JetTextComparer.Compare(a, b)
        ?? Math.Sign(string.Compare(a.TrimEnd(' '), b.TrimEnd(' '), StringComparison.InvariantCultureIgnoreCase));

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
