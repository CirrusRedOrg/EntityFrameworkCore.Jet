using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using LibRed.Engine.Plan;
using LibRed.Sql.Ast;
using Linq = System.Linq.Expressions;

namespace LibRed.Engine.Planning;

/// <summary>
/// A post-planning pass that tells each table read which columns the query can ever look at, so the rest are
/// never decoded. Decoding is most of a scan's cost, and a query rarely reads every column: a three-column
/// SELECT over a ten-column table decoded all ten, and a GROUP BY held all ten of every row alive.
/// </summary>
/// <remarks>
/// <para>An undecoded column reads as null, so the pass has to be right about two things.</para>
/// <para><b>Which names are read.</b> Every column reference anywhere in the plan counts — including inside a
/// subquery, whose correlated references read the outer tables — by name alone, whatever its qualifier. A table
/// decodes each column any reference could mean, which may be more than the query needs but is never less. The
/// references are found by walking every public property of the plan and AST records rather than a list of
/// the places expressions sit, so a clause added to a node later cannot be missed; a value of a type the walk
/// does not know stops the pass for the whole plan.</para>
/// <para><b>Where a table's rows can surface.</b> Names only cover what an expression reads. A row can also
/// reach the output whole: <c>SELECT *</c> plans no projection, a set operation or DISTINCT compares whole rows,
/// and DISTINCTROW dedupes on the underlying rows. So a read is pruned only below a node that builds its output
/// from expressions — a projection or an aggregate without a star in it — and nothing below a node this pass
/// does not know is pruned at all.</para>
/// </remarks>
internal static class ColumnPruning
{
    public static PlanNode Apply(PlanNode plan)
    {
        if (!HasHiddenRead(plan, visible: true))
            return plan;
        HashSet<string>? names = ReferencedNames(plan);
        return names is null ? plan : Rewrite(plan, visible: true, names);
    }

    /// <summary>Whether any table read sits where pruning could apply — so a plan with none is not walked. Its
    /// arms are <see cref="Rewrite"/>'s: a node it descends through, Rewrite must too, and on the same terms.</summary>
    private static bool HasHiddenRead(PlanNode node, bool visible) => node switch
    {
        ScanNode or IndexSeekNode or IndexRangeSeekNode => !visible,
        ProjectNode p => HasHiddenRead(p.Input, HasStar(p.Projection)),
        AggregateNode a => HasHiddenRead(a.Input, HasStar(a.Projection)),
        DistinctNode or SetOperationNode => node.Children.Any(c => HasHiddenRead(c, visible: true)),
        DerivedTableNode { Columns: not null } => false,
        FilterNode or SortNode or LimitNode or DerivedTableNode or WindowNode
            or JoinNode or HashJoinNode => node.Children.Any(c => HasHiddenRead(c, visible)),
        _ => false,
    };

    /// <param name="node">The subtree to rewrite.</param>
    /// <param name="visible">Whether this node's rows can reach the output whole, not only through expressions.</param>
    /// <param name="names">The column names the whole plan reads.</param>
    private static PlanNode Rewrite(PlanNode node, bool visible, HashSet<string> names) => node switch
    {
        ScanNode s => visible ? s : s with { Decode = names },
        IndexSeekNode s => visible ? s : s with { Decode = names },
        IndexRangeSeekNode s => visible ? s : s with { Decode = names },

        // Built from expressions, so what they read is all that is read — unless a star passes a row through.
        ProjectNode p => p with { Input = Rewrite(p.Input, HasStar(p.Projection), names) },
        AggregateNode a => a with { Input = Rewrite(a.Input, HasStar(a.Projection), names) },

        // Rows pass through as they are, so they are as visible as this node's own.
        FilterNode f => f with { Input = Rewrite(f.Input, visible, names) },
        SortNode s => s with { Input = Rewrite(s.Input, visible, names) },
        LimitNode l => l with { Input = Rewrite(l.Input, visible, names) },
        // Duplicate elimination observes every input value, even under a narrow outer projection.
        DistinctNode d => d with { Input = Rewrite(d.Input, visible: true, names) },
        // A column list renames by position. Physical names cannot be inferred from outer references, so
        // keep this subtree intact, including any joins that would otherwise drop unnamed columns.
        DerivedTableNode { Columns: not null } dt => dt,
        DerivedTableNode dt => dt with { Input = Rewrite(dt.Input, visible, names) },
        WindowNode w => w with { Input = Rewrite(w.Input, visible, names) },
        // A join builds each output row anew, so where its rows cannot surface whole it builds them from the read
        // columns alone. Dropping a column no reference names cannot change what any reference resolves to: the
        // names are matched whatever their qualifier, so every column a reference could mean stays.
        JoinNode j => j with
        {
            Left = Rewrite(j.Left, visible, names),
            Right = Rewrite(j.Right, visible, names),
            Keep = visible ? null : names,
        },
        HashJoinNode h => h with
        {
            Left = Rewrite(h.Left, visible, names),
            Right = Rewrite(h.Right, visible, names),
            Keep = visible ? null : names,
        },
        // Set operations compare whole rows and match columns positionally across differently named inputs.
        SetOperationNode so => so with { Left = Rewrite(so.Left, visible: true, names), Right = Rewrite(so.Right, visible: true, names) },

        // DistinctRowNode compares whole underlying rows; anything else is unknown. Neither is pruned below.
        _ => node,
    };

