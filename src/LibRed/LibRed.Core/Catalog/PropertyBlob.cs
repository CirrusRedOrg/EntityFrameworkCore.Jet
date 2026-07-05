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
/// <c>[short entryLen][byte flag=1][byte dataType][short nameIndex][short valueLen][UTF-16 value]</c>.
/// </remarks>
public static class PropertyBlob
{
    private static readonly byte[] SignatureAce = "MR2\0"u8.ToArray();
    private const ushort NameListBlock = 0x0080;
    private const ushort TableBlock = 0x0000;   // value block owned by the table (empty owner name)
    private const ushort ColumnBlock = 0x0001;  // value block owned by a column
    private const byte PropFlag = 0x01;
    public const string DefaultValueProperty = "DefaultValue";
    public const string CheckConstraintsProperty = "CheckConstraints";
    public const string RequiredProperty = "Required";

    /// <summary>A single property: the owning column (or "" for the table), the property name, its value
    /// (text; for a boolean, <c>"1"</c>/<c>"0"</c>), and its stored type. The type is an ordinary
    /// <see cref="JetDataType"/> code — the same byte used by column descriptors and MSysQueries — so Access
    /// stores <c>DefaultValue</c>/<c>CheckConstraints</c> as <see cref="JetDataType.Memo"/> and <c>Required</c>
    /// as <see cref="JetDataType.Boolean"/>.</summary>
    public readonly record struct Property(string Owner, string Name, string Value, JetDataType Type = JetDataType.Memo);

    /// <summary>A boolean property (e.g. <c>Required</c>), stored as a single 0/1 byte.</summary>
    public static Property Bool(string owner, string name, bool value) =>
        new(owner, name, value ? "1" : "0", JetDataType.Boolean);

    /// <summary>Builds the blob for a set of properties, grouped by owner in the given order — matching
    /// what ACE writes (verified byte-for-byte for column DefaultValues).</summary>
    public static byte[] Write(IReadOnlyList<Property> properties)
    {
        var names = properties.Select(p => p.Name).Distinct().ToList();
        var nameIndex = names.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);

        var blob = new List<byte>(SignatureAce);

        var namesBody = new List<byte>();
        foreach (string name in names) AppendString(namesBody, name);
        AppendBlock(blob, NameListBlock, namesBody);

        foreach (var group in GroupByOwnerPreservingOrder(properties))
        {
            var body = new List<byte>();
            var ownerRec = new List<byte> { 0, 0, 0, 0 }; // [recLen placeholder][0x0000]
            AppendString(ownerRec, group.Owner);
            BinaryPrimitives.WriteUInt16LittleEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(ownerRec), (ushort)ownerRec.Count);
            body.AddRange(ownerRec);

            foreach (Property p in group.Properties)
            {
                byte[] value = p.Type == JetDataType.Boolean
                    ? [(byte)(p.Value is "1" or "true" or "True" ? 1 : 0)]
                    : Encoding.Unicode.GetBytes(p.Value);
                var entry = new List<byte>();
                AppendUInt16(entry, (ushort)(2 + 1 + 1 + 2 + 2 + value.Length)); // entry length
                entry.Add(PropFlag);
                entry.Add((byte)p.Type);
                AppendUInt16(entry, (ushort)nameIndex[p.Name]);
                AppendUInt16(entry, (ushort)value.Length);
                entry.AddRange(value);
                body.AddRange(entry);
            }
            // A table-level map (empty owner) uses block type 0x00; a column map uses 0x01.
            AppendBlock(blob, group.Owner.Length == 0 ? TableBlock : ColumnBlock, body);
        }

        return [.. blob];
    }

    /// <summary>Parses every property (owner, name, value) from a blob. Empty owner = a table property.</summary>
    public static IReadOnlyList<Property> Read(ReadOnlySpan<byte> blob)
    {
        var result = new List<Property>();
        if (blob.Length < 4) return result;

        int pos = 4; // skip signature
        var names = new List<string>();
        while (pos + 6 <= blob.Length)
        {
            int len = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(pos, 4));
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(pos + 4, 2));
            if (len < 6 || pos + len > blob.Length) break;
            int bodyStart = pos + 6, bodyEnd = pos + len;

            if (type == NameListBlock)
            {
                int q = bodyStart;
                while (q + 2 <= bodyEnd)
                {
                    int nl = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(q, 2));
                    q += 2;
                    if (q + nl > bodyEnd) break;
                    names.Add(Encoding.Unicode.GetString(blob.Slice(q, nl)));
                    q += nl;
                }
            }
            else
            {
                int q = bodyStart;
                int ownerRecLen = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(q, 2));
                int onl = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(q + 4, 2));
                string owner = Encoding.Unicode.GetString(blob.Slice(q + 6, onl));
                q += ownerRecLen;
                while (q + 8 <= bodyEnd)
                {
                    int el = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(q, 2));
                    var dataType = (JetDataType)blob[q + 3];
                    int nameIdx = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(q + 4, 2));
                    int vl = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(q + 6, 2));
                    if (el < 8 || q + el > bodyEnd) break;
                    if (nameIdx < names.Count)
                    {
                        ReadOnlySpan<byte> raw = blob.Slice(q + 8, vl);
                        // A boolean is a single 0/1 byte; the text/memo values we consume are UTF-16.
                        string value = dataType == JetDataType.Boolean
                            ? (raw.Length > 0 && raw[0] != 0 ? "1" : "0")
                            : Encoding.Unicode.GetString(raw);
                        result.Add(new Property(owner, names[nameIdx], value, dataType));
                    }
                    q += el;
                }
            }
            pos += len;
        }
        return result;
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
        AppendInt32(blob, body.Count + 6);
        AppendUInt16(blob, type);
        blob.AddRange(body);
    }

    private static void AppendString(List<byte> buffer, string s)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(s);
        AppendUInt16(buffer, (ushort)bytes.Length);
        buffer.AddRange(bytes);
    }

    private static void AppendUInt16(List<byte> buffer, ushort value) { buffer.Add((byte)value); buffer.Add((byte)(value >> 8)); }
    private static void AppendInt32(List<byte> buffer, int value)
    {
        buffer.Add((byte)value); buffer.Add((byte)(value >> 8)); buffer.Add((byte)(value >> 16)); buffer.Add((byte)(value >> 24));
    }
}
