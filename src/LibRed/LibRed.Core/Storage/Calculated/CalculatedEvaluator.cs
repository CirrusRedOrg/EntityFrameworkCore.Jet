using System.Globalization;
using LibRed.Catalog;

namespace LibRed.Storage.Calculated;

/// <summary>
/// Evaluates a parsed calculated-column expression against one row, the way ACE does.
/// </summary>
/// <remarks>
/// <para>Only the functions ACE actually permits are implemented, and an expression using anything else
/// throws rather than guessing — a wrong cached value is invisible, because neither engine re-derives it on
/// read (page-02b §3.4a).</para>
/// <para><b>Null semantics are the point.</b> Access propagates Null through arithmetic and comparison but
/// <b>not</b> through <c>&amp;</c>, which treats Null as the empty string. That asymmetry is the whole
/// mechanism behind the idiom MSDN uses to introduce the feature —
/// <c>[First] &amp; " " &amp; ([Middle] + " ") &amp; [Last]</c> — where <c>+</c> makes the parenthesised
/// group Null when there is no middle initial and <c>&amp;</c> then drops it, so no double space appears.
/// Get the two operators the same way round and that expression silently gains a space.</para>
/// </remarks>
internal static class CalculatedEvaluator
{
    /// <summary>Evaluates <paramref name="node"/>; <paramref name="value"/> resolves a column by name.</summary>
    public static object? Evaluate(CalcNode node, Func<string, object?> value) => node switch
    {
        CalcLiteral l => l.Value,
        CalcColumn c => value(c.Name),
        CalcUnary u => Unary(u, value),
        CalcBinary b => Binary(b, value),
        CalcIsNull i => Evaluate(i.Operand, value) is null != i.Negated,
        CalcIn i => In(i, value),
        CalcCall f => Call(f, value),
        _ => throw new CalculatedExpressionException($"Unsupported expression node {node.GetType().Name}."),
    };

    private static object? Unary(CalcUnary node, Func<string, object?> value)
    {
        object? operand = Evaluate(node.Operand, value);
        if (operand is null) return null;
        return node.Operator switch
        {
            "-" => -ToNumber(operand),
            "Not" => !ToBoolean(operand),
            _ => throw new CalculatedExpressionException($"Unsupported unary operator '{node.Operator}'."),
        };
    }

    private static object? Binary(CalcBinary node, Func<string, object?> value)
    {
        object? left = Evaluate(node.Left, value);
        object? right = Evaluate(node.Right, value);

        // '&' is the one operator that does NOT propagate Null: it concatenates it as "".
        if (node.Operator == "&") return ToText(left) + ToText(right);

        if (left is null || right is null) return null;

        switch (node.Operator)
        {
            case "+":
            {
                // '+' concatenates when BOTH sides are text, and adds otherwise — the overload that makes
                // it the null-propagating sibling of '&'. Adding a number to a date yields a DATE, not the
                // serial underneath it, so `[D] + 1` stays a date the way ACE stores it.
                if (left is string a && right is string b) return a + b;
                double sum = ToNumber(left) + ToNumber(right);
                return left is DateTime ^ right is DateTime ? DateTime.FromOADate(sum) : sum;
            }
            case "-":
                return left is DateTime && right is DateTime
                    ? ToNumber(left) - ToNumber(right)
                    : left is DateTime d ? d.AddDays(-ToNumber(right)) : (object)(ToNumber(left) - ToNumber(right));
            case "*": return ToNumber(left) * ToNumber(right);
            case "/": return ToNumber(left) / ToNumber(right);
            case "^": return Math.Pow(ToNumber(left), ToNumber(right));
            case "And": return ToBoolean(left) && ToBoolean(right);
            case "Or": return ToBoolean(left) || ToBoolean(right);
            case "Like": return Like(ToText(left), ToText(right));
            case "=": case "<>": case "<": case ">": case "<=": case ">=":
            {
                int c = Compare(left, right);
                return node.Operator switch
                {
                    "=" => c == 0, "<>" => c != 0, "<" => c < 0,
                    ">" => c > 0, "<=" => c <= 0, _ => c >= 0,
                };
            }
            default:
                throw new CalculatedExpressionException($"Unsupported operator '{node.Operator}'.");
        }
    }

