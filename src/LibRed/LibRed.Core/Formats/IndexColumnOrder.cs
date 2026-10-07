namespace LibRed.Formats;

/// <summary>An index-data block column slot's order byte (<see cref="JetFormatBase.IndexDataColumnOrderOffset"/>).</summary>
public enum IndexColumnOrder : byte
{
    Descending = 0x00,
    Ascending = 0x01,
}