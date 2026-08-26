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

    /// <summary>
    /// The minimum file format a declared type needs, or <c>null</c> for the types every format can hold.
    /// </summary>
    /// <remarks>
    /// BIGINT and DATETIME2 arrived in DIFFERENT format versions — verified against files authored with each
    /// feature enabled: BIGINT (Large Number) forces the ACE 16 / Access 2016 format (version byte 0x05),
    /// DATETIME2 (Date/Time Extended) the ACE 17 / 2019+ format (0x06). Below those the engine cannot
    /// represent the type at all.
    /// <para>Callers that are about to write storage raise the file to meet this (see
    /// <c>StatementExecutor.MapColumn</c>), which is what Access itself does. <see cref="MapType"/> still
    /// refuses a type the open file is too old for, so a caller that skips the upgrade — or cannot perform it,
    /// on a read-only database — fails loudly instead of writing a column Access could not read.</para>
    /// </remarks>
    public static (JetVersion Min, string Label)? RequiredVersion(string typeName) =>
        Normalize(typeName) switch
        {
            "BIGINT" => (JetVersion.Version16_2016, "Access 2016 (ACE 16)"),
            "DATETIME2" => (JetVersion.Version17_2019, "Access 2019+ (ACE 17)"),
            _ => null,
        };

    /// <summary>Collapses internal whitespace so two-word aliases ("character  varying") match.</summary>
    private static string Normalize(string typeName) =>
        string.Join(' ', typeName.ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static ColumnSpec MapType(ColumnDefinition column, JetVersion version)
    {
        string t = Normalize(column.TypeName);

        if (RequiredVersion(t) is { } g && version < g.Min)
            throw new NotSupportedException(
                $"Column type '{column.TypeName}' requires {g.Label} or later; this database is {version}.");

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
            // SMALLMONEY is a SQL-Server alias ACE accepts and folds onto CURRENCY — there is no narrower
            // money storage in Jet/ACE (verified vs ACE: SMALLMONEY → Currency, 8 bytes).
            "CURRENCY" or "MONEY" or "SMALLMONEY"
                => Fixed(column, JetDataType.Currency, 8),
            // SMALLDATETIME likewise folds onto the single 8-byte DateTime (no narrower date storage exists);
            // no file-format upgrade (verified vs ACE: SMALLDATETIME → DateTime, version byte unchanged).
            "DATETIME" or "DATE" or "TIME" or "TIMESTAMP" or "SMALLDATETIME"
                => Fixed(column, JetDataType.DateTime, 8),
            // Date/Time Extended: a fixed 42-byte ASCII triple, not the 8-byte OA double (see JetTypeCodec).
            // Version-gated to ACE 17 above. ACE's own DDL accepts only this bare spelling - DATETIME2(7),
            // DATETIMEEXTENDED and DATE/TIME EXTENDED are all syntax errors - so there are no aliases to fold
            // in, and the declared precision is always 7.
            "DATETIME2"
                => Fixed(column, JetDataType.DateTimeExtended, 42),
            "BIT" or "YESNO" or "BOOLEAN" or "LOGICAL" or "LOGICAL1"
                => Fixed(column, JetDataType.Boolean, 1),
            "GUID" or "UNIQUEIDENTIFIER"
                => Fixed(column, JetDataType.Guid, 16),
            "DECIMAL" or "NUMERIC" or "DEC"
                => Decimal(column),
            // Fixed-length character types (the CHAR family) → a fixed-length Text column (ACE stores it in
            // the row's fixed-data region, space-padded). The N-prefixed / "national" forms are the same
            // storage — Jet/ACE has no separate nchar (all text is UTF-16). Access TEXT length is in
            // characters; on disk it is 2 bytes each.
            "CHAR" or "CHARACTER" or "NCHAR" or "NATIONAL CHAR" or "NATIONAL CHARACTER"
                => Text(column, isFixed: true),
            // Bare TEXT is a Jet quirk: with no size it means LONGTEXT (Memo), but TEXT(n) is a variable
            // Text column of n characters. Verified vs ACE: `TEXT` → Memo, `TEXT(50)` → Text(50).
            "TEXT" => column.Size is null
                ? new ColumnSpec(column.Name, JetDataType.Memo, 0, IsFixedLength: false)
                : Text(column, isFixed: false),
            // Variable-length character types (the VARCHAR family) → a variable Text column.
            "VARCHAR" or "NVARCHAR" or "STRING" or "ALPHANUMERIC"
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
    private const int MaxDecimalPrecision = 28;

    private static ColumnSpec Decimal(ColumnDefinition column)
    {
        int precision = column.Size ?? 18;
        int scale = column.Scale ?? 0;
        if (precision is < 1 or > MaxDecimalPrecision)
            throw new InvalidOperationException(
                $"Precision of field '{column.Name}' must be from 1 through {MaxDecimalPrecision} (got {precision}).");
        if (scale < 0 || scale > precision)
            throw new InvalidOperationException(
                $"Scale of field '{column.Name}' must be from 0 through its precision {precision} (got {scale}).");
        return new ColumnSpec(column.Name, JetDataType.FixedPoint, 17, IsFixedLength: true,
            Precision: (byte)precision, Scale: (byte)scale);
    }

    // A character column: length is declared in characters, stored as UTF-16 (2 bytes each). A size-less
    // char/varchar takes the MAXIMUM (255 characters) — verified vs ACE, which defaults a bare CHAR/VARCHAR
    // to a 255-char (510-byte) field, not CHAR(1).
    private static ColumnSpec Text(ColumnDefinition column, bool isFixed)
    {
        int characters = column.Size ?? MaxTextCharacters;
        if (characters <= 0)
            throw new InvalidOperationException(
                $"Size of field '{column.Name}' must be positive (got {characters}).");
        if (characters > MaxTextCharacters)
            throw new InvalidOperationException(
                $"Size of field '{column.Name}' is too long: a char/varchar column holds at most {MaxTextCharacters} " +
                $"characters in Jet/ACE (got {characters}). Use LONGTEXT/MEMO for longer text.");
        return new(column.Name, JetDataType.Text, characters * 2, IsFixedLength: isFixed);
    }

    // A binary column: length is in bytes (not char-doubled). A size-less binary/varbinary takes the
    // MAXIMUM (510 bytes) — verified vs ACE, which defaults a bare BINARY/VARBINARY to a 510-byte field.
    private static ColumnSpec Binary(ColumnDefinition column, bool isFixed)
    {
        int bytes = column.Size ?? MaxBinaryBytes;
        if (bytes <= 0)
            throw new InvalidOperationException(
                $"Size of field '{column.Name}' must be positive (got {bytes}).");
        if (bytes > MaxBinaryBytes)
            throw new InvalidOperationException(
                $"Size of field '{column.Name}' is too long: a binary/varbinary column holds at most {MaxBinaryBytes} " +
                $"bytes in Jet/ACE (got {bytes}). Use LONGBINARY/OLE for longer data.");
        return new(column.Name, JetDataType.Binary, bytes, IsFixedLength: isFixed);
    }
}