    private static object? In(CalcIn node, Func<string, object?> value)
    {
        object? operand = Evaluate(node.Operand, value);
        if (operand is null) return null;
        foreach (CalcNode candidate in node.Values)
        {
            object? other = Evaluate(candidate, value);
            if (other is not null && Compare(operand, other) == 0) return true;
        }
        return false;
    }

    private static object? Call(CalcCall node, Func<string, object?> value)
    {
        string name = node.Name;

        // Access's Expression Builder lists the '$' name variants (Left$, Trim$, UCase$, Mid$ …) and its
        // designer accepts one in a calculated column — but ACE then fails EVERY insert into that table with
        // "Error evaluating CHECK constraint", while the plain name works. The column is unusable in Access
        // itself, so refusing is matching it, not falling short of it.
        if (name.EndsWith('$'))
            throw new CalculatedExpressionException(
                $"'{name}' cannot be evaluated in a calculated column. Access offers the '$' name variants and "
                + $"accepts them at design time, but ACE fails every insert into such a table; use '{name[..^1]}'.");

        // IIf and Choose SHORT-CIRCUIT: only the selected branch is evaluated, matching both the Access
        // expression service and LibRed.Engine's own evaluator. (Access's VBA-side IIf famously does NOT
        // short circuit, but VBA is not what computes a calculated column.) This is more than an
        // optimisation now that an argument can THROW rather than merely be Null —
        // IIf(IsNull([x]), 0, CDbl([x])) is the way to keep CDbl away from a Null, and it only works if the
        // CDbl arm never runs.
        switch (name.ToUpperInvariant())
        {
            case "IIF":
            {
                if (node.Arguments.Count != 3)
                    throw new CalculatedExpressionException(
                        $"{name} takes 3 arguments, not {node.Arguments.Count}.");
                object? condition = Evaluate(node.Arguments[0], value);
                return Evaluate(node.Arguments[condition is not null && ToBoolean(condition) ? 1 : 2], value);
            }
            case "CHOOSE":
            {
                if (node.Arguments.Count < 2)
                    throw new CalculatedExpressionException($"{name} needs at least two arguments.");
                object? selector = Evaluate(node.Arguments[0], value);
                if (selector is null) return null;
                int index = (int)Math.Round(ToNumber(selector), MidpointRounding.ToEven);
                return index >= 1 && index < node.Arguments.Count
                    ? Evaluate(node.Arguments[index], value)
                    : null;
            }
        }

        object?[] args = [.. node.Arguments.Select(a => Evaluate(a, value))];

        switch (name.ToUpperInvariant())
        {
            case "ISNULL":
                Arity(name, args, 1);
                return args[0] is null;
            // VBA's IsEmpty asks whether a Variant was never initialised, which a stored column value never
            // is — measured against ACE, which returns False for a column holding a value AND for one
            // holding Null. It is on the whitelist but is a constant here.
            case "ISEMPTY":
                Arity(name, args, 1);
                return false;
        }

        // The VBA conversions RAISE on a Null argument instead of propagating it, and CDbl is the only
        // conversion a calculated column may contain. ACE caches that failure as VBA error 94 and its own
        // reader then refuses the entire row; LibRed refuses the WRITE instead — a deliberate divergence, so
        // that a row neither engine can read afterwards never reaches the file and no compact-and-repair is
        // needed to get rid of it (page-02e-calculated-columns).
        if (args.Length > 0 && args[0] is null && name.Equals("CDbl", StringComparison.OrdinalIgnoreCase))
            throw new CalculatedExpressionException(
                "CDbl cannot convert Null — the VBA conversions raise on Null rather than propagating it, so "
                + "this row's cached value would be an error ACE could not read back. Give the column a "
                + "value, or guard the expression, e.g. IIf(IsNull([x]), 0, CDbl([x])).");

