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
    private const ushort ValueBlock = 0x0001;
    private const byte PropFlag = 0x01;
    private const byte PropTypeMemo = 0x0C; // DefaultValue is stored as a memo/long-text property
    public const string DefaultValueProperty = "DefaultValue";

    /// <summary>A single property: the owning column (or "" for the table), the property name, and its
    /// value text (an expression for <c>DefaultValue</c>).</summary>
    public readonly record struct Property(string Owner, string Name, string Value);

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
                byte[] value = Encoding.Unicode.GetBytes(p.Value);
                var entry = new List<byte>();
                AppendUInt16(entry, (ushort)(2 + 1 + 1 + 2 + 2 + value.Length)); // entry length
                entry.Add(PropFlag);
                entry.Add(PropTypeMemo);
                AppendUInt16(entry, (ushort)nameIndex[p.Name]);
                AppendUInt16(entry, (ushort)value.Length);
                entry.AddRange(value);
                body.AddRange(entry);
            }
            AppendBlock(blob, ValueBlock, body);
        }

        return [.. blob];
    }

    /// <summary>Extracts each column's <c>DefaultValue</c> (owner → value text) from a blob, ignoring
    /// property names it does not recognise. Returns empty for a null/blank blob.</summary>
    public static IReadOnlyDictionary<string, string> ReadColumnDefaults(ReadOnlySpan<byte> blob)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                    int nameIdx = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(q + 4, 2));
                    int vl = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(q + 6, 2));
                    if (el < 8 || q + el > bodyEnd) break;
                    if (nameIdx < names.Count && names[nameIdx] == DefaultValueProperty && owner.Length > 0)
                        result[owner] = Encoding.Unicode.GetString(blob.Slice(q + 8, vl));
                    q += el;
                }
            }
            pos += len;
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
