using System.Buffers.Binary;
using System.Text;

namespace LibRed.Catalog;

/// <summary>
/// Access's Name AutoCorrect map for one object: the names that object, and the objects it depends on, had
/// when Access last wrote the map (docs/format/system-catalog.md §11, <i>MSysNameMap</i>). Access keeps it
/// twice, in two layouts — the <c>NameMap</c> column of the object's <c>MSysNameMap</c> row
/// (<see cref="ReadRow"/>/<see cref="WriteRow"/>) and the <c>NameMap</c> property in the object's
/// <c>LvProp</c> (<see cref="ReadProperty"/>/<see cref="WriteProperty"/>) — which usually agree and sometimes
/// do not, having been written at different times.
/// </summary>
/// <remarks>
/// The engine never maintains either: ACE's DDL leaves both as they were, and so does LibRed's. This type is
/// for a caller who wants to read or edit them deliberately. A map read and written back in the same layout is
/// byte-identical: every field is carried, including those whose meaning is not known.
/// </remarks>
public sealed record NameMap
{
    /// <summary>The <c>MSysNameMap.NameMap</c> version in every file examined.</summary>
    public const int RowVersion = 5;

    private const int RowFixedLength = 48;
    private const int PropertyEndKind = 12;
    private static readonly byte[] PropertySignature = [0x0A, 0xCC, 0x0E, 0x55];

    /// <summary>The records, in stored order.</summary>
    public IReadOnlyList<NameMapRecord> Records { get; init; } = [];

    /// <summary>The map's version: the header value of the row layout (<see cref="RowVersion"/>), or the value
    /// the property layout's closing kind-12 record carries (2 to 5 seen). Null for a property blob that has no
    /// closing record.</summary>
    public int? Version { get; init; } = RowVersion;

    /// <summary>The property layout's closing record's 16-byte slot, whose first four bytes are
    /// <see cref="Version"/>; carried so the rest is written back as read.</summary>
    private byte[]? _endSlot;

    /// <summary>Bytes after a property-layout map's last record that the layout does not account for, written
    /// back as read. The row layout has none: a row blob parses to its exact end or is refused.</summary>
    private byte[] _trailing = [];

    /// <summary>Decodes the <c>NameMap</c> column of an <c>MSysNameMap</c> row: <c>[int32 version][int32 48]</c>,
    /// then records of <c>[int32 length][48 fixed bytes][UTF-16 name]</c>.</summary>
    /// <exception cref="InvalidDataException">The blob is not in this layout.</exception>
    public static NameMap ReadRow(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < 8)
            throw new InvalidDataException($"A name map needs an 8-byte header; this one is {blob.Length} bytes.");
        int fixedLength = BinaryPrimitives.ReadInt32LittleEndian(blob[4..]);
        if (fixedLength != RowFixedLength)
            throw new InvalidDataException($"A name map's records have {RowFixedLength} fixed bytes; this one declares {fixedLength}.");

