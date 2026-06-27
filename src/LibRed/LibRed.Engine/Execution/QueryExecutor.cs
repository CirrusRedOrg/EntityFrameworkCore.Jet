using LibRed.Engine.Plan;

namespace LibRed.Engine.Execution;

/// <summary>
/// Interprets a logical plan tree against the storage layer, producing a
/// <see cref="ResultSet"/> for queries or an affected-row count for DML.
/// </summary>
public sealed class QueryExecutor(JetDatabase database)
{
    private readonly JetDatabase _database = database;

    /// <summary>Executes a query plan and returns its rows.</summary>
    public ResultSet ExecuteQuery(PlanNode plan)
    {
        // TODO: recursively evaluate the plan tree. Each node type maps to an
        // execution operator that pulls rows from its children; ScanNode pulls from
        // a Core Table cursor.
        _ = (_database, plan);
        return ResultSet.Empty;
    }

    /// <summary>Executes a DML plan and returns the number of affected rows.</summary>
    public int ExecuteNonQuery(PlanNode plan)
    {
        // TODO: insert/update/delete against the storage layer.
        _ = plan;
        return 0;
    }
}
