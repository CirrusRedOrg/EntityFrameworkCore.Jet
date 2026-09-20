using LibRed;
using LibRed.Catalog;
using LibRed.Engine;
using LibRed.Sql.Ast;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// <c>CLUSTERED</c> and <c>NONCLUSTERED</c> after <c>PRIMARY KEY</c> or <c>UNIQUE</c> in a constraint clause — in a
/// CREATE TABLE column or table constraint, and in ALTER TABLE's ADD CONSTRAINT, ADD COLUMN and ALTER COLUMN. ACE
/// accepts every one of those and stores nothing for the word: the file is byte-identical without it, and DAO reports
/// <c>Clustered = False</c> even for an index created with <c>Clustered = True</c>. So LibRed parses the word and
/// drops it, and rejects it wherever ACE does.
/// </summary>
public class ClusteredConstraintTests
{
    private static SqlStatement Parse(string sql) => new AntlrSqlParser().ParseStatement(sql);

    [Theory]
    [InlineData("CREATE TABLE T (Id LONG, V TEXT(10), CONSTRAINT pk PRIMARY KEY CLUSTERED (Id))",
                "CREATE TABLE T (Id LONG, V TEXT(10), CONSTRAINT pk PRIMARY KEY (Id))")]
    [InlineData("CREATE TABLE T (Id LONG, V TEXT(10), CONSTRAINT pk PRIMARY KEY NONCLUSTERED (Id))",
                "CREATE TABLE T (Id LONG, V TEXT(10), CONSTRAINT pk PRIMARY KEY (Id))")]
    [InlineData("CREATE TABLE T (Id LONG, V TEXT(10), PRIMARY KEY CLUSTERED (Id))",
                "CREATE TABLE T (Id LONG, V TEXT(10), PRIMARY KEY (Id))")]
    [InlineData("CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY CLUSTERED, V TEXT(10))",
                "CREATE TABLE T (Id LONG CONSTRAINT pk PRIMARY KEY, V TEXT(10))")]
    [InlineData("CREATE TABLE T (Id LONG PRIMARY KEY NONCLUSTERED NOT NULL, V TEXT(10))",
                "CREATE TABLE T (Id LONG PRIMARY KEY NOT NULL, V TEXT(10))")]
    [InlineData("CREATE TABLE T (Id LONG, V TEXT(10) CONSTRAINT uq UNIQUE CLUSTERED)",
                "CREATE TABLE T (Id LONG, V TEXT(10) CONSTRAINT uq UNIQUE)")]
    [InlineData("CREATE TABLE T (Id LONG, V TEXT(10), CONSTRAINT uq UNIQUE NONCLUSTERED (V))",
                "CREATE TABLE T (Id LONG, V TEXT(10), CONSTRAINT uq UNIQUE (V))")]
    [InlineData("create table T (Id long, constraint pk primary key clustered (Id))",
                "create table T (Id long, constraint pk primary key (Id))")]
    [InlineData("ALTER TABLE T ADD CONSTRAINT pk PRIMARY KEY CLUSTERED (Id)",
                "ALTER TABLE T ADD CONSTRAINT pk PRIMARY KEY (Id)")]
    [InlineData("ALTER TABLE T ADD CONSTRAINT uq UNIQUE NONCLUSTERED (V)",
                "ALTER TABLE T ADD CONSTRAINT uq UNIQUE (V)")]
    [InlineData("ALTER TABLE T ADD COLUMN Id LONG CONSTRAINT pk PRIMARY KEY CLUSTERED",
                "ALTER TABLE T ADD COLUMN Id LONG CONSTRAINT pk PRIMARY KEY")]
    [InlineData("ALTER TABLE T ALTER COLUMN V TEXT(20) CONSTRAINT uq UNIQUE CLUSTERED",
                "ALTER TABLE T ALTER COLUMN V TEXT(20) CONSTRAINT uq UNIQUE")]
    public void The_word_parses_to_the_statement_without_it(string withWord, string without)
        => Assert.Equal(Describe(Parse(without)), Describe(Parse(withWord)));