        var records = new List<NameMapRecord>();
        int pos = 8;
        while (pos < blob.Length)
        {
            int length = pos + 4 <= blob.Length ? BinaryPrimitives.ReadInt32LittleEndian(blob[pos..]) : -1;
            if (length < RowFixedLength || length % 2 != 0 || length > blob.Length - pos - 4)
                throw new InvalidDataException($"The name map record at offset {pos} overruns the blob.");
            ReadOnlySpan<byte> record = blob.Slice(pos + 4, length);
            ReadOnlySpan<byte> name = record[RowFixedLength..];
            bool terminated = name.Length >= 2 && name[^2] == 0 && name[^1] == 0;
            records.Add(ReadCommon(record, Encoding.Unicode.GetString(terminated ? name[..^2] : name)) with
            {
                TypeCode = BinaryPrimitives.ReadInt32LittleEndian(record[40..]),
                Unused = BinaryPrimitives.ReadInt32LittleEndian(record[44..]),
                NameTerminated = terminated,
            });
            pos += 4 + length;
        }
        return new NameMap { Records = records, Version = BinaryPrimitives.ReadInt32LittleEndian(blob) };
    }

    /// <summary>Decodes the <c>NameMap</c> property of an object's <c>LvProp</c>: the signature
    /// <c>0A CC 0E 55</c>, then records of <c>[40 fixed bytes][UTF-16 name, NUL-terminated]</c> up to a closing
    /// record of kind 12. A property record has no <see cref="NameMapRecord.TypeCode"/> or
    /// <see cref="NameMapRecord.Unused"/>; both read as zero.</summary>
    /// <exception cref="InvalidDataException">The blob is not in this layout.</exception>
    public static NameMap ReadProperty(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < 4 || !blob[..4].SequenceEqual(PropertySignature))
            throw new InvalidDataException("A NameMap property begins with the signature 0A CC 0E 55.");

        var records = new List<NameMapRecord>();
        int pos = 4;
        while (pos + 42 <= blob.Length)
        {
            int end = pos + 40;
            while (end + 1 < blob.Length && (blob[end] | blob[end + 1]) != 0) end += 2;
            if (end + 1 >= blob.Length) break;   // no terminating NUL: not a record, left as trailing bytes

            ReadOnlySpan<byte> record = blob[pos..(end + 2)];
            if (BinaryPrimitives.ReadInt32LittleEndian(record[20..]) == PropertyEndKind)
                return new NameMap
                {
                    Records = records,
                    Version = BinaryPrimitives.ReadInt32LittleEndian(record[24..]),
                    _endSlot = record.Slice(24, 16).ToArray(),
                    _trailing = blob[(end + 2)..].ToArray(),
                };
            records.Add(ReadCommon(record, Encoding.Unicode.GetString(blob[(pos + 40)..end])));
            pos = end + 2;
        }
        return new NameMap { Records = records, Version = null, _trailing = blob[pos..].ToArray() };
    }

    /// <summary>Encodes the map in the <c>MSysNameMap.NameMap</c> layout. <see cref="Version"/> is written as
    /// the header, <see cref="RowVersion"/> when it is null.</summary>
    public byte[] WriteRow()
    {
        var blob = new List<byte>();
        AppendInt32(blob, Version ?? RowVersion);
        AppendInt32(blob, RowFixedLength);
        foreach (NameMapRecord record in Records)
        {
            byte[] name = EncodeName(record);
            AppendInt32(blob, RowFixedLength + name.Length);
            AppendCommon(blob, record);
            AppendInt32(blob, record.TypeCode);
            AppendInt32(blob, record.Unused);
            blob.AddRange(name);
        }
        return [.. blob];
    }

    /// <summary>Encodes the map in the <c>NameMap</c> property layout, closed by a kind-12 record carrying
    /// <see cref="Version"/> — none when it is null, as in a map read from a blob that had none. A record's
    /// name is always NUL-terminated here, since the NUL is what ends it.</summary>
    public byte[] WriteProperty()
    {
        var blob = new List<byte>(PropertySignature);
        foreach (NameMapRecord record in Records)
        {
            AppendCommon(blob, record);
            blob.AddRange(Encoding.Unicode.GetBytes(record.Name));
            blob.AddRange([0, 0]);
        }
        if (Version is int version)
        {
            byte[] slot = _endSlot is null ? new byte[16] : (byte[])_endSlot.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(slot, version);
            AppendCommon(blob, new NameMapRecord(Guid.Empty, PropertyEndKind, "") { Slot = slot });
            blob.AddRange([0, 0]);
        }
        blob.AddRange(_trailing);
        return [.. blob];
    }

    /// <summary>The 40 bytes both layouts share: <c>[int32 reserved][GUID][int32 kind][16-byte slot]</c>.</summary>
    private static NameMapRecord ReadCommon(ReadOnlySpan<byte> record, string name) =>
        new(new Guid(record.Slice(4, 16)), BinaryPrimitives.ReadInt32LittleEndian(record[20..]), name)
        {
            Reserved = BinaryPrimitives.ReadInt32LittleEndian(record),
            Slot = record.Slice(24, 16).ToArray(),
        };

    private static void AppendCommon(List<byte> blob, NameMapRecord record)
    {
        if (record.Slot.Length != 16)
            throw new ArgumentException($"A name map record's slot is 16 bytes; '{record.Name}' has {record.Slot.Length}.", nameof(record));
        AppendInt32(blob, record.Reserved);
        blob.AddRange(record.ItemGuid.ToByteArray());
        AppendInt32(blob, record.Kind);
        blob.AddRange(record.Slot);
    }

    private static byte[] EncodeName(NameMapRecord record) =>
        [.. Encoding.Unicode.GetBytes(record.Name), .. record.NameTerminated ? new byte[2] : []];

    private static void AppendInt32(List<byte> blob, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        blob.AddRange(bytes);
    }
}

