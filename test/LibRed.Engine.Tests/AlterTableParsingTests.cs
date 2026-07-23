using LibRed.Sql.Ast;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

// Step 1 of ALTER TABLE: parsing only. Each Access ALTER TABLE form maps to the right AST action; execution
// of each action follows in its own step.
public class AlterTableParsingTests
{
    private static AlterTableStatement Parse(string sql)
        => Assert.IsType<AlterTableStatement>(new AntlrSqlParser().ParseStatement(sql));

    [Fact]
    public void Add_column_with_and_without_the_column_keyword()
    {
        var a = Assert.IsType<AddColumnAction>(Parse("ALTER TABLE Employees ADD COLUMN Notes TEXT(25)").Action);
        Assert.Equal("Notes", a.Column.Name);
        Assert.Equal("TEXT", a.Column.TypeName, ignoreCase: true);
        Assert.Equal(25, a.Column.Size);

        // EF omits the COLUMN keyword.
        var b = Assert.IsType<AddColumnAction>(Parse("ALTER TABLE `People` ADD `Alias` LONGCHAR NOT NULL").Action);
        Assert.Equal("Alias", b.Column.Name);
        Assert.True(b.Column.NotNull);
    }

    [Fact]
    public void Add_foreign_key_constraint()
    {
        var a = Assert.IsType<AddForeignKeyAction>(Parse(
            "ALTER TABLE `Products` ADD CONSTRAINT `FK_Products_Categories` " +
            "FOREIGN KEY (`CategoryID`) REFERENCES `Categories` (`CategoryID`)").Action);
        Assert.Equal("FK_Products_Categories", a.ForeignKey.Name);
        Assert.Equal(["CategoryID"], a.ForeignKey.Columns);
        Assert.Equal("Categories", a.ForeignKey.ReferencedTable);
        Assert.Equal(["CategoryID"], a.ForeignKey.ReferencedColumns);
    }

    [Fact]
    public void Add_primary_key_and_unique_constraints()
    {
        var pk = Assert.IsType<AddPrimaryKeyAction>(Parse(
            "ALTER TABLE `Orders` ADD CONSTRAINT `PK_Orders` PRIMARY KEY (`OrderID`)").Action);
        Assert.Equal("PK_Orders", pk.Name);
        Assert.Equal(["OrderID"], pk.Columns);

        var uq = Assert.IsType<AddUniqueAction>(Parse(
            "ALTER TABLE `Customers` ADD CONSTRAINT `UQ_Email` UNIQUE (`Email`)").Action);
        Assert.Equal("UQ_Email", uq.Unique.Name);
        Assert.Equal(["Email"], uq.Unique.Columns);
    }

    [Fact]
    public void Alter_column_changes_type()
    {
        var a = Assert.IsType<AlterColumnAction>(Parse("ALTER TABLE Employees ALTER COLUMN ZipCode TEXT(10)").Action);
        Assert.Equal("ZipCode", a.Field);
        Assert.Equal("TEXT", a.TypeName, ignoreCase: true);
        Assert.Equal(10, a.Size);
    }

    [Fact]
    public void Drop_column_and_drop_constraint()
    {
        var dc = Assert.IsType<DropColumnAction>(Parse("ALTER TABLE Employees DROP COLUMN Notes").Action);
        Assert.Equal("Notes", dc.Field);

        var dk = Assert.IsType<DropConstraintAction>(Parse("ALTER TABLE `Products` DROP CONSTRAINT `FK_Products_Categories`").Action);
        Assert.Equal("FK_Products_Categories", dk.Name);
    }

    // The three renames are one ALTER TABLE family, so the table is always the statement's subject. These are
    // the exact forms JetMigrationsSqlGenerator emits for Rename{Table,Column,Index}Operation.
    [Fact]
    public void Rename_table_column_and_index()
    {
        var t = Parse("ALTER TABLE `People` RENAME TO `Person`");
        Assert.Equal("People", t.Table);
        Assert.Equal("Person", Assert.IsType<RenameTableAction>(t.Action).NewName);

        var c = Parse("ALTER TABLE `Table1` RENAME COLUMN `Foo` TO `Bar`");
        Assert.Equal("Table1", c.Table);
        var rc = Assert.IsType<RenameColumnAction>(c.Action);
        Assert.Equal("Foo", rc.Field);
        Assert.Equal("Bar", rc.NewName);

        var i = Parse("ALTER TABLE `Orders` RENAME INDEX `IX_Old` TO `IX_New`");
        Assert.Equal("Orders", i.Table);
        var ri = Assert.IsType<RenameIndexAction>(i.Action);
        Assert.Equal("IX_Old", ri.Index);
        Assert.Equal("IX_New", ri.NewName);
    }

    // Undelimited and bracket-delimited identifiers parse the same way (EF always backticks, but hand-written
    // and ADOX-style SQL uses these), and the keywords are case-insensitive like the rest of the grammar.
    [Fact]
    public void Rename_accepts_bare_and_bracketed_identifiers_and_is_case_insensitive()
    {
        Assert.Equal("Person", Assert.IsType<RenameTableAction>(Parse("alter table People rename to Person").Action).NewName);

        var rc = Assert.IsType<RenameColumnAction>(Parse("ALTER TABLE [Table1] Rename Column [Foo] To [Bar]").Action);
        Assert.Equal("Foo", rc.Field);
        Assert.Equal("Bar", rc.NewName);
    }
}
