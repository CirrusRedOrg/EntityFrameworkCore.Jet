using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using Xunit;

namespace LibRed.Engine.Tests;

// A DATETIME2 column's locale field carries only the LOW byte of the database's LANGID - the primary
// language id, with the sublanguage half cleared - where every other type carries the whole thing.
//
// This exists because an en-US-only comparison could not see it. On a 0x0409 database ACE writes 0x0009 for
// Date/Time Extended, which reads as a constant, and it was first implemented as one. It is not: 0x0407
// gives 0x0007, 0x040E gives 0x000E, 0x041D gives 0x001D. The tell is 0x0809 (en-GB), a different LANGID
// that gives 0x0009 too, because it shares en-US's primary id.
//
// So compare the whole descriptor against ACE's under each collating order rather than under one, with a
// Text column alongside as the control - that one certainly carries the full LANGID. Changing a database's
// collating order needs DAO's CompactDatabase, which is the only way to get one that is not en-US here.
[Collection(AceCollection.Name)]
public class DateTime2LocaleAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    public static TheoryData<string, int> Locales => new()
    {
        { ";LANGID=0x0409;CP=1252;COUNTRY=0", 0x0409 },   // en-US
        { ";LANGID=0x0809;CP=1252;COUNTRY=0", 0x0809 },   // en-GB — same primary id as en-US
        { ";LANGID=0x0407;CP=1252;COUNTRY=0", 0x0407 },   // German
        { ";LANGID=0x040E;CP=1250;COUNTRY=0", 0x040E },   // Hungarian
        { ";LANGID=0x041D;CP=1252;COUNTRY=0", 0x041D },   // Swedish
    };

    private const string Ddl = "CREATE TABLE DT (Id LONG, T TEXT(20), V DATETIME2)";

    // The rule, measured on ACE alone so it holds for every order regardless of what LibRed can build.
    [Theory]
    [MemberData(nameof(Locales))]
    public void Ace_gives_date_time_extended_only_the_primary_language_id(string locale, int langId)
    {
        object? engine = DaoEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process.");

        string path = Recollated(engine!, locale);
        try
        {
            CreateThroughAce(path);
            Dictionary<string, byte[]> columns = Descriptors(path, "DT");
            foreach ((string name, byte[] bytes) in columns)
                output.WriteLine($"{locale} {name}: {Convert.ToHexString(bytes)}");

            byte[] text = columns["T"], extended = columns["V"];
            Assert.Equal(langId, text[0x0B] | (text[0x0C] << 8));            // the control: whole LANGID
            Assert.Equal(langId & 0x00FF, extended[0x0B] | (extended[0x0C] << 8));
            Assert.Equal(0, extended[0x0D]);
            Assert.Equal(0, extended[0x0E]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // And that LibRed writes the same bytes. Restricted to the orders whose index keys LibRed can encode:
    // creating any table writes an MSysObjects row, whose Name is indexed, so an unimplemented collation
    // stops LibRed before it reaches a descriptor. German and en-GB are unimplemented today — a gap in the
    // collation work, not in this rule, and the ACE-side theory above covers them meanwhile.
    [Theory]
    [InlineData(";LANGID=0x0409;CP=1252;COUNTRY=0")]
    [InlineData(";LANGID=0x040E;CP=1250;COUNTRY=0")]
    [InlineData(";LANGID=0x041D;CP=1252;COUNTRY=0")]
    public void Libred_writes_the_same_descriptor_as_ace(string locale)
    {
        object? engine = DaoEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process.");

        string ace = Recollated(engine!, locale);
        string libred = Recollated(engine!, locale);
        try
        {
            CreateThroughAce(ace);
            using (var database = JetDatabase.Open(libred, readOnly: false))
                new QueryEngine(database).ExecuteNonQuery(Ddl);

            Dictionary<string, byte[]> expected = Descriptors(ace, "DT");
            Dictionary<string, byte[]> actual = Descriptors(libred, "DT");
            foreach ((string name, byte[] bytes) in expected)
                Assert.Equal(Convert.ToHexString(bytes), Convert.ToHexString(actual[name]));
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    private static void CreateThroughAce(string path)
    {
        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = Ddl;
        try { command.ExecuteNonQuery(); }
        catch (OleDbException ex)
        {
            Assert.Skip($"This ACE build does not accept DATETIME2: {ex.Message.Trim()}");
        }
    }

    /// <summary>A Northwind copy compacted into the given collating order — DAO's documented way to change
    /// one.</summary>
    private static string Recollated(object engine, string locale)
    {
        string source = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "dt2-src-");
        string destination = TemporaryDatabase.CreatePath("dt2-dst-");
        try
        {
            engine.GetType().InvokeMember("CompactDatabase",
                System.Reflection.BindingFlags.InvokeMethod, null, engine, [source, destination, locale]);
            return destination;
        }
        finally { TemporaryDatabase.Delete(source); }
    }

    /// <summary>Every column's descriptor bytes, by name. A three-column table's definition fits one page.
    /// </summary>
    private static Dictionary<string, byte[]> Descriptors(string path, string table)
    {
        int definitionPage;
        using (var database = JetDatabase.Open(path, readOnly: true))
            definitionPage = database.Catalog.FindTable(table)!.DefinitionPage;

        using var channel = PageChannel.Open(path, readOnly: true);
        JetFormatBase format = channel.Format;
        byte[] page = channel.ReadPage(definitionPage).Span.ToArray();

        int dataCount = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(format.TdefIndexCountOffset, 4));
        int colCount = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.TdefColumnCountOffset, 2));
        int start = format.TdefRealIndexBlockOffset + dataCount * format.RealIndexEntrySize;
        int namePos = start + colCount * format.ColumnDescriptorSize;

        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (int i = 0; i < colCount; i++)
        {
            int len = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(namePos, 2));
            string name = System.Text.Encoding.Unicode.GetString(page.AsSpan(namePos + 2, len));
            namePos += 2 + len;
            result[name] = page.AsSpan(start + i * format.ColumnDescriptorSize,
                format.ColumnDescriptorSize).ToArray();
        }
        return result;
    }

    private static object? DaoEngine()
    {
        foreach (int n in new[] { 170, 160, 150, 140, 130, 120 })
        {
            Type? type = Type.GetTypeFromProgID($"DAO.DBEngine.{n}");
            if (type is null) continue;
            try { return Activator.CreateInstance(type); } catch (Exception) { }
        }
        return null;
    }
}
