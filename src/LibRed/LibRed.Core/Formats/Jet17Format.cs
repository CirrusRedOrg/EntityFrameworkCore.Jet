namespace LibRed.Formats;

/// <summary>Access 2019/2021 (ACE 17) — ACCDB.</summary>
internal sealed class Jet17Format : Jet16Format
{
    public override JetVersion Version => JetVersion.Version17_2019;
}
