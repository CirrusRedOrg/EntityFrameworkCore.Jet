namespace LibRed.Storage;

/// <summary>
/// The primary weight a character contributed, kept in case the next character copies it — a shadda doubling
/// it, or an iteration mark repeating it. Both encoders record one for every character, and almost every weight is
/// one or two bytes, so those are held inline and recording allocates nothing; a longer weight keeps an array.
/// </summary>
internal readonly struct CopiedPrimary
{
    private readonly byte[]? _bytes;
    private readonly byte _first, _second;

    /// <param name="bytes">A weight's bytes, which the caller does not change afterwards.</param>
    public CopiedPrimary(byte[] bytes)
    {
        _bytes = bytes;
        Length = bytes.Length;
    }

    public CopiedPrimary(byte only)
    {
        _first = only;
        Length = 1;
    }

    public CopiedPrimary(byte first, byte second)
    {
        (_first, _second) = (first, second);
        Length = 2;
    }

    /// <summary>A weight as it stands, copied only when it is longer than two bytes.</summary>
    public static CopiedPrimary Of(ReadOnlySpan<byte> weight) => weight.Length switch
    {
        1 => new CopiedPrimary(weight[0]),
        2 => new CopiedPrimary(weight[0], weight[1]),
        _ => new CopiedPrimary(weight.ToArray()),
    };

    /// <summary>How many bytes the weight has; zero when nothing was contributed.</summary>
    public int Length { get; }

    public void AppendTo(List<byte> primaries)
    {
        if (_bytes is not null)
        {
            primaries.AddRange(_bytes);
            return;
        }
        if (Length > 0) primaries.Add(_first);
        if (Length > 1) primaries.Add(_second);
    }

    /// <summary>The bytes as an array — for the copying itself, which is rare, rather than the recording.</summary>
    public byte[] ToArray() => _bytes ?? (Length switch { 0 => [], 1 => [_first], _ => [_first, _second] });
}
