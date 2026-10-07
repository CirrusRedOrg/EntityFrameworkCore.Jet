using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
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
    /// <summary>The table's logical index-info blocks, each with its name, in the order the TDEF stores them.</summary>
    private static List<TableDefinition.LogicalIndexSpec> ReadLogicalBlocks(PageChannel ch, int page)
    {
        JetFormatBase fmt = ch.Format;
        (PageBuffer buf, _) = TableDefinition.ReadChain(ch, page);
        TableDefinition.Regions regions = TableDefinition.Regions.Of(buf.Span, fmt);
        var blocks = new List<TableDefinition.LogicalIndexSpec>(regions.LogicalCount);
        int namePos = regions.IndexNames;
        for (int i = 0; i < regions.LogicalCount; i++)
        {
            (string name, namePos) = TableDefinition.ReadName(buf, namePos, "logical index", fmt);
            blocks.Add(TableDefinition.ReadInfoBlock(
                buf.Span.Slice(regions.InfoBlocks + i * fmt.IndexInfoBlockSize, fmt.IndexInfoBlockSize), fmt, name));
        }
        return blocks;
    }

    [Fact]
    public void Child_and_parent_carry_cross_linked_relationship_blocks()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "fklink-");
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
            TableDefinition.LogicalIndexSpec outgoing = child.Single(b => b.Name == "FKrel");
            Assert.Equal(ForeignKeyType.Outgoing, outgoing.FkType);
            Assert.Equal(IndexInfoType.Foreign, outgoing.Type);
            Assert.Equal(parentPage, outgoing.FkTablePage);

            // Parent: an incoming relationship block (hidden ".r" name), pointing back at the child page.
            TableDefinition.LogicalIndexSpec incoming = parent.Single(b => b.FkType == ForeignKeyType.Incoming);
            Assert.Equal(IndexInfoType.Foreign, incoming.Type);
            Assert.Equal(childPage, incoming.FkTablePage);
            Assert.StartsWith(".r", incoming.Name);

            // The two ends cross-reference by index_num (each block's fkNum is the other's num).
            Assert.Equal((uint)incoming.Number, outgoing.FkNumber);
            Assert.Equal((uint)outgoing.Number, incoming.FkNumber);

            // The parent's own primary key is untouched (still present, not a relationship).
            Assert.Contains(parent, b => b.Type == IndexInfoType.Primary && b.FkType == ForeignKeyType.None);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // FOREIGN KEY NO INDEX: ACE flags the child's outgoing block 0x03 instead of 0x02; the parent
    // incoming block is unchanged (0x01).
    [Fact]
    public void No_index_relationship_flags_the_child_block_0x03()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "fknoidx-");
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
            Assert.Equal(ForeignKeyType.OutgoingNoIndex, child.Single(b => b.Name == "FKni").FkType);
            Assert.Single(parent, b => b.FkType == ForeignKeyType.Incoming); // parent side unchanged
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A self-referencing foreign key (the table is its own parent) hosts BOTH ends in its one TDEF —
    // an outgoing 0x02 block and an incoming 0x01 block, each with fkPage = the table's own page,
    // cross-referenced by index_num. (Verified byte-for-byte against an ACE-created self-reference.)
    [Fact]
    public void Self_referencing_relationship_hosts_both_ends_in_one_table()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "fkself-");
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

            TableDefinition.LogicalIndexSpec outgoing = blocks.Single(b => b.Name == "fk");
            TableDefinition.LogicalIndexSpec incoming = blocks.Single(b => b.FkType == ForeignKeyType.Incoming);
            Assert.Equal(ForeignKeyType.Outgoing, outgoing.FkType);
            Assert.Equal(page, outgoing.FkTablePage);   // references its own table
            Assert.Equal(page, incoming.FkTablePage);
            Assert.StartsWith(".r", incoming.Name);
            Assert.Equal((uint)incoming.Number, outgoing.FkNumber); // cross-linked within the one table
            Assert.Equal((uint)outgoing.Number, incoming.FkNumber);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Cascade_actions_are_written_on_both_ends()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "fkcasc-");
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

            TableDefinition.LogicalIndexSpec outgoing = child.Single(b => b.Name == "FKcas");
            Assert.Equal(RelationshipAction.Cascade, outgoing.UpdateAction);   // cascade update
            Assert.Equal(RelationshipAction.Cascade, outgoing.DeleteAction);   // cascade delete
            TableDefinition.LogicalIndexSpec incoming = parent.Single(b => b.FkType == ForeignKeyType.Incoming);
            Assert.Equal(RelationshipAction.Cascade, incoming.UpdateAction);
            Assert.Equal(RelationshipAction.Cascade, incoming.DeleteAction);

            // And the catalog reads them back, on both ends.
            var catalog = new JetCatalog(ch);
            LogicalIndexDef childEnd = catalog.FindTable("C2")!.LogicalIndexes.Single(l => l.Name == "FKcas");
            LogicalIndexDef parentEnd = catalog.FindTable("P2")!.LogicalIndexes.Single(l => l.IsIncomingRelationship);
            Assert.Equal((RelationshipAction.Cascade, RelationshipAction.Cascade), (childEnd.UpdateAction, childEnd.DeleteAction));
            Assert.Equal((RelationshipAction.Cascade, RelationshipAction.Cascade), (parentEnd.UpdateAction, parentEnd.DeleteAction));
            Assert.Equal((RelationshipAction.NotRelationship, RelationshipAction.NotRelationship), (catalog.FindTable("C2")!.LogicalIndexes.Single(l => l.IsPrimaryKey).UpdateAction,
                                        catalog.FindTable("C2")!.LogicalIndexes.Single(l => l.IsPrimaryKey).DeleteAction));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ACE cascades by the child's relationship block, not by MSysRelationships.grbit: made to disagree, a delete
    // grbit calls cascading is refused and one grbit calls plain cascades (measured against ACE). So when the two
    // disagree, the relationship LibRed reads must carry the block's actions.
    [Fact]
    public void Cascade_actions_are_read_from_the_relationship_block_not_grbit()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "fkblock-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("P4", [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["Id"]);
                db.CreateTable("C4",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("Pid", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"],
                    relationships: [new RelationshipSpec("FKblock", "P4", [("Pid", "Id")], true, CascadeUpdate: true, CascadeDelete: true)]);
            }

            // grbit keeps cascade update and delete; the child's block now says no update action and SET NULL.
            using (var ch = PageChannel.Open(path, readOnly: false))
            {
                JetFormatBase fmt = ch.Format;
                int page = new JetCatalog(ch).FindTable("C4")!.DefinitionPage;
                List<TableDefinition.LogicalIndexSpec> blocks = ReadLogicalBlocks(ch, page);
                int i = blocks.FindIndex(b => b.Name == "FKblock");
                byte[] buf = ch.ReadPage(page).Span.ToArray();
                Span<byte> block = buf.AsSpan(TableDefinition.Regions.Of(buf, fmt).InfoBlocks + i * fmt.IndexInfoBlockSize, fmt.IndexInfoBlockSize);
                TableDefinition.WriteInfoBlock(block, fmt, blocks[i] with
                {
                    UpdateAction = RelationshipAction.NoCascade,
                    DeleteAction = RelationshipAction.SetNull,
                });
                ch.WritePage(page, buf);
            }

            using var reopened = JetDatabase.Open(path);
            ForeignKey fk = reopened.Catalog.Relationships.Single(r => r.Name == "FKblock");
            Assert.False(fk.CascadeUpdate);
            Assert.False(fk.CascadeDelete);
            Assert.True(fk.DeleteSetNull);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}