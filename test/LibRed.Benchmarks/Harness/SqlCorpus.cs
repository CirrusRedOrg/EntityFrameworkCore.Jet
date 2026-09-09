namespace LibRed.Benchmarks.Harness;

/// <summary>
/// Every statement the read-side suites benchmark, in one place. Adding a benchmark is adding a line here —
/// the suites all draw their <c>[ArgumentsSource]</c> from this list, so a new case is picked up by the
/// end-to-end timings, the pipeline-phase breakdown and the ACE head-to-head at once.
/// </summary>
/// <remarks>
/// Two rules keep the numbers meaningful. Every case must be <b>deterministic</b> — no <c>Now()</c>, no
/// unseeded randomness — so two runs of the same commit are comparable. And every case must have a
/// <b>fixed result size</b> independent of the scale factor where that is possible, or scale linearly with
/// it where the point of the case is volume; a case whose row count jumps unpredictably with scale produces
/// a chart that says nothing about the engine.
/// <para>Wildcards are written as <c>%</c>/<c>_</c>: LibRed accepts both Access and ANSI forms
/// (ExpressionEvaluator.Like), while the ACE OLE DB provider only understands the ANSI ones.</para>
/// </remarks>
public static class SqlCorpus
{
    /// <summary>Reads against the generated corpus. Row counts scale with <see cref="Corpus"/>'s scale factor
    /// except where a TOP/point predicate pins them.</summary>
    public static readonly IReadOnlyList<SqlCase> Synthetic =
    [
        // --- Scans: the cost floor. Every other read number should be read against these. ------------------
        new("scan.count_star",        "SELECT COUNT(*) FROM Bench"),
        new("scan.one_column",        "SELECT Id FROM Bench"),
        new("scan.three_columns",     "SELECT Id, K, Label FROM Bench"),
        new("scan.star",              "SELECT * FROM Bench"),
        new("scan.long_text",         "SELECT Id, Note FROM Bench"),

        // --- Predicates: index seek vs scan, and the selectivity that separates them. ----------------------
        new("predicate.pk_point",     "SELECT Id, K, Label FROM Bench WHERE Id = 4242"),
        new("predicate.index_eq",     "SELECT Id, G FROM Bench WHERE K = 500"),
        new("predicate.index_range",  "SELECT Id FROM Bench WHERE K BETWEEN 100 AND 110"),
        new("predicate.index_open_range", "SELECT Id FROM Bench WHERE K > 990"),
        new("predicate.composite_eq", "SELECT Id FROM Bench WHERE K = 500 AND Bucket = 3"),
        new("predicate.text_index_eq", "SELECT Id, K FROM Bench WHERE Label = 'label-00042'"),
        new("predicate.unindexed_eq", "SELECT Id FROM Bench WHERE G = 7"),
        new("predicate.like_prefix",  "SELECT Id FROM Bench WHERE Label LIKE 'label-0004%'"),
        new("predicate.like_contains", "SELECT Id FROM Bench WHERE Note LIKE '%qx%'"),
        new("predicate.in_list",      "SELECT Id FROM Bench WHERE K IN (1, 17, 42, 99, 500, 777)"),
        new("predicate.or_two_indexes", "SELECT Id FROM Bench WHERE K = 500 OR Label = 'label-00042'"),
        new("predicate.is_null",      "SELECT Id FROM Bench WHERE Note IS NULL"),
        new("predicate.boolean",      "SELECT COUNT(*) FROM Bench WHERE Flag = True"),
        new("predicate.date_range",   "SELECT Id FROM Bench WHERE Created BETWEEN #2020-01-01# AND #2020-01-02#"),
        new("predicate.negated",      "SELECT COUNT(*) FROM Bench WHERE NOT (K = 500)"),
        new("predicate.arithmetic",   "SELECT Id FROM Bench WHERE K * 2 = 1000"),

        // --- Joins: the access-path choice matters most here. ---------------------------------------------
        new("join.index_nested_loop", "SELECT b.Id, c.Qty FROM Bench b INNER JOIN BenchChild c ON b.Id = c.ParentId WHERE b.K = 500"),
        new("join.full_indexed",      "SELECT b.Id, c.Qty FROM Bench b INNER JOIN BenchChild c ON b.Id = c.ParentId"),
        new("join.hash_unindexed",    "SELECT b.Id, s.Descr FROM Bench b INNER JOIN BenchSmall s ON b.G = s.K"),
        new("join.small_probe",       "SELECT b.Id FROM BenchSmall s INNER JOIN Bench b ON b.K = s.K"),
        new("join.left_outer",        "SELECT b.Id, c.Qty FROM Bench b LEFT JOIN BenchChild c ON b.Id = c.ParentId WHERE b.K = 500"),
        new("join.three_way",         "SELECT b.Id, c.Qty, s.Descr FROM Bench b INNER JOIN BenchChild c ON b.Id = c.ParentId INNER JOIN BenchSmall s ON b.G = s.K WHERE b.K = 500"),
        new("join.self",              "SELECT a.Id, b.Id FROM Bench a INNER JOIN Bench b ON a.G = b.G WHERE a.K = 500"),
        new("join.cross_small",       "SELECT COUNT(*) FROM BenchSmall a, BenchSmall b"),

        // --- Aggregation: grouping cost against cardinality. ----------------------------------------------
        new("agg.global",             "SELECT COUNT(*), SUM(Amount), AVG(Ratio), MIN(Id), MAX(Id) FROM Bench"),
        new("agg.group_low_card",     "SELECT K, COUNT(*) FROM Bench GROUP BY K"),
        new("agg.group_high_card",    "SELECT G, COUNT(*) FROM Bench GROUP BY G"),
        new("agg.group_text_key",     "SELECT Label, COUNT(*) FROM Bench GROUP BY Label"),
        new("agg.group_multi_agg",    "SELECT K, COUNT(*), SUM(Amount), AVG(Ratio) FROM Bench GROUP BY K"),
        new("agg.having",             "SELECT K, COUNT(*) FROM Bench GROUP BY K HAVING COUNT(*) > 5"),
        new("agg.count_distinct",     "SELECT COUNT(DISTINCT G) FROM Bench"),
        new("agg.distinct_rows",      "SELECT DISTINCT K, G FROM Bench"),
        new("agg.group_over_join",    "SELECT b.K, COUNT(*), SUM(c.Qty) FROM Bench b INNER JOIN BenchChild c ON b.Id = c.ParentId GROUP BY b.K"),

        // --- Sorting and TOP: sorted-index read vs an actual sort. ----------------------------------------
        new("sort.by_indexed",        "SELECT Id, K FROM Bench ORDER BY K"),
        new("sort.by_unindexed",      "SELECT Id, Ratio FROM Bench ORDER BY Ratio"),
        new("sort.by_two_columns",    "SELECT Id FROM Bench ORDER BY K, Id DESC"),
        new("sort.top_10",            "SELECT TOP 10 Id, Ratio FROM Bench ORDER BY Ratio"),
        new("sort.top_10_indexed",    "SELECT TOP 10 Id FROM Bench ORDER BY Id"),
        new("sort.top_percent",       "SELECT TOP 5 PERCENT Id FROM Bench ORDER BY Id"),

        // --- Subqueries: correlated evaluation is the expensive shape. ------------------------------------
        new("sub.scalar_correlated",  "SELECT b.Id, (SELECT COUNT(*) FROM BenchChild c WHERE c.ParentId = b.Id) FROM Bench b WHERE b.K = 500"),
        new("sub.in_uncorrelated",    "SELECT Id FROM Bench WHERE K IN (SELECT K FROM BenchSmall)"),
        new("sub.exists_correlated",  "SELECT b.Id FROM Bench b WHERE b.K = 500 AND EXISTS (SELECT 1 FROM BenchChild c WHERE c.ParentId = b.Id AND c.Qty = 3)"),
        new("sub.not_exists",         "SELECT b.Id FROM Bench b WHERE b.K = 500 AND NOT EXISTS (SELECT 1 FROM BenchChild c WHERE c.ParentId = b.Id AND c.Qty = 99)"),
        new("sub.derived_table",      "SELECT t.K, t.N FROM (SELECT K, COUNT(*) AS N FROM Bench GROUP BY K) t WHERE t.N > 5"),

        // --- Set operations. ------------------------------------------------------------------------------
        new("setop.union_all",        "SELECT Id FROM Bench WHERE K = 500 UNION ALL SELECT Id FROM Bench WHERE K = 501"),
        new("setop.union_distinct",   "SELECT K FROM Bench WHERE K < 50 UNION SELECT K FROM Bench WHERE K < 80"),

        // --- Expressions: per-row evaluator cost, isolated by running over the whole table. ----------------
        new("expr.arithmetic",        "SELECT (Amount * 2) + Ratio FROM Bench"),
        new("expr.concat",            "SELECT Label & '/' & K FROM Bench"),
        new("expr.string_functions",  "SELECT UCASE(Label), LEFT(Note, 10), LEN(Note) FROM Bench"),
        new("expr.date_functions",    "SELECT YEAR(Created), MONTH(Created), DAY(Created) FROM Bench"),
        new("expr.date_add",          "SELECT DATEADD('d', 1, Created) FROM Bench"),
        new("expr.math_functions",    "SELECT ABS(Ratio), ROUND(Amount, 2), INT(Ratio * 100) FROM Bench"),
        new("expr.iif",               "SELECT IIF(Flag, 'yes', 'no') FROM Bench"),
        new("expr.nested_iif",        "SELECT IIF(K < 100, 'low', IIF(K < 500, 'mid', 'high')) FROM Bench"),

        // --- Window functions: LibRed-only. ACE has no OVER clause at all, which is one of the reasons
        //     LibRed exists — the head-to-head skips these rather than reporting them as ACE failures.
        new("window.row_number",      "SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) FROM Bench WHERE K = 500", RunsOnAce: false),
        new("window.rank_partitioned", "SELECT Id, RANK() OVER (PARTITION BY G ORDER BY Id) FROM Bench WHERE K = 500", RunsOnAce: false),

        // --- Catalog surface: what scaffolding and migrations pay on every model build. --------------------
        // The view name is one quoted identifier with a dot in it, not schema-qualified — that is the shape
        // EFCore.Jet's data layer emits and the shape InformationSchema recognises.
        new("catalog.schema_tables",  "SELECT * FROM `INFORMATION_SCHEMA.TABLES`", RunsOnAce: false),
        new("catalog.schema_columns", "SELECT * FROM `INFORMATION_SCHEMA.COLUMNS`", RunsOnAce: false),
    ];

