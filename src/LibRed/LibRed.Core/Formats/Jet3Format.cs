namespace LibRed.Formats;

/// <summary>Access 97 (Jet 3.x) — 2 KB pages, MDB. Abstract until its offsets are set: nothing reads or writes
/// Jet 3 yet, and <see cref="JetFormatBase.FromVersionByte"/> refuses its version byte.</summary>
internal abstract class Jet3Format : JetFormatBase
{
    public override int PageSize => 2048;

    public override JetVersion Version => JetVersion.Version3;

    public override bool IsAccdb => false;
}