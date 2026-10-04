using LibRed.Storage.Types;
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
/// (<c>[short len][UTF-16 name]</c> repeated); other blocks are a per-owner value map, whose type says what
/// owns it — <c>0x0000</c> the table, <c>0x0001</c> a column, and by mdbtools' account <c>0x0002</c> an index:
/// <c>[short ownerRecLen][short 0][short nameLen][owner name]</c> then property entries
/// <c>[short entryLen][byte flags][byte dataType][short nameIndex][short valueLen][UTF-16 value]</c>.
/// </remarks>
public static class PropertyBlob
{
    /// <summary>The signature an ACE blob opens with; <see cref="SignatureMdb"/> on an older <c>.mdb</c>.</summary>
    internal static ReadOnlySpan<byte> SignatureAce => "MR2\0"u8;
    internal static ReadOnlySpan<byte> SignatureMdb => "KKD\0"u8;
    internal const int SignatureSize = 4;

    // --- Block: [length:4][type:2][body] ---

    internal const int BlockLengthOffset = 0;
    internal const int BlockTypeOffset = 4;
    internal const int BlockHeaderSize = 6;

    /// <summary>The block types: the property-name pool, and the value blocks owned by the table (empty owner
    /// name) and by a column. By mdbtools' account an index's block is <c>0x0002</c>.</summary>
    internal const ushort NameListBlock = 0x0080;
    internal const ushort TableBlock = 0x0000;
    internal const ushort ColumnBlock = 0x0001;

    /// <summary>A name-pool entry's, and an owner's, UTF-16 byte length ahead of the text.</summary>
    internal const int NameLengthSize = 2;

    // --- Owner record: [recordLength:2][unmodelled:2][nameLength:2][UTF-16 name] ---

    internal const int OwnerRecordLengthOffset = 0;
    internal const int OwnerUnmodelledOffset = 2;
    internal const int OwnerNameLengthOffset = 4;
    internal const int OwnerHeaderSize = 6;

    // --- Property entry: [entryLength:2][flags:1][dataType:1][nameIndex:2][valueLength:2][value] ---

    internal const int EntryLengthOffset = 0;
    internal const int EntryFlagsOffset = 2;
    internal const int EntryTypeOffset = 3;
    internal const int EntryNameIndexOffset = 4;
    internal const int EntryValueLengthOffset = 6;
    internal const int EntryHeaderSize = 8;

    /// <summary>The entry flag marking a DDL property, protected as part of the object's definition.</summary>
    internal const byte DdlFlag = 0x01;


    public const string DefaultValueProperty = "DefaultValue";
    public const string CheckConstraintsProperty = "CheckConstraints";
    public const string RequiredProperty = "Required";
    public const string ValidationRuleProperty = "ValidationRule";
    public const string ValidationTextProperty = "ValidationText";

    /// <summary>A calculated column's expression text (§3.4a).</summary>
    public const string ExpressionProperty = "Expression";

    /// <summary>A calculated column's real result type, as a one-byte Jet type code (§3.4a).</summary>
    public const string ResultTypeProperty = "ResultType";

