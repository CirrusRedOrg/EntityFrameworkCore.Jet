namespace LibRed.Formats;

/// <summary>Access 97 (Jet 3.x) — 2 KB pages, MDB.</summary>
internal sealed class Jet3Format : JetFormatBase
{
    public Jet3Format() => PageSize = 2048;

    public override JetVersion Version => JetVersion.Version3;
}
