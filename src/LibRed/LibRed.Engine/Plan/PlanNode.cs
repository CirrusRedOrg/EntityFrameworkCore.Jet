namespace LibRed.Engine.Plan;

/// <summary>
/// Base type for logical query-plan nodes. A bound statement is lowered into a tree
/// of these (Scan → Filter → Project → …). Keeping the plan separate from both the
/// AST and the executor lets optimisation passes (predicate pushdown, join
/// reordering, index selection) run as tree rewrites, and lets new SQL features add
/// new node types without touching parsing or execution wiring.
/// </summary>
public abstract record PlanNode
{
    /// <summary>Input nodes feeding this operator (empty for leaves such as Scan).</summary>
    public virtual IReadOnlyList<PlanNode> Children => [];
}