    /// <summary>A single property: the owning column (or "" for the table), the property name, its value, and
    /// its stored type. The type is an ordinary <see cref="JetDataType"/> code — the same byte used by column
    /// descriptors and MSysQueries — and any of them can occur on any owner (§11): Access stores
    /// <c>DefaultValue</c>/<c>CheckConstraints</c> as <see cref="JetDataType.Memo"/>, <c>Description</c> as
    /// <see cref="JetDataType.Text"/>, <c>Required</c> as <see cref="JetDataType.Boolean"/>, <c>ColumnWidth</c>
    /// as <see cref="JetDataType.Int16"/>, a <c>GUID</c> as <see cref="JetDataType.Binary"/>.
    /// <para><see cref="Value"/> is the value as invariant text — the text itself for a text type,
    /// <c>"1"</c>/<c>"0"</c> for a boolean, the number, date or GUID in invariant form, and hex for binary —
    /// and <see cref="TypedValue"/> the value itself. The stored length decides the width, not the type: a
    /// Boolean or an Int16 can be four bytes.</para>
    /// <para><see cref="RawValue"/> holds the exact stored value bytes when the property was <see cref="Read"/>
    /// from a blob; <see cref="Write"/> emits it verbatim, so every property round-trips byte-for-byte. It is
    /// <c>null</c> for a property constructed from a <see cref="Value"/> string, which is then encoded from
    /// <see cref="Value"/>/<see cref="Type"/>; <see cref="Of"/> builds one from a CLR value instead.</para>
    /// <para><see cref="Flags"/> is the entry's flag byte, kept whole and written back unchanged. It is a bit
    /// field: <c>0x01</c> marks a DDL property, protected as part of the object's definition, and Access writes
    /// <c>0x80</c> on its own account (seen on stored queries). It defaults to <c>0x01</c> because the properties
    /// LibRed creates are schema properties such as <c>DefaultValue</c>, <c>Required</c> and
    /// <c>CheckConstraints</c>.</para>
    /// <para><see cref="Block"/> is the type of the value block the property was read from — by mdbtools' account an
    /// index's properties sit in a block of their own, under the index's name, which is usually its column's — and
    /// is written back as that type. It is null for a property LibRed constructs, which goes in the table's block or its column's.</para></summary>
#pragma warning disable CA1716 // "Property" is the format's own name for this record, and it is nested in PropertyBlob.
    public readonly record struct Property(
        string Owner, string Name, string Value, JetDataType Type = JetDataType.Memo, byte[]? RawValue = null)
    {
        /// <summary>The entry's flag byte.</summary>
        public byte Flags { get; init; } = DdlFlag;

        /// <summary>The type of the value block the property was read from; null for a constructed one.</summary>
        public ushort? Block { get; init; }

        /// <summary>Whether this is a property of the column named <paramref name="owner"/>, or of the table for
        /// <c>""</c> — and not of an index that shares the name.</summary>
        public bool IsOwnedBy(string owner) =>
            BlockType == (owner.Length == 0 ? TableBlock : ColumnBlock)
            && string.Equals(Owner, owner, StringComparison.OrdinalIgnoreCase);

        internal ushort BlockType => Block ?? (Owner.Length == 0 ? TableBlock : ColumnBlock);

        /// <summary>The value as its type holds it: <see cref="bool"/>, <see cref="byte"/>, a signed integer
        /// as wide as the stored value, <see cref="decimal"/> for Currency, <see cref="float"/>/<see cref="double"/>,
        /// <see cref="DateTime"/>, <see cref="System.Guid"/>, <see cref="string"/> for text, else the bytes.</summary>
        public object? TypedValue => Decode(Type, PropertyValue(this));
    }
#pragma warning restore CA1716

    /// <summary>A boolean property (e.g. <c>Required</c>), stored as a single 0/1 byte.</summary>
    public static Property Bool(string owner, string name, bool value) =>
        new(owner, name, value ? "1" : "0", JetDataType.Boolean);

    /// <summary>A property holding <paramref name="value"/>, stored as the type Access uses for that CLR type —
    /// <see cref="bool"/> Boolean, <see cref="byte"/> Byte, <see cref="short"/> Int16, <see cref="int"/> Int32,
    /// <see cref="long"/> Int64, <see cref="decimal"/> Currency, <see cref="float"/> Single, <see cref="double"/>
    /// Double, <see cref="DateTime"/> DateTime, <see cref="System.Guid"/> and <c>byte[]</c> Binary (Access stores a
    /// <c>GUID</c> property as 16 binary bytes in <see cref="System.Guid.ToByteArray()"/> order), <see cref="string"/>
    /// Memo — or as <paramref name="type"/> when given (<c>Text</c> for a string Access keeps as Text, <c>Ole</c>
    /// for a blob).</summary>
    public static Property Of(string owner, string name, object value, JetDataType? type = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        JetDataType stored = type ?? value switch
        {
            bool => JetDataType.Boolean,
            byte => JetDataType.Byte,
            short => JetDataType.Int16,
            int => JetDataType.Int32,
            long => JetDataType.Int64,
            decimal => JetDataType.Currency,
            float => JetDataType.Single,
            double => JetDataType.Double,
            DateTime => JetDataType.DateTime,
            Guid or byte[] => JetDataType.Binary,
            string => JetDataType.Memo,
            _ => throw new ArgumentException($"A {value.GetType().Name} has no property type.", nameof(value)),
        };
        byte[] raw = Encode(stored, value);
        return new Property(owner, name, Format(Decode(stored, raw)), stored, raw);
    }

