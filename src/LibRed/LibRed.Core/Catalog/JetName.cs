namespace LibRed.Catalog;

/// <summary>
/// Validates user-supplied object names (table, column, index, constraint) against Jet/ACE's naming rules.
/// LibRed writes the file format directly, bypassing ACE's DDL parser, so without this it can emit names ACE
/// can't use — both verified against ACE (OLE DB) 2026-07-12:
/// <list type="bullet">
///   <item>A name longer than <see cref="MaxLength"/> makes ACE reject the WHOLE file — a column name of 65+ is
///   "Unrecognized database format"; a table name of 65+ is "Unspecified error". Not just the object — the DB.</item>
///   <item>The characters <c>. ! ` [ ]</c> make the name unreferenceable in SQL (both bracket- and backtick-quoted
///   <c>SELECT</c> fail on ACE), matching Access's documented forbidden-character rule. ACE stores/round-trips
///   the other tested special characters (quotes, #, %, &amp;, inner and trailing spaces, unicode).</item>
///   <item>ACE's DDL refuses a leading space and a control character — a tab and U+0001 measured, for tables and
///   columns: <i>"' Lead' is not a valid name."</i> (verified 2026-09-24). Access documents the control-character
///   rule as the whole of U+0000–U+001F.</item>
/// </list>
/// Only call this on names the caller supplied — NOT on LibRed's internally-generated hidden names (e.g. the
/// <c>.rN</c> incoming-relationship index names, which legitimately start with a period).
/// </summary>
public static class JetName
{
    /// <summary>Jet/ACE object-name length cap. Exceeding it corrupts the file from ACE's view.</summary>
    public const int MaxLength = 64;

    // Verified to break SQL referencing on ACE (period, exclamation, grave accent, both brackets — Access's
    // documented set). `]` is the one that truly breaks bracket-quoting; the rest and `[` are included to match
    // Access and because a stray bracket is never intentional.
    private static readonly char[] Forbidden = ['.', '!', '`', '[', ']'];

    /// <summary>Throws <see cref="ArgumentException"/> if <paramref name="name"/> is empty, too long, or contains
    /// a character ACE forbids in an object name. <paramref name="kind"/> names the object type for the message.</summary>
    public static void Validate(string name, string kind = "name")
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException($"A {kind} cannot be empty.", nameof(name));

        if (name.Length > MaxLength)
            throw new ArgumentException(
                $"The {kind} '{name}' is {name.Length} characters long; an object name can be at most {MaxLength} " +
                "characters.", nameof(name));

        int i = name.IndexOfAny(Forbidden);
        if (i >= 0)
            throw new ArgumentException(
                $"The {kind} '{name}' contains '{name[i]}', which is not allowed in an object name " +
                "(forbidden characters: . ! ` [ ]).", nameof(name));

        if (name[0] == ' ')
            throw new ArgumentException($"The {kind} '{name}' begins with a space, which an object name cannot.", nameof(name));

        int control = name.AsSpan().IndexOfAnyInRange('\u0000', '\u001F');
        if (control >= 0)
            throw new ArgumentException(
                $"The {kind} '{name}' contains control character U+{(int)name[control]:X4}, which is not allowed "
                + "in an object name.", nameof(name));
    }
}