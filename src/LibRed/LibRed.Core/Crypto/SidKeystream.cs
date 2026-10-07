using System.Buffers.Binary;
using LibRed.Formats;
using LibRed.Pages;

namespace LibRed.Crypto;

/// <summary>
/// The per-file RC4 keystream every security SID in <c>MSysObjects.Owner</c> and <c>MSysACEs.SID</c> is stored
/// XOR'd with (page-00 §2.3). Its key is folded from page 0's decoded header — the database-password field, the
/// creation date and the header bytes around them — so a file's SIDs belong to that header, and changing one
/// of its inputs means re-masking every SID.
/// </summary>
internal static class SidKeystream
{
    // The default workgroup's account SIDs, as a stock System.mdw's MSysAccounts holds them (the file Access opens
    // first to authenticate). The Admins group alone has a 102-byte SID, which Access adds on first open, so it is
    // not emitted. On disk each is XOR'd with the file's own keystream (see MaskAccount).
    public static readonly byte[] AdminAccount = [0x03, 0x01];   // admin user — owner of what it creates, read grantee on the system tables
    public static readonly byte[] UsersAccount = [0x02, 0x01];   // Users group — full grantee
    public static readonly byte[] EngineAccount = [0x02, 0x03];  // Engine — owner of the system tables and the DAO containers
    public static readonly byte[] CreatorAccount = [0x02, 0x04]; // Creator — the inheritable container grant's placeholder for an object's owner

    /// <summary>The first <paramref name="length"/> keystream bytes for a page 0 as stored on disk. Each SID is
    /// XOR'd from the stream's first byte, whatever its length.</summary>
    public static byte[] For(ReadOnlySpan<byte> page0, int length, JetFormatBase format)
    {
        // 1-3. The region folded is the password field and as many bytes again after it, out from under the
        // fixed header mask; the password field is then unmasked with the creation date's integer part, as it is
        // stored.
        int passwordSize = format.PasswordSize;
        Span<byte> region = stackalloc byte[2 * passwordSize];
        DatabaseDefinitionPage.ReadMasked(page0, format.PasswordOffset, region, format);
        Span<byte> creationDate = stackalloc byte[sizeof(double)];
        DatabaseDefinitionPage.ReadMasked(page0, format.CreationDateOffset, creationDate, format);
        DatabaseDefinitionPage.XorPasswordDateMask(region[..passwordSize], creationDate);

        // 4-5. Seeded with the creation date's low four raw bytes, folding every even byte of the region.
        uint key = BinaryPrimitives.ReadUInt32LittleEndian(creationDate);
        for (int i = 0; i < passwordSize; i++) key ^= (uint)region[2 * i] << (i % 24);

        // 6. RC4 under the key's four little-endian bytes.
        Span<byte> rc4Key = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(rc4Key, key);
        var stream = new byte[length];
        Rc4Cipher.Apply(rc4Key, stream);
        return stream;
    }

    /// <summary>A workgroup account's SID — as the workgroup file's <c>MSysAccounts</c> holds it — the way this
    /// file stores it: the SID XOR the keystream, from its first byte.</summary>
    public static byte[] MaskAccount(ReadOnlySpan<byte> page0, ReadOnlySpan<byte> account, JetFormatBase format)
    {
        byte[] stream = For(page0, account.Length, format);
        byte[] masked = account.ToArray();
        for (int i = 0; i < masked.Length; i++) masked[i] ^= stream[i];
        return masked;
    }
}