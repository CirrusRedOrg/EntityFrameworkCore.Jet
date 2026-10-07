using System.Globalization;
using System.Reflection;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// SURVEY: which ANSI code page ACE writes at page 0x3C for each collation — the measurement behind
// JetCodePages. Access-authored fixtures carry the code page of the order's language (936 for Chinese
// Simplified, 1250 for Czech, 0 for Georgian Modern). DAO takes a CP= in its locale string and ignores it,
// deriving the code page from the LANGID exactly as Access does (Probe_cp_parameter; the fixtures agree), which
// is what makes DAO a fair oracle for the orders no fixture covers.
[Collection(AceCollection.Name)]
public class CodePageProbeTest(ITestOutputHelper output)
{
    private const int UseJet = 2;
    private const int Ace12 = 128;

    [Fact]
    public void Probe_cp_parameter()
    {
        object workspace = Workspace();
        foreach (int lcid in (int[])[0x0409, 0x0405, 0x0411, 0x20804, 0x10437, 0x0439])
            foreach (string suffix in (string[])[";CP=1252;COUNTRY=0", ";CP=932;COUNTRY=0", ";COUNTRY=0", ""])
                output.WriteLine($"0x{lcid:X5} {suffix,-20} -> {Create(workspace, $";LANGID=0x{lcid:X4}{suffix}")}");
    }

    [Fact]
    public void Dao_against_every_access_fixture()
    {
        object workspace = Workspace();
        int agree = 0, differ = 0;
        foreach (string path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Data"), "*.accdb").Order())
        {
            int lcid, codePage;
            try
            {
                using var db = JetDatabase.Open(path, readOnly: true);
                (lcid, codePage) = (db.Collation.Lcid, db.CodePage);
            }
            catch (InvalidOperationException) { continue; }   // the encrypted one

            string dao = Create(workspace, $";LANGID=0x{lcid:X4};COUNTRY=0");
            bool same = dao.StartsWith($"cp {codePage} ", StringComparison.Ordinal);
            if (same) agree++; else differ++;
            output.WriteLine($"{Path.GetFileNameWithoutExtension(path),-30} 0x{lcid:X5} access cp {codePage,-5} dao {dao}" +
                             (same ? "" : "   <-- differs"));
        }
        output.WriteLine($"{agree} agree, {differ} differ");
    }

    [Fact]
    public void Sweep_every_creatable_lcid()
    {
        object workspace = Workspace();
        var lcids = new SortedSet<int>();
        foreach (CollatingOrder order in Enum.GetValues<CollatingOrder>())
            foreach (byte version in (byte[])[0, 1])
                foreach (byte sortId in (byte[])[0, 1, 2, 3, 4])
                {
                    var collation = new Collation(order, version, sortId);
                    if (collation.IsIndexKeyEncodable) lcids.Add(collation.Lcid);
                }

        output.WriteLine($"{lcids.Count} distinct LCIDs");
        foreach (int lcid in lcids)
        {
            string icu;
            try { icu = CultureInfo.GetCultureInfo(lcid & 0xFFFF).TextInfo.ANSICodePage.ToString(CultureInfo.InvariantCulture); }
            catch (CultureNotFoundException) { icu = "-"; }
            output.WriteLine($"0x{lcid:X5}\t{Create(workspace, $";LANGID=0x{lcid:X4};COUNTRY=0")}\truntime {icu}");
        }
    }

    private string Create(object workspace, string locale)
    {
        string path = TemporaryDatabase.CreatePath("codepage-", ".accdb");
        try
        {
            object database = Invoke(workspace, "CreateDatabase", path, locale, Ace12)!;
            Invoke(database, "Close");
            using var db = JetDatabase.Open(path, readOnly: true);
            return $"cp {db.CodePage} lcid 0x{db.Collation.Lcid:X5} v{db.Collation.Version}";
        }
        catch (Exception e)
        {
            return "REJECTED " + ((e as TargetInvocationException)?.InnerException?.Message ?? e.Message).Trim();
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static object Workspace()
    {
        AceSurveys.RequireOptIn();
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not available in this process.");
        return Invoke(engine!, "CreateWorkspace", "", "admin", "", UseJet)!;
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);
}
