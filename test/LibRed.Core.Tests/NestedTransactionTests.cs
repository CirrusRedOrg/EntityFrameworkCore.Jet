using LibRed.Catalog;
using LibRed.Tests.Shared;
using Xunit;

namespace LibRed.Core.Tests;

public class NestedTransactionTests
{
    private static readonly ColumnSpec[] Schema =
    [
        new("Id", JetDataType.Int32, 4, IsFixedLength: true),
    ];

    private static TemporaryDatabase Fresh()
    {
        var temp = TemporaryDatabase.CopyOf(TestDatabases.NorthwindAccdb, "nested");
        JetDatabase db = temp.Open();
        db.CreateTable("NestedTxn", Schema, primaryKey: ["Id"]);
        return temp;
    }

    private static int[] Values(JetDatabase db)
        => db.OpenTable("NestedTxn").Rows().Select(r => Convert.ToInt32(r[0])).Order().ToArray();

    [Fact]
    public void Committing_inner_and_outer_levels_keeps_both_writes()
    {
        using var temp = Fresh();
        JetDatabase db = temp.Database;
        db.BeginNested();
        db.OpenTable("NestedTxn").Insert([1]);
        db.BeginNested();
        db.OpenTable("NestedTxn").Insert([2]);

        Assert.True(db.InTransaction);
        Assert.Equal(2, db.TransactionDepth);

        db.CommitNested();
        Assert.Equal(1, db.TransactionDepth);
        db.CommitNested();

        Assert.False(db.InTransaction);
        Assert.Equal(0, db.TransactionDepth);
        Assert.Equal([1, 2], Values(db));
    }

    [Fact]
    public void Rolling_back_inner_level_keeps_outer_write_and_transaction_open()
    {
        using var temp = Fresh();
        JetDatabase db = temp.Database;
        db.BeginNested();
        db.OpenTable("NestedTxn").Insert([1]);
        db.BeginNested();
        db.OpenTable("NestedTxn").Insert([2]);

        db.RollbackNested();

        Assert.True(db.InTransaction);
        Assert.Equal(1, db.TransactionDepth);
        Assert.Equal([1], Values(db));

        db.CommitNested();
        Assert.Equal([1], Values(db));
    }

    [Fact]
    public void Rollback_all_discards_every_level_and_resets_controller()
    {
        using var temp = Fresh();
        JetDatabase db = temp.Database;
        db.BeginNested();
        db.OpenTable("NestedTxn").Insert([1]);
        db.BeginNested();
        db.OpenTable("NestedTxn").Insert([2]);

        db.RollbackAll();

        Assert.False(db.InTransaction);
        Assert.Equal(0, db.TransactionDepth);
        Assert.Empty(Values(db));

        db.RollbackAll(); // idempotent at depth zero
        Assert.Equal(0, db.TransactionDepth);
    }

    [Fact]
    public void Outermost_rollback_invalidates_catalog_entries_created_in_transaction()
    {
        using var temp = Fresh();
        JetDatabase db = temp.Database;
        db.BeginNested();
        db.CreateTable("Transient", Schema);
        Assert.NotNull(db.Catalog.FindTable("Transient"));

        db.RollbackNested();

        Assert.Null(db.Catalog.FindTable("Transient"));
        var error = Assert.Throws<ArgumentException>(() => db.OpenTable("Transient"));
        Assert.Equal("name", error.ParamName);
    }

    [Fact]
    public void Commit_and_rollback_without_a_nested_transaction_are_rejected()
    {
        using var temp = Fresh();
        JetDatabase db = temp.Database;
        Assert.Throws<InvalidOperationException>(() => db.CommitNested());
        Assert.Throws<InvalidOperationException>(() => db.RollbackNested());
        Assert.Equal(0, db.TransactionDepth);
        Assert.False(db.InTransaction);
    }
}
