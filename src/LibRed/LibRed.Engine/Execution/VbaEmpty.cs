namespace LibRed.Engine.Execution;

/// <summary>
/// VBA's <c>Empty</c>: what Access's one-argument <c>Nz</c> gives for a Null (verified vs Access). It is written out
/// as <c>""</c>, and read as 0 where a number is wanted and as <c>""</c> where text is — so <c>Nz(Null) + 2</c> is 2
/// and <c>Nz(Null) &amp; 'x'</c> is <c>'x'</c>. Only <c>Nz</c> makes one.
/// </summary>
internal sealed class VbaEmpty
{
    public static readonly VbaEmpty Value = new();

    private VbaEmpty() { }

    public override string ToString() => "";
}
