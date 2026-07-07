using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

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
    public object? Evaluate(Expression expression) => expression switch
    {
        LiteralExpression l => l.Value,
        ColumnReference c => scope.TryResolve(c, out object? v) ? v
            : TryNiladicFunction(c, out object? nv) ? nv
            : throw new InvalidOperationException($"Column '{EvalScope.Describe(c)}' was not found."),
        ScalarSubquery s => subqueries.ExecuteScalar(s.Query, scope),
        ExistsExpression e => subqueries.ExecuteExists(e.Query, scope),
        InSubqueryExpression i => EvaluateInSubquery(i),
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
        foreach (object? item in subqueries.ExecuteColumn(inq.Query, scope))
        {
            if (item is null) hasNull = true;
            else if (Compare(val, item) == 0) { found = true; break; }
        }
        bool? result = found ? true : hasNull ? null : false;
        return inq.Negated ? (result is null ? null : !result) : result;
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

        return name switch
        {
            "IIF" => IsTrue(f.Arguments[0]) ? Evaluate(f.Arguments[1]) : Evaluate(f.Arguments[2]),
            "CHOOSE" => Choose(f),
            "SWITCH" => Switch(f),
            "DATEPART" => DatePart(Evaluate(f.Arguments[0]), Evaluate(f.Arguments[1])),
            "ROUND" => Round(f),
            "FIX" => Numeric1(f, Math.Truncate, Math.Truncate),  // toward zero
            "INT" => Numeric1(f, Math.Floor, Math.Floor),        // toward -infinity
            "ABS" => Numeric1(f, Math.Abs, Math.Abs),
            // VBA/Access type-conversion functions. All propagate NULL. CInt/CLng/CByte round half-to-even
            // ("banker's rounding"), which is exactly what Convert.ToInt16/Int32/Byte do. CVar is a no-op
            // passthrough (LibRed has no distinct Variant type).
            "CCUR" => Convert1(f, v => Math.Round(Convert.ToDecimal(v, CultureInfo.InvariantCulture), 4)), // to Currency (decimal, 4 dp)
            "CBOOL" => Convert1(f, v => v is bool b ? b : Convert.ToBoolean(v, CultureInfo.InvariantCulture)),
            "CBYTE" => Convert1(f, v => Convert.ToByte(v, CultureInfo.InvariantCulture)),
            "CINT" => Convert1(f, v => Convert.ToInt16(v, CultureInfo.InvariantCulture)),
            "CLNG" => Convert1(f, v => Convert.ToInt32(v, CultureInfo.InvariantCulture)),
            "CSNG" => Convert1(f, v => Convert.ToSingle(v, CultureInfo.InvariantCulture)),
            "CDBL" => Convert1(f, v => Convert.ToDouble(v, CultureInfo.InvariantCulture)),
            "CDEC" => Convert1(f, v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)),
            "CSTR" => Convert1(f, v => Convert.ToString(v, CultureInfo.InvariantCulture)),
            "CDATE" => Convert1(f, ToDate),
            "CVAR" => Evaluate(f.Arguments[0]), // passthrough (no Variant type)

            // VBA/Access string functions. All propagate NULL; positions are 1-based. Comparisons default to
            // case-insensitive (Access "Option Compare Database" = Text), overridable by a compare argument.
            "LEN" => Convert1(f, v => v.ToString()!.Length),
            "LCASE" => Convert1(f, v => v.ToString()!.ToLowerInvariant()),
            "UCASE" => Convert1(f, v => v.ToString()!.ToUpperInvariant()),
            "TRIM" => Convert1(f, v => v.ToString()!.Trim(' ')),
            "LTRIM" => Convert1(f, v => v.ToString()!.TrimStart(' ')),
            "RTRIM" => Convert1(f, v => v.ToString()!.TrimEnd(' ')),
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
            // Jet VBA math functions (double precision). SQR = sqrt, ATN = atan, SGN = sign, LOG =
            // natural log. Acos/Asin/Atan2/Floor/Ceiling/Log10/Log-base are emitted by EF as
            // expressions built from these plus arithmetic, so they need no dedicated cases.
            "SIN" => UnaryDouble(f, Math.Sin),
            "COS" => UnaryDouble(f, Math.Cos),
            "TAN" => UnaryDouble(f, Math.Tan),
            "ATN" => UnaryDouble(f, Math.Atan),
            "EXP" => UnaryDouble(f, Math.Exp),
            "LOG" => UnaryDouble(f, Math.Log),
            "SQR" => UnaryDouble(f, Math.Sqrt),
            "SGN" => UnaryDouble(f, d => Math.Sign(d)),

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
            "MONTHNAME" => Convert1(f, v => EnUs.DateTimeFormat.GetMonthName(Convert.ToInt32(v, CultureInfo.InvariantCulture))),
            "TIMER" => (DateTime.Now - DateTime.Today).TotalSeconds,
            "RND" => Random.Shared.NextDouble(),
            // Predicates / type inspection. IsError is always false — LibRed has no error-value type. ISNULL
            // returns a Boolean (ACE reports it as -1/0); both print as a boolean here.
            "ISNULL" => Evaluate(f.Arguments[0]) is null,
            "ISNUMERIC" => IsNumericValue(Evaluate(f.Arguments[0])),
            "ISERROR" => false,
            "TYPENAME" => TypeNameOf(Evaluate(f.Arguments[0])),
            "VARTYPE" => VarTypeOf(Evaluate(f.Arguments[0])),

            // Wide (Unicode code-point) variants. AscW = the first char's code point; ChrW = the char for a code
            // point (unlike Chr, not restricted to a byte). Verified vs ACE: ChrW(233) → 'é'.
            "ASCW" => Convert1(f, v => (int)v.ToString()![0]),
            "CHRW" => Convert1(f, v => ((char)Convert.ToInt32(v, CultureInfo.InvariantCulture)).ToString()),
            // Byte variants operate on the UTF-16 byte layout (2 bytes/char): LenB = 2×length, AscB = the low
            // byte of the first char, and Left/Right/Mid/InStr count bytes. Verified vs ACE (LenB('abc')=6,
            // InStrB(1,'abc','b')=3). ChrB is intentionally absent — ACE's expression service has no ChrB.
            "ASCB" => Convert1(f, v => v.ToString()![0] & 0xFF),
            "LENB" => Convert1(f, v => v.ToString()!.Length * 2),
            "LEFTB" => ByteLeft(f),
            "RIGHTB" => ByteRight(f),
            "MIDB" => ByteMid(f),
            "INSTRB" => InstrB(f),

            // GenUniqueID(): Access's random-Long generator. Not callable in a SELECT (ACE errors "Undefined
            // function") but valid as a LONG column's DEFAULT, where it yields a random signed Int32 per row —
            // the mechanism behind a "Random" AutoNumber. Accepted on a plain LONG default too (ACE allows it
            // only on a LONG column). AutoNumber columns take their random value in the row inserter instead.
            "GENUNIQUEID" => RandomLong(),
            _ => throw new NotSupportedException($"Function {f.Name} is not supported."),
        };
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

    /// <summary>Access <c>StrComp(a, b)</c>: -1/0/1. Case-insensitive (Access "Option Compare Database" = Text).
    /// NULL-propagating.</summary>
    private object? StrComp(FunctionCall f)
    {
        object? a = Evaluate(f.Arguments[0]);
        object? b = Evaluate(f.Arguments[1]);
        if (a is null || b is null) return null;
        return Math.Sign(string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Access <c>InStrRev(string, find)</c>: the 1-based position of the last occurrence, or 0.
    /// Case-insensitive. NULL-propagating.</summary>
    private object? InstrRev(FunctionCall f)
    {
        object? text = Evaluate(f.Arguments[0]);
        object? find = Evaluate(f.Arguments[1]);
        if (text is null || find is null) return null;
        return text.ToString()!.LastIndexOf(find.ToString()!, StringComparison.OrdinalIgnoreCase) + 1;
    }

    /// <summary>VBA <c>Str(number)</c>: the number as text, with a leading space for non-negative values (VBA
    /// reserves that column for the sign).</summary>
    private static string VbaStr(object v)
    {
        double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
        string s = d.ToString(CultureInfo.InvariantCulture);
        return d >= 0 ? " " + s : s;
    }

    /// <summary>VBA <c>Val(string)</c>: the leading numeric portion as a Double (0 if none). Ignores everything
    /// from the first non-numeric character.</summary>
    private static object VbaVal(string s)
    {
        Match m = Regex.Match(s, @"^\s*[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?");
        return m.Success && double.TryParse(m.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
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

    /// <summary>VBA <c>LeftB(string, bytes)</c>: the leading <c>bytes</c> bytes of the UTF-16 layout, i.e. the
    /// first <c>bytes/2</c> characters. NULL-propagating.</summary>
    private object? ByteLeft(FunctionCall f)
    {
        object? sv = Evaluate(f.Arguments[0]);
        if (sv is null) return null;
        string s = sv.ToString()!;
        int n = Math.Clamp(Convert.ToInt32(Evaluate(f.Arguments[1]), CultureInfo.InvariantCulture) / 2, 0, s.Length);
        return s[..n];
    }

    /// <summary>VBA <c>RightB(string, bytes)</c>: the trailing <c>bytes/2</c> characters. NULL-propagating.</summary>
    private object? ByteRight(FunctionCall f)
    {
        object? sv = Evaluate(f.Arguments[0]);
        if (sv is null) return null;
        string s = sv.ToString()!;
        int n = Math.Clamp(Convert.ToInt32(Evaluate(f.Arguments[1]), CultureInfo.InvariantCulture) / 2, 0, s.Length);
        return s[^n..];
    }

    /// <summary>VBA <c>MidB(string, startByte[, lenBytes])</c>: 1-based byte start, byte length — mapped to
    /// characters (2 bytes each). NULL-propagating.</summary>
    private object? ByteMid(FunctionCall f)
    {
        object? sv = Evaluate(f.Arguments[0]);
        if (sv is null) return null;
        string s = sv.ToString()!;
        int startChar = Math.Max(0, (Convert.ToInt32(Evaluate(f.Arguments[1]), CultureInfo.InvariantCulture) - 1) / 2);
        if (startChar >= s.Length) return "";
        int lenChar = f.Arguments.Count > 2
            ? Convert.ToInt32(Evaluate(f.Arguments[2]), CultureInfo.InvariantCulture) / 2
            : s.Length - startChar;
        lenChar = Math.Clamp(lenChar, 0, s.Length - startChar);
        return s.Substring(startChar, lenChar);
    }

    /// <summary>VBA <c>InStrB([start,] string1, string2)</c>: the 1-based BYTE position of string2 in string1
    /// (0 if not found) — i.e. the character position mapped back to bytes. NULL-propagating.</summary>
    private object? InstrB(FunctionCall f)
    {
        int argc = f.Arguments.Count;
        object? s1v = Evaluate(f.Arguments[argc >= 3 ? 1 : 0]);
        object? s2v = Evaluate(f.Arguments[argc >= 3 ? 2 : 1]);
        if (s1v is null || s2v is null) return null;
        int startChar = argc >= 3
            ? Math.Max(0, (Convert.ToInt32(Evaluate(f.Arguments[0]), CultureInfo.InvariantCulture) - 1) / 2)
            : 0;
        string s1 = s1v.ToString()!, s2 = s2v.ToString()!;
        if (startChar >= s1.Length) return 0;
        int idx = s1.IndexOf(s2, startChar, StringComparison.OrdinalIgnoreCase);
        return idx < 0 ? 0 : idx * 2 + 1;
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
    private object? StringInt(FunctionCall f, Func<string, int, string> op)
    {
        object? s = Evaluate(f.Arguments[0]);
        return s is null ? null : op(s.ToString()!, Convert.ToInt32(Evaluate(f.Arguments[1]), CultureInfo.InvariantCulture));
    }

    /// <summary>Access MID(string, start[, length]) — a 1-based substring; length omitted means to the end.</summary>
    private object? Mid(FunctionCall f)
    {
        object? sv = Evaluate(f.Arguments[0]);
        if (sv is null) return null;
        string s = sv.ToString()!;
        int start = Math.Max(1, Convert.ToInt32(Evaluate(f.Arguments[1]), CultureInfo.InvariantCulture));
        int from = start - 1;
        if (from >= s.Length) return "";
        int avail = s.Length - from;
        int len = avail;
        if (f.Arguments.Count > 2 && Evaluate(f.Arguments[2]) is { } lenVal)
            len = Math.Clamp(Convert.ToInt32(lenVal, CultureInfo.InvariantCulture), 0, avail);
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

        string s1 = s1v.ToString()!, s2 = s2v.ToString()!;
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
        if (sv is null || findv is null || replv is null) return null;
        string s = sv.ToString()!, find = findv.ToString()!, repl = replv.ToString()!;

        int start = f.Arguments.Count > 3 ? Math.Max(1, Convert.ToInt32(Evaluate(f.Arguments[3]), CultureInfo.InvariantCulture)) : 1;
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
            _ => throw new NotSupportedException($"DATEPART interval '{interval}' is not supported."),
        };
    }

    /// <summary>A bitwise op (Access <c>BAND</c>/<c>BOR</c>/<c>BXOR</c>) over integer operands; the result
    /// keeps the operand's int type (Int32, or Int64 if either operand is long).</summary>
    private static object BitwiseOp(object a, object b, Func<long, long, long> op) =>
        a is long or ulong || b is long or ulong ? (object)op(Lng(a), Lng(b)) : (int)op(Int(a), Int(b));

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
        return (intervalV?.ToString() ?? "").ToLowerInvariant() switch
        {
            "yyyy" => d2.Year - d1.Year,
            "q" => (d2.Year - d1.Year) * 4 + (d2.Month - 1) / 3 - (d1.Month - 1) / 3,
            "m" => (d2.Year - d1.Year) * 12 + d2.Month - d1.Month,
            "y" or "d" or "w" => (int)(d2.Date - d1.Date).TotalDays,
            "ww" => (int)((d2.Date - d1.Date).TotalDays / 7),
            "h" => (int)(d2 - d1).TotalHours,
            "n" => (int)(d2 - d1).TotalMinutes,
            "s" => (int)(d2 - d1).TotalSeconds,
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
            UnaryOperator.Negate => v switch // preserve the operand's numeric type (EF contract), like C# unary minus
            {
                null => null,
                decimal d => -d,
                double db => -db,
                float f => -f,
                long or ulong => -Lng(v),
                _ => -Int(v), // int/short/byte → int
            },
            UnaryOperator.BitNot => v is null ? null : v is long or ulong ? (object)~Lng(v) : ~Int(v),
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

        object? left = Evaluate(b.Left);
        object? right = Evaluate(b.Right);

        if (b.Operator == BinaryOperator.Concat)
            return (left?.ToString() ?? "") + (right?.ToString() ?? "");

        if (left is null || right is null)
            return null;

        return b.Operator switch
        {
            BinaryOperator.Equal => Compare(left, right) == 0,
            BinaryOperator.NotEqual => Compare(left, right) != 0,
            BinaryOperator.LessThan => Compare(left, right) < 0,
            BinaryOperator.LessThanOrEqual => Compare(left, right) <= 0,
            BinaryOperator.GreaterThan => Compare(left, right) > 0,
            BinaryOperator.GreaterThanOrEqual => Compare(left, right) >= 0,
            BinaryOperator.Like => Like(left.ToString()!, right.ToString()!),
            // Access '+' concatenates when either operand is text (but, unlike '&', null already propagated above).
            BinaryOperator.Add => left is string || right is string ? left.ToString() + right.ToString() : Arithmetic(left, right, '+'),
            BinaryOperator.Subtract => Arithmetic(left, right, '-'),
            BinaryOperator.Multiply => Arithmetic(left, right, '*'),
            BinaryOperator.Divide => Divide(left, right), // Access '/' is floating division
            BinaryOperator.Modulo => IntegerOp(left, right, '%'),
            BinaryOperator.IntDivide => IntegerOp(left, right, '\\'),
            BinaryOperator.Power => Math.Pow(Convert.ToDouble(left, CultureInfo.InvariantCulture), Convert.ToDouble(right, CultureInfo.InvariantCulture)),
            BinaryOperator.BitAnd => BitwiseOp(left, right, (x, y) => x & y),
            BinaryOperator.BitOr => BitwiseOp(left, right, (x, y) => x | y),
            BinaryOperator.BitXor => BitwiseOp(left, right, (x, y) => x ^ y),
            _ => throw new NotSupportedException($"Binary operator {b.Operator}."),
        };
    }

    /// <summary>SQL LIKE: '%'/'*' match any run, '_'/'?' match one char; case-insensitive.</summary>
    private static bool Like(string value, string pattern)
    {
        // Access/Jet LIKE wildcards: * or % = any run, ? or _ = any single char, # = any single DIGIT, and
        // [charlist] / [!charlist] = a (negated) single-char class. A literal special char is escaped by
        // bracketing it — e.g. EF's Contains("C#") emits `%C[#]%`, where [#] matches a literal '#'. Without
        // bracket-class support that pattern would look for the literal text "C[#]" and match nothing.
        var sb = new StringBuilder("^");
        int i = 0;
        while (i < pattern.Length)
        {
            char ch = pattern[i];
            if (ch == '[')
            {
                int close = pattern.IndexOf(']', i + 1);
                if (close > i)
                {
                    sb.Append(TranslateLikeClass(pattern.Substring(i + 1, close - i - 1)));
                    i = close + 1;
                    continue;
                }
                // No closing ']' → a literal '['.
            }

            sb.Append(ch switch
            {
                '%' or '*' => ".*",
                '_' or '?' => ".",
                '#' => "[0-9]",
                _ => Regex.Escape(ch.ToString()),
            });
            i++;
        }
        sb.Append('$');
        return Regex.IsMatch(value, sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }

    /// <summary>Translates an Access LIKE bracket list (the text between <c>[</c> and <c>]</c>) to a regex
    /// character class: a leading <c>!</c> is negation (<c>^</c>), ranges (<c>a-z</c>) carry over, and the
    /// regex-special <c>\ ] ^</c> are escaped so a bracketed literal like <c>[#]</c>/<c>[[]</c> matches itself.</summary>
    private static string TranslateLikeClass(string inner)
    {
        var sb = new StringBuilder("[");
        if (inner.StartsWith('!')) { sb.Append('^'); inner = inner[1..]; }
        foreach (char c in inner)
        {
            if (c is '\\' or ']' or '^') sb.Append('\\');
            sb.Append(c);
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static bool? AsBool(object? v) => v switch { bool b => b, null => null, _ => Convert.ToBoolean(v) };

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
                '+' => RoundToSecond(DateTime.FromOADate(a + b)),
                '-' => RoundToSecond(DateTime.FromOADate(a - b)),
                _ => a * b,                                        // date × n has no date meaning → numeric
            };
        }
        if (left is decimal || right is decimal) { decimal a = Dec(left), b = Dec(right); return op == '+' ? a + b : op == '-' ? a - b : a * b; }
        if (left is double || right is double) { double a = Dbl(left), b = Dbl(right); return op == '+' ? a + b : op == '-' ? a - b : a * b; }
        if (left is float || right is float) { float a = (float)Dbl(left), b = (float)Dbl(right); return op == '+' ? a + b : op == '-' ? a - b : a * b; }
        if (left is long or ulong || right is long or ulong) { long a = Lng(left), b = Lng(right); return op == '+' ? a + b : op == '-' ? a - b : a * b; }
        int x = Int(left), y = Int(right); return op == '+' ? x + y : op == '-' ? x - y : x * y;
    }

    /// <summary>Access <c>/</c> is floating division — Decimal when either operand is Decimal/Currency,
    /// otherwise Double (never integer division; that is <c>\</c>).</summary>
    private static object Divide(object left, object right) =>
        left is decimal || right is decimal ? Dec(left) / Dec(right) : Dbl(left) / Dbl(right);

    /// <summary>Access integer operators <c>\</c> (int division) and <c>MOD</c>: operands round to an
    /// integer, and the result keeps the operand's integer type (int, or long if either is Int64) — so
    /// <c>int \ int</c> is Int32, matching the EF contract.</summary>
    private static object IntegerOp(object left, object right, char op)
    {
        if (left is long or ulong || right is long or ulong)
        { long a = Lng(left), b = Lng(right); return op == '%' ? a % b : a / b; }
        int x = Int(left), y = Int(right); return op == '%' ? x % y : x / y;
    }

    // Jet's boolean convention (true = -1, false = 0) so a bool matches the numeric column it is stored in.
    private static object Numeric(object v) => v is bool b ? (b ? -1 : 0) : v;
    private static decimal Dec(object v) => Convert.ToDecimal(Numeric(v), CultureInfo.InvariantCulture);
    private static double Dbl(object v) => Convert.ToDouble(Numeric(v), CultureInfo.InvariantCulture);
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

    /// <summary>Coerces a value to a decimal for comparisons, using Jet's boolean convention.</summary>
    private static decimal ToNumber(object v) => Dec(v);

    private static int Compare(object left, object right)
    {
        if (IsNumeric(left) && IsNumeric(right))
            return ToNumber(left).CompareTo(ToNumber(right));

        // Binary (byte[]) columns: structural, length-sensitive byte compare — lexicographic then by
        // length, so a shorter value sorts before a longer one sharing its prefix (Jet's binary order,
        // matching IndexKeyEncoder). Without this, byte[] falls through to ToString() ("System.Byte[]"
        // for every array) and all binaries compare *equal* — so `WHERE binKey = @p` matches every row.
        if (left is byte[] lb && right is byte[] rb)
            return CompareBytes(lb, rb);

        if (left is string || right is string)
            return CompareText(left.ToString()!, right.ToString()!);

        if (left is IComparable c && left.GetType() == right.GetType())
            return c.CompareTo(right);

        return CompareText(left.ToString()!, right.ToString()!);
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
    // a boolean predicate (e.g. IS NOT NULL) must compare equal to that stored value. ToNumber uses
    // Jet's convention (false = 0, true = -1) so a bool matches the numeric value it is stored as.
    private static bool IsNumeric(object v) =>
        v is bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;
}
