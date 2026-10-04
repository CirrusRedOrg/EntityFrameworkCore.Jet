using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;

namespace LibRed.Engine.Tests;

/// <summary>
/// The pairs of text <c>LIKE</c> might count as the same character, and ACE's answer for them. The runtime only
/// PROPOSES pairs — each BMP character against its upper and lower case, the Turkish i forms, and each character
/// against the letters it decomposes to — and ACE decides every one. ACE's answer is the same in every collation
/// (measured across all 405 LibRed can create), so one database asks it.
/// </summary>
internal static class LikeCandidates
{
    public static (string Value, string Pattern)[] Pairs { get; } = [.. Generate()];

    private static IEnumerable<(string Value, string Pattern)> Generate()
    {
        var seen = new HashSet<(string, string)>();
        IEnumerable<(string, string)> Both(string a, string b) => a == b ? [] : [(a, b), (b, a)];

        for (int c = 0x20; c <= 0xFFFF; c++)
        {
            if (c is >= 0xD800 and <= 0xDFFF || c is '%' or '_' or '[' or ']') continue;
            string s = ((char)c).ToString();
            foreach (string other in (string[])[s.ToUpperInvariant(), s.ToLowerInvariant(),
                         char.ToUpperInvariant((char)c).ToString(), char.ToLowerInvariant((char)c).ToString()])
                foreach (var p in Both(s, other))
                    if (seen.Add(p)) yield return p;

            // Noncharacters have no decomposition to propose, and Normalize refuses them.
            string decomposed;
            try { decomposed = s.Normalize(System.Text.NormalizationForm.FormKD); }
            catch (ArgumentException) { continue; }
            if (decomposed.Length >= 2 && decomposed.All(char.IsLetter) && !decomposed.Any(ch => ch is '%' or '_' or '['))
                foreach (string spelling in (string[])[decomposed, decomposed.ToUpperInvariant(), decomposed.ToLowerInvariant()])
                    foreach (var p in Both(s, spelling))
                        if (seen.Add(p)) yield return p;
        }

        (string, string)[] extra =
        [
            ("i", "İ"), ("ı", "I"), ("i", "I"), ("ı", "i"), ("İ", "I"), ("ı", "İ"),
            ("ß", "ss"), ("ß", "SS"), ("ẞ", "ss"), ("ẞ", "SS"), ("ẞ", "ß"),
            ("æ", "ae"), ("Æ", "AE"), ("æ", "AE"), ("œ", "oe"), ("Œ", "OE"), ("œ", "OE"),
            ("þ", "th"), ("Þ", "TH"), ("þ", "TH"), ("ð", "d"), ("ð", "dh"), ("Ð", "D"), ("ø", "o"), ("ø", "oe"),
            ("ł", "l"), ("đ", "d"), ("ħ", "h"), ("ŋ", "ng"), ("ĸ", "q"), ("ſ", "s"), ("ſ", "S"), ("ŉ", "'n"),
        ];
        foreach ((string a, string b) in extra)
            foreach (var p in Both(a, b))
                if (seen.Add(p)) yield return p;
    }

    /// <summary>Which pairs ACE says match, by index: all loaded into one table and asked in one query.</summary>
    public static HashSet<int> AceMatches(Collation collation)
    {
        string path = TemporaryDatabase.CreatePath("like-pairs-", ".accdb");
        try
        {
            JetDatabase.Create(path, collation: collation);
            using OleDbConnection ace = AceTestDatabase.Open(path);
            using (OleDbCommand create = ace.CreateCommand())
            {
                create.CommandText = "CREATE TABLE Pairs (Id LONG, V TEXT(10), P TEXT(10))";
                create.ExecuteNonQuery();
            }
            using (OleDbTransaction transaction = ace.BeginTransaction())
            {
                using OleDbCommand insert = ace.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO Pairs (Id, V, P) VALUES (?, ?, ?)";
                OleDbParameter id = insert.Parameters.Add("i", OleDbType.Integer);
                OleDbParameter v = insert.Parameters.Add("v", OleDbType.VarWChar, 10);
                OleDbParameter p = insert.Parameters.Add("p", OleDbType.VarWChar, 10);
                for (int i = 0; i < Pairs.Length; i++)
                {
                    id.Value = i;
                    v.Value = Pairs[i].Value;
                    p.Value = Pairs[i].Pattern;
                    insert.ExecuteNonQuery();
                }
                transaction.Commit();
            }

            using OleDbCommand query = ace.CreateCommand();
            query.CommandText = "SELECT Id FROM Pairs WHERE V LIKE P";
            var matched = new HashSet<int>();
            using OleDbDataReader reader = query.ExecuteReader();
            while (reader.Read()) matched.Add(reader.GetInt32(0));
            return matched;
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Which pairs LibRed says match, asked the same way.</summary>
    public static HashSet<int> LibRedMatches(Collation collation)
    {
        string path = TemporaryDatabase.CreatePath("like-pairs-libred-", ".accdb");
        try
        {
            JetDatabase.Create(path, collation: collation);
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);
            engine.ExecuteNonQuery("CREATE TABLE Pairs (Id LONG, V TEXT(10), P TEXT(10))");
            engine.ExecuteNonQuery("BEGIN TRANSACTION");
            for (int i = 0; i < Pairs.Length; i++)
                engine.ExecuteNonQuery("INSERT INTO Pairs (Id, V, P) VALUES (@i, @v, @p)",
                    new Dictionary<string, object?> { ["@i"] = i, ["@v"] = Pairs[i].Value, ["@p"] = Pairs[i].Pattern });
            engine.ExecuteNonQuery("COMMIT");
            return [.. engine.ExecuteQuery("SELECT Id FROM Pairs WHERE V LIKE P").Rows.Select(r => Convert.ToInt32(r[0]))];
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}