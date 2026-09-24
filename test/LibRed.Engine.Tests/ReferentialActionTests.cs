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
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ri-");
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
        finally { TemporaryDatabase.Delete(path); }
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

    // SET NULL on a SELF-referencing table, where the row being nulled and the row being deleted are rows of
    // one table. The action reads each child from the snapshot it took before the delete, so a child that the
    // delete has already removed — or that an earlier child's nulling has already rewritten — is looked up by
    // a key the table no longer holds, and the index says "entry not found".
    [Fact]
    public void Set_null_delete_on_a_self_referencing_table_nulls_the_children()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery(
                "CREATE TABLE T (Id long PRIMARY KEY, ParentId long, "
                + "CONSTRAINT FK_T FOREIGN KEY (ParentId) REFERENCES T (Id) ON DELETE SET NULL)");
            e.ExecuteNonQuery("INSERT INTO T (Id, ParentId) VALUES (1, NULL)");   // the root
            e.ExecuteNonQuery("INSERT INTO T (Id, ParentId) VALUES (2, 1)");      // three children of it
            e.ExecuteNonQuery("INSERT INTO T (Id, ParentId) VALUES (3, 1)");
            e.ExecuteNonQuery("INSERT INTO T (Id, ParentId) VALUES (4, 1)");
            e.ExecuteNonQuery("INSERT INTO T (Id, ParentId) VALUES (5, 2)");      // and a grandchild

            Assert.Equal(1, e.ExecuteNonQuery("DELETE FROM T WHERE Id = 1"));

            // The root is gone, its three children point at nothing, and the grandchild is untouched.
            var rows = e.ExecuteQuery("SELECT Id, ParentId FROM T ORDER BY Id").Rows
                .Select(r => (Convert.ToInt32(r[0]), r[1] is null ? -1 : Convert.ToInt32(r[1]))).ToArray();
            Assert.Equal([(2, -1), (3, -1), (4, -1), (5, 2)], rows);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The harder half: the row the action nulls is ALSO one the statement is deleting. Deleting the parent
    // rewrites the child's FK, and the statement then reaches that child carrying the values it read before —
    // whose key no longer names anything in the index.
    [Fact]
    public void Set_null_delete_reaches_a_child_the_same_statement_is_deleting()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery(
                "CREATE TABLE T (Id long PRIMARY KEY, ParentId long, "
                + "CONSTRAINT FK_T FOREIGN KEY (ParentId) REFERENCES T (Id) ON DELETE SET NULL)");
            e.ExecuteNonQuery("CREATE INDEX IX_Parent ON T (ParentId)");
            e.ExecuteNonQuery("INSERT INTO T (Id, ParentId) VALUES (1, NULL)");
            e.ExecuteNonQuery("INSERT INTO T (Id, ParentId) VALUES (2, 1)");
            e.ExecuteNonQuery("INSERT INTO T (Id, ParentId) VALUES (3, 2)");
            e.ExecuteNonQuery("INSERT INTO T (Id, ParentId) VALUES (4, 1)");

            // 1 and 2 go together, and 2 is 1's child — so the action rewrites a row the delete also removes.
            Assert.Equal(2, e.ExecuteNonQuery("DELETE FROM T WHERE Id = 1 OR Id = 2"));

            var rows = e.ExecuteQuery("SELECT Id, ParentId FROM T ORDER BY Id").Rows
                .Select(r => (Convert.ToInt32(r[0]), r[1] is null ? -1 : Convert.ToInt32(r[1]))).ToArray();
            Assert.Equal([(3, -1), (4, -1)], rows);

            // And the index agrees with the rows: a seek on the nulled column finds both survivors.
            Assert.Equal(2, e.ExecuteQuery("SELECT Id FROM T WHERE ParentId IS NULL").Rows.Count());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ON UPDATE SET NULL is a pathway only — its Jet storage bytes are unverified (ACE's OLE DB provider
    // rejects the DDL), so creating one throws NotImplemented rather than guessing the bytes.
    [Fact]
    public void On_update_set_null_throws_not_implemented()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE P (Id long PRIMARY KEY)");
            var ex = Assert.Throws<NotImplementedException>(() => e.ExecuteNonQuery(
                "CREATE TABLE C (Id long PRIMARY KEY, ParentId long, CONSTRAINT FK_C FOREIGN KEY (ParentId) REFERENCES P (Id) ON UPDATE SET NULL ON DELETE SET NULL)"));
            Assert.Contains("ON UPDATE SET NULL", ex.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
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
        finally { TemporaryDatabase.Delete(path); }
    }

    // A cascade rewrites a child row, so it owes that row every invariant an UPDATE of it would: SET NULL may
    // not null a Required column, and CASCADE may not drive two children onto one unique key. Table.Update
    // enforces nothing itself, so without these checks the statement — which never names the child table —
    // writes what the UPDATE path a few lines away explicitly refuses.
    [Fact]
    public void Set_null_refuses_to_null_a_required_child_column()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE P (Id long PRIMARY KEY)");
            e.ExecuteNonQuery("CREATE TABLE C (Id long PRIMARY KEY, ParentId long NOT NULL, "
                + "CONSTRAINT FK_C FOREIGN KEY (ParentId) REFERENCES P (Id) ON DELETE SET NULL)");
            e.ExecuteNonQuery("INSERT INTO P (Id) VALUES (1)");
            e.ExecuteNonQuery("INSERT INTO C (Id, ParentId) VALUES (100, 1)");

            Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("DELETE FROM P WHERE Id = 1"));

            // The child row is intact, not half-nulled.
            Assert.Equal(1, Convert.ToInt32(e.ExecuteQuery("SELECT ParentId FROM C WHERE Id = 100").Rows.Single()[0]));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Cascade_refuses_to_create_a_duplicate_child_key()
    {
        Run(" ON UPDATE CASCADE", e =>
        {
            // One child per parent, and a unique index over the FK column: moving parent 1 onto 2 would
            // cascade its child onto the other child's key.
            e.ExecuteNonQuery("DELETE FROM C WHERE Id = 101");
            e.ExecuteNonQuery("INSERT INTO C (Id, ParentId) VALUES (102, 2)");
            e.ExecuteNonQuery("CREATE UNIQUE INDEX UX_C ON C (ParentId)");

            Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("UPDATE P SET Id = 2 WHERE Id = 1"));
        });
    }
}