    /// <summary>Builds the blob for a set of properties, grouped by owner in the given order — matching
    /// what ACE writes (verified byte-for-byte for column DefaultValues).</summary>
    /// <param name="properties">The properties to write, grouped by owner in the order given.</param>
    /// <param name="original">The blob being rewritten, when rewriting one: its signature is stamped, so a
    /// Jet-4 <c>KKD\0</c> blob is not silently reissued as an ACE <c>MR2\0</c> one, and its name pool is kept
    /// whole and in order, new names appended. Access's pool keeps the names of properties since deleted
    /// (measured: <c>Description</c>, <c>Filter</c>, <c>OrderBy</c> on tables whose entries are gone), so a pool
    /// rebuilt from the surviving properties changes every such blob it rewrites. Four bytes stamp the
    /// signature alone. Omit it only when authoring a blob from nothing, where ACE's signature is the default.</param>
    public static byte[] Write(IReadOnlyList<Property> properties, ReadOnlySpan<byte> original = default)
    {
        var names = original.Length > SignatureSize ? Parse(original).Names.ToList() : [];
        foreach (string name in properties.Select(p => p.Name))
            if (!names.Contains(name)) names.Add(name);
        ValidateForWrite(properties, names);
        var nameIndex = new Dictionary<string, int>();
        for (int i = 0; i < names.Count; i++) nameIndex.TryAdd(names[i], i);

        var blob = new List<byte>(original.Length >= SignatureSize ? original[..SignatureSize].ToArray() : SignatureAce.ToArray());

        var namesBody = new List<byte>();
        foreach (string name in names) AppendString(namesBody, name);
        AppendBlock(blob, NameListBlock, namesBody);

        foreach (var group in GroupByOwnerPreservingOrder(properties))
            AppendOwnerBlock(blob, group.Block, group.Owner, group.Properties, nameIndex);

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

        var result = new List<byte>(blob[..SignatureSize].ToArray());   // keep the original signature, MR2\0 or KKD\0
        var namesBody = new List<byte>();
        foreach (string n in names) AppendString(namesBody, n);
        AppendBlock(result, NameListBlock, namesBody);
        foreach (byte[] b in otherBlocks) result.AddRange(b);
        AppendOwnerBlock(result, owner.Length == 0 ? TableBlock : ColumnBlock, owner, newProps, nameIndex);
        return [.. result];
    }

    /// <summary>Appends one owner's value block of the given type: the owner record then a property entry per
    /// property.</summary>
    private static void AppendOwnerBlock(
        List<byte> blob, ushort type, string owner, IEnumerable<Property> props, Dictionary<string, int> nameIndex)
    {
        Property[] propertyArray = props.ToArray();
        ValidateOwnerProperties(owner, propertyArray, nameIndex);
        var body = new List<byte>();
        AppendOwnerRecord(body, owner, unmodelled: 0);

        foreach (Property p in propertyArray)
        {
            // A property Read from a blob carries its exact stored bytes (RawValue) — emit them verbatim so
            // anything LibRed does not model round-trips byte-for-byte. A LibRed-constructed property has no
            // RawValue and is encoded from Value/Type (Boolean = one 0/1 byte, else UTF-16).
            byte[] value = PropertyValue(p);
            // In field order: [entryLength][flags][dataType][nameIndex][valueLength][value].
            AppendUInt16(body, (ushort)(EntryHeaderSize + value.Length));
            body.Add(p.Flags);
            body.Add((byte)p.Type);
            AppendUInt16(body, (ushort)nameIndex[p.Name]);
            AppendUInt16(body, (ushort)value.Length);
            body.AddRange(value);
        }
        AppendBlock(blob, type, body);
    }

