using LibRed.Catalog;
using LibRed.Sql.Ast;

namespace LibRed.Engine.Execution;

/// <summary>
/// Maps a CREATE TABLE column's declared SQL/Access type name to a storage <see cref="ColumnSpec"/>.
/// Aliases follow Microsoft's "Equivalent ANSI SQL Data Types" reference:
/// https://learn.microsoft.com/office/client-developer/access/desktop-database-reference/equivalent-ansi-sql-data-types
/// EFCore.Jet itself defaults to a subset (counter, long, single, double, currency, datetime,
/// yesno, guid, decimal, varchar, …), but every documented alias is accepted.
/// </summary>
internal static class AccessTypeMapper
{
    public static ColumnSpec ToColumnSpec(ColumnDefinition column)
    {
        // Collapse any internal whitespace so two-word aliases ("character  varying") match.
        string t = string.Join(' ', column.TypeName.ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return t switch
        {
            "COUNTER" or "AUTOINCREMENT" or "IDENTITY"
                => Fixed(column, JetDataType.Int32, 4, autoNumber: true),
            "INTEGER" or "INT" or "LONG" or "INTEGER4"
                => Fixed(column, JetDataType.Int32, 4),
            "SMALLINT" or "SHORT" or "INTEGER2"
                => Fixed(column, JetDataType.Int16, 2),
            "BYTE" or "TINYINT" or "INTEGER1"
                => Fixed(column, JetDataType.Byte, 1),
            "BIGINT"
                => Fixed(column, JetDataType.Int64, 8),
            "REAL" or "SINGLE" or "IEEESINGLE" or "FLOAT4"
                => Fixed(column, JetDataType.Single, 4),
            "FLOAT" or "DOUBLE" or "IEEEDOUBLE" or "FLOAT8" or "NUMBER"
                => Fixed(column, JetDataType.Double, 8),
            "CURRENCY" or "MONEY"
                => Fixed(column, JetDataType.Currency, 8),
            "DATETIME" or "DATE" or "TIME" or "TIMESTAMP"
                => Fixed(column, JetDataType.DateTime, 8),
            "BIT" or "YESNO" or "BOOLEAN" or "LOGICAL" or "LOGICAL1"
                => Fixed(column, JetDataType.Boolean, 1),
            "GUID" or "UNIQUEIDENTIFIER"
                => Fixed(column, JetDataType.Guid, 16),
            "DECIMAL" or "NUMERIC"
                => new ColumnSpec(column.Name, JetDataType.FixedPoint, 17, IsFixedLength: true,
                    Precision: (byte)(column.Size ?? 18), Scale: (byte)(column.Scale ?? 0)),
            "TEXT" or "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR" or "STRING" or "ALPHANUMERIC"
            or "CHARACTER" or "CHARACTER VARYING" or "CHAR VARYING"
                // Access TEXT length is in characters; on disk it is UTF-16 (2 bytes each).
                => new ColumnSpec(column.Name, JetDataType.Text, (column.Size ?? 255) * 2, IsFixedLength: false),
            "BINARY" or "VARBINARY" or "BIT VARYING"
                => new ColumnSpec(column.Name, JetDataType.Binary, column.Size ?? 255, IsFixedLength: false),

            // Long-value (LVAL-page) columns: recognised but not writable yet — fail clearly.
            "MEMO" or "LONGTEXT" or "LONGCHAR" or "NOTE"
                => throw new NotSupportedException($"Memo/long-text columns ('{column.TypeName}') cannot be created yet (long values are not writable)."),
            "OLEOBJECT" or "IMAGE" or "LONGBINARY"
                => throw new NotSupportedException($"OLE/long-binary columns ('{column.TypeName}') cannot be created yet (long values are not writable)."),

            _ => throw new NotSupportedException($"CREATE TABLE column type '{column.TypeName}' is not supported yet."),
        };
    }

    private static ColumnSpec Fixed(ColumnDefinition column, JetDataType type, int length, bool autoNumber = false)
        => new(column.Name, type, length, IsFixedLength: true, IsAutoNumber: autoNumber);
}
