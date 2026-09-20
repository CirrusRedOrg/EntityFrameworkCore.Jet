using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using LibRed.Engine.Execution;

namespace LibRed.Data;

/// <summary>Forward-only reader projecting an engine <see cref="ResultSet"/> as ADO.NET rows.</summary>
public sealed class LibRedDataReader : DbDataReader, IDbColumnSchemaGenerator
{
    private readonly ResultSet _result;
    private readonly IEnumerator<object?[]> _rows;
    private readonly int _recordsAffected;
    private readonly bool _singleRow;
    private readonly LibRedConnection? _ownedConnection;
    private object?[] _current = [];
    private bool _pendingFirst;
    private bool _hadRows;
    private bool _closed;

    /// <param name="recordsAffected">Rows affected for a DML command; -1 for a query (ADO convention).</param>
    /// <param name="behavior">
    /// The behavior the command was executed with. Two of its flags reach the reader: <c>SingleRow</c> caps the
    /// result at the one row (the constructor has already buffered it, so nothing further is ever read), and
    /// <c>CloseConnection</c> hands the reader <paramref name="connection"/>'s lifetime — closing the reader
    /// then closes it, as ACE does. <c>SingleResult</c> is already the only shape this reader has (a batch
    /// returns its last statement's rows and <c>NextResult</c> is always false), <c>SequentialAccess</c> asks
    /// for a restriction on a row that is already in memory, and the key and base-column information
    /// <c>KeyInfo</c> asks for costs a catalog lookup that <c>GetSchemaTable</c> makes anyway — so those three
    /// are accepted and need nothing.
    /// </param>
    /// <param name="connection">The connection to close when this reader closes, for <c>CloseConnection</c>.</param>
    internal LibRedDataReader(
        ResultSet result, int recordsAffected = -1,
        CommandBehavior behavior = CommandBehavior.Default, LibRedConnection? connection = null)
    {
        _result = result;
        _rows = result.Rows.GetEnumerator();
        _recordsAffected = recordsAffected;
        _singleRow = behavior.HasFlag(CommandBehavior.SingleRow);
        _ownedConnection = behavior.HasFlag(CommandBehavior.CloseConnection) ? connection : null;

        // Buffer the first row eagerly so column types (GetFieldType/GetDataTypeName) are available
        // before the first Read — EF's BufferedDataReader reads that metadata before reading any rows.
        if (_rows.MoveNext())
        {
            _current = _rows.Current;
            _pendingFirst = true;
            _hadRows = true;
        }
    }

