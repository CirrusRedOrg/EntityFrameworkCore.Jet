namespace LibRed.Engine.Execution;

/// <summary>
/// Resolves query parameter references (<c>@name</c>) to supplied values. Names are matched
/// case-insensitively and the leading <c>@</c> is optional on both the reference and the key,
/// so a value supplied as "p0" satisfies a "@p0" reference and vice versa.
/// </summary>
internal sealed class ParameterBag
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

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
            return value;
        throw new InvalidOperationException($"No value was supplied for parameter '{name}'.");
    }

    private static string Normalize(string name) => name.TrimStart('@');
}
