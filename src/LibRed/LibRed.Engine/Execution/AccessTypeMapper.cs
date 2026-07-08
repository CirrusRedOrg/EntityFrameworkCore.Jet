using LibRed.Catalog;
using LibRed.Formats;
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
    public static ColumnSpec ToColumnSpec(ColumnDefinition column, JetVersion version) =>
        MapType(column, version) with { IsNullable = !column.NotNull };

    private static ColumnSpec MapType(ColumnDefinition column, JetVersion version)
    {
        // Collapse any internal whitespace so two-word aliases ("character  varying") match.
        string t = string.Join(' ', column.TypeName.ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // BIGINT and DATETIME2 are Access 2016 (ACE 16) additions. On any older format the engine cannot
        // represent them, so refuse to create/alter a column to one rather than silently producing a file the
        // real Access version couldn't open. Gated here, the single choke point for every DDL type name.
        if (t is "BIGINT" or "DATETIME2" && version < JetVersion.Version16_2016)
            throw new NotSupportedException(
                $"Column type '{column.TypeName}' requires Access 2016 (ACE 16) or later; this database is {version}.");

        return t switch
        {
            // AutoNumber. COUNTER(seed, increment) parses seed/increment as the (size, scale) pair; a plain
            // COUNTER defaults to 1/1. INTEGER IDENTITY(seed, increment) is the ANSI-style spelling.
            "COUNTER" or "AUTOINCREMENT" or "IDENTITY"
            or "INTEGER IDENTITY" or "INT IDENTITY" or "LONG IDENTITY"
                => new ColumnSpec(column.Name, JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true,
                    Seed: column.Size ?? 1, Increment: column.Scale ?? 1),
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
            "FLOAT" or "DOUBLE" or "DOUBLE PRECISION" or "IEEEDOUBLE" or "FLOAT8" or "NUMBER"
                => Fixed(column, JetDataType.Double, 8),
            "CURRENCY" or "MONEY"
                => Fixed(column, JetDataType.Currency, 8),
            "DATETIME" or "DATE" or "TIME" or "TIMESTAMP"
                => Fixed(column, JetDataType.DateTime, 8),
            "BIT" or "YESNO" or "BOOLEAN" or "LOGICAL" or "LOGICAL1"
                => Fixed(column, JetDataType.Boolean, 1),
            "GUID" or "UNIQUEIDENTIFIER"
                => Fixed(column, JetDataType.Guid, 16),
            "DECIMAL" or "NUMERIC" or "DEC"
                => new ColumnSpec(column.Name, JetDataType.FixedPoint, 17, IsFixedLength: true,
                    Precision: (byte)(column.Size ?? 18), Scale: (byte)(column.Scale ?? 0)),
            // Fixed-length character types (the CHAR family) → a fixed-length Text column (ACE stores it in
            // the row's fixed-data region, space-padded). The N-prefixed / "national" forms are the same
            // storage — Jet/ACE has no separate nchar (all text is UTF-16). Access TEXT length is in
            // characters; on disk it is 2 bytes each.
            "CHAR" or "CHARACTER" or "NCHAR" or "NATIONAL CHAR" or "NATIONAL CHARACTER"
                => Text(column, isFixed: true),
            // Variable-length character types (the VARCHAR family, incl. Access TEXT) → a variable Text column.
            "TEXT" or "VARCHAR" or "NVARCHAR" or "STRING" or "ALPHANUMERIC"
            or "CHARACTER VARYING" or "CHAR VARYING"
            or "NATIONAL CHAR VARYING" or "NATIONAL CHARACTER VARYING" or "NCHAR VARYING"
                => Text(column, isFixed: false),
            // Fixed- vs variable-length binary → fixed / variable Binary (byte length, not char-doubled).
            "BINARY"
                => Binary(column, isFixed: true),
            "VARBINARY" or "BINARY VARYING" or "BIT VARYING"
                => Binary(column, isFixed: false),

            // Long-value columns: variable-length with no fixed byte length. The in-row value is a
            // 12-byte long-value descriptor; short values are stored inline after it.
            "MEMO" or "LONGTEXT" or "LONGCHAR" or "NOTE" or "NTEXT"
                => new ColumnSpec(column.Name, JetDataType.Memo, 0, IsFixedLength: false),
            "OLEOBJECT" or "IMAGE" or "LONGBINARY" or "GENERAL"
                => new ColumnSpec(column.Name, JetDataType.Ole, 0, IsFixedLength: false),

            _ => throw new NotSupportedException($"CREATE TABLE column type '{column.TypeName}' is not supported yet."),
        };
    }

    private static ColumnSpec Fixed(ColumnDefinition column, JetDataType type, int length, bool autoNumber = false)
        => new(column.Name, type, length, IsFixedLength: true, IsAutoNumber: autoNumber);

    // Jet/ACE column-size caps (verified vs ACE): a char/varchar column holds at most 255 characters, a
    // binary/varbinary column at most 510 bytes. Beyond these, Access requires LONGTEXT/LONGBINARY (Memo/OLE);
    // it rejects an over-long fixed size rather than widening — so LibRed rejects it too, keeping the file
    // openable by Access (an over-long fixed column produces an unreadable file).
    private const int MaxTextCharacters = 255;
    private const int MaxBinaryBytes = 510;

    // A character column: length is declared in characters, stored as UTF-16 (2 bytes each). A type with no
    // declared size takes length 1 (SQL's CHAR == CHAR(1); ACE reports length 1 for a size-less char/varchar).
    private static ColumnSpec Text(ColumnDefinition column, bool isFixed)
    {
        int characters = column.Size ?? 1;
        if (characters > MaxTextCharacters)
            throw new InvalidOperationException(
                $"Size of field '{column.Name}' is too long: a char/varchar column holds at most {MaxTextCharacters} " +
                $"characters in Jet/ACE (got {characters}). Use LONGTEXT/MEMO for longer text.");
        return new(column.Name, JetDataType.Text, characters * 2, IsFixedLength: isFixed);
    }

    // A binary column: length is in bytes (not char-doubled). Size-less takes length 1.
    private static ColumnSpec Binary(ColumnDefinition column, bool isFixed)
    {
        int bytes = column.Size ?? 1;
        if (bytes > MaxBinaryBytes)
            throw new InvalidOperationException(
                $"Size of field '{column.Name}' is too long: a binary/varbinary column holds at most {MaxBinaryBytes} " +
                $"bytes in Jet/ACE (got {bytes}). Use LONGBINARY/OLE for longer data.");
        return new(column.Name, JetDataType.Binary, bytes, IsFixedLength: isFixed);
    }
}
