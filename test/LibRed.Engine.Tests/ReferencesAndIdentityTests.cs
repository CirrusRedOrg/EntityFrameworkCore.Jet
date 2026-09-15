using LibRed;
using LibRed.Catalog;
using LibRed.Engine;
using LibRed.Sql.Ast;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// <c>REFERENCES table</c> with no column list, and the trailing <c>IDENTITY [(seed [, increment])]</c> column
/// attribute — both measured against ACE (see <c>ReferencesAndIdentityAccessTests</c>, which runs each statement
/// through both engines).
/// </summary>
public class ReferencesAndIdentityTests
{
    private const string Parent = "CREATE TABLE P (Id LONG CONSTRAINT pkP PRIMARY KEY, Code TEXT(10))";

    private static SqlStatement Parse(string sql) => new AntlrSqlParser().ParseStatement(sql);

    private static JetDatabase Run(params string[] statements)
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "refident-");
        JetDatabase db = TemporaryDatabase.OpenTracked(path, readOnly: false);
        var engine = new QueryEngine(db);
        foreach (string statement in statements) engine.ExecuteNonQuery(statement);
        return db;
    }

    // ---- REFERENCES table, no column list ----

    [Fact]
    public void References_without_columns_pairs_with_the_parent_primary_key_by_position()
    {
        // The child column shares its name with a non-key parent column, and still references the key.
        JetDatabase db = Run(Parent, "CREATE TABLE C (Id LONG, Code LONG, CONSTRAINT fk FOREIGN KEY (Code) REFERENCES P)");
        ForeignKey fk = Assert.Single(db.Catalog.ForeignKeysOf("C"));
        Assert.Equal("P", fk.ReferencedTable);
        Assert.Equal([("Code", "Id")], fk.Columns);
    }

    [Fact]
    public void A_composite_key_pairs_in_key_order()
    {
        JetDatabase db = Run(
            "CREATE TABLE P2 (A LONG, B LONG, CONSTRAINT pkP2 PRIMARY KEY (A, B))",
            "CREATE TABLE C (Id LONG, X LONG, Y LONG, CONSTRAINT fk FOREIGN KEY (X, Y) REFERENCES P2)");
        Assert.Equal([("X", "A"), ("Y", "B")], Assert.Single(db.Catalog.ForeignKeysOf("C")).Columns);
    }

    [Theory]
    [InlineData("CREATE TABLE P3 (Id LONG, Code TEXT(10) CONSTRAINT uq3 UNIQUE)", "CREATE TABLE C (Id LONG, PId LONG REFERENCES P3)", "does not have a primary key")]
    [InlineData("CREATE TABLE P2 (A LONG, B LONG, CONSTRAINT pkP2 PRIMARY KEY (A, B))", "CREATE TABLE C (Id LONG, X LONG, CONSTRAINT fk FOREIGN KEY (X) REFERENCES P2)", "same number of fields")]
    [InlineData("CREATE TABLE Unrelated (Id LONG)", "CREATE TABLE C (Id LONG, PId LONG REFERENCES Nope)", "Cannot find table")]
    public void Ace_refuses_it_and_so_does_libred(string parent, string child, string message)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Run(parent, child));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void A_self_reference_takes_a_primary_key_declared_before_it()
    {
        JetDatabase db = Run("CREATE TABLE C (Id LONG CONSTRAINT pkC PRIMARY KEY, ParentId LONG REFERENCES C)");
        Assert.Equal([("ParentId", "Id")], Assert.Single(db.Catalog.ForeignKeysOf("C")).Columns);
    }

    [Theory]
    [InlineData("CREATE TABLE C (ParentId LONG REFERENCES C, Id LONG CONSTRAINT pkC PRIMARY KEY)")]
    [InlineData("CREATE TABLE C (Id LONG, ParentId LONG, CONSTRAINT fk FOREIGN KEY (ParentId) REFERENCES C, CONSTRAINT pkC PRIMARY KEY (Id))")]
    public void A_self_reference_before_its_primary_key_has_no_key_to_reference(string sql)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Run(sql));
        Assert.Contains("does not have a primary key", error.Message);
    }

    [Theory]
    [InlineData("ALTER TABLE C ADD COLUMN PId LONG REFERENCES P")]
    [InlineData("ALTER TABLE C ADD COLUMN PId LONG CONSTRAINT fk REFERENCES P (Id)")]
    [InlineData("ALTER TABLE C ADD CONSTRAINT fk FOREIGN KEY (PId) REFERENCES P")]
    public void Alter_table_creates_the_relationship(string alter)
    {
        string create = alter.Contains("ADD COLUMN") ? "CREATE TABLE C (Id LONG)" : "CREATE TABLE C (Id LONG, PId LONG)";
        JetDatabase db = Run(Parent, create, alter);
        Assert.Equal([("PId", "Id")], Assert.Single(db.Catalog.ForeignKeysOf("C")).Columns);
    }

    [Theory]
    [InlineData("COUNTER", "LONG")]
    [InlineData("LONG", "COUNTER")]
    [InlineData("TEXT(10)", "TEXT(20)")]
    [InlineData("CHAR(10)", "TEXT(5)")]
    [InlineData("DECIMAL(10, 2)", "DECIMAL(12, 4)")]
    [InlineData("BINARY(8)", "VARBINARY(8)")]
    public void A_relationship_pairs_columns_of_the_same_storage_type_whatever_their_lengths(string parent, string child)
    {
        JetDatabase db = Run($"CREATE TABLE P (Id {parent} CONSTRAINT pkP PRIMARY KEY)", $"CREATE TABLE C (K LONG, PId {child} REFERENCES P (Id))");
        Assert.Single(db.Catalog.ForeignKeysOf("C"));
    }

    [Theory]
    [InlineData("LONG", "TEXT(10)", false)]
    [InlineData("LONG", "SHORT", false)]
    [InlineData("DOUBLE", "SINGLE", false)]
    [InlineData("CURRENCY", "DECIMAL(10, 2)", false)]
    [InlineData("LONG", "TEXT(10)", true)]
    public void A_relationship_between_different_storage_types_is_refused(string parent, string child, bool alterTable)
    {
        string[] statements = alterTable
            ? [$"CREATE TABLE P (Id {parent} CONSTRAINT pkP PRIMARY KEY)", $"CREATE TABLE C (K LONG, PId {child})", "ALTER TABLE C ADD CONSTRAINT fk FOREIGN KEY (PId) REFERENCES P (Id)"]
            : [$"CREATE TABLE P (Id {parent} CONSTRAINT pkP PRIMARY KEY)", $"CREATE TABLE C (K LONG, PId {child} REFERENCES P (Id))"];
        var error = Assert.Throws<InvalidOperationException>(() => Run(statements));
        Assert.Contains("same data types", error.Message);
    }

    // ---- ADD COLUMN constraints ----

    [Fact]
    public void Add_column_creates_its_primary_key_and_unique_index()
    {
        JetDatabase db = Run("CREATE TABLE T (V TEXT(10))",
            "ALTER TABLE T ADD COLUMN Id LONG CONSTRAINT pk PRIMARY KEY",
            "ALTER TABLE T ADD COLUMN Code TEXT(10) CONSTRAINT uq UNIQUE");
        TableDef t = db.Catalog.FindTable("T")!;
        IndexDef pk = Assert.Single(t.Indexes, i => i.IsPrimaryKey);
        Assert.Equal("pk", pk.Name);
        IndexDef uq = Assert.Single(t.Indexes, i => i.Name == "uq");
        Assert.True(uq.IsUnique);
        Assert.Equal("Code", Assert.Single(uq.Columns).Column.Name);
    }

    [Theory]
    [InlineData("CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, V TEXT(10));ALTER TABLE T ADD COLUMN Id2 LONG CONSTRAINT pk2 PRIMARY KEY", "Primary key already exists")]
    [InlineData("CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, W LONG);ALTER TABLE T ADD CONSTRAINT pk2 PRIMARY KEY (W)", "Primary key already exists")]
    [InlineData("CREATE TABLE T (V TEXT(10));INSERT INTO T (V) VALUES ('a');ALTER TABLE T ADD COLUMN Id LONG CONSTRAINT pk PRIMARY KEY", "cannot contain a Null value")]
    [InlineData("CREATE TABLE T (V TEXT(10), W LONG);INSERT INTO T (V) VALUES ('a');CREATE INDEX pk ON T (W) WITH PRIMARY", "cannot contain a Null value")]
    [InlineData("CREATE TABLE T (V TEXT(10), W LONG);INSERT INTO T (V) VALUES ('a');CREATE INDEX ix ON T (V, W) WITH DISALLOW NULL", "cannot contain a Null value")]
    public void A_second_primary_key_or_a_key_over_nulls_is_refused(string statements, string message)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Run(statements.Split(';')));
        Assert.Contains(message, error.Message);
    }

    [Theory]
    [InlineData("COUNTER", "1,2,3,4")]
    [InlineData("COUNTER(1, 1)", "1,2,3,4")]
    [InlineData("INT IDENTITY", "1,2,3,4")]
    [InlineData("COUNTER(2, 1)", "1,2,2,3")]
    [InlineData("COUNTER(1, 5)", "1,1,2,6")]
    [InlineData("COUNTER(10, -1)", "1,2,9,10")]
    public void An_autonumber_added_to_a_populated_table_numbers_its_rows(string type, string ids)
    {
        JetDatabase db = Run("CREATE TABLE T (V TEXT(10))", "INSERT INTO T (V) VALUES ('a')", "INSERT INTO T (V) VALUES ('b')",
            $"ALTER TABLE T ADD COLUMN Id {type}");
        var engine = new QueryEngine(db);
        engine.ExecuteNonQuery("INSERT INTO T (V) VALUES ('c')");
        engine.ExecuteNonQuery("INSERT INTO T (V) VALUES ('d')");
        Assert.Equal(ids, string.Join(",", engine.ExecuteQuery("SELECT Id FROM T ORDER BY Id").Rows.Select(r => r[0])));
    }

    // ---- IDENTITY ----

    [Theory]
    [InlineData("Id INT NOT NULL IDENTITY", 1, 1, true)]
    [InlineData("Id INT IDENTITY NOT NULL", 1, 1, true)]
    [InlineData("Id INT IDENTITY", 1, 1, false)]
    [InlineData("Id INT NOT NULL IDENTITY(5, 2)", 5, 2, true)]
    [InlineData("Id INT IDENTITY(5)", 5, 1, false)]
    [InlineData("Id LONG NOT NULL IDENTITY(5, -1)", 5, -1, true)]
    [InlineData("Id IDENTITY(5, 2)", 5, 2, false)]
    [InlineData("Id INT IDENTITY DEFAULT 1", 1, 1, false)]
    [InlineData("Id INT NOT NULL IDENTITY PRIMARY KEY", 1, 1, true)]
    [InlineData("Id COUNTER(5, 2) IDENTITY(9, 3)", 9, 3, false)]
    // IDENTITY's own seed and increment — 1 each when omitted — replace the type's.
    [InlineData("Id COUNTER(5, 2) IDENTITY", 1, 1, false)]
    public void Identity_makes_a_long_column_an_autonumber(string column, int seed, int increment, bool required)
    {
        JetDatabase db = Run($"CREATE TABLE T ({column}, V TEXT(10))");
        ColumnDef id = db.Catalog.FindTable("T")!.FindColumn("Id")!;
        Assert.True(id.IsAutoNumber);
        Assert.Equal(increment, id.Increment);
        Assert.Equal(!required, id.IsNullable);

        var engine = new QueryEngine(db);
        engine.ExecuteNonQuery("INSERT INTO T (V) VALUES ('a')");
        Assert.Equal(seed, Convert.ToInt32(engine.ExecuteQuery("SELECT Id FROM T").Rows.Single()[0]));
    }

    [Theory]
    [InlineData("Id SHORT NOT NULL IDENTITY", JetDataType.Int16)]
    [InlineData("Id BYTE IDENTITY(5, 2)", JetDataType.Byte)]
    [InlineData("Id BIGINT IDENTITY", JetDataType.Int64)]
    [InlineData("Id DOUBLE IDENTITY", JetDataType.Double)]
    [InlineData("Id GUID IDENTITY", JetDataType.Guid)]
    [InlineData("Id TEXT(10) NOT NULL IDENTITY", JetDataType.Text)]
    [InlineData("Id TEXT IDENTITY(5, 2)", JetDataType.Memo)]
    public void Identity_on_any_other_type_is_ignored(string column, JetDataType type)
    {
        ColumnDef id = Run($"CREATE TABLE T ({column}, V TEXT(10))").Catalog.FindTable("T")!.FindColumn("Id")!;
        Assert.Equal(type, id.Type);
        Assert.False(id.IsAutoNumber);
    }

    [Theory]
    [InlineData("Id INT PRIMARY KEY IDENTITY(5, 2)")]
    [InlineData("Id INT CONSTRAINT pk PRIMARY KEY NOT NULL IDENTITY")]
    [InlineData("Id INT DEFAULT 1 IDENTITY")]
    [InlineData("Id INT NOT NULL IDENTITY(5, 2, 1)")]
    [InlineData("Id INT NOT NULL IDENTITY()")]
    // COUNTER and AUTOINCREMENT are types only; neither trails a type as IDENTITY does.
    [InlineData("Id INT NOT NULL AUTOINCREMENT")]
    [InlineData("Id INT NOT NULL COUNTER")]
    // Reserved, as ACE reserves it.
    [InlineData("Identity LONG")]
    public void Ace_refuses_these_and_so_does_libred(string column)
        => Assert.ThrowsAny<Exception>(() => Parse($"CREATE TABLE T ({column}, V TEXT(10))"));

    [Fact]
    public void A_bracketed_column_named_identity_is_still_allowed()
        => Assert.Equal("Identity", Assert.IsType<CreateTableStatement>(Parse("CREATE TABLE T ([Identity] LONG)")).Columns[0].Name);

    [Fact]
    public void At_at_identity_still_reads_the_last_autonumber()
    {
        JetDatabase db = Run("CREATE TABLE T (Id INT NOT NULL IDENTITY(7, 1), V TEXT(10))");
        var engine = new QueryEngine(db);
        engine.ExecuteNonQuery("INSERT INTO T (V) VALUES ('a')");
        Assert.Equal(7, Convert.ToInt32(engine.ExecuteQuery("SELECT @@IDENTITY").Rows.Single()[0]));
    }

    [Fact]
    public void Alter_table_takes_identity_too()
    {
        JetDatabase db = Run("CREATE TABLE T (V TEXT(10))", "ALTER TABLE T ADD COLUMN Id INT NOT NULL IDENTITY(3, 3)");
        ColumnDef id = db.Catalog.FindTable("T")!.FindColumn("Id")!;
        Assert.True(id.IsAutoNumber);
        Assert.False(id.IsNullable);

        db = Run("CREATE TABLE T (Id INT, V TEXT(10))", "ALTER TABLE T ALTER COLUMN Id INT IDENTITY");
        Assert.True(db.Catalog.FindTable("T")!.FindColumn("Id")!.IsAutoNumber);
    }
}
