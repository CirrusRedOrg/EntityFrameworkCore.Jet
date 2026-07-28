using LibRed.Sql.Ast;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

// Grammar wiring for the standalone DROP statement:
//   DROP { TABLE table | INDEX index ON table | PROCEDURE procedure | VIEW view }
// Parsing only — the executors follow in their own steps (DROP INDEX, then DROP TABLE, then VIEW/PROC).
public class DropStatementParsingTests
{
    private static SqlStatement Parse(string sql) => new AntlrSqlParser().ParseStatement(sql);

    [Fact]
    public void Drop_table()
    {
        Assert.Equal("Employees", Assert.IsType<DropTableStatement>(Parse("DROP TABLE Employees")).Table);
        Assert.Equal("Order Details", Assert.IsType<DropTableStatement>(Parse("DROP TABLE `Order Details`")).Table);
    }

    [Fact]
    public void Drop_index_on_table()
    {
        var d = Assert.IsType<DropIndexStatement>(Parse("DROP INDEX IX_Name ON `Customers`"));
        Assert.Equal("IX_Name", d.Index);
        Assert.Equal("Customers", d.Table);
    }

    [Fact]
    public void Drop_view_and_procedure()
    {
        Assert.Equal("Invoices", Assert.IsType<DropViewStatement>(Parse("DROP VIEW Invoices")).View);
        Assert.Equal("Ten Most Expensive", Assert.IsType<DropProcedureStatement>(Parse("DROP PROCEDURE `Ten Most Expensive`")).Procedure);
    }

    [Fact]
    public void Trailing_semicolon_is_accepted()
    {
        Assert.IsType<DropTableStatement>(Parse("DROP TABLE T;"));
    }
}
