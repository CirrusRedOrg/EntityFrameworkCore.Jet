using System.Buffers.Binary;
using System.Data.OleDb;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Pages;
using Xunit;

namespace LibRed.Core.Tests;

// A complex column created by LibRed against the same column created by DAO, the one route ACE offers, on copies of
// one file: every page byte for byte, the generated GUIDs masked — except MSysObjects' own pages, whose rows carry
// creation times and whose index keys encode the names, so its rows are compared by value. Then ACE reads the
// values LibRed adds.
[Collection(AceCollection.Name)]
public partial class ComplexColumnCreationAccessTests(ITestOutputHelper output)
{
    private const string Locale = ";LANGID=0x0409;CP=1252;COUNTRY=0";
    private const int DaoAttachment = 101;

    // DAO's field type for each complex kind, and the column LibRed builds for it.
    private static readonly Dictionary<int, Func<string, ColumnSpec>> Kinds = new()
    {
        [DaoAttachment] = ColumnSpec.Attachment,
        [102] = n => ColumnSpec.MultiValue(n, JetDataType.Byte),
        [103] = n => ColumnSpec.MultiValue(n, JetDataType.Int16),
        [104] = n => ColumnSpec.MultiValue(n, JetDataType.Int32),
        [105] = n => ColumnSpec.MultiValue(n, JetDataType.Single),
        [106] = n => ColumnSpec.MultiValue(n, JetDataType.Double),
        [107] = n => ColumnSpec.MultiValue(n, JetDataType.Guid),
        [108] = n => ColumnSpec.MultiValue(n, JetDataType.FixedPoint),
        [109] = n => ColumnSpec.MultiValue(n, JetDataType.Text),
    };

