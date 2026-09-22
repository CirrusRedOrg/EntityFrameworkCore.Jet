using System.Reflection;
using System.Text;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// LibRed attaching a file to an attachment column, with ACE asked to read it back through DAO — the only
/// surface that exposes a complex column's values at all.
/// </summary>
[Collection(AceCollection.Name)]
public class ComplexAttachmentWriteAccessTests(ITestOutputHelper output)
{
    private const string Source = @"D:\exampleaccdb\complex1.accdb";

    // Both sides of the documented compression rule: a .txt is absent from the natively-compressed list so
    // Access deflates it, while a .png is on the list and is stored as it is. ACE has to extract either.
    [Theory]
    [InlineData("libred-note.txt", true)]
    [InlineData("libred-note.png", false)]
    public void Ace_reads_an_attachment_libred_wrote(string fileName, bool expectCompressed)
    {
        if (!File.Exists(Source)) { output.WriteLine($"Skipped: {Source} not present."); return; }
        object? engine = CreateDbEngine(out string progId);
        if (engine is null) { output.WriteLine("Skipped: DAO unavailable."); return; }

        byte[] content = Encoding.UTF8.GetBytes("LibRed wrote this attachment.\n" + new string('x', 5000));
        string FileName = fileName;
        Assert.Equal(expectCompressed, ComplexAttachment.Compresses(Path.GetExtension(fileName).TrimStart('.')));

        string path = TemporaryDatabase.CopyPath(Source, "attach-write-");
        try
        {
            int recordKey, complexId;
            string columnName;

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                ComplexColumn column = db.Catalog.ComplexColumns
                    .First(c => c.OwnerTable.Name == "Table1" && c.IsAttachment);
                columnName = column.ColumnName;
                TableDef owner = column.OwnerTable;
                ColumnDef inRow = owner.FindColumn(column.ColumnName)!;

                object?[] record = db.OpenTable("Table1").Rows().First();
                complexId = (int)record[inRow.Index]!;
                recordKey = Convert.ToInt32(record[owner.Columns[0].Index]);

                int before = db.ReadComplexValues(column, complexId).Count;
                db.AddAttachment(column, complexId, FileName, content);
                Assert.Equal(before + 1, db.ReadComplexValues(column, complexId).Count);

                // A record may not hold the same file twice — ACE refuses it, so LibRed must too.
                Assert.ThrowsAny<Exception>(() => db.AddAttachment(column, complexId, FileName, content));
                output.WriteLine($"wrote {FileName} ({content.Length} bytes) to Table1.{columnName} record {recordKey}");
            }

            // LibRed reads its own value back through the payload decoder.
            using (var db = JetDatabase.Open(path))
            {
                ComplexColumn column = db.Catalog.FindComplexColumn("Table1", columnName)!;
                int name = column.ValueColumns.ToList().FindIndex(c => c.Name == "FileName");
                int data = column.ValueColumns.ToList().FindIndex(c => c.Name == "FileData");
                object?[] written = db.ReadComplexValues(column, complexId).Single(v => (string?)v[name] == FileName);
                byte[] blob = (byte[])written[data]!;

                // The outer flag says which form was stored, and it must follow the documented rule.
                Assert.Equal(expectCompressed ? 1u : 0u, BitConverter.ToUInt32(blob, 0));
                output.WriteLine($"stored {(expectCompressed ? "deflated" : "raw")}: blob {blob.Length} bytes for {content.Length} of content");

                ComplexAttachment unwrapped = ComplexAttachment.Unwrap(blob);
                Assert.Equal(Path.GetExtension(FileName).TrimStart('.'), unwrapped.Extension);
                Assert.Equal(content, unwrapped.Content);
            }

            // And the real question: does ACE agree the attachment is there, with the right bytes?
            object dao = Invoke(engine, "OpenDatabase", path)!;
            try
            {
                object rs = Invoke(dao, "OpenRecordset",
                    $"SELECT * FROM [Table1] WHERE [{ /* pk */ "ID"}] = {recordKey}")!;
                try
                {
                    object child = Get(Fields(rs, columnName), "Value")!;
                    var seen = new List<string>();
                    long size = -1;
                    string extracted = Path.Combine(Path.GetTempPath(), $"libred-extract-{Guid.NewGuid():N}.txt");
                    while (!Convert.ToBoolean(Get(child, "EOF")))
                    {
                        string name = (string)Get(Fields(child, "FileName"), "Value")!;
                        seen.Add(name);
                        if (name == FileName)
                        {
                            size = Convert.ToInt64(Get(Fields(child, "FileData"), "FieldSize"));
                            // The real proof: ACE extracting the file it was handed.
                            Invoke(Fields(child, "FileData"), "SaveToFile", extracted);
                        }
                        Invoke(child, "MoveNext");
                    }
                    output.WriteLine($"ACE sees attachments: [{string.Join(", ", seen)}]  FileData size {size}");
                    Assert.Contains(FileName, seen);

                    Assert.True(File.Exists(extracted), "ACE did not extract the attachment.");
                    try
                    {
                        byte[] saved = File.ReadAllBytes(extracted);
                        output.WriteLine($"ACE extracted {saved.Length} bytes (LibRed wrote {content.Length})");
                        Assert.Equal(content, saved);
                    }
                    finally { File.Delete(extracted); }
                }
                finally { Invoke(rs, "Close"); }
            }
            finally { Invoke(dao, "Close"); }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static object Fields(object target, string name) =>
        target.GetType().InvokeMember("Fields", BindingFlags.GetProperty, null, target, [name])!;

    private static object? Get(object target, string member) =>
        target.GetType().InvokeMember(member, BindingFlags.GetProperty, null, target, null);

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);

    private static object? CreateDbEngine(out string progId)
    {
        AceTestDatabase.ReleaseAbandonedComObjects();
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
}