/// <summary>One <c>MSysNameMap</c> row (docs/format/system-catalog.md §11).</summary>
/// <param name="ObjectGuid">The object's <c>GUID</c> property — what identifies the row.</param>
/// <param name="Id">The row's <c>Id</c>. Not the object's <c>MSysObjects.Id</c>, and not decoded.</param>
/// <param name="Name">The object's name when the row was written, without the trailing NUL it is stored with.</param>
/// <param name="Type">The object's <c>MSysObjects.Type</c> read as unsigned: 1 table, 5 query, 32768 form,
/// 32772 report.</param>
/// <param name="Map">The row's map; null when its <c>NameMap</c> column is.</param>
public sealed record NameMapRow(Guid ObjectGuid, int Id, string Name, int Type, NameMap? Map);

/// <summary>
/// One record of a <see cref="NameMap"/>: an object, or a field of one (docs/format/system-catalog.md §11).
/// </summary>
/// <param name="ItemGuid">The object's or field's GUID — for an object, its <c>GUID</c> property.</param>
/// <param name="Kind">The record kind. Not decoded: object records carry 0, 1 or 2, field records almost always 7.</param>
/// <param name="Name">The object's or field's name, without the terminating NUL.</param>
public sealed record NameMapRecord(Guid ItemGuid, int Kind, string Name)
{
    /// <summary>The 16-byte slot after the kind: in an object record an OLE Automation date then eight zero
    /// bytes, in a field record a GUID (in most records the preceding object record's). See
    /// <see cref="SlotDate"/> and <see cref="SlotGuid"/>.</summary>
    public byte[] Slot { get; init; } = new byte[16];

    /// <summary>The record's MSysObjects type (an object record) or data type code (a field record). Only the
    /// <c>MSysNameMap</c> layout stores it; a record read from the property layout has zero.</summary>
    public int TypeCode { get; init; }

    /// <summary>The first four bytes of the record, zero in every file examined; carried so they are written back
    /// as read.</summary>
    public int Reserved { get; init; }

    /// <summary>The last four fixed bytes of a <c>MSysNameMap</c> record, which Access leaves uninitialised —
    /// whatever was in memory. Carried so a map is written back as read; zero for a new record. Not stored in
    /// the property layout.</summary>
    public int Unused { get; init; }

    /// <summary>Whether the name is stored with its terminating NUL, as every record but one in the files
    /// examined is. Only the <c>MSysNameMap</c> layout can store it without.</summary>
    public bool NameTerminated { get; init; } = true;

    /// <summary>The slot read as an object record's OLE Automation date; null when it holds none.</summary>
    public DateTime? SlotDate
    {
        get
        {
            double days = BinaryPrimitives.ReadDoubleLittleEndian(Slot);
            return days != 0 && double.IsFinite(days) && days is > -657435 and < 2958466 ? DateTime.FromOADate(days) : null;
        }
    }

    /// <summary>The slot read as a field record's GUID.</summary>
    public Guid SlotGuid => new(Slot);
}