    /// <summary>Reads against the tracked Northwind sample. Small — these measure query <i>shape</i> cost
    /// (text keys, a table name with a space, skewed group sizes), not volume.</summary>
    public static readonly IReadOnlyList<SqlCase> Northwind =
    [
        new("nw.orders_scan",         "SELECT OrderID, CustomerID, OrderDate FROM Orders", CaseSource.Northwind),
        new("nw.text_pk_lookup",      "SELECT CompanyName, City FROM Customers WHERE CustomerID = 'ALFKI'", CaseSource.Northwind),
        new("nw.like_prefix",         "SELECT CustomerID FROM Customers WHERE CompanyName LIKE 'A%'", CaseSource.Northwind),
        new("nw.orders_details_join", "SELECT o.OrderID, d.ProductID, d.Quantity FROM Orders o INNER JOIN `Order Details` d ON o.OrderID = d.OrderID", CaseSource.Northwind),
        new("nw.three_way_join",      "SELECT c.CompanyName, o.OrderID, d.ProductID FROM Customers c INNER JOIN Orders o ON c.CustomerID = o.CustomerID INNER JOIN `Order Details` d ON o.OrderID = d.OrderID", CaseSource.Northwind),
        new("nw.left_join",           "SELECT c.CustomerID, o.OrderID FROM Customers c LEFT JOIN Orders o ON c.CustomerID = o.CustomerID", CaseSource.Northwind),
        new("nw.unindexed_join",      "SELECT o.OrderID, c.CompanyName FROM Orders o INNER JOIN Customers c ON o.ShipCity = c.City", CaseSource.Northwind),
        new("nw.group_by_customer",   "SELECT CustomerID, COUNT(*) FROM Orders GROUP BY CustomerID", CaseSource.Northwind),
        new("nw.group_agg_join",      "SELECT o.CustomerID, SUM(d.Quantity) FROM Orders o INNER JOIN `Order Details` d ON o.OrderID = d.OrderID GROUP BY o.CustomerID", CaseSource.Northwind),
        new("nw.order_by_text",       "SELECT CompanyName FROM Customers ORDER BY CompanyName", CaseSource.Northwind),
        new("nw.top_10",              "SELECT TOP 10 OrderID FROM Orders ORDER BY OrderDate DESC", CaseSource.Northwind),
        new("nw.correlated_subquery", "SELECT c.CustomerID, (SELECT COUNT(*) FROM Orders o WHERE o.CustomerID = c.CustomerID) FROM Customers c", CaseSource.Northwind),
    ];

    /// <summary>Every read case, both sources.</summary>
    public static IEnumerable<SqlCase> All => Synthetic.Concat(Northwind);

    /// <summary>A small, hand-picked spread — one cheap, one mid, one expensive shape — for the suites that
    /// would take hours over the full corpus (the four-phase pipeline breakdown, the ACE head-to-head).</summary>
    public static readonly IReadOnlyList<SqlCase> Representative =
    [
        Find("predicate.pk_point"),
        Find("predicate.index_range"),
        Find("scan.three_columns"),
        Find("join.index_nested_loop"),
        Find("join.hash_unindexed"),
        Find("agg.group_low_card"),
        Find("sort.top_10"),
        // Its indexed twin, kept alongside deliberately: one orders by an unindexed column and must sort, the
        // other matches an index and is read straight off it. Same statement shape, and the pair is what shows
        // the ordered-index read is doing anything at all.
        Find("sort.top_10_indexed"),
        Find("sub.scalar_correlated"),
    ];

    public static SqlCase Find(string name) =>
        All.FirstOrDefault(c => c.Name == name)
        ?? throw new ArgumentOutOfRangeException(nameof(name), name, "No such case in the corpus.");
}
