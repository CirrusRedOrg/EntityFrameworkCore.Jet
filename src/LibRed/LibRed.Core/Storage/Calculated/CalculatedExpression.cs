using System.Globalization;

namespace LibRed.Storage.Calculated;

/// <summary>A parsed calculated-column expression (spec: page-02b §3.4a).</summary>
internal abstract record CalcNode;

/// <summary>A literal: number, string, <c>#date#</c>, <c>True</c>/<c>False</c>, or <c>Null</c>.</summary>
internal sealed record CalcLiteral(object? Value) : CalcNode;

/// <summary>A reference to another column of the same row — <c>[Name]</c> or a bare identifier.</summary>
internal sealed record CalcColumn(string Name) : CalcNode;

internal sealed record CalcUnary(string Operator, CalcNode Operand) : CalcNode;

internal sealed record CalcBinary(string Operator, CalcNode Left, CalcNode Right) : CalcNode;

internal sealed record CalcCall(string Name, IReadOnlyList<CalcNode> Arguments) : CalcNode;

/// <summary><c>x Is Null</c> / <c>x Is Not Null</c>.</summary>
internal sealed record CalcIsNull(CalcNode Operand, bool Negated) : CalcNode;

/// <summary><c>x In (a, b, ...)</c>.</summary>
internal sealed record CalcIn(CalcNode Operand, IReadOnlyList<CalcNode> Values) : CalcNode;

