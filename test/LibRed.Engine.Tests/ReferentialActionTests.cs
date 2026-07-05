using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Referential integrity on UPDATE/DELETE + the ON UPDATE/ON DELETE actions, all verified against ACE:
// NO ACTION rejects, CASCADE propagates, ON DELETE SET NULL nulls the child FK (Jet has no ON UPDATE SET NULL).
public class ReferentialActionTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ri-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    private static QueryEngine SetUp(JetDatabase db, string fkClause)
    {
        var e = new QueryEngine(db);
        e.ExecuteNonQuery("CREATE TABLE P (Id long PRIMARY KEY, N long)");
        e.ExecuteNonQuery($"CREATE TABLE C (Id long PRIMARY KEY, ParentId long, CONSTRAINT FK_C FOREIGN KEY (ParentId) REFERENCES P (Id){fkClause})");
        e.ExecuteNonQuery("INSERT INTO P (Id, N) VALUES (1, 10)");
        e.ExecuteNonQuery("INSERT INTO P (Id, N) VALUES (2, 20)");
        e.ExecuteNonQuery("INSERT INTO C (Id, ParentId) VALUES (100, 1)"); // two children of P#1
        e.ExecuteNonQuery("INSERT INTO C (Id, ParentId) VALUES (101, 1)");
        return e;
    }

    private static void Run(string fkClause, Action<QueryEngine> act)
    {
        string path = Fresh();
        try { using var db = JetDatabase.Open(path, readOnly: false); act(SetUp(db, fkClause)); }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    private static int[] ChildParents(QueryEngine e) =>
        e.ExecuteQuery("SELECT ParentId FROM C ORDER BY Id").Rows.Select(r => r[0] is null ? -1 : Convert.ToInt32(r[0])).ToArray();

    [Fact]
    public void No_action_rejects_deleting_or_key_updating_a_parent_with_children()
    {
        Run("", e =>
        {
            Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("DELETE FROM P WHERE Id = 1"));
            Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("UPDATE P SET Id = 9 WHERE Id = 1"));
            Assert.Equal(2, e.ExecuteQuery("SELECT Id FROM P").Rows.Count()); // nothing changed
            Assert.Equal(new[] { 1, 1 }, ChildParents(e));

            // A parent with NO children can be deleted / key-updated freely.
            Assert.Equal(1, e.ExecuteNonQuery("DELETE FROM P WHERE Id = 2"));
        });
    }

    [Fact]
    public void Child_fk_update_must_reference_an_existing_parent()
    {
        Run("", e =>
        {
            Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("UPDATE C SET ParentId = 99 WHERE Id = 100"));
            Assert.Equal(1, e.ExecuteNonQuery("UPDATE C SET ParentId = 2 WHERE Id = 100")); // 2 exists → allowed
            Assert.Equal(new[] { 2, 1 }, ChildParents(e));
        });
    }

    [Fact]
    public void Cascade_delete_removes_the_children()
    {
        Run(" ON DELETE CASCADE", e =>
        {
            Assert.Equal(1, e.ExecuteNonQuery("DELETE FROM P WHERE Id = 1"));
            Assert.Empty(e.ExecuteQuery("SELECT Id FROM C").Rows);                 // both children gone
            Assert.Equal(new[] { 2 }, e.ExecuteQuery("SELECT Id FROM P").Rows.Select(r => Convert.ToInt32(r[0])));
        });
    }

    [Fact]
    public void Cascade_update_rewrites_the_children_fk()
    {
        Run(" ON UPDATE CASCADE", e =>
        {
            Assert.Equal(1, e.ExecuteNonQuery("UPDATE P SET Id = 9 WHERE Id = 1"));
            Assert.Equal(new[] { 9, 9 }, ChildParents(e));                          // children followed the new key
            Assert.Equal(9, Convert.ToInt32(e.ExecuteQuery("SELECT Id FROM P WHERE N = 10").Rows.Single()[0]));
        });
    }

    [Fact]
    public void Set_null_delete_nulls_the_children_fk()
    {
        Run(" ON DELETE SET NULL", e =>
        {
            Assert.Equal(1, e.ExecuteNonQuery("DELETE FROM P WHERE Id = 1"));
            Assert.Equal(new[] { -1, -1 }, ChildParents(e));                        // children's ParentId set to NULL
            Assert.Equal(2, e.ExecuteQuery("SELECT Id FROM C").Rows.Count());       // children still there
        });
    }

    [Fact]
    public void Set_null_action_persists_and_reads_back()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false)) SetUp(db, " ON DELETE SET NULL");
            using var db2 = JetDatabase.Open(path);
            var fk = Assert.Single(db2.Catalog.ForeignKeysOf("C"));
            Assert.True(fk.DeleteSetNull);
            Assert.False(fk.CascadeDelete);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
