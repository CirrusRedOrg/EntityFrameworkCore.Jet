using System;
using System.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EntityFrameworkCore.Jet.Data.Tests
{
    /// <summary>
    /// Measures what the ACE engine actually does to an object's <b>dependents</b> when a table or a column is
    /// renamed through the DAO/ADOX path (what <c>ALTER TABLE … RENAME …</c> is intercepted into). LibRed has no
    /// COM to delegate to and must do the catalog/TDEF surgery itself, so these measurements <i>are</i> the
    /// specification for its native rename: it should fix up exactly what ACE fixes up, and no more.
    ///
    /// <para>Measured behaviour, common to both (asserted below so it can't drift):</para>
    /// <list type="bullet">
    /// <item>Neither rename is <b>refused</b>, even with an enforced relationship involved.</item>
    /// <item><c>MSysRelationships</c> <b>follows</b> — it stores tables and columns by name, and ACE repoints
    /// both, preserving the relationship's own name and its enforcement.</item>
    /// <item><b>Indexes need no fixup</b>: they ride along and keep their own names (they reference the table
    /// and its columns by id, not by name).</item>
    /// <item>A <b>stored query/view breaks</b> — ACE does not rewrite <c>MSysQueries</c>. Name AutoCorrect is an
    /// Access <i>application</i> feature, so it never runs for a DAO/ADOX rename. LibRed must leave them
    /// dangling too rather than "helpfully" fixing them, or the two providers diverge on the same migration.
    /// </item>
    /// <item>A renamed column <b>keeps its DEFAULT</b>: those properties live in the table's <c>LvProp</c> blob
    /// keyed by column <i>name</i>, and ACE rewrites that key.</item>
    /// </list>
    ///
    /// <para>Everything is still written to the test output, which is what you want if a different ACE version
    /// ever behaves differently.</para>
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class RenameFanOutProbeTest
    {
        private const string StoreName = nameof(RenameFanOutProbeTest) + ".accdb";

        private JetConnection _connection;

        [TestInitialize]
        public void Setup()
        {
            _connection = Helpers.CreateAndOpenDatabase(StoreName);

            using var command = _connection.CreateCommand(
                """
                CREATE TABLE Parent (Id INT NOT NULL CONSTRAINT PK_Parent PRIMARY KEY, Name TEXT(50));
                CREATE TABLE Child (Id INT NOT NULL CONSTRAINT PK_Child PRIMARY KEY, ParentId INT,
                    CONSTRAINT FK_Child_Parent FOREIGN KEY (ParentId) REFERENCES Parent (Id));
                CREATE INDEX IX_Parent_Name ON Parent (Name);
                CREATE VIEW vwParent AS SELECT Id, Name FROM Parent;
                """);
            command.ExecuteNonQuery();
        }

        [TestCleanup]
        public void TearDown()
        {
            _connection?.Close();
            Helpers.DeleteDatabase(StoreName);
        }

        [TestMethod]
        public void Probe_what_a_table_rename_does_to_its_dependents()
        {
            Report("--- BEFORE ---");
            ReportRelations();
            ReportIndexes();
            ReportView();

            // The rename goes through JetSchemaOperationsHandling → ISchemaOperationsProvider (ADOX/DAO).
            Exception refused = null;
            try
            {
                using var rename = _connection.CreateCommand("ALTER TABLE `Parent` RENAME TO `ParentRenamed`");
                rename.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                refused = e;
            }

            ReOpenConnection();

            Report("--- AFTER ---");
            if (refused is not null)
            {
                // Finding #1: ACE will not rename a table that participates in an enforced relationship.
                // That is itself the behaviour LibRed must match (reject, don't silently break the FK).
                Report($"RENAME REFUSED: {refused.GetType().Name}: {refused.Message}");
                Report("=> LibRed's native rename must reject this case too, rather than proceed.");
                return;
            }

            Report($"old name still present : {TableExists("Parent")}");
            Report($"new name present       : {TableExists("ParentRenamed")}");
            ReportRelations();
            ReportIndexes();
            ReportView();

            // Measured ACE behaviour — this is the contract LibRed's native rename must reproduce.
            Assert.IsTrue(TableExists("ParentRenamed"), "Table was not renamed.");
            Assert.IsFalse(TableExists("Parent"), "Old table name still resolves after the rename.");

            // The relationship follows the new name: ACE rewrites MSysRelationships, which stores tables by name.
            Assert.AreEqual(
                "ParentRenamed",
                RelationValue("FK_Child_Parent", "PRINCIPAL_TABLE_NAME"),
                "ACE did not repoint the relationship at the renamed table.");
            Assert.AreEqual("True", RelationValue("FK_Child_Parent", "IS_ENFORCED"), ignoreCase: true,
                "Relationship lost its enforcement across the rename.");

            // Indexes ride along with the table and keep their own names — a table rename never renames them.
            Assert.IsTrue(IndexExists("ParentRenamed", "PK_Parent"), "Primary key did not follow the renamed table.");
            Assert.IsTrue(IndexExists("ParentRenamed", "IX_Parent_Name"), "Secondary index did not follow the renamed table.");

            // Stored queries are NOT rewritten — Name AutoCorrect is an Access application feature, not an engine
            // one, so a DAO/ADOX rename leaves the view dangling. LibRed must match this and not "helpfully" fix it.
            Assert.IsFalse(ViewResolves("vwParent"), "The view still resolves — ACE rewrote the stored query, which would change LibRed's contract.");
        }

        /// <summary>Whether the stored query still resolves — the direct test of whether ACE rewrote it.</summary>
        private void ReportView()
        {
            try
            {
                using var command = _connection.CreateCommand("SELECT * FROM `vwParent`");
                using var reader = command.ExecuteReader();
                Report($"view vwParent          : OK (resolves, {reader.FieldCount} columns)");
            }
            catch (Exception e)
            {
                Report($"view vwParent          : BROKEN — {e.Message.ReplaceLineEndings(" ")}");
            }
        }

        /// <summary>
        /// The column-rename counterpart. Open questions: does the column's <b>DEFAULT</b> (stored in the table's
        /// LvProp blob, keyed by column <i>name</i>) survive; does an <b>index</b> over the column follow; does a
        /// <b>relationship</b> that names the column follow; does a <b>view</b> naming it break.
        /// </summary>
        [TestMethod]
        public void Probe_what_a_column_rename_does_to_its_dependents()
        {
            using (var setup = _connection.CreateCommand(
                """
                CREATE TABLE Doc (Id INT NOT NULL CONSTRAINT PK_Doc PRIMARY KEY, Title TEXT(50) DEFAULT 'untitled');
                CREATE INDEX IX_Doc_Title ON Doc (Title);
                CREATE VIEW vwDoc AS SELECT Id, Title FROM Doc;
                CREATE TABLE DocChild (Id INT NOT NULL CONSTRAINT PK_DocChild PRIMARY KEY, DocId INT,
                    CONSTRAINT FK_DocChild_Doc FOREIGN KEY (DocId) REFERENCES Doc (Id));
                """))
            {
                setup.ExecuteNonQuery();
            }

            Report("--- COLUMN: BEFORE ---");
            Report($"Doc.Title default      : {ColumnDefault("Doc", "Title") ?? "(none)"}");
            ReportTable("relation columns", p => p.GetRelationColumns());
            ReportIndexes();

            Exception refused = null;
            try
            {
                // The plain column, and the FK column, renamed through the intercepted DAO/ADOX path.
                using var c1 = _connection.CreateCommand("ALTER TABLE `Doc` RENAME COLUMN `Title` TO `Heading`");
                c1.ExecuteNonQuery();
                using var c2 = _connection.CreateCommand("ALTER TABLE `DocChild` RENAME COLUMN `DocId` TO `ParentDocId`");
                c2.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                refused = e;
            }

            ReOpenConnection();

            Report("--- COLUMN: AFTER ---");
            if (refused is not null)
            {
                Report($"RENAME COLUMN REFUSED: {refused.GetType().Name}: {refused.Message.ReplaceLineEndings(" ")}");
                return;
            }

            Report($"Doc.Heading default    : {ColumnDefault("Doc", "Heading") ?? "(none — DEFAULT was LOST)"}");
            Report($"Doc.Title still present: {ColumnExists("Doc", "Title")}");
            ReportTable("relation columns", p => p.GetRelationColumns());
            ReportIndexes();
            Report($"view vwDoc             : {(ViewResolves("vwDoc") ? "OK (resolves)" : "BROKEN")}");

            // Measured ACE behaviour — the contract LibRed's native column rename must reproduce.
            Assert.IsTrue(ColumnExists("Doc", "Heading"), "Column was not renamed.");
            Assert.IsFalse(ColumnExists("Doc", "Title"), "Old column name still resolves after the rename.");

            // The DEFAULT lives in the table's LvProp blob keyed by column NAME, and ACE rewrites that key — a
            // renamed column keeps its default. LibRed's TDEF rewrite must carry the property blob across too.
            Assert.IsTrue(
                (ColumnDefault("Doc", "Heading") ?? "").Contains("untitled", StringComparison.OrdinalIgnoreCase),
                "The column's DEFAULT did not survive the rename (LvProp key was not rewritten).");

            // MSysRelationships stores the FK's columns by name, and ACE repoints them.
            Assert.AreEqual(
                "ParentDocId",
                RelationColumnValue("FK_DocChild_Doc", "REFERENCING_COLUMN_NAME"),
                "ACE did not repoint the relationship at the renamed column.");

            // The index over the renamed column survives and keeps its own name: indexes reference columns by
            // id, not name, so a column rename needs no index fixup.
            Assert.IsTrue(IndexExists("Doc", "IX_Doc_Title"), "Index over the renamed column did not survive.");

            // Stored queries are not rewritten, exactly as for a table rename.
            Assert.IsFalse(ViewResolves("vwDoc"), "The view still resolves — ACE rewrote the stored query.");
        }

        /// <summary>A single field of the named relationship's column mapping, or null if it is gone.</summary>
        private string RelationColumnValue(string relationName, string columnName)
        {
            using var provider = SchemaProvider.CreateInstance(_connection.SchemaProviderType, _connection, false);
            using DataTable relationColumns = provider.GetRelationColumns();
            foreach (DataRow row in relationColumns.Rows)
            {
                if (string.Equals(Convert.ToString(row["RELATION_NAME"]), relationName, StringComparison.OrdinalIgnoreCase))
                    return Convert.ToString(row[columnName]);
            }

            return null;
        }

        /// <summary>
        /// The column's DEFAULT as INFORMATION_SCHEMA reports it. Note the emulated INFORMATION_SCHEMA views only
        /// support <c>SELECT *</c> (JetCommand rewrites them), so the projection happens client-side.
        /// </summary>
        private string ColumnDefault(string tableName, string columnName)
        {
            using var command = _connection.CreateCommand(
                $"SELECT * FROM `INFORMATION_SCHEMA.COLUMNS` " +
                $"WHERE `TABLE_NAME` = '{tableName}' AND `COLUMN_NAME` = '{columnName}'");
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return "(column not found)";

            int ordinal;
            try
            {
                ordinal = reader.GetOrdinal("COLUMN_DEFAULT");
            }
            catch (IndexOutOfRangeException)
            {
                return "(INFORMATION_SCHEMA.COLUMNS has no COLUMN_DEFAULT)";
            }

            return reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));
        }

        private bool ColumnExists(string tableName, string columnName)
        {
            using var command = _connection.CreateCommand(
                $"SELECT * FROM `INFORMATION_SCHEMA.COLUMNS` " +
                $"WHERE `TABLE_NAME` = '{tableName}' AND `COLUMN_NAME` = '{columnName}'");
            using var reader = command.ExecuteReader();
            return reader.HasRows;
        }

        /// <summary>
        /// Does ACE reject renaming a table onto the name of an existing <b>table</b>? (Setup leaves Parent,
        /// Child and the view vwParent in place, so Child → Parent is a straight collision.)
        /// </summary>
        [TestMethod]
        public void Probe_rename_onto_an_existing_table_name()
        {
            string outcome = TryRename("Child", "Parent");
            Report($"rename onto TABLE name : {outcome}");
            Assert.IsTrue(outcome.StartsWith("REJECTED"), $"ACE allowed a table rename onto an existing table name: {outcome}");
        }

        /// <summary>
        /// Does ACE reject renaming a table onto the name of an existing <b>saved query</b>? Access shares one
        /// object namespace between tables and queries, so this should collide too — but they live in different
        /// MSysObjects containers (different ParentId), so the unique (ParentId, Name) index would not stop it.
        /// </summary>
        [TestMethod]
        public void Probe_rename_onto_an_existing_query_name()
        {
            string outcome = TryRename("Child", "vwParent");
            Report($"rename onto QUERY name : {outcome}");
            Assert.IsTrue(outcome.StartsWith("REJECTED"), $"ACE allowed a table rename onto an existing query name: {outcome}");
        }

        /// <summary>
        /// A table renamed to its own name. EF's <c>Move_table</c> (a schema move) degrades to exactly this on a
        /// schema-less engine — the generator emits <c>RENAME TO</c> the same name — so it has to be benign.
        /// The case-only variant matters for the same reason: it's the same object, not a collision.
        /// </summary>
        [TestMethod]
        public void Probe_rename_a_table_to_its_own_name()
        {
            string outcome = TryRename("Child", "Child");
            Report($"rename onto ITSELF     : {outcome}");
            Assert.AreEqual("ALLOWED (no error)", outcome,
                "ACE rejected renaming a table to its own name — EF's schema 'move' degrades to exactly this.");
        }

        [TestMethod]
        public void Probe_rename_a_table_changing_only_case()
        {
            string outcome = TryRename("Child", "CHILD");
            Report($"rename changing CASE   : {outcome}");
            Assert.AreEqual("ALLOWED (no error)", outcome, "ACE rejected a case-only rename of a table.");
        }

        private string TryRename(string from, string to)
        {
            try
            {
                using var command = _connection.CreateCommand($"ALTER TABLE `{from}` RENAME TO `{to}`");
                command.ExecuteNonQuery();
                return "ALLOWED (no error)";
            }
            catch (Exception e)
            {
                return $"REJECTED — {e.GetType().Name}: {e.Message.ReplaceLineEndings(" ")}";
            }
        }

        /// <summary>Whether the stored query still resolves (false = ACE left it dangling).</summary>
        private bool ViewResolves(string viewName)
        {
            try
            {
                using var command = _connection.CreateCommand($"SELECT * FROM `{viewName}`");
                using var reader = command.ExecuteReader();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>A single field of the named relationship, or null if the relationship is gone.</summary>
        private string RelationValue(string relationName, string columnName)
        {
            using var provider = SchemaProvider.CreateInstance(_connection.SchemaProviderType, _connection, false);
            using DataTable relations = provider.GetRelations();
            foreach (DataRow row in relations.Rows)
            {
                if (string.Equals(Convert.ToString(row["RELATION_NAME"]), relationName, StringComparison.OrdinalIgnoreCase))
                    return Convert.ToString(row[columnName]);
            }

            return null;
        }

        private bool IndexExists(string tableName, string indexName)
        {
            using var provider = SchemaProvider.CreateInstance(_connection.SchemaProviderType, _connection, false);
            using DataTable indexes = provider.GetIndexes();
            foreach (DataRow row in indexes.Rows)
            {
                if (string.Equals(Convert.ToString(row["TABLE_NAME"]), tableName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Convert.ToString(row["INDEX_NAME"]), indexName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void ReportRelations() => ReportTable("relations", p => p.GetRelations());

        private void ReportIndexes() => ReportTable("indexes", p => p.GetIndexes());

        /// <summary>Dumps a schema DataTable generically, so the probe doesn't depend on column names.</summary>
        private void ReportTable(string label, Func<SchemaProvider, DataTable> select)
        {
            try
            {
                using var provider = SchemaProvider.CreateInstance(_connection.SchemaProviderType, _connection, false);
                using DataTable table = select(provider);
                if (table.Rows.Count == 0)
                {
                    Report($"{label,-23}: (none)");
                    return;
                }

                foreach (DataRow row in table.Rows)
                {
                    var cells = new System.Text.StringBuilder();
                    foreach (DataColumn column in table.Columns)
                    {
                        object value = row[column];
                        if (value is null || value == DBNull.Value) continue;
                        if (cells.Length > 0) cells.Append(", ");
                        cells.Append($"{column.ColumnName}={value}");
                    }

                    Report($"{label,-23}: {cells}");
                }
            }
            catch (Exception e)
            {
                Report($"{label,-23}: FAILED to read — {e.Message.ReplaceLineEndings(" ")}");
            }
        }

        private bool TableExists(string tableName)
        {
            using var command = _connection.CreateCommand(
                $"SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE `TABLE_NAME` = '{tableName}'");
            using var reader = command.ExecuteReader();
            return reader.HasRows;
        }

        private void ReOpenConnection()
        {
            _connection.Close();
            JetConnection.ClearAllPools();
            _connection.Open();
        }

        private static void Report(string message) => Console.WriteLine(message);
    }
}
