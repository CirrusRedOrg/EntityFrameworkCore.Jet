namespace LibRed.Formats;

/// <summary>Access 2007 (ACE 12) — first ACCDB format.</summary>
internal class Jet12Format : Jet4Format
{
    public override JetVersion Version => JetVersion.Version12_2007;

    public override bool IsAccdb => true;
}
