using System.Buffers.Binary;
using System.Text;

namespace LibRed.Catalog;

/// <summary>
/// Reads and writes the Jet/ACE per-object extended-properties blob (the <c>LvProp</c> value on an
/// object's <c>MSysObjects</c> row). The blob holds column-level properties such as <c>DefaultValue</c>.
/// </summary>
/// <remarks>
/// Layout (verified against an ACE-created table, §11): a 4-byte signature (<c>MR2\0</c> for ACE,
/// <c>KKD\0</c> for older MDB) followed by blocks. Each block is <c>[int length][short type][body]</c>,
/// the length covering the whole block. Type <c>0x80</c> is the property-name pool
/// (<c>[short len][UTF-16 name]</c> repeated); other blocks are a per-owner value map:
/// <c>[short ownerRecLen][short 0][short nameLen][owner name]</c> then property entries
/// <c>[short entryLen][byte DDL flag][byte dataType][short nameIndex][short valueLen][UTF-16 value]</c>.
/// </remarks>
public static class PropertyBlob
{
    private static readonly byte[] SignatureAce = "MR2\0"u8.ToArray();
    private static readonly byte[] SignatureMdb = "KKD\0"u8.ToArray();
    private const ushort NameListBlock = 0x0080;
    private const ushort TableBlock = 0x0000;   // value block owned by the table (empty owner name)
    private const ushort ColumnBlock = 0x0001;  // value block owned by a column
    private const byte DdlFlag = 0x01;
    public const string DefaultValueProperty = "DefaultValue";
    public const string CheckConstraintsProperty = "CheckConstraints";
    public const string RequiredProperty = "Required";
    public const string ValidationRuleProperty = "ValidationRule";
    public const string ValidationTextProperty = "ValidationText";

    /// <summary>A single property: the owning column (or "" for the table), the property name, its value
    /// (text; for a boolean, <c>"1"</c>/<c>"0"</c>), and its stored type. The type is an ordinary
    /// <see cref="JetDataType"/> code — the same byte used by column descriptors and MSysQueries — so Access
    /// stores <c>DefaultValue</c>/<c>CheckConstraints</c> as <see cref="JetDataType.Memo"/> and <c>Required</c>
    /// as <see cref="JetDataType.Boolean"/>.
    /// <para><see cref="RawValue"/> holds the exact stored value bytes when the property was <see cref="Read"/>
    /// from a blob; <see cref="Write"/> emits it verbatim, so a property LibRed does not model (a numeric
    /// <c>DecimalPlaces</c>, a designer <c>ValidationRule</c>/<c>Format</c>, …) round-trips byte-for-byte even
    /// though its <see cref="Value"/> string is only a best-effort UTF-16 decode. It is <c>null</c> for a
    /// property LibRed constructs, which is then encoded from <see cref="Value"/>/<see cref="Type"/>.</para>
    /// <para><see cref="IsDdl"/> preserves the entry's DDL-property flag. DDL properties are protected as
    /// part of the object's definition and some are only recognised correctly by Access when flagged. It
    /// defaults to <see langword="true"/> because the properties LibRed currently creates are schema
    /// properties such as <c>DefaultValue</c>, <c>Required</c>, and <c>CheckConstraints</c>.</para></summary>
    public readonly record struct Property(
        string Owner, string Name, string Value, JetDataType Type = JetDataType.Memo, byte[]? RawValue = null)
    {
        /// <summary>Whether this entry is a DDL/property-definition property.</summary>
        public bool IsDdl { get; init; } = true;
    }

    /// <summary>A boolean property (e.g. <c>Required</c>), stored as a single 0/1 byte.</summary>
    public static Property Bool(string owner, string name, bool value) =>
        new(owner, name, value ? "1" : "0", JetDataType.Boolean);

