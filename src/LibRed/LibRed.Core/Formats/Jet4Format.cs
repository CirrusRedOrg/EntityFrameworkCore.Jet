namespace LibRed.Formats;

/// <summary>Access 2000–2003 (Jet 4.x) — 4 KB pages, MDB.</summary>
internal class Jet4Format : JetFormatBase
{
    public Jet4Format() => PageSize = 4096;

    public override JetVersion Version => JetVersion.Version4;
}
