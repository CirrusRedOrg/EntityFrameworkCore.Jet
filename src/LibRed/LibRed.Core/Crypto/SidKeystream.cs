using System.Buffers.Binary;
using LibRed.Formats;

namespace LibRed.Crypto;

/// <summary>
/// The per-file RC4 keystream every security SID in <c>MSysObjects.Owner</c> and <c>MSysACEs.SID</c> is stored
/// XOR'd with (page-00 §2.3). Its key is folded from page 0's decoded header — the database-password field, the
/// creation date and the header bytes around them — so a file's SIDs belong to that header, and changing one
/// of its inputs means re-masking every SID.
/// </summary>
internal static class SidKeystream
{
    private const int FoldRegion = 0x42;        // 80 bytes from here: the 40-byte password field and 40 after it
    private const int PasswordFieldSize = 40;
    private const int CreationDateOffset = JetFormatBase.CreationDateOffset;

    /// <summary>The first <paramref name="length"/> keystream bytes for a page 0 as stored on disk. Each SID is
    /// XOR'd from the stream's first byte, whatever its length.</summary>
    public static byte[] For(ReadOnlySpan<byte> page0, int length)
    {
        // 1. Remove the fixed header mask.
        int start = JetFormatBase.PageZeroHeaderMaskStart;
        ReadOnlySpan<byte> headerMask = JetFormatBase.PageZeroHeaderMask;
        Span<byte> header = stackalloc byte[start + headerMask.Length];
        page0[..header.Length].CopyTo(header);
        for (int i = 0; i < headerMask.Length; i++) header[start + i] ^= headerMask[i];

        // 2-3. The 80-byte region, its first 40 bytes (the password field) unmasked with the creation date's
        // integer part, as the field is stored.
        Span<byte> region = stackalloc byte[2 * PasswordFieldSize];
        header.Slice(FoldRegion, region.Length).CopyTo(region);
        Span<byte> dateMask = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(dateMask,
            (int)BinaryPrimitives.ReadDoubleLittleEndian(header.Slice(CreationDateOffset, 8)));
        for (int i = 0; i < PasswordFieldSize; i++) region[i] ^= dateMask[i % 4];

        // 4-5. Seeded with the creation date's low four raw bytes, folding every even byte of the region.
        uint key = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(CreationDateOffset, 4));
        for (int i = 0; i < PasswordFieldSize; i++) key ^= (uint)region[2 * i] << (i % 24);

        // 6. RC4 under the key's four little-endian bytes.
        Span<byte> rc4Key = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(rc4Key, key);
        var stream = new byte[length];
        Rc4Cipher.Apply(rc4Key, stream);
        return stream;
    }

    /// <summary>A workgroup account's SID — as the workgroup file's <c>MSysAccounts</c> holds it — the way this
    /// file stores it: the SID XOR the keystream, from its first byte.</summary>
    public static byte[] MaskAccount(ReadOnlySpan<byte> page0, ReadOnlySpan<byte> account)
    {
        byte[] stream = For(page0, account.Length);
        byte[] masked = account.ToArray();
        for (int i = 0; i < masked.Length; i++) masked[i] ^= stream[i];
        return masked;
    }
}