    /// <summary>The decode mask for <paramref name="table"/> from the names a read may use — by
    /// <see cref="LibRed.Catalog.ColumnDef.Index"/>, as <c>RowCodec</c> takes it — or null to decode every
    /// column, when there are no names or every column is among them.</summary>
    internal static bool[]? Mask(LibRed.Catalog.TableDefinition table, IReadOnlySet<string>? names)
    {
        if (names is null) return null;
        var mask = new bool[table.Columns.Count];
        bool all = true;
        foreach (LibRed.Catalog.ColumnDef column in table.Columns)
            all &= mask[column.Index] = names.Contains(column.Name);
        return all ? null : mask;
    }

    /// <summary>Whether a projection passes some input row through whole (<c>*</c> or <c>table.*</c>). COUNT(*)'s
    /// star is an argument, not a projection item, so it does not count.</summary>
    private static bool HasStar(IReadOnlyList<SelectItem> projection) =>
        projection.Any(item => item.Value is StarExpression or QualifiedStarExpression);

    /// <summary>Every column name any reference in <paramref name="root"/> uses, or null when the walk met a value
    /// it cannot see into — then nothing is pruned.</summary>
    internal static HashSet<string>? ReferencedNames(object root)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return Collect(root, names) ? names : null;
    }

    private static bool Collect(object? value, HashSet<string> names)
    {
        switch (value)
        {
            case null or string or byte[] or decimal or DateTime or DateTimeOffset or TimeSpan or Guid or Type:
                return true;
            case ColumnReference reference:
                names.Add(reference.Column);
                return true;
            case IEnumerable items:
                foreach (object? item in items)
                    if (!Collect(item, names)) return false;
                return true;
        }

        Type type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type.Namespace == "LibRed.Catalog")
            return true;
        if (type.Namespace is not ("LibRed.Sql.Ast" or "LibRed.Engine.Plan"))
            return false;

        foreach (Func<object, object?> property in Getters.GetOrAdd(type, ReadableProperties))
        {
            object? child;
#pragma warning disable CA1031 // Whatever a computed property throws, it means the walk cannot see into it.
            try
            {
                child = property(value);
            }
            catch (Exception)
            {
                return false;   // a computed property this shape cannot answer: be safe, prune nothing
            }
#pragma warning restore CA1031

            if (!Collect(child, names)) return false;
        }

        return true;
    }

    // Compiled rather than read through PropertyInfo.GetValue: the walk runs for every statement planned, and
    // reflective reads made it several microseconds — a fifth of a primary-key lookup.
    private static readonly ConcurrentDictionary<Type, Func<object, object?>[]> Getters = new();

    // PlanNode.Children repeats the inputs its own properties already hold.
    private static Func<object, object?>[] ReadableProperties(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0
                && !(p.Name == nameof(PlanNode.Children) && typeof(PlanNode).IsAssignableFrom(type)))
            .Select(Getter)];

    private static Func<object, object?> Getter(PropertyInfo property)
    {
        Linq.ParameterExpression instance = Linq.Expression.Parameter(typeof(object));
        Linq.Expression read = Linq.Expression.Property(Linq.Expression.Convert(instance, property.DeclaringType!), property);
        return Linq.Expression.Lambda<Func<object, object?>>(Linq.Expression.Convert(read, typeof(object)), instance).Compile();
    }
}