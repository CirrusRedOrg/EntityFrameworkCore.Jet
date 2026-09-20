using Antlr4.Runtime;
using LibRed.Sql.Grammar;

namespace LibRed.Sql;

/// <summary>
/// The words the SQL front end treats as keywords, taken from the grammar itself rather than a list kept
/// beside it: every lexer token whose name lexes back to that same token is a keyword rule (<c>SELECT</c>,
/// <c>FROM</c>, …), while a token named for a category (<c>IDENT</c>, a literal, whitespace) lexes as an
/// identifier and drops out. Served through ADO.NET's <c>ReservedWords</c> metadata collection.
/// </summary>
public static class SqlKeywords
{
    private static readonly Lazy<IReadOnlyList<string>> All = new(Collect);

    /// <summary>Every keyword, upper-case and sorted.</summary>
    public static IReadOnlyList<string> Reserved => All.Value;

    private static IReadOnlyList<string> Collect()
    {
        var vocabulary = AccessSqlLexer.DefaultVocabulary;
        var words = new List<string>();

        // Token types run 1..n in the lexer's own rule order; the vocabulary names them.
        for (int type = 1; type <= AccessSqlLexer.ruleNames.Length; type++)
        {
            string? name = vocabulary.GetSymbolicName(type);
            if (name is null || !name.All(char.IsAsciiLetterUpper)) continue;

            // The name is a keyword only if lexing it produces exactly that token. An identifier-shaped token
            // name lexes to IDENT instead, and so is not one.
            var lexer = new AccessSqlLexer(CharStreams.fromString(name)) { TokenFactory = CommonTokenFactory.Default };
            lexer.RemoveErrorListeners();
            IToken first = lexer.NextToken();
            if (first.Type == type && lexer.NextToken().Type == TokenConstants.EOF)
                words.Add(name);
        }

        words.Sort(StringComparer.Ordinal);
        return words;
    }
}