        // Everything else propagates Null through any argument.
        if (args.Any(a => a is null)) return null;

        return name.ToUpperInvariant() switch
        {
            "ABS" => Math.Abs(Num(name, args, 1)),
            "SGN" => (double)Math.Sign(Num(name, args, 1)),
            "INT" => Math.Floor(Num(name, args, 1)),
            "FIX" => Math.Truncate(Num(name, args, 1)),
            "SQR" => Math.Sqrt(Num(name, args, 1)),
            "EXP" => Math.Exp(Num(name, args, 1)),
            "LOG" => Math.Log(Num(name, args, 1)),
            "SIN" => Math.Sin(Num(name, args, 1)),
            "COS" => Math.Cos(Num(name, args, 1)),
            "TAN" => Math.Tan(Num(name, args, 1)),
            "ATN" => Math.Atan(Num(name, args, 1)),
            "CDBL" => Num(name, args, 1),
            // Round is banker's rounding in VBA, and its second argument is genuinely optional here.
            "ROUND" => args.Length == 1
                ? Math.Round(ToNumber(args[0]!), MidpointRounding.ToEven)
                : Math.Round(ToNumber(args[0]!), (int)ToNumber(args[1]!), MidpointRounding.ToEven),

            "LEN" => (double)ToText(args[0]).Length,
            "LCASE" => ToText(Single(name, args)).ToLowerInvariant(),
            "UCASE" => ToText(Single(name, args)).ToUpperInvariant(),
            "TRIM" => ToText(Single(name, args)).Trim(' '),
            "SPACE" => new string(' ', (int)Num(name, args, 1)),
            "ASC" => ToText(Single(name, args)) is { Length: > 0 } s ? (double)s[0]
                     : throw new CalculatedExpressionException("Asc of an empty string."),
            "STR" => Str(Num(name, args, 1)),
            "STRING" => Repeat(args),
            "LEFT" => Take(args, name, fromLeft: true),
            "RIGHT" => Take(args, name, fromLeft: false),
            // Mid needs all three arguments in a calculated column, optional in VBA or not; InStr needs three
            // or four but not two. A wrong count is ACE's "Syntax error in expression", NOT its policy
            // refusal — measured, and the reverse of what an unknown function name gets.
            "MID" => Mid(args, name),
            "INSTR" => InStr(args, name),

            "YEAR" => (double)ToDate(Single(name, args)).Year,
            "MONTH" => (double)ToDate(Single(name, args)).Month,
            "DAY" => (double)ToDate(Single(name, args)).Day,
            "HOUR" => (double)ToDate(Single(name, args)).Hour,
            "MINUTE" => (double)ToDate(Single(name, args)).Minute,
            "SECOND" => (double)ToDate(Single(name, args)).Second,
            "WEEKDAY" => (double)(((int)ToDate(args[0]!).DayOfWeek) + 1),
            "DATESERIAL" => DateSerial(args, name),
            "TIMESERIAL" => TimeSerial(args, name),
            "MONTHNAME" => MonthName(args, name),
            "WEEKDAYNAME" => WeekdayName(args, name),

            "PMT" => Pmt(args, name),
            "FV" => Fv(args, name),
            "PV" => Pv(args, name),
            "NPER" => NPer(args, name),
            "IPMT" => IPmt(args, name),
            "PPMT" => PPmt(args, name),
            "RATE" => Rate(args, name),
            "SLN" => Sln(args, name),
            "SYD" => Syd(args, name),
            "DDB" => Ddb(args, name),

            _ => throw new CalculatedExpressionException(
                $"LibRed cannot evaluate '{name}' in a calculated column yet."),
        };
    }

    // ---------------------------------------------------------------- function helpers

    private static void Arity(string name, object?[] args, int expected)
    {
        if (args.Length != expected)
            throw new CalculatedExpressionException($"{name} takes {expected} arguments, not {args.Length}.");
    }