    public override int FieldCount => _result.ColumnNames.Count;
    public override int Depth => 0;
    public override bool HasRows => _hadRows;
    public override bool IsClosed => _closed;
    public override int RecordsAffected => _recordsAffected;

    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        if (_pendingFirst) { _pendingFirst = false; return true; } // yield the pre-buffered first row
        if (_singleRow) return false;                              // that buffered row was the only one asked for
        if (!_rows.MoveNext()) return false;
        _current = _rows.Current;
        return true;
    }

    public override bool NextResult() => false;

    public override string GetName(int ordinal) => _result.ColumnNames[ordinal];

    public override int GetOrdinal(string name)
    {
        for (int i = 0; i < _result.ColumnNames.Count; i++)
            if (string.Equals(_result.ColumnNames[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        throw new IndexOutOfRangeException(name);
    }

    public override object GetValue(int ordinal) => _current[ordinal] ?? DBNull.Value;

    /// <summary>The OLE epoch (1899-12-30) — Jet stores TimeSpan/TimeOnly as an offset from it.</summary>
    private static readonly DateTime OleEpoch = new(1899, 12, 30);

    /// <summary>
    /// Typed accessor EF Core uses. Jet has no TimeSpan/DateOnly/TimeOnly/DateTimeOffset type — they are
    /// all stored in a DateTime column — so convert a stored <see cref="DateTime"/> back when one of those
    /// is requested. For <see cref="DateTimeOffset"/> there is no offset on disk (the mapping strips it and
    /// stores UTC on the way in), so it is read back at offset zero.
    /// <para>default(DateTime) (Ticks 0 / 0001-01-01) is below Jet's OLE date floor, so the write path (the
    /// inherited EFCore.Jet DateTime mapping) collapses it onto the epoch OA 0. This reverses it on the way out
    /// — the epoch reads back as default — matching EFCore.Jet's JetDataReader. Scoped to DateTime/DateTimeOffset:
    /// a TimeSpan/TimeOnly at the epoch is a legitimate midnight, handled above.</para>
    /// </summary>
    public override T GetFieldValue<T>(int ordinal)
    {
        if (_current[ordinal] is DateTime dt)
        {
            if (typeof(T) == typeof(TimeSpan)) return (T)(object)(dt - OleEpoch);
            if (typeof(T) == typeof(DateOnly)) return (T)(object)DateOnly.FromDateTime(dt);
            if (typeof(T) == typeof(TimeOnly)) return (T)(object)TimeOnly.FromDateTime(dt);
            if (typeof(T) == typeof(DateTime)) return (T)(object)(dt == OleEpoch ? default : dt);
            if (typeof(T) == typeof(DateTimeOffset)) return (T)(object)new DateTimeOffset(dt == OleEpoch ? default : dt, TimeSpan.Zero);
        }
        return base.GetFieldValue<T>(ordinal);
    }

    /// <summary>The stored epoch is the on-disk home of default(DateTime) — see <see cref="GetFieldValue{T}"/>.</summary>
    private static DateTime FromStored(DateTime dt) => dt == OleEpoch ? default : dt;

    public override int GetValues(object[] values)
    {
        int count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++) values[i] = GetValue(i);
        return count;
    }

    public override bool IsDBNull(int ordinal) => _current[ordinal] is null;

    public override Type GetFieldType(int ordinal)
    {
        Type declared = _result.ColumnTypes[ordinal];
        return declared != typeof(object)
            ? declared
            : ordinal < _current.Length && _current[ordinal] is { } value ? value.GetType() : typeof(object);
    }

    /// <summary>
    /// The provider's name for the column's type — <c>VarChar</c>, <c>Char</c>, <c>Long</c>, <c>Currency</c> —
    /// the same name <see cref="GetColumnSchema"/>, <see cref="GetSchemaTable"/> and the <c>DataTypes</c>
    /// metadata collection give it, so one type has one name across the whole surface. (ACE's OLE DB provider
    /// answers this with the OLE DB spelling, <c>DBTYPE_WVARCHAR</c>; its own schema rowsets use these names,
    /// and matching them is what keeps this provider self-consistent.) A result with nothing described behind
    /// it — a system-variable select, say — falls back to the CLR type's name.
    /// </summary>
    public override string GetDataTypeName(int ordinal) =>
        _result.Columns[ordinal].TypeName is { Length: > 0 } name ? name : GetFieldType(ordinal).Name;

    /// <summary>The result's columns as <see cref="DbColumn"/>s: each column's type, and for one read straight
    /// from a table the stored column behind it — its table, its own name, and whether it is a key, unique,
    /// an AutoNumber or computed.</summary>
    public ReadOnlyCollection<DbColumn> GetColumnSchema() =>
        new(_result.Columns.Select((c, i) => (DbColumn)new LibRedDbColumn(c, i)).ToList());

    /// <summary>The same description in the older <c>DataTable</c> form, with the column set ADO.NET
    /// defines for it.</summary>
    public override DataTable GetSchemaTable()
    {
        var table = new DataTable("SchemaTable") { Locale = System.Globalization.CultureInfo.InvariantCulture };
        foreach ((string name, Type type) in new (string, Type)[]
        {
            (SchemaTableColumn.ColumnName, typeof(string)), (SchemaTableColumn.ColumnOrdinal, typeof(int)),
            (SchemaTableColumn.ColumnSize, typeof(int)), (SchemaTableColumn.NumericPrecision, typeof(short)),
            (SchemaTableColumn.NumericScale, typeof(short)), (SchemaTableColumn.IsUnique, typeof(bool)),
            (SchemaTableColumn.IsKey, typeof(bool)), (SchemaTableOptionalColumn.BaseServerName, typeof(string)),
            (SchemaTableOptionalColumn.BaseCatalogName, typeof(string)), (SchemaTableColumn.BaseColumnName, typeof(string)),
            (SchemaTableColumn.BaseSchemaName, typeof(string)), (SchemaTableColumn.BaseTableName, typeof(string)),
            (SchemaTableColumn.DataType, typeof(Type)), (SchemaTableColumn.AllowDBNull, typeof(bool)),
            (SchemaTableColumn.ProviderType, typeof(int)), (SchemaTableColumn.IsAliased, typeof(bool)),
            (SchemaTableColumn.IsExpression, typeof(bool)), (SchemaTableOptionalColumn.IsAutoIncrement, typeof(bool)),
            (SchemaTableOptionalColumn.IsRowVersion, typeof(bool)), (SchemaTableOptionalColumn.IsHidden, typeof(bool)),
            (SchemaTableColumn.IsLong, typeof(bool)), (SchemaTableOptionalColumn.IsReadOnly, typeof(bool)),
            ("DataTypeName", typeof(string)),
        })
            table.Columns.Add(name, type);

        for (int i = 0; i < _result.Columns.Count; i++)
        {
            ResultColumn c = _result.Columns[i];
            table.Rows.Add(
                c.Name, i, (object?)c.Size ?? DBNull.Value,
                c.Precision is { } p ? (short)p : DBNull.Value, c.Scale is { } s ? (short)s : DBNull.Value,
                c.IsUnique, c.IsKey,
                DBNull.Value, DBNull.Value,                       // a Jet file has no server or catalog
                (object?)c.BaseColumnName ?? DBNull.Value, DBNull.Value,
                (object?)c.BaseTableName ?? DBNull.Value,
                c.ClrType, c.AllowNull, c.ProviderType,
                // Aliased when the query renamed the stored column it reads.
                c.BaseColumnName is not null && !string.Equals(c.BaseColumnName, c.Name, StringComparison.OrdinalIgnoreCase),
                c.IsExpression, c.IsAutoIncrement,
                false, false,                                     // Jet has no rowversion, and hides no column
                c.IsLong, c.IsReadOnly,
                c.TypeName);
        }

        return table;
    }

    public override bool GetBoolean(int ordinal)
    {
        var value = GetValue(ordinal);
        if (value is short) return Convert.ToBoolean(value);
        return (bool)value;
    }
    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
    public override char GetChar(int ordinal) => (char)GetValue(ordinal);
    public override DateTime GetDateTime(int ordinal) => FromStored((DateTime)GetValue(ordinal));
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);

    public override long GetInt64(int ordinal)
    {
        var result = GetValue(ordinal);
        if (result is long l)
        {
            return l;
        }

        try
        {
            return Convert.ToInt64(result);
        }
        catch (Exception)
        {
            // ignored
        }

        return (long)result;

    }
    public override string GetString(int ordinal) => (string)GetValue(ordinal);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        var source = (byte[])GetValue(ordinal);
        if (buffer is null) return source.Length;

        ValidateCopyArguments(dataOffset, buffer.Length, bufferOffset, length);
        if (dataOffset >= source.Length) return 0;

        int copy = Math.Min(length, source.Length - checked((int)dataOffset));
        source.AsSpan(checked((int)dataOffset), copy).CopyTo(buffer.AsSpan(bufferOffset, copy));
        return copy;
    }

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
    {
        var source = GetString(ordinal);
        if (buffer is null) return source.Length;

        ValidateCopyArguments(dataOffset, buffer.Length, bufferOffset, length);
        if (dataOffset >= source.Length) return 0;

        int copy = Math.Min(length, source.Length - checked((int)dataOffset));
        source.AsSpan(checked((int)dataOffset), copy).CopyTo(buffer.AsSpan(bufferOffset, copy));
        return copy;
    }

    private static void ValidateCopyArguments(long dataOffset, int bufferLength, int bufferOffset, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dataOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(bufferOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (bufferOffset > bufferLength || length > bufferLength - bufferOffset)
            throw new ArgumentException("The requested range does not fit in the destination buffer.");
    }

    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    /// <summary>Releases the cursors the rows are read through, and — under
    /// <see cref="CommandBehavior.CloseConnection"/> — closes the connection they came from.</summary>
    public override void Close()
    {
        if (_closed) return;
        _closed = true;
        _rows.Dispose();
        _ownedConnection?.Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Close();
        base.Dispose(disposing);
    }
}
