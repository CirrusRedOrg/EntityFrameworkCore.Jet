using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>Resolves an <c>IN externaldatabase</c> clause to the database it names.</summary>
internal static class ExternalDatabases
{
    /// <summary>
    /// The Access database <paramref name="reference"/> names, reached from <paramref name="home"/> — which keeps it
    /// open for as long as it is open itself (<see cref="JetDatabase.External"/>) — or <paramref name="home"/> itself
    /// when there is no clause. ACE also reaches dBASE, Paradox, Excel and text files through the clause; LibRed reads
    /// and writes Access files only, so any other database type is refused.
    /// </summary>
    public static JetDatabase Open(JetDatabase home, ExternalDatabase? reference, bool writable = false)
    {
        if (reference is null) return home;
        if (reference.Type.Length > 0 && !string.Equals(reference.Type, "MS Access", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException(
                $"An IN clause naming a '{reference.Type}' database is not supported: LibRed reads Microsoft Access databases only.");
        string file = reference.File
            ?? throw new InvalidOperationException("The IN clause names no database: its connect string has no DATABASE setting.");
        return home.External(file, reference.Password, writable);
    }
}