    [Theory]
    [InlineData("CREATE TABLE T (Id LONG, V TEXT(10), CONSTRAINT pk PRIMARY CLUSTERED KEY (Id))")]
    [InlineData("CREATE TABLE T (Id LONG, V TEXT(10), CONSTRAINT pk PRIMARY KEY CLUSTERED CLUSTERED (Id))")]
    [InlineData("CREATE TABLE P (Id LONG, PId LONG, CONSTRAINT fk FOREIGN KEY CLUSTERED (PId) REFERENCES Q (Id))")]
    [InlineData("CREATE TABLE T (Id LONG CLUSTERED, V TEXT(10))")]
    [InlineData("CREATE CLUSTERED INDEX ix ON T (V)")]
    [InlineData("CREATE UNIQUE CLUSTERED INDEX ix ON T (V)")]
    // Reserved, as ACE reserves both words: unbracketed, neither names a table, column or alias.
    [InlineData("CREATE TABLE Clustered (Id LONG)")]
    [InlineData("CREATE TABLE T (Clustered LONG)")]
    [InlineData("CREATE TABLE T (Nonclustered LONG)")]
    [InlineData("SELECT CustomerID AS Clustered FROM Customers")]
    public void Ace_rejects_it_everywhere_else_and_so_does_libred(string sql)
        => Assert.ThrowsAny<Exception>(() => Parse(sql));

    [Fact]
    public void A_bracketed_name_is_still_allowed()
    {
        var create = Assert.IsType<CreateTableStatement>(Parse("CREATE TABLE T ([Clustered] LONG, `Nonclustered` LONG)"));
        Assert.Equal(["Clustered", "Nonclustered"], create.Columns.Select(c => c.Name));
    }

    [Theory]
    [InlineData("CONSTRAINT pk PRIMARY KEY CLUSTERED (Id), CONSTRAINT uq UNIQUE NONCLUSTERED (V)")]
    [InlineData("CONSTRAINT pk PRIMARY KEY NONCLUSTERED (Id), CONSTRAINT uq UNIQUE CLUSTERED (V)")]
    public void A_table_created_with_the_word_has_the_same_indexes_as_one_without(string constraints)
    {
        IndexDef[] With(string clause)
        {
            string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "clustered-");
            try
            {
                using (var db = JetDatabase.Open(path, readOnly: false))
                    new QueryEngine(db).ExecuteNonQuery($"CREATE TABLE T (Id LONG, V TEXT(10), {clause})");
                using (var db = JetDatabase.Open(path))
                    return db.Catalog.FindTable("T")!.Indexes.OrderBy(i => i.Name).ToArray();
            }
            finally { TemporaryDatabase.Delete(path); }
        }

        IndexDef[] plain = With("CONSTRAINT pk PRIMARY KEY (Id), CONSTRAINT uq UNIQUE (V)");
        IndexDef[] clustered = With(constraints);

        Assert.Equal(2, plain.Length);
        Assert.Equal(
            plain.Select(i => (i.Name, i.IsPrimaryKey, i.IsUnique)),
            clustered.Select(i => (i.Name, i.IsPrimaryKey, i.IsUnique)));
    }

    /// <summary>A statement's parsed shape as text, with its lists spelled out — records compare lists by
    /// reference, so two identical parses would otherwise not be equal.</summary>
    private static string Describe(SqlStatement statement) => statement switch
    {
        CreateTableStatement c =>
            $"CREATE {c.Table} cols[{string.Join("; ", c.Columns)}] pk[{string.Join(",", c.PrimaryKey)}] " +
            $"pkName={c.PrimaryKeyName} uniques[{string.Join("; ", c.UniqueConstraints.Select(u => $"{u.Name}:{string.Join(",", u.Columns)}"))}] " +
            $"fks={c.ForeignKeys.Count} checks={c.CheckConstraints.Count}",
        AlterTableStatement a => $"ALTER {a.Table} " + a.Action switch
        {
            AddPrimaryKeyAction pk => $"pk {pk.Name}:{string.Join(",", pk.Columns)}",
            AddUniqueAction uq => $"unique {uq.Unique.Name}:{string.Join(",", uq.Unique.Columns)}",
            AddColumnAction add => $"add {add.Column}",
            var other => other.ToString(),
        },
        _ => statement.ToString(),
    };
}