    /// <summary>Appends an owner record in field order — its length, the unmodelled word, the owner's name — the
    /// inverse of <see cref="ReadOwner"/>.</summary>
    private static void AppendOwnerRecord(List<byte> body, string owner, ushort unmodelled)
    {
        int start = body.Count;
        AppendUInt16(body, 0); // the record's length, known once the name is in
        AppendUInt16(body, unmodelled);
        AppendString(body, owner);
        BinaryPrimitives.WriteUInt16LittleEndian(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(body)[(start + OwnerRecordLengthOffset)..],
            (ushort)(body.Count - start));
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
        result.AddRange(blob[..SignatureSize]);
        foreach (ParsedBlock block in parsed.Blocks)
        {
            // Only the column's own block: an index's can carry the same name.
            bool drop = block.Type == ColumnBlock
                && string.Equals(ReadOwner(block.Body), owner, StringComparison.OrdinalIgnoreCase);
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
        result.AddRange(blob[..SignatureSize]);
        foreach (ParsedBlock block in parsed.Blocks)
        {
            if (block.Type != ColumnBlock
                || !string.Equals(ReadOwner(block.Body), oldOwner, StringComparison.OrdinalIgnoreCase))
            {
                result.AddRange(block.Raw);
                continue;
            }

            // A new owner record, its unmodelled word carried through; everything past the old one is this
            // owner's property entries, which the rename must not disturb.
            int oldRecordLength = BinaryPrimitives.ReadUInt16LittleEndian(
                block.Body.AsSpan(OwnerRecordLengthOffset, sizeof(ushort)));
            var body = new List<byte>();
            AppendOwnerRecord(body, newOwner,
                BinaryPrimitives.ReadUInt16LittleEndian(block.Body.AsSpan(OwnerUnmodelledOffset, sizeof(ushort))));
            body.AddRange(block.Body.AsSpan(oldRecordLength).ToArray());
            AppendBlock(result, block.Type, body);
        }

        return [.. result];
    }

    /// <summary>Parses every property (owner, name, value) from a blob. Empty owner = a table property.</summary>
    /// <summary>Every property in the blob. The accessors below select from the result rather than taking the
    /// blob themselves, so a caller wanting several of them parses once — <see cref="Catalog.JetCatalog"/>
    /// wants five per table, two of them per column.</summary>
    public static IReadOnlyList<Property> Read(ReadOnlySpan<byte> blob)
    {
        if (blob.Length == 0) return [];
        return Parse(blob).Properties;
    }

    /// <summary>Extracts each column's <c>DefaultValue</c> (column name → value text).</summary>
    public static IReadOnlyDictionary<string, string> ReadColumnDefaults(IReadOnlyList<Property> properties)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Property p in properties)
            if (p.BlockType == ColumnBlock && p.Name == DefaultValueProperty)
                result[p.Owner] = p.Value;
        return result;
    }

    /// <summary>The set of columns marked <c>Required</c> (NOT NULL) — a column has the property
    /// only when it is required (Access omits it for a nullable column).</summary>
    public static IReadOnlySet<string> ReadRequiredColumns(IReadOnlyList<Property> properties)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Property p in properties)
            if (p.BlockType == ColumnBlock && p.Name == RequiredProperty && p.Value == "1")
                result.Add(p.Owner);
        return result;
    }

    /// <summary>Extracts the <c>ValidationRule</c>/<c>ValidationText</c> designer properties for the given
    /// owner (a column name, or "" for the table); each is null if absent. Access stores these as
    /// ordinary text properties in the <c>LvProp</c> blob, which is what EFCore.Jet's ADOX surfaces as
    /// <c>Jet OLEDB:{Column,Table} Validation Rule/Text</c>.</summary>
    public static (string? Rule, string? Text) ReadValidation(IReadOnlyList<Property> properties, string owner)
    {
        string? rule = null, text = null;
        foreach (Property p in properties)
        {
            if (!p.IsOwnedBy(owner)) continue;
            if (p.Name == ValidationRuleProperty) rule = p.Value.Length > 0 ? p.Value : null;
            else if (p.Name == ValidationTextProperty) text = p.Value.Length > 0 ? p.Value : null;
        }
        return (rule, text);
    }

    /// <summary>Extracts a calculated column's <c>Expression</c> and <c>ResultType</c> properties for the
    /// given column; each is null if absent. <c>ResultType</c> is a single byte holding the Jet type code
    /// the payload is encoded in — the type the column was declared with, which is NOT the descriptor's
    /// type (see page-02b §3.4a).</summary>
    public static (string? Expression, JetDataType? ResultType) ReadCalculated(IReadOnlyList<Property> properties, string owner)
    {
        string? expression = null;
        JetDataType? resultType = null;
        foreach (Property p in properties)
        {
            if (!p.IsOwnedBy(owner)) continue;
            if (p.Name == ExpressionProperty) expression = p.Value.Length > 0 ? p.Value : null;
            else if (p.Name == ResultTypeProperty && p.RawValue is { Length: > 0 } raw)
                resultType = (JetDataType)raw[0];
        }
        return (expression, resultType);
    }

    /// <summary>Extracts the table's CHECK constraints (name, expression). The
    /// <c>CheckConstraints</c> table property stores them as a <c>name\0expression\0</c> list, terminated
    /// by an empty entry.</summary>
    public static IReadOnlyList<(string Name, string Expression)> ReadCheckConstraints(IReadOnlyList<Property> properties)
    {
        foreach (Property p in properties)
            if (p.IsOwnedBy("") && p.Name == CheckConstraintsProperty)
                return ParseCheckList(p.Value);
        return [];
    }

    /// <summary>Serialises CHECK constraints into the <c>CheckConstraints</c> property value: each is
    /// <c>name\0expression\0</c>, then a trailing <c>\0</c> terminator.</summary>
    /// <remarks>Identifier quoting is normalised on the way in. A CHECK is evaluated by the Access
    /// <b>expression service</b>, which does not understand SQL's <c>`backtick`</c> quoting — measured, it
    /// reads <c>`Qty`</c> as a field whose name includes the backticks and fails "Could not find field". The
    /// DDL parser accepts such a constraint quite happily and stores it, so the table is created and then
    /// refuses every INSERT. This is the single point where check text becomes stored bytes, so normalising
    /// here is what stops any route reaching the blob with SQL-flavoured quoting.</remarks>
    public static string WriteCheckList(IReadOnlyList<(string Name, string Expression)> checks)
    {
        var sb = new StringBuilder();
        foreach (var (name, expr) in checks)
            sb.Append(name).Append('\0')
              .Append(Storage.Calculated.CalculatedExpression.NormaliseIdentifierQuoting(expr)).Append('\0');
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
        if (blob.Length < SignatureSize)
            throw new InvalidDataException(
                $"Property blob has {blob.Length} bytes; expected a {SignatureSize}-byte signature.");
        if (!blob[..SignatureSize].SequenceEqual(SignatureAce) && !blob[..SignatureSize].SequenceEqual(SignatureMdb))
            throw new InvalidDataException("Property blob has an unknown signature.");

        var blocks = new List<ParsedBlock>();
        int pos = SignatureSize;
        while (pos < blob.Length)
        {
            if (blob.Length - pos < BlockHeaderSize)
                throw new InvalidDataException($"Property blob has {blob.Length - pos} trailing bytes after its last complete block.");
            int length = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(pos + BlockLengthOffset, sizeof(int)));
            if (length < BlockHeaderSize || length > blob.Length - pos)
                throw new InvalidDataException(
                    $"Property block at {pos} declares invalid length {length} with {blob.Length - pos} bytes remaining.");
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(pos + BlockTypeOffset, sizeof(ushort)));
            blocks.Add(new ParsedBlock(type, blob.Slice(pos + BlockHeaderSize, length - BlockHeaderSize).ToArray(),
                blob.Slice(pos, length).ToArray()));
            pos += length;
        }

        var names = new List<string>();
        foreach (ParsedBlock block in blocks)
            if (block.Type == NameListBlock) ReadNames(block.Body, names);

        var properties = new List<Property>();
        foreach (ParsedBlock block in blocks)
            if (block.Type != NameListBlock) ReadProperties(block.Type, block.Body, names, properties);

        return new ParsedBlob(blocks, names, properties);
    }

    private static void ReadNames(ReadOnlySpan<byte> body, List<string> names)
    {
        int pos = 0;
        while (pos < body.Length)
        {
            if (body.Length - pos < NameLengthSize)
                throw new InvalidDataException("Property name pool ends inside a length field.");
            int length = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(pos, NameLengthSize));
            pos += NameLengthSize;
            if ((length & 1) != 0 || length > body.Length - pos)
                throw new InvalidDataException(
                    $"Property name declares invalid UTF-16 length {length} with {body.Length - pos} bytes remaining.");
            names.Add(Encoding.Unicode.GetString(body.Slice(pos, length)));
            pos += length;
        }
    }

    private static string ReadOwner(ReadOnlySpan<byte> body)
    {
        if (body.Length < OwnerHeaderSize)
            throw new InvalidDataException(
                $"Property owner block is shorter than its {OwnerHeaderSize}-byte owner header.");
        int recordLength = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(OwnerRecordLengthOffset, sizeof(ushort)));
        int ownerLength = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(OwnerNameLengthOffset, sizeof(ushort)));
        if (recordLength < OwnerHeaderSize || recordLength > body.Length || (ownerLength & 1) != 0
            || ownerLength != recordLength - OwnerHeaderSize)
            throw new InvalidDataException(
                $"Property owner record length {recordLength} and UTF-16 name length {ownerLength} are inconsistent.");
        return Encoding.Unicode.GetString(body.Slice(OwnerHeaderSize, ownerLength));
    }

    private static void ReadProperties(ushort block, ReadOnlySpan<byte> body, List<string> names, List<Property> properties)
    {
        string owner = ReadOwner(body);
        int pos = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(OwnerRecordLengthOffset, sizeof(ushort)));
        while (pos < body.Length)
        {
            if (body.Length - pos < EntryHeaderSize)
                throw new InvalidDataException(
                    $"Property value block ends inside an {EntryHeaderSize}-byte entry header.");
            ReadOnlySpan<byte> entry = body[pos..];
            int entryLength = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(EntryLengthOffset, sizeof(ushort)));
            int nameIndex = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(EntryNameIndexOffset, sizeof(ushort)));
            int valueLength = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(EntryValueLengthOffset, sizeof(ushort)));
            if (entryLength < EntryHeaderSize || entryLength > body.Length - pos || valueLength != entryLength - EntryHeaderSize)
                throw new InvalidDataException(
                    $"Property entry at {pos} has inconsistent entry/value lengths {entryLength}/{valueLength}.");
            if (nameIndex >= names.Count)
                throw new InvalidDataException(
                    $"Property entry at {pos} names pool index {nameIndex}, but the pool has {names.Count} entries.");

            var dataType = (JetDataType)entry[EntryTypeOffset];
            byte[] raw = entry.Slice(EntryHeaderSize, valueLength).ToArray();
            properties.Add(new Property(owner, names[nameIndex], Format(Decode(dataType, raw)), dataType, raw)
            {
                Flags = entry[EntryFlagsOffset],
                Block = block,
            });
            pos += entryLength;
        }
    }

    private static void ValidateForWrite(IReadOnlyList<Property> properties, List<string> names)
    {
        ValidateNames(names);
        var nameIndex = new Dictionary<string, int>();
        for (int i = 0; i < names.Count; i++) nameIndex.TryAdd(names[i], i);
        foreach (var group in GroupByOwnerPreservingOrder(properties))
            ValidateOwnerProperties(group.Owner, group.Properties, nameIndex);
    }

    private static void ValidateNames(List<string> names)
    {
        if (names.Count > ushort.MaxValue + 1)
            throw new ArgumentException($"A property blob cannot name more than {ushort.MaxValue + 1} properties.");
        long bodyLength = 0;
        foreach (string name in names)
        {
            int length = Encoding.Unicode.GetByteCount(name);
            if (length > ushort.MaxValue)
                throw new ArgumentException($"Property name '{name[..Math.Min(name.Length, 32)]}' is too long for its 16-bit byte length.");
            bodyLength += (long)NameLengthSize + length;
        }
        if (bodyLength > int.MaxValue - BlockHeaderSize)
            throw new ArgumentException("Property name-pool block exceeds its 32-bit block length.");
    }

    private static void ValidateOwnerProperties(
        string owner, IEnumerable<Property> properties, Dictionary<string, int> nameIndex)
    {
        int ownerLength = Encoding.Unicode.GetByteCount(owner);
        if (ownerLength > ushort.MaxValue - OwnerHeaderSize)
            throw new ArgumentException("Property owner name is too long for its 16-bit owner-record length.", nameof(owner));

        long bodyLength = (long)OwnerHeaderSize + ownerLength;
        foreach (Property property in properties)
        {
            if (!nameIndex.TryGetValue(property.Name, out int index) || index > ushort.MaxValue)
                throw new ArgumentException($"Property '{property.Name}' has no encodable 16-bit name-pool index.");
            int valueLength = PropertyValue(property).Length;
            if (valueLength > ushort.MaxValue - EntryHeaderSize)
                throw new ArgumentException(
                    $"Property '{property.Name}' value is too long for its 16-bit entry length.");
            bodyLength += (long)EntryHeaderSize + valueLength;
        }
        if (bodyLength > int.MaxValue - BlockHeaderSize)
            throw new ArgumentException($"Property block for owner '{owner}' exceeds its 32-bit block length.");
    }

    private static byte[] PropertyValue(Property property) => property.RawValue ?? Encode(property.Type, property.Value);

    private static bool IsText(JetDataType type) => type is JetDataType.Text or JetDataType.Memo;

    /// <summary>A stored value as its type holds it. The stored length sets the width — Access writes some
    /// Booleans and Int16s as four bytes (measured), and a four-byte "Int16" is a signed 32-bit value
    /// (<c>ColumnWidth</c> <c>FFFFFFFF</c> is -1). A Boolean is true when any byte is non-zero (Access writes both
    /// <c>01</c> and <c>FF</c>). A value no reading fits comes back as its bytes.</summary>
    private static object? Decode(JetDataType type, ReadOnlySpan<byte> raw)
    {
        if (IsText(type)) return Encoding.Unicode.GetString(raw);
        switch (type)
        {
            case JetDataType.Boolean:
                return raw.IndexOfAnyExcept((byte)0) >= 0;
            case JetDataType.Byte when raw.Length == 1:
                return raw[0];
            case JetDataType.Byte or JetDataType.Int16 or JetDataType.Int32 or JetDataType.Int64:
                switch (raw.Length)
                {
                    case 1: return (short)(sbyte)raw[0];
                    case 2: return BinaryPrimitives.ReadInt16LittleEndian(raw);
                    case 4: return BinaryPrimitives.ReadInt32LittleEndian(raw);
                    case 8: return BinaryPrimitives.ReadInt64LittleEndian(raw);
                }
                break;
            case JetDataType.Currency when raw.Length == 8:
                return JetTypeCodec.CurrencyFromScaled(BinaryPrimitives.ReadInt64LittleEndian(raw));
            case JetDataType.Single when raw.Length == 4:
                return BinaryPrimitives.ReadSingleLittleEndian(raw);
            case JetDataType.Double when raw.Length == 8:
                return BinaryPrimitives.ReadDoubleLittleEndian(raw);
            case JetDataType.DateTime when raw.Length == 8:
                return JetTypeCodec.TryFromOaDate(BinaryPrimitives.ReadDoubleLittleEndian(raw), out DateTime date)
                    ? date
                    : raw.ToArray();
            case JetDataType.Guid when raw.Length == 16:
                return new Guid(raw);
        }
        return raw.ToArray();
    }

    /// <summary>A decoded value as <see cref="Property.Value"/> text: invariant, round-trippable through
    /// <see cref="Encode(JetDataType, string)"/>, and hex for bytes.</summary>
    private static string Format(object? value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "1" : "0",
        float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToHexString(bytes),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    /// <summary>Encodes a constructed property's <see cref="Property.Value"/> text as its type stores it — the
    /// inverse of <see cref="Format"/>. Booleans are one byte, integers the type's own width.</summary>
    private static byte[] Encode(JetDataType type, string value)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        if (IsText(type)) return Encoding.Unicode.GetBytes(value);
        return type switch
        {
            JetDataType.Boolean => [(byte)(value is "1" or "-1" or "true" or "True" ? 1 : 0)],
            JetDataType.Byte => Encode(type, byte.Parse(value, invariant)),
            JetDataType.Int16 => Encode(type, short.Parse(value, invariant)),
            JetDataType.Int32 => Encode(type, int.Parse(value, invariant)),
            JetDataType.Int64 => Encode(type, long.Parse(value, invariant)),
            JetDataType.Currency => Encode(type, decimal.Parse(value, invariant)),
            JetDataType.Single => Encode(type, float.Parse(value, invariant)),
            JetDataType.Double => Encode(type, double.Parse(value, invariant)),
            JetDataType.DateTime => Encode(type, DateTime.Parse(value, invariant, System.Globalization.DateTimeStyles.RoundtripKind)),
            JetDataType.Guid => Encode(type, Guid.Parse(value)),
            _ => Convert.FromHexString(value),
        };
    }

    /// <summary>Encodes a CLR value as <paramref name="type"/> stores it.</summary>
    private static byte[] Encode(JetDataType type, object value)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        if (IsText(type)) return Encoding.Unicode.GetBytes(Convert.ToString(value, invariant) ?? "");
        byte[] b;
        switch (type)
        {
            case JetDataType.Boolean:
                return [(byte)(Convert.ToBoolean(value, invariant) ? 1 : 0)];
            case JetDataType.Byte:
                return [Convert.ToByte(value, invariant)];
            case JetDataType.Int16:
                b = new byte[2]; BinaryPrimitives.WriteInt16LittleEndian(b, Convert.ToInt16(value, invariant)); return b;
            case JetDataType.Int32:
                b = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, Convert.ToInt32(value, invariant)); return b;
            case JetDataType.Int64:
                b = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(b, Convert.ToInt64(value, invariant)); return b;
            case JetDataType.Currency:
                b = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(b, JetTypeCodec.CurrencyToScaled(value, invariant)); return b;
            case JetDataType.Single:
                b = new byte[4]; BinaryPrimitives.WriteSingleLittleEndian(b, Convert.ToSingle(value, invariant)); return b;
            case JetDataType.Double:
                b = new byte[8]; BinaryPrimitives.WriteDoubleLittleEndian(b, Convert.ToDouble(value, invariant)); return b;
            case JetDataType.DateTime:
                b = new byte[8]; BinaryPrimitives.WriteDoubleLittleEndian(b, ((DateTime)value).ToOADate()); return b;
        }
        return value switch
        {
            Guid g => g.ToByteArray(),
            byte[] bytes => bytes.ToArray(),
            _ => throw new ArgumentException($"A {value.GetType().Name} cannot be stored as a {type} property.", nameof(value)),
        };
    }

    /// <summary>The properties grouped into value blocks — one per block type and owner, since an index's block
    /// can carry the same name as a column's — in the order each block first appears.</summary>
    private static IEnumerable<(ushort Block, string Owner, List<Property> Properties)> GroupByOwnerPreservingOrder(
        IReadOnlyList<Property> properties)
    {
        var order = new List<(ushort, string)>();
        var byOwner = new Dictionary<(ushort, string), List<Property>>();
        foreach (Property p in properties)
        {
            var key = (p.BlockType, p.Owner);
            if (!byOwner.TryGetValue(key, out var list)) { byOwner[key] = list = []; order.Add(key); }
            list.Add(p);
        }
        return order.Select(k => (k.Item1, k.Item2, byOwner[k]));
    }

    private static void AppendBlock(List<byte> blob, ushort type, List<byte> body)
    {
        AppendInt32(blob, checked(body.Count + BlockHeaderSize)); // [length][type], in field order
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