    /// <summary>Builds the blob for a set of properties, grouped by owner in the given order — matching
    /// what ACE writes (verified byte-for-byte for column DefaultValues).</summary>
    public static byte[] Write(IReadOnlyList<Property> properties)
    {
        ValidateForWrite(properties);
        var names = properties.Select(p => p.Name).Distinct().ToList();
        var nameIndex = names.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);

        var blob = new List<byte>(SignatureAce);

        var namesBody = new List<byte>();
        foreach (string name in names) AppendString(namesBody, name);
        AppendBlock(blob, NameListBlock, namesBody);

        foreach (var group in GroupByOwnerPreservingOrder(properties))
            AppendOwnerBlock(blob, group.Owner, group.Properties, nameIndex);

        return [.. blob];
    }

    /// <summary>
    /// Adds a column's (or the table's) property block to an existing blob — the reverse of
    /// <see cref="RemoveOwner"/>, used by ALTER TABLE ADD COLUMN with NOT NULL / DEFAULT. Extends the name
    /// pool with any new property names (appended, so existing name indexes stay valid), keeps every existing
    /// block verbatim, and appends the new owner block. If the blob is empty it builds a fresh one.
    /// </summary>
    public static byte[] AddColumnProperties(ReadOnlySpan<byte> blob, string owner, IReadOnlyList<Property> newProps)
    {
        if (newProps.Count == 0) return blob.ToArray();
        if (blob.Length == 0) return Write(newProps);

        ParsedBlob parsed = Parse(blob);
        var names = parsed.Names.ToList();
        var otherBlocks = new List<byte[]>();
        foreach (ParsedBlock block in parsed.Blocks)
            if (block.Type != NameListBlock) otherBlocks.Add(block.Raw);

        var nameIndex = new Dictionary<string, int>();
        for (int i = 0; i < names.Count; i++) nameIndex[names[i]] = i;
        foreach (Property p in newProps)
            if (!nameIndex.ContainsKey(p.Name)) { nameIndex[p.Name] = names.Count; names.Add(p.Name); }

        ValidateNames(names);
        ValidateOwnerProperties(owner, newProps, nameIndex);

        var result = new List<byte>(SignatureAce);
        var namesBody = new List<byte>();
        foreach (string n in names) AppendString(namesBody, n);
        AppendBlock(result, NameListBlock, namesBody);
        foreach (byte[] b in otherBlocks) result.AddRange(b);
        AppendOwnerBlock(result, owner, newProps, nameIndex);
        return [.. result];
    }

    /// <summary>Appends one owner's value block: the owner record then a property entry per property.</summary>
    private static void AppendOwnerBlock(List<byte> blob, string owner, IEnumerable<Property> props, IReadOnlyDictionary<string, int> nameIndex)
    {
        Property[] propertyArray = props.ToArray();
        ValidateOwnerProperties(owner, propertyArray, nameIndex);
        var body = new List<byte>();
        var ownerRec = new List<byte> { 0, 0, 0, 0 }; // [recLen placeholder][0x0000]
        AppendString(ownerRec, owner);
        BinaryPrimitives.WriteUInt16LittleEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(ownerRec), (ushort)ownerRec.Count);
        body.AddRange(ownerRec);

        foreach (Property p in propertyArray)
        {
            // A property Read from a blob carries its exact stored bytes (RawValue) — emit them verbatim so
            // anything LibRed does not model round-trips byte-for-byte. A LibRed-constructed property has no
            // RawValue and is encoded from Value/Type (Boolean = one 0/1 byte, else UTF-16).
            byte[] value = PropertyValue(p);
            var entry = new List<byte>();
            AppendUInt16(entry, (ushort)(2 + 1 + 1 + 2 + 2 + value.Length)); // entry length
            entry.Add(p.IsDdl ? DdlFlag : (byte)0x00);
            entry.Add((byte)p.Type);
            AppendUInt16(entry, (ushort)nameIndex[p.Name]);
            AppendUInt16(entry, (ushort)value.Length);
            entry.AddRange(value);
            body.AddRange(entry);
        }
        // A table-level map (empty owner) uses block type 0x00; a column map uses 0x01.
        AppendBlock(blob, owner.Length == 0 ? TableBlock : ColumnBlock, body);
    }

    /// <summary>
    /// Removes the property-value block owned by <paramref name="owner"/> (a column being dropped), keeping
    /// every other block — including the name pool — verbatim. The name pool is deliberately left untouched
    /// so the surviving blocks' name indexes stay valid (an unreferenced pooled name is harmless). Returns
    /// the blob unchanged if the owner has no block. This is what ACE does on DROP COLUMN (verified: a dropped
    /// column's DefaultValue/Required entry disappears from the blob).
    /// </summary>
    public static byte[] RemoveOwner(ReadOnlySpan<byte> blob, string owner)
    {
        if (blob.Length == 0) return [];

        ParsedBlob parsed = Parse(blob);

        var result = new List<byte>(blob.Length);
        result.AddRange(blob[..4]); // signature
        foreach (ParsedBlock block in parsed.Blocks)
        {
            bool drop = false;
            if (block.Type != NameListBlock)
                drop = string.Equals(ReadOwner(block.Body), owner, StringComparison.OrdinalIgnoreCase);
            if (!drop) result.AddRange(block.Raw);
        }
        return [.. result];
    }

    /// <summary>
    /// Renames the owner of a property block (a column being renamed), keeping the block's property entries —
    /// and every other block, including the name pool — byte-for-byte. Only the owner record's name and the two
    /// lengths that describe it change; the unmodelled field at +2 is carried through. Returns the blob
    /// unchanged if the owner has no block. This is what ACE does on a column rename: the column keeps its
    /// DefaultValue/Required (verified — <c>RenameFanOutProbeTest</c>).
    /// </summary>
    public static byte[] RenameOwner(ReadOnlySpan<byte> blob, string oldOwner, string newOwner)
    {
        if (blob.Length == 0) return [];

        ParsedBlob parsed = Parse(blob);

        var result = new List<byte>(blob.Length);
        result.AddRange(blob[..4]); // signature
        foreach (ParsedBlock block in parsed.Blocks)
        {
            if (block.Type == NameListBlock
                || !string.Equals(ReadOwner(block.Body), oldOwner, StringComparison.OrdinalIgnoreCase))
            {
                result.AddRange(block.Raw);
                continue;
            }

            // Owner record: [uint16 recordLength][uint16 unmodelled][uint16 ownerLength][UTF-16 name].
            // Everything past it is this owner's property entries, which the rename must not disturb.
            int oldRecordLength = BinaryPrimitives.ReadUInt16LittleEndian(block.Body.AsSpan(0, 2));
            byte[] nameBytes = Encoding.Unicode.GetBytes(newOwner);

            var body = new List<byte>(block.Body.Length - oldRecordLength + 6 + nameBytes.Length);
            byte[] header = new byte[6];
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0, 2), (ushort)(6 + nameBytes.Length));
            block.Body.AsSpan(2, 2).CopyTo(header.AsSpan(2, 2)); // preserved verbatim
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4, 2), (ushort)nameBytes.Length);
            body.AddRange(header);
            body.AddRange(nameBytes);
            body.AddRange(block.Body.AsSpan(oldRecordLength).ToArray());

            byte[] raw = new byte[6 + body.Count];
            BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(0, 4), raw.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(4, 2), block.Type);
            body.CopyTo(raw, 6);
            result.AddRange(raw);
        }

        return [.. result];
    }

    /// <summary>Parses every property (owner, name, value) from a blob. Empty owner = a table property.</summary>
    public static IReadOnlyList<Property> Read(ReadOnlySpan<byte> blob)
    {
        if (blob.Length == 0) return [];
        return Parse(blob).Properties;
    }

    /// <summary>Extracts each column's <c>DefaultValue</c> (column name → value text) from a blob.</summary>
    public static IReadOnlyDictionary<string, string> ReadColumnDefaults(ReadOnlySpan<byte> blob)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Property p in Read(blob))
            if (p.Owner.Length > 0 && p.Name == DefaultValueProperty)
                result[p.Owner] = p.Value;
        return result;
    }

    /// <summary>The set of columns marked <c>Required</c> (NOT NULL) in a blob — a column has the property
    /// only when it is required (Access omits it for a nullable column).</summary>
    public static IReadOnlySet<string> ReadRequiredColumns(ReadOnlySpan<byte> blob)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Property p in Read(blob))
            if (p.Owner.Length > 0 && p.Name == RequiredProperty && p.Value == "1")
                result.Add(p.Owner);
        return result;
    }

    /// <summary>Extracts the <c>ValidationRule</c>/<c>ValidationText</c> designer properties for the given
    /// owner (a column name, or "" for the table) from a blob; each is null if absent. Access stores these as
    /// ordinary text properties in the <c>LvProp</c> blob, which is what EFCore.Jet's ADOX surfaces as
    /// <c>Jet OLEDB:{Column,Table} Validation Rule/Text</c>.</summary>
    public static (string? Rule, string? Text) ReadValidation(ReadOnlySpan<byte> blob, string owner)
    {
        string? rule = null, text = null;
        foreach (Property p in Read(blob))
        {
            if (!string.Equals(p.Owner, owner, StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Name == ValidationRuleProperty) rule = p.Value.Length > 0 ? p.Value : null;
            else if (p.Name == ValidationTextProperty) text = p.Value.Length > 0 ? p.Value : null;
        }
        return (rule, text);
    }

    /// <summary>Extracts the table's CHECK constraints (name, expression) from a blob. The
    /// <c>CheckConstraints</c> table property stores them as a <c>name\0expression\0</c> list, terminated
    /// by an empty entry.</summary>
    public static IReadOnlyList<(string Name, string Expression)> ReadCheckConstraints(ReadOnlySpan<byte> blob)
    {
        foreach (Property p in Read(blob))
            if (p.Owner.Length == 0 && p.Name == CheckConstraintsProperty)
                return ParseCheckList(p.Value);
        return [];
    }

    /// <summary>Serialises CHECK constraints into the <c>CheckConstraints</c> property value: each is
    /// <c>name\0expression\0</c>, then a trailing <c>\0</c> terminator.</summary>
    public static string WriteCheckList(IReadOnlyList<(string Name, string Expression)> checks)
    {
        var sb = new StringBuilder();
        foreach (var (name, expr) in checks) sb.Append(name).Append('\0').Append(expr).Append('\0');
        sb.Append('\0');
        return sb.ToString();
    }

    private static List<(string Name, string Expression)> ParseCheckList(string value)
    {
        var result = new List<(string, string)>();
        string[] parts = value.Split('\0');
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            if (parts[i].Length == 0) break; // empty name = list terminator
            result.Add((parts[i], parts[i + 1]));
        }
        return result;
    }

    private sealed record ParsedBlock(ushort Type, byte[] Body, byte[] Raw);
    private sealed record ParsedBlob(
        IReadOnlyList<ParsedBlock> Blocks, IReadOnlyList<string> Names, IReadOnlyList<Property> Properties);

    /// <summary>Parses and validates the complete blob once, so every read/add/remove caller observes the
    /// same block, owner-record, entry, and name-index boundaries.</summary>
    private static ParsedBlob Parse(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < 4)
            throw new InvalidDataException($"Property blob has {blob.Length} bytes; expected a 4-byte signature.");
        if (!blob[..4].SequenceEqual(SignatureAce) && !blob[..4].SequenceEqual(SignatureMdb))
            throw new InvalidDataException("Property blob has an unknown signature.");

        var blocks = new List<ParsedBlock>();
        int pos = 4;
        while (pos < blob.Length)
        {
            if (blob.Length - pos < 6)
                throw new InvalidDataException($"Property blob has {blob.Length - pos} trailing bytes after its last complete block.");
            int length = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(pos, 4));
            if (length < 6 || length > blob.Length - pos)
                throw new InvalidDataException(
                    $"Property block at {pos} declares invalid length {length} with {blob.Length - pos} bytes remaining.");
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(pos + 4, 2));
            blocks.Add(new ParsedBlock(type, blob.Slice(pos + 6, length - 6).ToArray(), blob.Slice(pos, length).ToArray()));
            pos += length;
        }

        var names = new List<string>();
        foreach (ParsedBlock block in blocks)
            if (block.Type == NameListBlock) ReadNames(block.Body, names);

        var properties = new List<Property>();
        foreach (ParsedBlock block in blocks)
            if (block.Type != NameListBlock) ReadProperties(block.Body, names, properties);

        return new ParsedBlob(blocks, names, properties);
    }

    private static void ReadNames(ReadOnlySpan<byte> body, List<string> names)
    {
        int pos = 0;
        while (pos < body.Length)
        {
            if (body.Length - pos < 2)
                throw new InvalidDataException("Property name pool ends inside a length field.");
            int length = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(pos, 2));
            pos += 2;
            if ((length & 1) != 0 || length > body.Length - pos)
                throw new InvalidDataException(
                    $"Property name declares invalid UTF-16 length {length} with {body.Length - pos} bytes remaining.");
            names.Add(Encoding.Unicode.GetString(body.Slice(pos, length)));
            pos += length;
        }
    }

    private static string ReadOwner(ReadOnlySpan<byte> body)
    {
        if (body.Length < 6)
            throw new InvalidDataException("Property owner block is shorter than its 6-byte owner header.");
        int recordLength = BinaryPrimitives.ReadUInt16LittleEndian(body[..2]);
        int ownerLength = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(4, 2));
        if (recordLength < 6 || recordLength > body.Length || (ownerLength & 1) != 0 || ownerLength != recordLength - 6)
            throw new InvalidDataException(
                $"Property owner record length {recordLength} and UTF-16 name length {ownerLength} are inconsistent.");
        return Encoding.Unicode.GetString(body.Slice(6, ownerLength));
    }

    private static void ReadProperties(ReadOnlySpan<byte> body, IReadOnlyList<string> names, List<Property> properties)
    {
        string owner = ReadOwner(body);
        int pos = BinaryPrimitives.ReadUInt16LittleEndian(body[..2]);
        while (pos < body.Length)
        {
            if (body.Length - pos < 8)
                throw new InvalidDataException("Property value block ends inside an 8-byte entry header.");
            int entryLength = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(pos, 2));
            int nameIndex = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(pos + 4, 2));
            int valueLength = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(pos + 6, 2));
            if (entryLength < 8 || entryLength > body.Length - pos || valueLength != entryLength - 8)
                throw new InvalidDataException(
                    $"Property entry at {pos} has inconsistent entry/value lengths {entryLength}/{valueLength}.");
            byte ddlFlag = body[pos + 2];
            if (ddlFlag is not (0x00 or DdlFlag))
                throw new InvalidDataException($"Property entry at {pos} has unsupported flag 0x{body[pos + 2]:X2}.");
            if (nameIndex >= names.Count)
                throw new InvalidDataException(
                    $"Property entry at {pos} names pool index {nameIndex}, but the pool has {names.Count} entries.");

            var dataType = (JetDataType)body[pos + 3];
            ReadOnlySpan<byte> raw = body.Slice(pos + 8, valueLength);
            string value = dataType == JetDataType.Boolean
                ? (raw.Length > 0 && raw[0] != 0 ? "1" : "0")
                : Encoding.Unicode.GetString(raw);
            properties.Add(new Property(owner, names[nameIndex], value, dataType, raw.ToArray()) { IsDdl = ddlFlag != 0 });
            pos += entryLength;
        }
    }

    private static void ValidateForWrite(IReadOnlyList<Property> properties)
    {
        var names = properties.Select(p => p.Name).Distinct().ToList();
        ValidateNames(names);
        var nameIndex = names.Select((name, index) => (name, index)).ToDictionary(x => x.name, x => x.index);
        foreach (var group in GroupByOwnerPreservingOrder(properties))
            ValidateOwnerProperties(group.Owner, group.Properties, nameIndex);
    }

    private static void ValidateNames(IReadOnlyList<string> names)
    {
        if (names.Count > ushort.MaxValue + 1)
            throw new ArgumentException($"A property blob cannot name more than {ushort.MaxValue + 1} properties.");
        long bodyLength = 0;
        foreach (string name in names)
        {
            int length = Encoding.Unicode.GetByteCount(name);
            if (length > ushort.MaxValue)
                throw new ArgumentException($"Property name '{name[..Math.Min(name.Length, 32)]}' is too long for its 16-bit byte length.");
            bodyLength += 2L + length;
        }
        if (bodyLength > int.MaxValue - 6)
            throw new ArgumentException("Property name-pool block exceeds its 32-bit block length.");
    }

    private static void ValidateOwnerProperties(
        string owner, IEnumerable<Property> properties, IReadOnlyDictionary<string, int> nameIndex)
    {
        int ownerLength = Encoding.Unicode.GetByteCount(owner);
        if (ownerLength > ushort.MaxValue - 6)
            throw new ArgumentException("Property owner name is too long for its 16-bit owner-record length.", nameof(owner));

        long bodyLength = 6L + ownerLength;
        foreach (Property property in properties)
        {
            if (!nameIndex.TryGetValue(property.Name, out int index) || index > ushort.MaxValue)
                throw new ArgumentException($"Property '{property.Name}' has no encodable 16-bit name-pool index.");
            int valueLength = PropertyValue(property).Length;
            if (valueLength > ushort.MaxValue - 8)
                throw new ArgumentException(
                    $"Property '{property.Name}' value is too long for its 16-bit entry length.");
            bodyLength += 8L + valueLength;
        }
        if (bodyLength > int.MaxValue - 6)
            throw new ArgumentException($"Property block for owner '{owner}' exceeds its 32-bit block length.");
    }

    private static byte[] PropertyValue(Property property) => property.RawValue
        ?? (property.Type == JetDataType.Boolean
            ? [(byte)(property.Value is "1" or "true" or "True" ? 1 : 0)]
            : Encoding.Unicode.GetBytes(property.Value));

    private static IEnumerable<(string Owner, List<Property> Properties)> GroupByOwnerPreservingOrder(IReadOnlyList<Property> properties)
    {
        var order = new List<string>();
        var byOwner = new Dictionary<string, List<Property>>();
        foreach (Property p in properties)
        {
            if (!byOwner.TryGetValue(p.Owner, out var list)) { byOwner[p.Owner] = list = []; order.Add(p.Owner); }
            list.Add(p);
        }
        return order.Select(o => (o, byOwner[o]));
    }

    private static void AppendBlock(List<byte> blob, ushort type, List<byte> body)
    {
        AppendInt32(blob, checked(body.Count + 6));
        AppendUInt16(blob, type);
        blob.AddRange(body);
    }

    private static void AppendString(List<byte> buffer, string s)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(s);
        if (bytes.Length > ushort.MaxValue)
            throw new ArgumentException("String is too long for its 16-bit property-blob byte length.", nameof(s));
        AppendUInt16(buffer, (ushort)bytes.Length);
        buffer.AddRange(bytes);
    }

    private static void AppendUInt16(List<byte> buffer, ushort value) { buffer.Add((byte)value); buffer.Add((byte)(value >> 8)); }
    private static void AppendInt32(List<byte> buffer, int value)
    {
        buffer.Add((byte)value); buffer.Add((byte)(value >> 8)); buffer.Add((byte)(value >> 16)); buffer.Add((byte)(value >> 24));
    }
}