    /// <summary>Arity for a function whose optional arguments really may be omitted. Which functions those
    /// are is per-function and measured, not a rule: <c>Mid</c> and <c>InStr</c> demand every argument while
    /// <c>Round</c>, <c>Weekday</c>, <c>MonthName</c> and <c>WeekdayName</c> do not.</summary>
    private static void Arity(string name, object?[] args, int min, int max)
    {
        if (args.Length < min || args.Length > max)
            throw new CalculatedExpressionException(
                $"{name} takes {min} to {max} arguments, not {args.Length}.");
    }

    private static object Single(string name, object?[] args) { Arity(name, args, 1); return args[0]!; }

    private static double Num(string name, object?[] args, int expected)
    {
        Arity(name, args, expected);
        return ToNumber(args[0]!);
    }

    /// <summary>VBA <c>Str</c>: invariant formatting, with a leading space for a non-negative number.</summary>
    private static string Str(double value) =>
        (value < 0 ? "" : " ") + value.ToString("R", CultureInfo.InvariantCulture);

    private static string Repeat(object?[] args)
    {
        if (args.Length != 2) throw new CalculatedExpressionException("String takes two arguments.");
        string s = ToText(args[1]);
        if (s.Length == 0) throw new CalculatedExpressionException("String of an empty character.");
        return new string(s[0], (int)ToNumber(args[0]!));
    }

    private static string Take(object?[] args, string name, bool fromLeft)
    {
        if (args.Length != 2) throw new CalculatedExpressionException($"{name} takes two arguments.");
        string s = ToText(args[0]);
        int n = Math.Clamp((int)ToNumber(args[1]!), 0, s.Length);
        return fromLeft ? s[..n] : s[^n..];
    }

    private static string Mid(object?[] args, string name)
    {
        if (args.Length != 3)
            throw new CalculatedExpressionException(
                $"{name} needs all three arguments in a calculated column, even though the third is optional in VBA.");
        string s = ToText(args[0]);
        int start = (int)ToNumber(args[1]!);
        int length = (int)ToNumber(args[2]!);
        if (start < 1) throw new CalculatedExpressionException("Mid start is 1-based.");
        if (start > s.Length) return "";
        return s.Substring(start - 1, Math.Clamp(length, 0, s.Length - start + 1));
    }