    public static TheoryData<int, bool, int, bool> Cases => new()
    {
        { 101, false, 0, false }, { 102, false, 0, false }, { 103, false, 0, false }, { 104, false, 0, false },
        { 105, false, 0, false }, { 106, false, 0, false }, { 107, false, 0, false }, { 108, false, 0, false },
        { 109, false, 0, false },
        { 104, true, 2, false }, { 101, true, 2, false }, { 104, true, 400, false },
        { 109, false, 0, true }, { 101, false, 0, true }, { 109, true, 2, true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Libred_creates_what_dao_creates(int daoType, bool appended, int rows, bool generalV1)
    {
        string owner = appended ? "P" : "C";
        Compare(generalV1, rows, owner, [("X", daoType)],
            dao: db =>
            {
                if (appended) AppendField(db, "P", "X", daoType);
                else CreateTable(db, "C", ("X", daoType));
            },
            libred: db =>
            {
                if (appended) Assert.True(db.AddColumn("P", Kinds[daoType]("X")));
                else db.CreateTable("C", [Kinds[daoType]("X")]);
            });
    }

    // Two complex columns in one new table; and a second appended to rows that already hold ids from a first, with a
    // gap left by a delete.
    [Fact]
    public void Two_complex_columns_in_one_table() =>
        Compare(false, 0, "C", [("X", 104), ("Y", DaoAttachment)],
            dao: db => CreateTable(db, "C", ("X", 104), ("Y", DaoAttachment)),
            libred: db => db.CreateTable("C", [Kinds[104]("X"), Kinds[DaoAttachment]("Y")]));

    [Fact]
    public void A_second_complex_column_on_rows_holding_ids() =>
        Compare(false, 3, "P", [("X", 104), ("Y", DaoAttachment)],
            dao: db => AppendField(db, "P", "Y", DaoAttachment),
            libred: db => Assert.True(db.AddColumn("P", Kinds[DaoAttachment]("Y"))),
            prepare: (engine, seed) =>
            {
                Dao(engine, seed, db => AppendField(db, "P", "X", 104));
                Ole(seed, c =>
                {
                    Exec(c, "DELETE FROM P WHERE ID = 1");
                    Exec(c, "INSERT INTO P (ID, Txt) VALUES (4, 'xxxxxxxxxx')");
                });
            });

    // Names at and past the 64-character limit: the flat table and owner link cut, the value id falling back to the
    // table's first character, and the owner index's column part cut to 31.
    public static TheoryData<string, string> Names => new()
    {
        { "T" + new string('t', 62) + "Z", "X" },
        { "T" + new string('t', 40) + "Z", "Col" + new string('k', 30) + "Z" },
        { "Short", "C" + new string('c', 62) + "Z" },
        { "Spaced Table", "My Col #1" },
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void Generated_names(string table, string column) =>
        Compare(false, 0, table, [(column, 104)],
            dao: db => CreateTable(db, table, (column, 104)),
            libred: db => db.CreateTable(table, [Kinds[104](column)]));

    // The descriptor's ComplexID at 0x0B–0x0E is an Int32 (page-02b §3.2), and DAO takes it from MSysComplexColumns'
    // AutoNumber: raised so the next id needs a second byte (300) and a third (70000, which a 16-bit id would lose),
    // the id LibRed reads is the one DAO assigned, and the value stored under it resolves.
    [Theory]
    [InlineData(300)]
    [InlineData(70000)]
    public void A_complex_id_past_one_byte_reads_as_dao_assigned_it(int expectedId)
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not available in this process.");
        string path = TemporaryDatabase.CreatePath("complex-id-");
        try
        {
            Invoke(Invoke(engine!, "CreateDatabase", path, Locale)!, "Close");
            AceTestDatabase.ReleaseAbandonedComObjects();
            Ole(path, c =>
            {
                Exec(c, "CREATE TABLE P (ID LONG, Txt TEXT(10))");
                Exec(c, "INSERT INTO P (ID, Txt) VALUES (1, 'a')");
            });

            int registryPage, counterOffset, pageSize;
            using (var db = JetDatabase.Open(path, readOnly: true))
            {
                registryPage = db.Catalog.FindTable("MSysComplexColumns")!.DefinitionPage;
                counterOffset = db.Format.TdefLastAutoNumberOffset;
                pageSize = db.Format.PageSize;
            }
            using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                Span<byte> counter = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(counter, expectedId - 1);
                file.Position = (long)registryPage * pageSize + counterOffset;
                file.Write(counter);
            }

            Dao(engine!, path, db =>
            {
                AppendField(db, "P", "X", 104);
                object rs = Invoke(db, "OpenRecordset", "SELECT * FROM P WHERE ID = 1")!;
                Invoke(rs, "Edit");
                object child = Get(Item(Get(rs, "Fields"), "X"), "Value");
                Invoke(child, "AddNew");
                Set(Item(Get(child, "Fields"), "Value"), "Value", 7);
                Invoke(child, "Update");
                Invoke(child, "Close");
                Invoke(rs, "Update");
                Invoke(rs, "Close");
            });

            using (var db = JetDatabase.Open(path, readOnly: true))
            {
                ColumnDef x = db.Catalog.FindTable("P")!.FindColumn("X")!;
                Assert.Equal(expectedId, x.ComplexId);
                int link = Convert.ToInt32(db.OpenTable("P").Rows().Single()[x.Index]);
                Assert.Equal(7, Convert.ToInt32(Assert.Single(db.ReadComplexValues("P", "X", link))[0]));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Seeds a file — DAO-created, or LibRed-created for General v1, which DAO cannot make — with
    /// <c>P (ID, Txt)</c> and <paramref name="rows"/> rows, runs <paramref name="prepare"/> on it, then has DAO and LibRed
    /// each make the change on a copy and compares the two.</summary>
    private void Compare(bool generalV1, int rows, string owner, (string Name, int DaoType)[] columns,
        Action<object> dao, Action<JetDatabase> libred, Action<object, string>? prepare = null)
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not available in this process.");
        string seed = TemporaryDatabase.CreatePath("complex-parity-seed-");
        string daoPath = "", libredPath = "";
        try
        {
            if (generalV1) JetDatabase.Create(seed, collation: Collation.General);
            else
            {
                Invoke(Invoke(engine!, "CreateDatabase", seed, Locale)!, "Close");
                AceTestDatabase.ReleaseAbandonedComObjects();
            }
            Ole(seed, c =>
            {
                Exec(c, "CREATE TABLE P (ID LONG, Txt TEXT(10))");
                for (int i = 1; i <= rows; i++) Exec(c, $"INSERT INTO P (ID, Txt) VALUES ({i}, 'xxxxxxxxxx')");
            });
            prepare?.Invoke(engine!, seed);
            daoPath = TemporaryDatabase.CopyPath(seed, "complex-parity-dao-");
            libredPath = TemporaryDatabase.CopyPath(seed, "complex-parity-lib-");

            Dao(engine!, daoPath, dao);
            using (var lib = JetDatabase.Open(libredPath, readOnly: false)) libred(lib);

            string[] names = [.. columns.Select(c => c.Name)];
            (byte[] daoBytes, HashSet<int> daoCatalog, string daoRows, int pageSize) = Snapshot(daoPath, owner, names);
            (byte[] libBytes, HashSet<int> libCatalog, string libRows, _) = Snapshot(libredPath, owner, names);
            output.WriteLine($"DAO:    {daoRows}");
            output.WriteLine($"LibRed: {libRows}");
            Assert.Equal(daoRows, libRows);

            Assert.Equal(daoBytes.Length, libBytes.Length);
            var differ = new List<int>();
            for (int p = 0; p < daoBytes.Length / pageSize; p++)
                if (!daoCatalog.Contains(p) && !libCatalog.Contains(p)
                    && !daoBytes.AsSpan(p * pageSize, pageSize).SequenceEqual(libBytes.AsSpan(p * pageSize, pageSize)))
                    differ.Add(p);
            foreach (int p in differ)
                output.WriteLine($"page {p} differs: {Describe(daoBytes, libBytes, p, pageSize)}");
            Assert.Empty(differ);

            foreach ((string name, int daoType) in columns) ReadBackThroughAce(libredPath, owner, name, daoType);
        }
        finally
        {
            TemporaryDatabase.Delete(seed);
            if (daoPath != "") TemporaryDatabase.Delete(daoPath);
            if (libredPath != "") TemporaryDatabase.Delete(libredPath);
        }
    }

    // LibRed adds a value to the column it created, and ACE reads it back through the column's sub-field.
    private void ReadBackThroughAce(string path, string owner, string name, int daoType)
    {
        using (var db = JetDatabase.Open(path, readOnly: false))
        {
            Storage.Table table = db.OpenTable(owner);
            if (!table.Rows().Any()) table.Insert(new object?[table.Definition.Columns.Count]);
            ComplexColumn column = db.Catalog.FindComplexColumn(owner, name)!;
            int index = column.OwnerTable.FindColumn(name)!.Index;
            int id = (int)db.OpenTable(owner).Rows().First()[index]!;
            if (column.IsAttachment) db.AddAttachment(column, id, "a.txt", "abc"u8.ToArray());
            else db.AddComplexValue(column, id, column.ValueColumns[0].Type switch
            {
                JetDataType.Byte => (byte)7, JetDataType.Int16 => (short)7, JetDataType.Single => 7f,
                JetDataType.Double => 7d, JetDataType.FixedPoint => 7m, JetDataType.Text => "7",
                JetDataType.Guid => new Guid("00000000-0000-0000-0000-000000000007"),
                _ => (object)7,
            });
        }
        string field = $"[{name}].{(daoType == DaoAttachment ? "FileName" : "Value")}";
        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand select = connection.CreateCommand();
        select.CommandText = $"SELECT {field} FROM [{owner}] WHERE {field} IS NOT NULL";
        object? value = select.ExecuteScalar();
        output.WriteLine($"ACE reads {name}: {value}");
        Assert.Equal(daoType switch
            {
                DaoAttachment => "a.txt",
                107 => "00000000-0000-0000-0000-000000000007",
                _ => "7",
            },
            Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)?.Trim('{', '}'));
    }

    // The file with every generated GUID zeroed; the pages MSysObjects owns; and the catalog rows the creation
    // touched, by value.
    private static (byte[] Bytes, HashSet<int> CatalogPages, string Rows, int PageSize) Snapshot(
        string path, string owner, string[] columns)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using var db = JetDatabase.Open(path, readOnly: true);
        JetFormatBase format = db.Format;
        int pageSize = format.PageSize;
        int msysObjects = db.Catalog.FindTable("MSysObjects")!.DefinitionPage;
        var catalogPages = new HashSet<int>();
        for (int p = 0; p < bytes.Length / pageSize; p++)
        {
            ReadOnlySpan<byte> page = bytes.AsSpan(p * pageSize, pageSize);
            int ownerOffset = PageHeader.ReadType(page) switch
            {
                PageType.DataPage => format.DataOwnerOffset,
                PageType.IntermediateIndexPage or PageType.LeafIndexPage => format.IndexOwnerOffset,
                _ => -1,
            };
            if (ownerOffset >= 0 && BinaryPrimitives.ReadInt32LittleEndian(page[ownerOffset..]) == msysObjects) catalogPages.Add(p);
        }

        TableDefinition ownerTable = db.Catalog.FindTable(owner)!;
        ComplexColumn[] complex = [.. columns.Select(c => db.Catalog.FindComplexColumn(owner, c)!)];
        var guids = new HashSet<string>(ownerTable.Indexes.Select(i => i.Name)
            .Concat(complex.Select(c => c.FlatTable.Name))
            .SelectMany(n => Guid().Matches(n).Select(m => m.Value)));
        foreach (string guid in guids)
        {
            byte[] encoded = Encoding.Unicode.GetBytes(guid);
            for (int at = bytes.AsSpan().IndexOf(encoded); at >= 0; at = bytes.AsSpan().IndexOf(encoded))
                bytes.AsSpan(at, encoded.Length).Clear();
        }

        // Page 0's commit-byte table counts each engine's own commits, and LibRed does not maintain it (page-00 §2.2).
        bytes.AsSpan(format.CommitByteTableOffset, pageSize - format.CommitByteTableOffset).Clear();

        string Mask(string s) => Guid().Replace(s, "<GUID>");
        var objects = db.OpenTable("MSysObjects");
        TableDefinition mo = objects.Definition;
        string Object(string name)
        {
            object?[] r = objects.Rows().Single(r => (string?)r[mo.RequireColumn("Name").Index] == name
                && Convert.ToInt32(r[mo.RequireColumn("Type").Index]) == 1);
            string props = r[mo.RequireColumn("LvProp").Index] is byte[] { Length: > 0 } blob
                ? string.Join(";", PropertyBlob.Read(blob).Select(p => $"[{p.Owner}]{p.Name}={p.Value}")) : "";
            return $"{Mask(name)} Id={r[mo.RequireColumn("Id").Index]} Flags=0x{Convert.ToInt32(r[mo.RequireColumn("Flags").Index]):X8} " +
                   $"Owner={Convert.ToHexString((byte[])r[mo.RequireColumn("Owner").Index]!)} props=[{props}]";
        }
        TableDefinition registry = db.Catalog.FindTable("MSysComplexColumns")!;
        string complexRows = string.Join(" | ", db.OpenTable("MSysComplexColumns").Rows()
            .Select(r => string.Join(",", registry.Columns.Select(c => $"{c.Name}={r[c.Index]}"))));
        return (bytes, catalogPages,
            string.Join(" || ", complex.Select(c => Object(c.FlatTable.Name)).Prepend(Object(owner)).Append(complexRows)),
            pageSize);
    }

    private static string Describe(byte[] a, byte[] b, int page, int pageSize)
    {
        int first = -1, count = 0;
        for (int i = 0; i < pageSize; i++)
            if (a[page * pageSize + i] != b[page * pageSize + i]) { count++; if (first < 0) first = i; }
        return $"type 0x{(ushort)PageHeader.ReadType(a.AsSpan(page * pageSize)):X4}, {count} bytes, first at 0x{first:X3}: " +
               $"DAO {Convert.ToHexString(a, page * pageSize + first, Math.Min(16, pageSize - first))} " +
               $"LibRed {Convert.ToHexString(b, page * pageSize + first, Math.Min(16, pageSize - first))}";
    }

    [GeneratedRegex("[0-9A-F]{32}")]
    private static partial Regex Guid();

    private static void Dao(object engine, string path, Action<object> act)
    {
        object db = Invoke(engine, "OpenDatabase", path)!;
        try { act(db); }
        finally { Invoke(db, "Close"); AceTestDatabase.ReleaseAbandonedComObjects(); }
    }

    private static void Ole(string path, Action<OleDbConnection> act)
    {
        using (OleDbConnection connection = AceTestDatabase.Open(path)) act(connection);
        OleDbConnection.ReleaseObjectPool();
    }

    private static void CreateTable(object db, string name, params (string Name, int Type)[] fields)
    {
        object td = Invoke(db, "CreateTableDef", name)!;
        foreach ((string field, int type) in fields)
            Invoke(Get(td, "Fields"), "Append", Invoke(td, "CreateField", field, type));
        Invoke(Get(db, "TableDefs"), "Append", td);
    }

    private static void AppendField(object db, string table, string name, int type)
    {
        object td = Item(Get(db, "TableDefs"), table);
        Invoke(Get(td, "Fields"), "Append", Invoke(td, "CreateField", name, type));
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object Get(object target, string member) =>
        target.GetType().InvokeMember(member, BindingFlags.GetProperty, null, target, null)!;

    private static void Set(object target, string member, object value) =>
        target.GetType().InvokeMember(member, BindingFlags.SetProperty, null, target, [value]);

    private static object Item(object collection, object key) =>
        collection.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, collection, [key])!;

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);
}
