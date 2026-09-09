using System.Reflection;
using LibRed.Catalog;
using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE: can DAO's DBEngine.CreateDatabase produce a database with the Access-2010 "General" (v1) sort order?
//
// The v1 weight table is the one piece of the text collation LibRed cannot encode, and the blocker is that no
// v1 database exists to reverse-engineer. Access's UI can make one ("New database sort order" = General), but
// that is a manual step. DAO is the programmatic creator the repo already uses (DaoDatabaseCreator), so the
// question is whether its connect string or database-type argument can select the sort version too — the
// documented string carries only LANGID/CP/COUNTRY, i.e. the locale, not the version.
//
// Each attempt is created, then opened with LibRed to read the sort-order version byte (page 0, 0x71).
public class DaoDatabaseCreationProbeTest(ITestOutputHelper output)
{
    private const int UseJet = 2;

    [Fact]
    public void Probe_dao_created_database_sort_order_versions()
    {
        object? engine = CreateDbEngine(out string progId);
        if (engine is null)
        {
            output.WriteLine("No DAO.DBEngine ProgID could be instantiated — DAO is unavailable in this process.");
            return;
        }

        output.WriteLine($"DAO engine: {progId}");
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", UseJet)!;

        // (label, connect string, database-type argument). 0 = omit the argument entirely.
        (string Label, string Connect, int Type)[] attempts =
        [
            ("default (ACE 12)",          ";LANGID=0x0409;CP=1252;COUNTRY=0", 128),
            ("no type argument",          ";LANGID=0x0409;CP=1252;COUNTRY=0", 0),
            ("Jet 4",                     ";LANGID=0x0409;CP=1252;COUNTRY=0", 64),
            ("general-legacy spelled out", ";LANGID=0x0409;CP=1252;COUNTRY=0;SORTORDER=GeneralLegacy", 128),
            ("general spelled out",       ";LANGID=0x0409;CP=1252;COUNTRY=0;SORTORDER=General", 128),
            ("NLS version requested",     ";LANGID=0x0409;CP=1252;COUNTRY=0;NLSVERSION=1", 128),
        ];

        foreach ((string label, string connect, int type) in attempts)
        {
            string path = TemporaryDatabase.CreatePath("dao-probe-");
            try
            {
                object database = type > 0
                    ? Invoke(workspace, "CreateDatabase", path, connect, type)!
                    : Invoke(workspace, "CreateDatabase", path, connect)!;
                Invoke(database, "Close");

                using var db = JetDatabase.Open(path);
                output.WriteLine($"  {label,-28} -> created; collation version = {db.DefaultCollationVersion}");
            }
            catch (TargetInvocationException ex)
            {
                output.WriteLine($"  {label,-28} -> rejected: {ex.InnerException?.Message.Trim()}");
            }
            catch (Exception ex)
            {
                output.WriteLine($"  {label,-28} -> {ex.GetType().Name}: {ex.Message.Trim()}");
            }
            finally { TemporaryDatabase.Delete(path); }
        }
    }

    // CompactDatabase is the documented way to give an existing database a different collating order: it takes
    // a destination locale. If the sort *version* can be selected anywhere in DAO, this is the other candidate.
    [Fact]
    public void Probe_dao_compact_with_destination_locale()
    {
        object? engine = CreateDbEngine(out string progId);
        if (engine is null) { output.WriteLine("DAO unavailable."); return; }
        output.WriteLine($"DAO engine: {progId}");

        string[] locales =
        [
            ";LANGID=0x0409;CP=1252;COUNTRY=0",
            ";LANGID=0x0409;CP=1252;COUNTRY=0;SORTORDER=General",
            ";LANGID=0x0809;CP=1252;COUNTRY=0",   // en-GB, a different LANGID entirely
        ];

        foreach (string locale in locales)
        {
            string source = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "dao-compact-src-");
            string destination = TemporaryDatabase.CreatePath("dao-compact-dst-");
            try
            {
                Invoke(engine, "CompactDatabase", source, destination, locale);
                using var db = JetDatabase.Open(destination);
                output.WriteLine($"  {locale,-52} -> version {db.DefaultCollationVersion}, lcid {db.DefaultCollationLcid}");
            }
            catch (TargetInvocationException ex)
            {
                output.WriteLine($"  {locale,-52} -> rejected: {ex.InnerException?.Message.Trim()}");
            }
            catch (Exception ex)
            {
                output.WriteLine($"  {locale,-52} -> {ex.GetType().Name}: {ex.Message.Trim()}");
            }
            finally { TemporaryDatabase.Delete(source); TemporaryDatabase.Delete(destination); }
        }
    }

    // Read the sort order of a database created by Access itself (via Access.Application.NewCurrentDatabase),
    // which — unlike DAO — honours the application's "New Database Sort Order" option. Path comes from the
    // LIBRED_V1_PROBE environment variable so the COM automation stays outside the test.
    [Fact]
    public void Probe_sort_order_of_an_access_created_database()
    {
        string? path = Environment.GetEnvironmentVariable("LIBRED_V1_PROBE");
        if (path is null || !File.Exists(path))
        {
            output.WriteLine("LIBRED_V1_PROBE not set to an existing file — nothing to inspect.");
            return;
        }

        using var db = JetDatabase.Open(path);
        output.WriteLine($"{Path.GetFileName(path)}");
        output.WriteLine($"  page-0 sort order : lcid {db.DefaultCollationLcid}, version {db.DefaultCollationVersion}");
        foreach (var table in db.Catalog.Tables.Where(t => t.Columns.Any(c => c.Type is JetDataType.Text or JetDataType.Memo)))
        {
            var collations = table.Columns
                .Where(c => c.Type is JetDataType.Text or JetDataType.Memo)
                .Select(c => $"{c.Collation.Order} v{c.Collation.Version}")
                .Distinct();
            output.WriteLine($"  {table.Name,-24} {string.Join(", ", collations)}");
        }
    }

    private static object? CreateDbEngine(out string progId)
    {
        foreach (int n in new[] { 170, 160, 150, 140, 130, 120 })
        {
            progId = $"DAO.DBEngine.{n}";
            Type? type = Type.GetTypeFromProgID(progId);
            if (type is null) continue;
            try { return Activator.CreateInstance(type); }
            catch (Exception) { /* registered but not instantiable in this bitness */ }
        }
        progId = "(none)";
        return null;
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);
}
