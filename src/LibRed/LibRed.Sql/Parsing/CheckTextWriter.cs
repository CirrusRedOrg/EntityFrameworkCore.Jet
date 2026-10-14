using System.Globalization;
using LibRed.Sql.Ast;
using static LibRed.Sql.Grammar.AccessSqlParser;

namespace LibRed.Sql.Parsing;

/// <summary>
/// A CHECK constraint's expression written out the way ACE writes one it has re-formed — which it does to any it took
/// the table's own name off a column of (see <see cref="AstBuilder"/>'s CheckText). The rules are the stored form's,
/// in docs/format/system-catalog.md under <c>CheckConstraints</c>. Anything ACE has no spelling for — CASE, IS TRUE,
/// IS DISTINCT FROM, the bitwise operators, a window or an aggregate's DISTINCT — is refused, as ACE refuses it.
/// </summary>
internal static class CheckTextWriter
{
    public static string Write(ExpressionContext expression)
    {
        expression = Unparenthesised(expression);
        int strength = Strength(expression);

        // An operand, bracketed where it binds looser than the operator it is under — or, on the operator's right,
        // as loose, everything grouping left to right.
        string Left(ExpressionContext operand) =>
            Strength(Unparenthesised(operand)) < strength ? $"({Write(operand)})" : Write(operand);
        string Right(ExpressionContext operand) =>
            Strength(Unparenthesised(operand)) <= strength ? $"({Write(operand)})" : Write(operand);
        string Binary(ExpressionContext left, string op, ExpressionContext right) => Left(left) + op + Right(right);
        static string Not(Antlr4.Runtime.IToken? not) => not is null ? "" : " Not";

        return expression switch
        {
            PowExprContext e => Binary(e.left, "^", e.right),
            NegateExprContext e => e.op.Text + Left(e.expression()),
            MulDivExprContext e => Binary(e.left, e.op.Text, e.right),
            IntDivExprContext e => Binary(e.left, "\\", e.right),
            ModExprContext e => Binary(e.left, " Mod ", e.right),
            AddSubExprContext e => Binary(e.left, e.op.Text, e.right),
            ConcatExprContext e => Binary(e.left, " & ", e.right),
            ComparisonExprContext e when e.op.Type == NEQ && IsNull(e.right) => Left(e.left) + " Is Not Null",
            ComparisonExprContext e => Binary(e.left, e.op.Type == NEQ ? "<>" : e.op.Text, e.right),
            BetweenExprContext e => Left(e.val) + Not(e.not) + " Between " + Right(e.lo) + " And " + Right(e.hi),
            LikeExprContext e => Left(e.left) + Not(e.not) + " ALike " + Right(e.right),
            InSubqueryExprContext e => Left(e.val) + Not(e.not) + " In (" + AstBuilder.OriginalText(e.sub) + ")",
            InExprContext e => Left(e.val) + Not(e.not) + " In (" + string.Join(",", e._items.Select(Write)) + ")",
            IsNullExprContext e => Left(e.operand) + (e.not is null ? " Is Null" : " Is Not Null"),
            NotExprContext { op.Type: NOT } e => "Not " + Left(e.expression()),
            AndExprContext { op.Type: AND } e => Binary(e.left, " And ", e.right),
            OrExprContext { op.Type: OR } e => Binary(e.left, " Or ", e.right),
            XorExprContext { op.Type: XOR } e => Binary(e.left, " Xor ", e.right),
            EqvExprContext e => Binary(e.left, " Eqv ", e.right),
            ImpExprContext e => Binary(e.left, " Imp ", e.right),
            PrimaryExprContext e => Primary(e.primary()),
            _ => throw CheckConstraint.Refusal(),
        };
    }

    /// <summary>How tightly an expression binds, by VBA's operator precedence: the higher, the tighter.</summary>
    private static int Strength(ExpressionContext expression) => expression switch
    {
        PowExprContext => 14,
        NegateExprContext => 13,
        MulDivExprContext => 12,
        IntDivExprContext => 11,
        ModExprContext => 10,
        AddSubExprContext => 9,
        ConcatExprContext => 8,
        ComparisonExprContext or BetweenExprContext or LikeExprContext or InSubqueryExprContext or InExprContext
            or IsNullExprContext => 7,
        NotExprContext => 6,
        AndExprContext => 5,
        OrExprContext => 4,
        XorExprContext => 3,
        EqvExprContext => 2,
        ImpExprContext => 1,
        _ => 15,
    };

    /// <summary>The expression inside any brackets written about it: where ACE needs them it puts its own.</summary>
    private static ExpressionContext Unparenthesised(ExpressionContext expression) =>
        expression is PrimaryExprContext { } p && p.primary() is ParenPrimaryContext paren
            ? Unparenthesised(paren.expression())
            : expression;

    private static bool IsNull(ExpressionContext expression) =>
        Unparenthesised(expression) is PrimaryExprContext p && p.primary() is LiteralPrimaryContext l
        && l.literal() is NullLiteralContext;

    private static string Primary(PrimaryContext primary) => primary switch
    {
        LiteralPrimaryContext p => Literal(p.literal()),
        FunctionCallPrimaryContext p => Call(p.functionCall()),
        ColumnPrimaryContext p when p.columnRef() is { _separators.Count: 0, first: { } first }
                                     && first.Start.Type == IDENTIFIER
                                     && first.GetText().Equals("Now", StringComparison.OrdinalIgnoreCase) => "Now()",
        ColumnPrimaryContext p => p.columnRef().GetText(),
        ExistsPrimaryContext p => "Exists (" + AstBuilder.OriginalText(p.queryExpression()) + ")",
        ScalarSubqueryPrimaryContext p => "(" + AstBuilder.OriginalText(p.queryExpression()) + ")",
        ParamPrimaryContext or SystemVariablePrimaryContext => primary.GetText(),
        _ => throw CheckConstraint.Refusal(),
    };

    private static string Call(FunctionCallContext call)
    {
        if (call.distinct is not null || call.withinGroup() is not null || call.filterClause() is not null
            || call.windowSpecification() is not null)
            throw CheckConstraint.Refusal();
        string name = call.name.GetText();
        if (name.Equals("IIf", StringComparison.OrdinalIgnoreCase))
            name = "IIf";
        return $"{name}({(call.star is not null ? "*" : string.Join(",", call.expression().Select(Write)))})";
    }

    private static string Literal(LiteralContext literal) => literal switch
    {
        IntLiteralContext or NumberLiteralContext => Number(literal.GetText()),
        DateLiteralContext => Date(AstBuilder.ParseDate(literal.GetText())),
        TrueLiteralContext or FalseLiteralContext => literal.Start.Type switch
        {
            TRUE => "True",
            YES => "Yes",
            ON => "On",
            FALSE => "False",
            NO => "No",
            _ => "Off",
        },
        NullLiteralContext => "Null",
        _ => literal.GetText(),
    };

    /// <summary>A number in plain decimal with its digits exact: dividing by a one of the widest scale drops the
    /// trailing zeros a Decimal keeps from how it was written.</summary>
    private static string Number(string text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value)
            ? (value / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture)
            : text;

    private static string Date(DateTime value) =>
        value.TimeOfDay == TimeSpan.Zero
            ? $"#{value.Month}/{value.Day}/{value.Year}#"
            : $"#{value.Month}/{value.Day}/{value.Year} {value.Hour}:{value.Minute}:{value.Second}#";
}
