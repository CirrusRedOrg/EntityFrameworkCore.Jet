namespace LibRed.Catalog;

/// <summary>
/// The column data types supported by Jet/ACE, with their on-disk type codes.
/// </summary>
public enum JetDataType : byte
{
    Boolean = 0x01,
    Byte = 0x02,
    Int16 = 0x03,
    Int32 = 0x04,
    Currency = 0x05,
    Single = 0x06,
    Double = 0x07,
    DateTime = 0x08,
    Binary = 0x09,
    Text = 0x0A,
    Ole = 0x0B,
    Memo = 0x0C,
    Guid = 0x0F,
    FixedPoint = 0x10, // NUMERIC / DECIMAL
    Complex = 0x12,    // ACE complex/multi-value columns
    Int64 = 0x13,      // ACE 16: BIGINT (Large Number)
    DateTimeExtended = 0x14, // ACE 16: DATETIME2
}