/// <summary>
/// Parses the Access expression subset ACE permits in a calculated column. This is deliberately its own
/// small parser rather than a reuse of <c>LibRed.Sql</c>: the dependency runs Engine → Core, the value has
/// to be produced at row-encode time down here, and the language is a different (much smaller) one than
/// Access SQL — a fixed whitelist ACE will not extend, since a calculated field cannot call a user-defined
/// function.
/// </summary>
/// <remarks>
/// Precedence follows VBA, loosest first: <c>Or</c>, <c>And</c>, <c>Not</c>, comparison (including
/// <c>Like</c>, <c>Is Null</c> and <c>In</c>), <c>&amp;</c>, <c>+ -</c>, <c>* /</c>, unary minus, <c>^</c>.
/// Concatenation binding tighter than comparison but looser than arithmetic is what makes
/// <c>[A] &amp; "x" = "yx"</c> parse as <c>([A] &amp; "x") = "yx"</c>.
/// </remarks>
internal static class CalculatedExpression
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CalcNode> Cache = new(StringComparer.Ordinal);

    /// <summary>Parses <paramref name="text"/>, reusing an earlier parse of the same text. Every row written
    /// to a table re-evaluates its calculated columns, so the parse must not be repeated per row.</summary>
    public static CalcNode ParseCached(string text) => Cache.GetOrAdd(text, Parse);

    /// <summary>Parses <paramref name="text"/>, or throws <see cref="CalculatedExpressionException"/>.</summary>
    public static CalcNode Parse(string text)
    {
        var tokens = Tokenise(text);
        int position = 0;
        CalcNode node = ParseOr(tokens, ref position);
        if (position < tokens.Count)
            throw new CalculatedExpressionException($"Unexpected '{tokens[position].Text}' in expression.");
        return node;
    }

    /// <summary>Every column the expression reads, which is what decides whether an UPDATE must recompute.</summary>
    /// <summary>Every function ACE accepts in a calculated column. Measured over all 147 candidates rather
    /// than derived: "deterministic, row-local, no I/O" explains the big exclusions but not the edges, where
    /// <c>Trim</c> is in and <c>LTrim</c>/<c>RTrim</c> are out, <c>Asc</c> is in and <c>Chr</c> is out, and
    /// <c>CDbl</c> is the only conversion of the ten. Independently confirmed against the list Access's own
    /// Expression Builder offers.</summary>
    private static readonly HashSet<string> Accepted = new(StringComparer.OrdinalIgnoreCase)
    {
        "Abs", "Sgn", "Int", "Fix", "Round", "Sqr", "Exp", "Log", "Sin", "Cos", "Tan", "Atn",
        "Len", "LCase", "UCase", "Trim", "Left", "Right", "Mid", "InStr", "Space", "String", "Str", "Asc",
        "DateSerial", "TimeSerial", "Year", "Month", "Day", "Hour", "Minute", "Second", "Weekday",
        "MonthName", "WeekdayName",
        "IIf", "Choose", "IsNull", "IsEmpty", "CDbl",
        "Pmt", "FV", "PV", "NPer", "Rate", "IPmt", "PPmt", "SLN", "SYD", "DDB",
    };

    /// <summary>Parses <paramref name="expression"/> and checks it is one ACE would accept for a column named
    /// <paramref name="self"/> over <paramref name="columns"/>.
    /// <para>Validation is <b>mandatory</b> before writing, not a courtesy: an expression ACE rejects does not
    /// produce a merely odd file, it produces a column ACE refuses to read at all — every SELECT of it raises
    /// "This calculated column contains an invalid expression", and every INSERT leaves the value empty.
    /// Proved by patching the stored property text behind DAO's back (§3.4a).</para></summary>
    public static CalcNode ParseValidated(string expression, IReadOnlyCollection<string> columns, string self)
    {
        CalcNode root = Parse(expression);
        Check(root);
        return root;

        void Check(CalcNode node)
        {
            switch (node)
            {
                case CalcColumn c when string.Equals(c.Name, self, StringComparison.OrdinalIgnoreCase):
                    throw new CalculatedExpressionException(
                        "The expression cannot be saved because it refers to itself.");
                case CalcColumn c when !columns.Any(n => string.Equals(n, c.Name, StringComparison.OrdinalIgnoreCase)):
                    throw new CalculatedExpressionException(
                        $"The expression cannot be saved because it refers to another table: '{c.Name}' is not "
                        + "a column of this one.");
                // Access's designer offers the '$' name variants and accepts them, but ACE then fails every
                // insert into the table -- so a column using one can never be populated (§3.4a).
                case CalcCall f when f.Name.EndsWith('$'):
                    throw new CalculatedExpressionException(
                        $"The expression {f.Name} cannot be used in a calculated column. Access accepts the '$' "
                        + $"name variants at design time but cannot populate such a column; use '{f.Name[..^1]}'.");
                case CalcCall f when !Accepted.Contains(f.Name):
                    throw new CalculatedExpressionException(
                        $"The expression {f.Name} cannot be used in a calculated column.");
            }

            foreach (CalcNode child in Children(node)) Check(child);
        }

        static IEnumerable<CalcNode> Children(CalcNode node) => node switch
        {
            CalcUnary u => [u.Operand],
            CalcBinary b => [b.Left, b.Right],
            CalcIsNull i => [i.Operand],
            CalcIn i => [i.Operand, .. i.Values],
            CalcCall f => f.Arguments,
            _ => [],
        };
    }

    /// <summary>Rewrites <c>`backtick`</c>-quoted identifiers into the <c>[bracket]</c> form Access's
    /// expression service understands, leaving the rest of the text alone.
    /// <para>SQL quotes identifiers with backticks — it is what EF Core's Jet provider emits — but the
    /// expression service is <b>not</b> the SQL parser and does not know them. Measured: ACE reads
    /// <c>`Qty`*2</c> as a reference to a field literally named <c>`Qty`</c> and fails with "Could not find
    /// field". A backtick expression therefore has to be translated on the way in; storing it verbatim would
    /// author a column ACE cannot evaluate.</para></summary>
    public static string NormaliseIdentifierQuoting(string expression)
    {
        if (!expression.Contains('`', StringComparison.Ordinal)) return expression;

        var result = new System.Text.StringBuilder(expression.Length);
        int i = 0;
        while (i < expression.Length)
        {
            char c = expression[i];
            if (c is '"' or '\'' or '#')                     // a literal: copy through to its closing mark
            {
                result.Append(c);
                for (i++; i < expression.Length; i++)
                {
                    result.Append(expression[i]);
                    if (expression[i] == c) { i++; break; }
                }
                continue;
            }
            if (c == '[' && expression.IndexOf(']', i + 1) is var bracket && bracket > i)
            {
                result.Append(expression[i..(bracket + 1)]);  // already bracketed: leave exactly as written
                i = bracket + 1;
                continue;
            }
            if (c == '`' && expression.IndexOf('`', i + 1) is var tick && tick > i)
            {
                result.Append('[').Append(expression[(i + 1)..tick]).Append(']');
                i = tick + 1;
                continue;
            }
            result.Append(c);
            i++;
        }
        return result.ToString();
    }

    /// <summary>Repoints every reference to <paramref name="oldName"/> at <paramref name="newName"/>, leaving
    /// the rest of the text byte-identical. Renaming a column a calculated expression reads without this
    /// leaves a dangling reference, and ACE then fails every read of that column — the table is effectively
    /// broken by an operation that looked unrelated.
    /// <para>Textual rather than a re-emit from the parse tree, because the parse tree cannot reproduce the
    /// author's spacing or bracketing. It walks the same lexical structure the tokeniser does, so a name
    /// inside a string or <c>#date#</c> literal is left alone, and a bare word followed by <c>(</c> is a
    /// function call rather than a column.</para></summary>
    public static string RenameColumnReference(string expression, string oldName, string newName)
    {
        var result = new System.Text.StringBuilder(expression.Length);
        int i = 0;
        while (i < expression.Length)
        {
            char c = expression[i];
            if (c is '"' or '\'' or '#')                     // a literal: copy through to its closing mark
            {
                result.Append(c);
                for (i++; i < expression.Length; i++)
                {
                    result.Append(expression[i]);
                    if (expression[i] == c) { i++; break; }
                }
                continue;
            }
            if (c == '[' && expression.IndexOf(']', i + 1) is var close && close > i)
            {
                string inner = expression[(i + 1)..close];
                result.Append('[')
                      .Append(Matches(inner) ? newName : inner)
                      .Append(']');
                i = close + 1;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] is '_' or '$')) i++;
                string word = expression[start..i];
                int after = i;
                while (after < expression.Length && char.IsWhiteSpace(expression[after])) after++;
                bool isCall = after < expression.Length && expression[after] == '(';
                result.Append(!isCall && Matches(word) ? Bracketed(newName) : word);
                continue;
            }
            result.Append(c);
            i++;
        }
        return result.ToString();

        bool Matches(string candidate) => string.Equals(candidate, oldName, StringComparison.OrdinalIgnoreCase);

        // A bare reference can only stay bare if the new name is still a plain identifier.
        static string Bracketed(string name) =>
            name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_')
            && name.All(ch => char.IsLetterOrDigit(ch) || ch == '_')
                ? name : $"[{name}]";
    }

    public static IReadOnlySet<string> ReferencedColumns(CalcNode node)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(node);
        return names;

        void Walk(CalcNode n)
        {
            switch (n)
            {
                case CalcColumn c: names.Add(c.Name); break;
                case CalcUnary u: Walk(u.Operand); break;
                case CalcBinary b: Walk(b.Left); Walk(b.Right); break;
                case CalcCall f: foreach (CalcNode a in f.Arguments) Walk(a); break;
                case CalcIsNull i: Walk(i.Operand); break;
                case CalcIn i: Walk(i.Operand); foreach (CalcNode v in i.Values) Walk(v); break;
            }
        }
    }

    // ---------------------------------------------------------------- parsing

    private static CalcNode ParseOr(List<Token> t, ref int p)
    {
        CalcNode left = ParseAnd(t, ref p);
        while (IsKeyword(t, p, "Or")) { p++; left = new CalcBinary("Or", left, ParseAnd(t, ref p)); }
        return left;
    }

    private static CalcNode ParseAnd(List<Token> t, ref int p)
    {
        CalcNode left = ParseNot(t, ref p);
        while (IsKeyword(t, p, "And")) { p++; left = new CalcBinary("And", left, ParseNot(t, ref p)); }
        return left;
    }

    private static CalcNode ParseNot(List<Token> t, ref int p)
    {
        if (!IsKeyword(t, p, "Not")) return ParseComparison(t, ref p);
        p++;
        return new CalcUnary("Not", ParseNot(t, ref p));
    }

    private static CalcNode ParseComparison(List<Token> t, ref int p)
    {
        CalcNode left = ParseConcat(t, ref p);
        while (true)
        {
            if (IsKeyword(t, p, "Is"))
            {
                p++;
                bool negated = IsKeyword(t, p, "Not");
                if (negated) p++;
                Expect(t, ref p, "Null", TokenKind.Keyword);
                left = new CalcIsNull(left, negated);
            }
            else if (IsKeyword(t, p, "Like"))
            {
                p++;
                left = new CalcBinary("Like", left, ParseConcat(t, ref p));
            }
            else if (IsKeyword(t, p, "In"))
            {
                p++;
                Expect(t, ref p, "(", TokenKind.Punctuation);
                var values = new List<CalcNode> { ParseOr(t, ref p) };
                while (IsPunctuation(t, p, ",")) { p++; values.Add(ParseOr(t, ref p)); }
                Expect(t, ref p, ")", TokenKind.Punctuation);
                left = new CalcIn(left, values);
            }
            else if (p < t.Count && t[p].Kind == TokenKind.Operator
                     && t[p].Text is "=" or "<>" or "<" or ">" or "<=" or ">=")
            {
                string op = t[p].Text;
                p++;
                left = new CalcBinary(op, left, ParseConcat(t, ref p));
            }
            else return left;
        }
    }

    private static CalcNode ParseConcat(List<Token> t, ref int p)
    {
        CalcNode left = ParseAdditive(t, ref p);
        while (IsOperator(t, p, "&")) { p++; left = new CalcBinary("&", left, ParseAdditive(t, ref p)); }
        return left;
    }

    private static CalcNode ParseAdditive(List<Token> t, ref int p)
    {
        CalcNode left = ParseMultiplicative(t, ref p);
        while (p < t.Count && t[p].Kind == TokenKind.Operator && t[p].Text is "+" or "-")
        {
            string op = t[p].Text;
            p++;
            left = new CalcBinary(op, left, ParseMultiplicative(t, ref p));
        }
        return left;
    }

    private static CalcNode ParseMultiplicative(List<Token> t, ref int p)
    {
        CalcNode left = ParseUnary(t, ref p);
        while (p < t.Count && t[p].Kind == TokenKind.Operator && t[p].Text is "*" or "/")
        {
            string op = t[p].Text;
            p++;
            left = new CalcBinary(op, left, ParseUnary(t, ref p));
        }
        return left;
    }

    private static CalcNode ParseUnary(List<Token> t, ref int p)
    {
        if (IsOperator(t, p, "-")) { p++; return new CalcUnary("-", ParseUnary(t, ref p)); }
        if (IsOperator(t, p, "+")) { p++; return ParseUnary(t, ref p); }
        return ParsePower(t, ref p);
    }

    // Right-associative, and binds tighter than unary minus: -2^2 is -4.
    private static CalcNode ParsePower(List<Token> t, ref int p)
    {
        CalcNode left = ParsePrimary(t, ref p);
        if (!IsOperator(t, p, "^")) return left;
        p++;
        return new CalcBinary("^", left, ParseUnary(t, ref p));
    }

    private static CalcNode ParsePrimary(List<Token> t, ref int p)
    {
        if (p >= t.Count) throw new CalculatedExpressionException("Expression ended unexpectedly.");
        Token token = t[p];

        switch (token.Kind)
        {
            case TokenKind.Number:
                p++;
                return new CalcLiteral(double.Parse(token.Text, CultureInfo.InvariantCulture));

            case TokenKind.String:
                p++;
                return new CalcLiteral(token.Text);

            case TokenKind.Date:
                p++;
                return new CalcLiteral(ParseDateLiteral(token.Text));

            case TokenKind.Bracketed:
                p++;
                return new CalcColumn(token.Text);

            case TokenKind.Punctuation when token.Text == "(":
            {
                p++;
                CalcNode inner = ParseOr(t, ref p);
                Expect(t, ref p, ")", TokenKind.Punctuation);
                return inner;
            }

            case TokenKind.Keyword or TokenKind.Identifier:
            {
                if (token.Text.Equals("True", StringComparison.OrdinalIgnoreCase)) { p++; return new CalcLiteral(true); }
                if (token.Text.Equals("False", StringComparison.OrdinalIgnoreCase)) { p++; return new CalcLiteral(false); }
                if (token.Text.Equals("Null", StringComparison.OrdinalIgnoreCase)) { p++; return new CalcLiteral(null); }

                p++;
                if (!IsPunctuation(t, p, "(")) return new CalcColumn(token.Text);

                p++;                                             // the '('
                var args = new List<CalcNode>();
                if (!IsPunctuation(t, p, ")"))
                {
                    args.Add(ParseOr(t, ref p));
                    while (IsPunctuation(t, p, ",")) { p++; args.Add(ParseOr(t, ref p)); }
                }
                Expect(t, ref p, ")", TokenKind.Punctuation);
                return new CalcCall(token.Text, args);
            }

            default:
                throw new CalculatedExpressionException($"Unexpected '{token.Text}' in expression.");
        }
    }

    private static DateTime ParseDateLiteral(string text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime value)
            ? value
            : throw new CalculatedExpressionException($"'#{text}#' is not a valid date literal.");

    private static bool IsKeyword(List<Token> t, int p, string word) =>
        p < t.Count && (t[p].Kind is TokenKind.Keyword or TokenKind.Identifier)
        && string.Equals(t[p].Text, word, StringComparison.OrdinalIgnoreCase);

    private static bool IsOperator(List<Token> t, int p, string op) =>
        p < t.Count && t[p].Kind == TokenKind.Operator && t[p].Text == op;

    private static bool IsPunctuation(List<Token> t, int p, string text) =>
        p < t.Count && t[p].Kind == TokenKind.Punctuation && t[p].Text == text;

    private static void Expect(List<Token> t, ref int p, string text, TokenKind kind)
    {
        bool ok = p < t.Count
                  && (kind == TokenKind.Keyword
                      ? t[p].Kind is TokenKind.Keyword or TokenKind.Identifier
                      : t[p].Kind == kind)
                  && string.Equals(t[p].Text, text, StringComparison.OrdinalIgnoreCase);
        if (!ok)
            throw new CalculatedExpressionException(
                $"Expected '{text}' but found {(p < t.Count ? $"'{t[p].Text}'" : "the end of the expression")}.");
        p++;
    }

    // ---------------------------------------------------------------- tokenising

    private enum TokenKind { Number, String, Date, Bracketed, Identifier, Keyword, Operator, Punctuation }

    private readonly record struct Token(TokenKind Kind, string Text);

    private static readonly string[] Keywords = ["And", "Or", "Not", "Is", "Null", "Like", "In", "True", "False"];

    private static List<Token> Tokenise(string text)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '[')
            {
                int end = text.IndexOf(']', i + 1);
                if (end < 0) throw new CalculatedExpressionException("Unclosed '[' in expression.");
                tokens.Add(new Token(TokenKind.Bracketed, text[(i + 1)..end]));
                i = end + 1;
            }
            else if (c == '"')
            {
                var value = new System.Text.StringBuilder();
                i++;
                while (true)
                {
                    if (i >= text.Length) throw new CalculatedExpressionException("Unterminated string in expression.");
                    if (text[i] == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i += 2; continue; }
                        i++;
                        break;
                    }
                    value.Append(text[i++]);
                }
                tokens.Add(new Token(TokenKind.String, value.ToString()));
            }
            else if (c == '#')
            {
                int end = text.IndexOf('#', i + 1);
                if (end < 0) throw new CalculatedExpressionException("Unclosed '#' in expression.");
                tokens.Add(new Token(TokenKind.Date, text[(i + 1)..end]));
                i = end + 1;
            }
            else if (char.IsDigit(c) || (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
            {
                int start = i;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                if (i < text.Length && (text[i] is 'e' or 'E'))
                {
                    i++;
                    if (i < text.Length && (text[i] is '+' or '-')) i++;
                    while (i < text.Length && char.IsDigit(text[i])) i++;
                }
                tokens.Add(new Token(TokenKind.Number, text[start..i]));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                // A trailing '$' is part of the name (Left$, Trim$ …). Access's Expression Builder offers
                // these and its designer accepts them, so they DO occur in real files — but the engine
                // cannot evaluate one, and the evaluator says so rather than the tokeniser reporting a
                // stray character.
                if (i < text.Length && text[i] == '$') i++;
                string word = text[start..i];
                bool keyword = Keywords.Contains(word, StringComparer.OrdinalIgnoreCase);
                tokens.Add(new Token(keyword ? TokenKind.Keyword : TokenKind.Identifier, word));
            }
            else if (c is '<' or '>' && i + 1 < text.Length && (text[i + 1] is '=' or '>'))
            {
                tokens.Add(new Token(TokenKind.Operator, text[i..(i + 2)]));
                i += 2;
            }
            else if (c is '+' or '-' or '*' or '/' or '^' or '&' or '=' or '<' or '>')
            {
                tokens.Add(new Token(TokenKind.Operator, c.ToString()));
                i++;
            }
            else if (c is '(' or ')' or ',')
            {
                tokens.Add(new Token(TokenKind.Punctuation, c.ToString()));
                i++;
            }
            else throw new CalculatedExpressionException($"Unexpected character '{c}' in expression.");
        }
        return tokens;
    }
}

/// <summary>A calculated-column expression LibRed cannot parse, or cannot evaluate the way ACE would.
/// Never thrown for a value: a row that fails to evaluate must not be written at all.</summary>
public sealed class CalculatedExpressionException(string message) : InvalidOperationException(message);
