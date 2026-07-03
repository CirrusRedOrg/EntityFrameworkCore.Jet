using LibRed;
using LibRed.Catalog;
using LibRed.IO;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Verifies the byte-faithful relationship logical-index linkage LibRed writes into both tables' TDEFs
/// (§3.6), matching what ACE writes for a minimal parent/child pair: the child's FK-column index carries
/// an outgoing (fkType 0x02) block pointing at the parent page, and the parent gains an incoming
/// (fkType 0x01) block pointing back — the two cross-referenced by index_num.
/// </summary>
public class ForeignKeyLinkageTests
{
    private readonly record struct Block(int Num, int Num2, byte FkType, uint FkNum, int FkPage, byte Upd, byte Del, byte Type, string Name);

    private static List<Block> ReadLogicalBlocks(PageChannel ch, int page)
    {
        var fmt = ch.Format;
        var buf = ch.ReadPage(page);
        int dataCount = buf.ReadInt32(fmt.TdefIndexCountOffset);
        int logicalCount = buf.ReadInt32(fmt.TdefRealIndexCountOffset);
        int cols = buf.ReadUInt16(fmt.TdefColumnCountOffset);
        int pos = fmt.TdefRealIndexBlockOffset + dataCount * fmt.RealIndexEntrySize + cols * fmt.ColumnDescriptorSize;
        for (int i = 0; i < cols; i++) pos += 2 + buf.ReadUInt16(pos);
        int infoStart = pos + dataCount * 52;
        int namePos = infoStart + logicalCount * 28;
        var names = new string[logicalCount];
        for (int i = 0; i < logicalCount; i++) { int len = buf.ReadUInt16(namePos); namePos += 2; names[i] = System.Text.Encoding.Unicode.GetString(buf.Slice(namePos, len)); namePos += len; }
        var blocks = new List<Block>(logicalCount);
        for (int i = 0; i < logicalCount; i++)
        {
            int b = infoStart + i * 28;
            blocks.Add(new Block(buf.ReadInt32(b + 4), buf.ReadInt32(b + 8), buf.ReadByte(b + 0x0C),
                (uint)buf.ReadInt32(b + 0x0D), buf.ReadInt32(b + 0x11), buf.ReadByte(b + 0x15),
                buf.ReadByte(b + 0x16), buf.ReadByte(b + 0x17), names[i]));
        }
        return blocks;
    }

    [Fact]
    public void Child_and_parent_carry_cross_linked_relationship_blocks()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fklink-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            int parentPage, childPage;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("P1", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["Id"]);
                db.CreateTable("C1",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("Pid", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"],
                    relationships: [new RelationshipSpec("FKrel", "P1", [("Pid", "Id")], true, false, false)]);
                parentPage = db.Catalog.FindTable("P1")!.DefinitionPage;
                childPage = db.Catalog.FindTable("C1")!.DefinitionPage;
            }

            using var ch = PageChannel.Open(path, readOnly: true);
            var child = ReadLogicalBlocks(ch, childPage);
            var parent = ReadLogicalBlocks(ch, parentPage);

            // Child: an outgoing relationship block named FKrel, pointing at the parent page, type foreign.
            Block outgoing = child.Single(b => b.Name == "FKrel");
            Assert.Equal(0x02, outgoing.FkType);
            Assert.Equal(0x02, outgoing.Type);
            Assert.Equal(parentPage, outgoing.FkPage);

            // Parent: an incoming relationship block (hidden ".r" name), pointing back at the child page.
            Block incoming = parent.Single(b => b.FkType == 0x01);
            Assert.Equal(0x02, incoming.Type);
            Assert.Equal(childPage, incoming.FkPage);
            Assert.StartsWith(".r", incoming.Name);

            // The two ends cross-reference by index_num (each block's fkNum is the other's num).
            Assert.Equal((uint)incoming.Num, outgoing.FkNum);
            Assert.Equal((uint)outgoing.Num, incoming.FkNum);

            // The parent's own primary key is untouched (still present, not a relationship).
            Assert.Contains(parent, b => b.Type == 0x01 && b.FkType == 0x00);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // FOREIGN KEY NO INDEX: ACE flags the child's outgoing block 0x03 instead of 0x02; the parent
    // incoming block is unchanged (0x01).
    [Fact]
    public void No_index_relationship_flags_the_child_block_0x03()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fknoidx-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("P3", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["Id"]);
                db.CreateTable("C3",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("Pid", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"],
                    relationships: [new RelationshipSpec("FKni", "P3", [("Pid", "Id")], true, false, false, NoIndex: true)]);
            }
            using var ch = PageChannel.Open(path, readOnly: true);
            var child = ReadLogicalBlocks(ch, new JetCatalog(ch).FindTable("C3")!.DefinitionPage);
            var parent = ReadLogicalBlocks(ch, new JetCatalog(ch).FindTable("P3")!.DefinitionPage);
            Assert.Equal(0x03, child.Single(b => b.Name == "FKni").FkType);
            Assert.Equal(0x01, parent.Single(b => b.FkType == 0x01).FkType); // parent side unchanged
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // A self-referencing foreign key (the table is its own parent) hosts BOTH ends in its one TDEF —
    // an outgoing 0x02 block and an incoming 0x01 block, each with fkPage = the table's own page,
    // cross-referenced by index_num. (Verified byte-for-byte against an ACE-created self-reference.)
    [Fact]
    public void Self_referencing_relationship_hosts_both_ends_in_one_table()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fkself-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            int page;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("S",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("Mgr", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"],
                    relationships: [new RelationshipSpec("fk", "S", [("Mgr", "Id")], true, false, false)]);
                page = db.Catalog.FindTable("S")!.DefinitionPage;
            }
            using var ch = PageChannel.Open(path, readOnly: true);
            var blocks = ReadLogicalBlocks(ch, page);

            Block outgoing = blocks.Single(b => b.Name == "fk");
            Block incoming = blocks.Single(b => b.FkType == 0x01);
            Assert.Equal(0x02, outgoing.FkType);
            Assert.Equal(page, outgoing.FkPage);   // references its own table
            Assert.Equal(page, incoming.FkPage);
            Assert.StartsWith(".r", incoming.Name);
            Assert.Equal((uint)incoming.Num, outgoing.FkNum); // cross-linked within the one table
            Assert.Equal((uint)outgoing.Num, incoming.FkNum);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Cascade_actions_are_written_on_both_ends()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fkcasc-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("P2", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["Id"]);
                db.CreateTable("C2",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("Pid", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"],
                    relationships: [new RelationshipSpec("FKcas", "P2", [("Pid", "Id")], true, CascadeUpdate: true, CascadeDelete: true)]);
            }
            using var ch = PageChannel.Open(path, readOnly: true);
            var child = ReadLogicalBlocks(ch, new JetCatalog(ch).FindTable("C2")!.DefinitionPage);
            var parent = ReadLogicalBlocks(ch, new JetCatalog(ch).FindTable("P2")!.DefinitionPage);

            Block outgoing = child.Single(b => b.Name == "FKcas");
            Assert.Equal(0x01, outgoing.Upd);   // cascade update
            Assert.Equal(0x01, outgoing.Del);   // cascade delete
            Block incoming = parent.Single(b => b.FkType == 0x01);
            Assert.Equal(0x01, incoming.Upd);
            Assert.Equal(0x01, incoming.Del);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
