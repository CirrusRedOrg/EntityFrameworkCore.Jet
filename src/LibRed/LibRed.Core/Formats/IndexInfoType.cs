namespace LibRed.Formats;

/// <summary>An index-info block's type byte (<see cref="JetFormatBase.IndexInfoTypeOffset"/>).</summary>
public enum IndexInfoType : byte
{
    Secondary = 0x00,
    Primary = 0x01,
    Foreign = 0x02,
}