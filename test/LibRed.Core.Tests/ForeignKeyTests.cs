using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

public class ForeignKeyTests
{
    [Fact]
    public void Reads_relationships_from_the_catalog()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var fk = Assert.Single(db.Catalog.Relationships, r => r.Name == "FK_Orders_Customers");
        Assert.Equal("Orders", fk.Table);
        Assert.Equal("Customers", fk.ReferencedTable);
        Assert.Equal([("CustomerID", "CustomerID")], fk.Columns);
        Assert.True(fk.IsEnforced);
        Assert.False(fk.CascadeUpdate);
        Assert.False(fk.CascadeDelete);

        // A foreign key whose column name differs from the referenced column.
        var shippers = Assert.Single(db.Catalog.Relationships, r => r.Name == "FK_Orders_Shippers");
        Assert.Equal([("ShipVia", "ShipperID")], shippers.Columns);

        // A self-referencing relationship.
        var selfRef = Assert.Single(db.Catalog.Relationships, r => r.Name == "FK_Employees_Employees");
        Assert.Equal("Employees", selfRef.Table);
        Assert.Equal("Employees", selfRef.ReferencedTable);
        Assert.Equal([("ReportsTo", "EmployeeID")], selfRef.Columns);
    }

    [Fact]
    public void Lists_a_tables_outgoing_foreign_keys()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var names = db.Catalog.ForeignKeysOf("Orders").Select(f => f.Name).OrderBy(n => n).ToList();

        Assert.Equal(["FK_Orders_Customers", "FK_Orders_Employees", "FK_Orders_Shippers"], names);
    }

    [Fact]
    public void Reads_cascade_flags()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        // The MSysNavPane relationships cascade on update and delete (grbit 0x1100).
        var cascading = db.Catalog.Relationships.First(r => r.CascadeDelete);
        Assert.True(cascading.CascadeUpdate);
        Assert.True(cascading.CascadeDelete);
    }
}
