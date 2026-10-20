using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>
/// The names a SELECT's output columns go by — a projection's or a grouping's — as ACE gives them (verified vs ACE):
/// <list type="bullet">
/// <item><description>An alias is the column's name; a column reference, or a column a <c>*</c> returns, is named for the
/// column; any other expression is <c>Expr</c> and 1000 past its item's place in the select list, a bare <c>*</c> not
/// counted (<c>1, CompanyName, 2</c> is <c>Expr1000, CompanyName, Expr1002</c>).</description></item>
/// <item><description>A column reference written again, letter for letter but for case, is the same item twice: all but
/// its last are <c>Expr</c> names (<c>CompanyName, companyname</c> is <c>Expr1000, companyname</c>; <c>CompanyName,
/// [CompanyName]</c> is two items).</description></item>
/// <item><description>Names the rest share are told apart by their table — <c>Orders.CustomerID</c>,
/// <c>Customers.CustomerID</c>, a <c>*</c>'s columns included — except that an alias keeps its name, beside which an
/// unqualified reference becomes an <c>Expr</c> name instead. An <c>Expr</c> name is never told apart, from another or
/// from an alias spelled the same.</description></item>
/// </list>
/// </summary>
internal static class OutputNames
{
    internal enum Kind { Alias, Reference, Star, Expression }

    /// <param name="Item">The place of the item the column came from in the select list, a bare <c>*</c> not counted.</param>
    /// <param name="Kind">What the item is.</param>
    /// <param name="Name">The name it goes by on its own: the alias, or the column's.</param>
    /// <param name="Qualifier">The table it is told apart by: the one a reference names, or else the one it is read from.</param>
    /// <param name="Qualified">Whether a reference names its table.</param>
    /// <param name="Written">A reference as written, which tells it from another.</param>
    internal readonly record struct Output(int Item, Kind Kind, string Name, string? Qualifier, bool Qualified = true, string? Written = null);

    /// <summary>The output a select item that is not a <c>*</c> gives, over the columns it is evaluated against.</summary>
    public static Output For(SelectItem item, int index, string? sourceQualifier)
    {
        // A reference with no alias of its own — a name a bang gave it is still a reference's.
        if (item.Value is ColumnReference reference && (item.Alias is null || item.Written is not null))
            return new(index, Kind.Reference, item.Alias ?? reference.Column,
                reference.Table ?? sourceQualifier, reference.Table is not null,
                item.Written ?? reference.QualifiedName);
        return item.Alias is { } alias ? new(index, Kind.Alias, alias, null) : new(index, Kind.Expression, "", null);
    }

    /// <summary>The name of each output, in order.</summary>
    public static string[] Of(IReadOnlyList<Output> outputs)
    {
        var names = new string?[outputs.Count];
        string Generated(int i) => $"Expr{1000 + outputs[i].Item}";

        for (int i = 0; i < outputs.Count; i++)
            if (outputs[i].Kind == Kind.Expression)
                names[i] = Generated(i);

        for (int i = 0; i < outputs.Count; i++)
            if (outputs[i].Kind == Kind.Reference
                && names[i] is null)
                for (int j = i + 1; j < outputs.Count; j++)
                    if (outputs[j].Kind == Kind.Reference
                        && string.Equals(outputs[j].Written, outputs[i].Written, StringComparison.OrdinalIgnoreCase))
                    {
                        names[i] = Generated(i);
                        break;
                    }

        // Only collisions need grouping, and a one-column result cannot have one. Count the remaining
        // names directly rather than allocating a grouping and an index iterator for every output column.
        if (outputs.Count > 1)
        {
#pragma warning disable IDE0028 // Preserve case-insensitive grouping and the known output capacity.
            var shared = new Dictionary<string, (int Count, bool Aliased)>(outputs.Count, StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028
            for (int i = 0; i < outputs.Count; i++)
                if (names[i] is null)
                {
                    shared.TryGetValue(outputs[i].Name, out var group);
                    shared[outputs[i].Name] = (group.Count + 1, group.Aliased || outputs[i].Kind == Kind.Alias);
                }
            for (int i = 0; i < outputs.Count; i++)
                if (names[i] is null && shared[outputs[i].Name] is { Count: > 1 } group)
                {
                    names[i] = outputs[i] switch
                    {
                        { Kind: Kind.Alias } => outputs[i].Name,
                        { Kind: Kind.Reference, Qualified: false } when group.Aliased => Generated(i),
                        { Qualifier: { } table } => $"{table}.{outputs[i].Name}",
                        _ => outputs[i].Name,
                    };
                }
        }

        for (int i = 0; i < outputs.Count; i++)
            names[i] ??= outputs[i].Name;
        return names!;
    }
}