    /// <summary>Measured against ACE: three or four arguments, never two — <c>InStr([A],"a")</c> is a syntax
    /// error, <c>InStr(1,[A],"a")</c> and <c>InStr(1,[A],"a",0)</c> are both accepted and both populate. With
    /// <c>compare</c> omitted the match is case-<b>insensitive</b>: <c>InStr(1,"hello","H")</c> is 1 while an
    /// explicit <c>,0</c> gives 0. That is Access's <c>Option Compare Database</c> default rather than VBA's
    /// binary one, so defaulting to 0 the way VBA does would silently miss matches.</summary>
    private static double InStr(object?[] args, string name)
    {
        if (args.Length is not (3 or 4))
            throw new CalculatedExpressionException(
                $"{name} takes three or four arguments in a calculated column; the two-argument form VBA "
                + "allows is a syntax error here.");
        int start = (int)ToNumber(args[0]!);
        string haystack = ToText(args[1]);
        string needle = ToText(args[2]);
        bool caseInsensitive = args.Length < 4 || (int)ToNumber(args[3]!) == 1;
        if (start < 1) throw new CalculatedExpressionException("InStr start is 1-based.");
        if (start > haystack.Length) return 0;
        int found = haystack.IndexOf(needle, start - 1,
            caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        return found < 0 ? 0 : found + 1;
    }

    private static DateTime DateSerial(object?[] args, string name)
    {
        if (args.Length != 3) throw new CalculatedExpressionException($"{name} takes three arguments.");
        return new DateTime((int)ToNumber(args[0]!), 1, 1)
            .AddMonths((int)ToNumber(args[1]!) - 1)
            .AddDays((int)ToNumber(args[2]!) - 1);
    }

    private static DateTime TimeSerial(object?[] args, string name)
    {
        if (args.Length != 3) throw new CalculatedExpressionException($"{name} takes three arguments.");
        return JetEpoch
            .AddHours(ToNumber(args[0]!))
            .AddMinutes(ToNumber(args[1]!))
            .AddSeconds(ToNumber(args[2]!));
    }

    /// <summary>Access <c>MonthName(month, [abbreviate])</c>. Measured accepted with the optional omitted,
    /// unlike <c>Mid</c> and <c>InStr</c> — the "supply every optional" rule is per-function.</summary>
    private static string MonthName(object?[] args, string name)
    {
        Arity(name, args, 1, 2);
        int month = (int)ToNumber(args[0]!);
        return args.Length > 1 && ToBoolean(args[1]!)
            ? EnUs.DateTimeFormat.GetAbbreviatedMonthName(month)
            : EnUs.DateTimeFormat.GetMonthName(month);
    }

    /// <summary>Access <c>WeekdayName(weekday, [abbreviate], [firstDayOfWeek])</c>, likewise measured
    /// accepted with its optionals omitted.</summary>
    private static string WeekdayName(object?[] args, string name)
    {
        Arity(name, args, 1, 3);
        int weekday = (int)ToNumber(args[0]!);
        int firstDay = args.Length > 2 ? (int)ToNumber(args[2]!) : 1;
        if (firstDay == 0) firstDay = 1;                    // vbUseSystem -> vbSunday, for determinism
        int index = ((((firstDay - 1) + (weekday - 1)) % 7) + 7) % 7;
        string[] names = args.Length > 1 && ToBoolean(args[1]!)
            ? EnUs.DateTimeFormat.AbbreviatedDayNames
            : EnUs.DateTimeFormat.DayNames;
        return names[index];
    }

    // ---------------------------------------------------------------- financial
    //
    // All ten of these are on ACE's calculated-column whitelist. They are ported from LibRed.Engine's
    // ExpressionEvaluator rather than re-derived, so a formula cannot answer one way through SQL and another
    // through a calculated column. Two copies exist only because of the layering: this evaluation happens in
    // Core at row-encode time, and Core cannot reach Engine (docs/format/page-02e-calculated-columns.md).

    private static double Pow1(double rate, double nper) => Math.Pow(1 + rate, nper);

    private static double AnnuityFactor(double rate, double nper, double type)
        => (1 + rate * type) * (Pow1(rate, nper) - 1) / rate;

    private static double PaymentOf(double rate, double nper, double pv, double fv, double type)
        => rate == 0 ? -(pv + fv) / nper : -(pv * Pow1(rate, nper) + fv) / AnnuityFactor(rate, nper, type);

    private static double InterestOf(double rate, double per, double nper, double pv, double fv, double type)
    {
        if (type == 1 && per == 1) return 0;
        double pmt = PaymentOf(rate, nper, pv, fv, type);
        double balance = pv * Pow1(rate, per - 1)
                       + pmt * (rate == 0 ? per - 1 : AnnuityFactor(rate, per - 1, type));
        double interest = -balance * rate;                  // an outflow, so negative like Pmt itself
        return type == 1 ? interest / (1 + rate) : interest;
    }

    /// <summary>VBA <c>Pmt(rate, nper, pv, [fv], [type])</c>: the constant payment for an annuity.</summary>
    private static double Pmt(object?[] args, string name)
    {
        Arity(name, args, 3, 5);
        return PaymentOf(Fin(args, 0), Fin(args, 1), Fin(args, 2), Fin(args, 3, 0), Fin(args, 4, 0));
    }

    /// <summary>VBA <c>FV(rate, nper, pmt, [pv], [type])</c>: the future value of an annuity.</summary>
    private static double Fv(object?[] args, string name)
    {
        Arity(name, args, 3, 5);
        double rate = Fin(args, 0), nper = Fin(args, 1), pmt = Fin(args, 2),
               pv = Fin(args, 3, 0), type = Fin(args, 4, 0);
        return rate == 0
            ? -(pv + pmt * nper)
            : -(pv * Pow1(rate, nper) + pmt * AnnuityFactor(rate, nper, type));
    }

    /// <summary>VBA <c>PV(rate, nper, pmt, [fv], [type])</c>: the present value of an annuity.</summary>
    private static double Pv(object?[] args, string name)
    {
        Arity(name, args, 3, 5);
        double rate = Fin(args, 0), nper = Fin(args, 1), pmt = Fin(args, 2),
               fv = Fin(args, 3, 0), type = Fin(args, 4, 0);
        return rate == 0
            ? -(fv + pmt * nper)
            : -(fv + pmt * AnnuityFactor(rate, nper, type)) / Pow1(rate, nper);
    }

    /// <summary>VBA <c>NPer(rate, pmt, pv, [fv], [type])</c>: the number of periods for an annuity.</summary>
    private static double NPer(object?[] args, string name)
    {
        Arity(name, args, 3, 5);
        double rate = Fin(args, 0), pmt = Fin(args, 1), pv = Fin(args, 2),
               fv = Fin(args, 3, 0), type = Fin(args, 4, 0);
        if (rate == 0) return -(pv + fv) / pmt;
        double a = pmt * (1 + rate * type);
        return Math.Log((a - fv * rate) / (a + pv * rate)) / Math.Log(1 + rate);
    }

    /// <summary>VBA <c>IPmt(rate, per, nper, pv, [fv], [type])</c>: the interest portion of one payment.</summary>
    private static double IPmt(object?[] args, string name)
    {
        Arity(name, args, 4, 6);
        return InterestOf(
            Fin(args, 0), Fin(args, 1), Fin(args, 2), Fin(args, 3), Fin(args, 4, 0), Fin(args, 5, 0));
    }

    /// <summary>VBA <c>PPmt(rate, per, nper, pv, [fv], [type])</c>: the principal portion, Pmt minus IPmt.</summary>
    private static double PPmt(object?[] args, string name)
    {
        Arity(name, args, 4, 6);
        double rate = Fin(args, 0), nper = Fin(args, 2), pv = Fin(args, 3),
               fv = Fin(args, 4, 0), type = Fin(args, 5, 0);
        return PaymentOf(rate, nper, pv, fv, type) - InterestOf(rate, Fin(args, 1), nper, pv, fv, type);
    }

    /// <summary>VBA <c>SLN(cost, salvage, life)</c>: straight-line depreciation.</summary>
    private static double Sln(object?[] args, string name)
    {
        Arity(name, args, 3);
        return (Fin(args, 0) - Fin(args, 1)) / Fin(args, 2);
    }

    /// <summary>VBA <c>SYD(cost, salvage, life, period)</c>: sum-of-years'-digits depreciation.</summary>
    private static double Syd(object?[] args, string name)
    {
        Arity(name, args, 4);
        double life = Fin(args, 2);
        return (Fin(args, 0) - Fin(args, 1)) * (life - Fin(args, 3) + 1) / (life * (life + 1) / 2);
    }

    /// <summary>VBA <c>DDB(cost, salvage, life, period, [factor])</c>: double-declining-balance depreciation,
    /// clamped so the book value never drops below salvage.</summary>
    private static double Ddb(object?[] args, string name)
    {
        Arity(name, args, 4, 5);
        double cost = Fin(args, 0), salvage = Fin(args, 1), period = Fin(args, 3);
        double rate = Fin(args, 4, 2) / Fin(args, 2);
        double bookStart = cost * Math.Pow(1 - rate, period - 1);
        double depreciation = bookStart * rate;
        return bookStart - depreciation < salvage ? Math.Max(0, bookStart - salvage) : depreciation;
    }

    /// <summary>VBA <c>Rate(nper, pmt, pv, [fv], [type], [guess])</c>: the per-period rate. There is no closed
    /// form, so it is solved by Newton-Raphson on the annuity equation, from the caller's guess.</summary>
    private static double Rate(object?[] args, string name)
    {
        Arity(name, args, 3, 6);
        double nper = Fin(args, 0), pmt = Fin(args, 1), pv = Fin(args, 2),
               fv = Fin(args, 3, 0), type = Fin(args, 4, 0);
        double r = Fin(args, 5, 0.1);

        for (int iteration = 0; iteration < 100; iteration++)
        {
            const double step = 1e-6;
            double value = Annuity(r);
            double derivative = (Annuity(r + step) - value) / step;
            if (Math.Abs(derivative) < 1e-12) break;
            double next = r - value / derivative;
            if (Math.Abs(next - r) < 1e-10) return next;
            r = next;
        }
        return r;

        double Annuity(double rate) => rate == 0
            ? pv + pmt * nper + fv
            : pv * Pow1(rate, nper) + pmt * (1 + rate * type) * (Pow1(rate, nper) - 1) / rate + fv;
    }

    private static double Fin(object?[] args, int index) => ToNumber(args[index]!);

    private static double Fin(object?[] args, int index, double fallback)
        => args.Length > index ? ToNumber(args[index]!) : fallback;

    // ---------------------------------------------------------------- coercion

    /// <summary>Month and day names come from a fixed en-US, not the ambient culture: the value is written
    /// into the file, so it must not depend on who wrote the row.</summary>
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    /// <summary>The OLE Automation epoch Jet dates count from (see the heritage note in CLAUDE.md).</summary>
    private static readonly DateTime JetEpoch = new(1899, 12, 30);

    private static double ToNumber(object value) => value switch
    {
        double d => d,
        float f => f,
        decimal m => (double)m,
        bool b => b ? -1 : 0,                        // VARIANT_TRUE is -1, not 1
        DateTime t => t.ToOADate(),
        string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed) => parsed,
        byte or short or int or long => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        _ => throw new CalculatedExpressionException($"Cannot use '{value}' as a number."),
    };

