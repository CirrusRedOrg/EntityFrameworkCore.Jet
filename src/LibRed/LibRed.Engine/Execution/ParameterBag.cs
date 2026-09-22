namespace LibRed.Engine.Execution;

/// <summary>
/// Resolves query parameter references (<c>@name</c>) to supplied values. Names are matched
/// case-insensitively and the leading <c>@</c> is optional on both the reference and the key,
/// so a value supplied as "p0" satisfies a "@p0" reference and vice versa.
/// </summary>
/// <remarks>
/// A <see cref="TimeSpan"/> or <see cref="TimeOnly"/> resolves to the <see cref="DateTime"/> Jet stores a time as —
/// the time on the OLE epoch, 1899-12-30 — so everything that reads a parameter, saving it included, still sees only
/// a date, as the evaluator expects. What it was is kept too (<see cref="Duration"/>), because a date plus or less a
/// span is a date, where a date less a date is a day count.
/// </remarks>
internal sealed class ParameterBag
{
    /// <summary>The OLE epoch: Jet stores a time as the epoch plus the time of day.</summary>
    private static readonly DateTime OleEpoch = new(1899, 12, 30);

    // IDE0028's only fix here is `[]`, which would silently drop the comparer and make parameter
    // lookup case-sensitive.
#pragma warning disable IDE0028
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028

    public ParameterBag(IReadOnlyDictionary<string, object?>? values)
    {
        if (values is null) return;
        foreach (var (key, value) in values)
            _values[Normalize(key)] = value;
    }

    public object? Resolve(string name)
    {
        if (name.StartsWith('?'))
            throw new NotSupportedException("Positional ('?') parameters are not supported yet; use named (@name) parameters.");
        if (_values.TryGetValue(Normalize(name), out object? value))
            return value switch
            {
                TimeSpan span => OleEpoch + span,
                TimeOnly time => OleEpoch + time.ToTimeSpan(),
                _ => value,
            };
        throw new InvalidOperationException($"No value was supplied for parameter '{name}'.");
    }

    /// <summary>The span a <see cref="TimeSpan"/> or <see cref="TimeOnly"/> parameter was bound to; null for any other
    /// value, an unbound name or a positional reference.</summary>
    public TimeSpan? Duration(string name) =>
        name.StartsWith('?') || !_values.TryGetValue(Normalize(name), out object? value) ? null
        : value switch
        {
            TimeSpan span => span,
            TimeOnly time => time.ToTimeSpan(),
            _ => null,
        };

    /// <summary>The type of the value <paramref name="name"/> resolves to; null when it is unbound, positional or Null,
    /// which declares nothing.</summary>
    public Type? TypeOf(string name) =>
        name.StartsWith('?') || !_values.TryGetValue(Normalize(name), out object? value) || value is null ? null
        : value is TimeSpan or TimeOnly ? typeof(DateTime)
        : value.GetType();

    private static string Normalize(string name) => name.TrimStart('@');
}