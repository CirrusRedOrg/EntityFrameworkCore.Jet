using System.Buffers.Binary;
using System.Security.Cryptography;
using LibRed.Formats;

namespace LibRed.Crypto;

/// <summary>
/// Sets, removes, and changes the password/encryption of an existing Access database file, in place.
/// Implements every <c>.accdb</c> scheme — Office "Standard"/CryptoAPI (RC4-40, AES-256) and Agile
/// (AES-256-CBC / SHA-512); legacy Jet set-password is reserved in <see cref="AccessEncryption"/> but not yet
/// built. The whole file is loaded into memory, transformed, and written back — intended for the typical Access
/// database size, not multi-gigabyte files.
/// </summary>
public static class DatabaseEncryption
{
    private const int PageSize = 4096;
    private const int KeyOffset = 0x3E;          // 4-byte database (encoding) key, XOR-masked by the page-0 header mask
    private const int LengthOffset = 0x299;      // 2-byte EncryptionInfo blob length (Access's "is encrypted" signal)
    private const int DescriptorOffset = 0x29B;  // the EncryptionInfo blob itself

    /// <summary>Encrypts a currently-unencrypted database with a new <paramref name="password"/> using
    /// <paramref name="scheme"/>. Throws if the file is already encrypted (use <see cref="ChangePassword"/>) or the
    /// scheme is invalid for the file format.</summary>
    public static void SetPassword(string path, string password, AccessEncryption scheme)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        byte[] file = File.ReadAllBytes(path);
        ValidateScheme(scheme, DetectFormat(file));
        if (DecodeDatabaseKey(file) != 0)
            throw new InvalidOperationException("Database is already encrypted; use ChangePassword.");

        Encrypt(file, password, scheme);
        File.WriteAllBytes(path, file);
    }

    /// <summary>Decrypts an encrypted database, removing its password. Throws if the file is not encrypted or the
    /// password is incorrect.</summary>
    public static void RemovePassword(string path, string password)
    {
        byte[] file = File.ReadAllBytes(path);
        int dbKey = DecodeDatabaseKey(file);
        if (dbKey == 0)
            throw new InvalidOperationException("Database is not encrypted.");

        var codec = OpenDecryptor(file, dbKey, password); // validates the password
        int pages = file.Length / PageSize;
        for (int p = 1; p < pages; p++)
            codec.DecryptPage(p, file.AsSpan(p * PageSize, PageSize));
        ClearEncryption(file);
        File.WriteAllBytes(path, file);
    }

    /// <summary>Changes the password (and optionally the scheme): decrypt with the old password, then re-encrypt
    /// with the new one — exactly remove + set.</summary>
    public static void ChangePassword(string path, string oldPassword, string newPassword, AccessEncryption scheme)
    {
        RemovePassword(path, oldPassword);
        SetPassword(path, newPassword, scheme);
    }

    private static void Encrypt(byte[] file, string password, AccessEncryption scheme)
    {
        int dbKey = BinaryPrimitives.ReadInt32LittleEndian(RandomBytes(4));
        if (dbKey == 0) dbKey = 1; // 0 would read back as "unencrypted"

        byte[] descriptor;
        IPageCodec codec;
        switch (scheme)
        {
            case AccessEncryption.Agile: (descriptor, codec) = AgileEncryption.Create(password, dbKey); break;
            case AccessEncryption.OfficeStandardAes: (descriptor, codec) = OfficeStandardEncryption.Create(password, aes: true, dbKey); break;
            case AccessEncryption.OfficeStandardRc4: (descriptor, codec) = OfficeStandardEncryption.Create(password, aes: false, dbKey); break;
            default: throw new ArgumentOutOfRangeException(nameof(scheme));
        }
        WriteDatabaseKey(file, dbKey);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(LengthOffset, 2), (ushort)descriptor.Length);
        descriptor.CopyTo(file, DescriptorOffset);

        int pages = file.Length / PageSize;
        for (int p = 1; p < pages; p++)
            codec.EncryptPage(p, file.AsSpan(p * PageSize, PageSize));
    }

    private static void ClearEncryption(byte[] file)
    {
        int blobLen = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(LengthOffset, 2));
        Array.Clear(file, LengthOffset, 2 + blobLen); // the length signal + the descriptor
        WriteDatabaseKey(file, 0);                    // decodes back to 0 = unencrypted
    }

    private static IPageCodec OpenDecryptor(byte[] file, int dbKey, string password)
    {
        var page0 = file.AsSpan(0, PageSize);
        IPageCodec? codec = DetectFormat(file).IsAccdb
            ? (IPageCodec?)AgileEncryption.TryCreate(page0, dbKey, password)
                ?? OfficeStandardEncryption.TryCreate(page0, dbKey, password)
            : JetLegacyEncryption.TryCreate(dbKey);
        return codec ?? throw new InvalidOperationException("Unrecognised or unsupported encryption scheme.");
    }

    private static void ValidateScheme(AccessEncryption scheme, JetFormatBase format)
    {
        switch (scheme)
        {
            case AccessEncryption.OfficeStandardRc4:
            case AccessEncryption.OfficeStandardAes:
                if (!format.IsAccdb)
                    throw new ArgumentException($"{scheme} requires an .accdb (ACE) database.", nameof(scheme));
                break;
            case AccessEncryption.Agile:
                if (!format.IsAccdb)
                    throw new ArgumentException("Agile encryption requires an .accdb (ACE) database.", nameof(scheme));
                break;
            case AccessEncryption.LegacyJet:
                if (format.IsAccdb)
                    throw new ArgumentException("Legacy Jet encryption applies to .mdb, not .accdb.", nameof(scheme));
                throw new NotSupportedException("Legacy Jet set-password is not yet implemented.");
            case AccessEncryption.None:
                throw new ArgumentException("Use RemovePassword to remove encryption.", nameof(scheme));
            default:
                throw new ArgumentOutOfRangeException(nameof(scheme));
        }
    }

    private static JetFormatBase DetectFormat(byte[] file)
    {
        using var ms = new MemoryStream(file, writable: false);
        return JetFormatBase.Detect(ms);
    }

    private static int DecodeDatabaseKey(byte[] file)
    {
        ReadOnlySpan<byte> mask = JetFormatBase.PageZeroHeaderMask;
        int start = JetFormatBase.PageZeroHeaderMaskStart;
        Span<byte> key = stackalloc byte[4];
        for (int i = 0; i < 4; i++) key[i] = (byte)(file[KeyOffset + i] ^ mask[KeyOffset - start + i]);
        return BinaryPrimitives.ReadInt32LittleEndian(key);
    }

    private static void WriteDatabaseKey(byte[] file, int dbKey)
    {
        ReadOnlySpan<byte> mask = JetFormatBase.PageZeroHeaderMask;
        int start = JetFormatBase.PageZeroHeaderMaskStart;
        Span<byte> k = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(k, dbKey);
        for (int i = 0; i < 4; i++) file[KeyOffset + i] = (byte)(k[i] ^ mask[KeyOffset - start + i]);
    }

    private static byte[] RandomBytes(int n) { byte[] b = new byte[n]; RandomNumberGenerator.Fill(b); return b; }
}