    private static bool ToBoolean(object value) => value is bool b ? b : ToNumber(value) != 0;

    private static string ToText(object? value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "True" : "False",
        DateTime d => d.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    private static DateTime ToDate(object value) => value is DateTime d ? d : DateTime.FromOADate(ToNumber(value));

    private static int Compare(object left, object right)
    {
        if (left is string || right is string)
            return string.Compare(ToText(left), ToText(right), StringComparison.OrdinalIgnoreCase);
        if (left is DateTime || right is DateTime)
            return ToDate(left).CompareTo(ToDate(right));
        return ToNumber(left).CompareTo(ToNumber(right));
    }

    /// <summary>Access <c>Like</c>: <c>*</c> any run, <c>?</c> one character, <c>#</c> one digit.</summary>
    private static bool Like(string text, string pattern)
    {
        var regex = new System.Text.StringBuilder("^");
        foreach (char c in pattern)
            regex.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                '#' => "[0-9]",
                _ => System.Text.RegularExpressions.Regex.Escape(c.ToString()),
            });
        regex.Append('$');
        return System.Text.RegularExpressions.Regex.IsMatch(
            text, regex.ToString(), System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>Coerces an evaluated result to the column's <c>ResultType</c>, which is what the payload is
    /// encoded in.</summary>
    public static object? Coerce(object? value, JetDataType type)
    {
        if (value is null) return null;
        return type switch
        {
            JetDataType.Boolean => ToBoolean(value),
            JetDataType.Byte => (byte)Math.Round(ToNumber(value), MidpointRounding.ToEven),
            JetDataType.Int16 => (short)Math.Round(ToNumber(value), MidpointRounding.ToEven),
            JetDataType.Int32 => (int)Math.Round(ToNumber(value), MidpointRounding.ToEven),
            JetDataType.Int64 => (long)Math.Round(ToNumber(value), MidpointRounding.ToEven),
            JetDataType.Single => (float)ToNumber(value),
            JetDataType.Double => ToNumber(value),
            JetDataType.Currency => Math.Round((decimal)ToNumber(value), 4, MidpointRounding.ToEven),
            JetDataType.DateTime => ToDate(value),
            JetDataType.Text or JetDataType.Memo => ToText(value),
            _ => throw new CalculatedExpressionException($"Cannot store a calculated {type} value."),
        };
    }
}
