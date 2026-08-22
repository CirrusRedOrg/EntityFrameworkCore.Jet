using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using LibRed.Formats;

namespace LibRed.Crypto;

/// <summary>
/// Sets, removes, and changes the password/encryption of an existing Access database file, in place.
/// Implements every <c>.accdb</c> scheme — Office "Standard"/CryptoAPI (RC4-40, AES-256) and Agile
/// (AES-256-CBC / SHA-512) — plus the legacy Jet 4 (<c>.mdb</c>) database password (password-only obfuscation).
/// The whole file is loaded into memory, transformed, and written back — intended for the typical Access
/// database size, not multi-gigabyte files.
/// </summary>
public static class DatabaseEncryption
{
    private const int PageSize = 4096;
    private const int KeyOffset = 0x3E;          // 4-byte database (encoding) key, XOR-masked by the page-0 header mask
    private const int LengthOffset = 0x299;      // 2-byte EncryptionInfo blob length (Access's "is encrypted" signal)
    private const int DescriptorOffset = 0x29B;  // the EncryptionInfo blob itself

    private const int JetPasswordOffset = 0x42;  // 40-byte legacy Jet database-password field (header-masked)
    private const int JetPasswordSize = 40;      // 20 UTF-16LE chars
    private const int HeaderDateOffset = 0x72;   // 8-byte creation-date OLE double (header-masked)

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

    /// <summary>Encrypts a currently-unencrypted <c>.accdb</c> with Office "Standard" RC4, letting the caller pick
    /// the <paramref name="keyBits"/> (40–128, multiple of 8) and <paramref name="hash"/>. RC4 is the only Standard
    /// cipher a stock/add-in Access reads back; key lengths above 56 bits or SHA-2 hashes require the "Enhanced"
    /// provider (the EncryptionEnhancer add-in) to open in Access, though LibRed reads them regardless.</summary>
    public static void SetPasswordRc4(string path, string password, int keyBits = 40, StandardHash hash = StandardHash.Sha1)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ValidateRc4Options(keyBits, hash);

        byte[] file = File.ReadAllBytes(path);
        if (!DetectFormat(file).IsAccdb)
            throw new ArgumentException("Office-Standard encryption requires an .accdb (ACE) database.", nameof(path));
        if (DecodeDatabaseKey(file) != 0)
            throw new InvalidOperationException("Database is already encrypted; use ChangePassword.");

        int dbKey = NewDatabaseKey();
        var (descriptor, codec) = OfficeStandardEncryption.CreateRc4(password, keyBits, ToHashName(hash), dbKey);
        ApplyEncryption(file, dbKey, descriptor, codec);
        File.WriteAllBytes(path, file);
    }

    /// <summary>Changes the password (and optionally the scheme): decrypt with the old password, then re-encrypt
    /// with the new one — exactly remove + set.</summary>
    public static void ChangePassword(string path, string oldPassword, string newPassword, AccessEncryption scheme)
    {
        ArgumentException.ThrowIfNullOrEmpty(newPassword);
        // Validate before RemovePassword decrypts, so a rejected scheme can't leave the database plaintext.
        // Detect reads only the header, so this costs one page rather than a second copy of the whole file.
        ValidateScheme(scheme, DetectFormatOf(path));
        RemovePassword(path, oldPassword);
        SetPassword(path, newPassword, scheme);
    }

    /// <summary>Changes the password to a fresh Office-Standard RC4 encryption with the given key length and hash —
    /// decrypt with the old password, then <see cref="SetPasswordRc4"/>.</summary>
    public static void ChangePasswordRc4(string path, string oldPassword, string newPassword, int keyBits = 40, StandardHash hash = StandardHash.Sha1)
    {
        ArgumentException.ThrowIfNullOrEmpty(newPassword);
        ValidateRc4Options(keyBits, hash);
        RemovePassword(path, oldPassword);
        SetPasswordRc4(path, newPassword, keyBits, hash);
    }

    private static void ValidateRc4Options(int keyBits, StandardHash hash)
    {
        if (keyBits is < 40 or > 128 || keyBits % 8 != 0)
            throw new ArgumentOutOfRangeException(nameof(keyBits), "RC4 key length must be 40–128 bits, in multiples of 8.");
        _ = ToHashName(hash);
    }

    /// <summary>Sets the legacy Jet 4 (<c>.mdb</c>) database password — the "Set Database Password" feature, which is
    /// password obfuscation only (the data pages stay plaintext; this is not RC4 page encryption). The password
    /// (≤20 chars) is stored UTF-16LE at <c>0x42</c>, XOR-masked with the 32-bit truncation of the creation-date
    /// double at <c>0x72</c>, all within the header-masked region. Verified byte-identical to Access's own output.</summary>
    public static void SetJetPassword(string path, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (password.Length > JetPasswordSize / 2)
            throw new ArgumentException($"A Jet database password is at most {JetPasswordSize / 2} characters.", nameof(password));

        byte[] file = File.ReadAllBytes(path);
        if (DetectFormat(file).IsAccdb)
            throw new ArgumentException("The legacy Jet password applies to .mdb, not .accdb — use SetPassword.", nameof(path));

        WriteJetPasswordField(file, password);
        File.WriteAllBytes(path, file);
    }

    /// <summary>Removes a legacy Jet 4 (<c>.mdb</c>) database password by clearing the password field (equivalent to
    /// setting an empty password).</summary>
    public static void RemoveJetPassword(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        if (DetectFormat(file).IsAccdb)
            throw new ArgumentException("The legacy Jet password applies to .mdb, not .accdb — use RemovePassword.", nameof(path));

        WriteJetPasswordField(file, ""); // empty password → field encodes the mask alone, decoding back to ""
        File.WriteAllBytes(path, file);
    }

    // Encodes the password into the header-masked 0x42 field: plaintext = UTF-16LE(password) zero-padded to 40 bytes,
    // XORed with the 4-byte little-endian (int)creationDateDouble mask (cycled); the on-disk bytes are that plaintext
    // XORed with the page-0 header mask. Reading (jackcess/LibRed) is the exact inverse.
    private static void WriteJetPasswordField(byte[] file, string password)
    {
        ReadOnlySpan<byte> hmask = JetFormatBase.PageZeroHeaderMask;
        int start = JetFormatBase.PageZeroHeaderMaskStart;

        Span<byte> date = stackalloc byte[8];
        for (int i = 0; i < 8; i++) date[i] = (byte)(file[HeaderDateOffset + i] ^ hmask[HeaderDateOffset - start + i]);
        Span<byte> dateMask = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(dateMask, (int)BitConverter.ToDouble(date));

        Span<byte> field = stackalloc byte[JetPasswordSize];
        field.Clear();
        Encoding.Unicode.GetBytes(password).CopyTo(field);
        for (int i = 0; i < JetPasswordSize; i++)
            file[JetPasswordOffset + i] = (byte)(field[i] ^ dateMask[i % 4] ^ hmask[JetPasswordOffset - start + i]);
    }

    /// <summary>Applies legacy Jet 4 (<c>.mdb</c>) page encoding — the "Encode Database" feature — RC4-encrypting
    /// every data page with a fresh random database key stored at <c>0x3E</c>. This is <b>independent</b> of the
    /// database password (<see cref="SetJetPassword"/>): a file may carry both (encoding scrambles the pages, the
    /// password gates opening). Throws if the file is already encoded, is an <c>.accdb</c>, or is Jet 3.</summary>
    public static void SetJetEncoding(string path) => SetJetEncoding(path, NewDatabaseKey());

    // Explicit-key overload (internal): the public API picks a fresh random key; tests use a specific key to
    // reproduce a real Access-encoded file byte-for-byte.
    internal static void SetJetEncoding(string path, int dbKey)
    {
        byte[] file = File.ReadAllBytes(path);
        RequireJet4Mdb(file);
        if (DecodeDatabaseKey(file) != 0)
            throw new InvalidOperationException("Database is already encoded.");

        ApplyJetEncoding(file, dbKey);
        File.WriteAllBytes(path, file);
    }

    /// <summary>Removes legacy Jet 4 (<c>.mdb</c>) page encoding — RC4-decrypts every page with the stored
    /// <c>0x3E</c> key and clears it. Leaves any database password (the <c>0x42</c> field) untouched.</summary>
    public static void RemoveJetEncoding(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        RequireJet4Mdb(file);
        int dbKey = DecodeDatabaseKey(file);
        if (dbKey == 0)
            throw new InvalidOperationException("Database is not encoded.");

        var codec = new JetLegacyEncryption(dbKey);
        int pages = file.Length / PageSize;
        for (int p = 1; p < pages; p++)
            codec.DecryptPage(p, file.AsSpan(p * PageSize, PageSize));
        WriteDatabaseKey(file, 0);
        File.WriteAllBytes(path, file);
    }

    private static void ApplyJetEncoding(byte[] file, int dbKey)
    {
        WriteDatabaseKey(file, dbKey);                 // page 0 (header) is never encrypted
        var codec = new JetLegacyEncryption(dbKey);
        int pages = file.Length / PageSize;
        for (int p = 1; p < pages; p++)
            codec.EncryptPage(p, file.AsSpan(p * PageSize, PageSize));
    }

    private static void RequireJet4Mdb(byte[] file)
    {
        if (DetectFormat(file).IsAccdb)
            throw new ArgumentException("Legacy Jet encoding applies to .mdb, not .accdb.", nameof(file));
        if (file[0x14] == 0) // version byte: 0 = Jet 3 (2048-byte pages), 1 = Jet 4
            throw new NotSupportedException("Jet 3 (Access 97) page encoding is not supported.");
    }

    private static void Encrypt(byte[] file, string password, AccessEncryption scheme)
    {
        int dbKey = NewDatabaseKey();
        byte[] descriptor;
        IPageCodec codec;
        switch (scheme)
        {
            case AccessEncryption.Agile: (descriptor, codec) = AgileEncryption.Create(password, dbKey); break;
            case AccessEncryption.OfficeStandardAes: (descriptor, codec) = OfficeStandardEncryption.Create(password, aes: true, dbKey); break;
            case AccessEncryption.OfficeStandardRc4: (descriptor, codec) = OfficeStandardEncryption.Create(password, aes: false, dbKey); break;
            default: throw new ArgumentOutOfRangeException(nameof(scheme));
        }
        ApplyEncryption(file, dbKey, descriptor, codec);
    }

    private static int NewDatabaseKey()
    {
        int dbKey = BinaryPrimitives.ReadInt32LittleEndian(RandomBytes(4));
        return dbKey == 0 ? 1 : dbKey; // 0 would read back as "unencrypted"
    }

    // Writes the database key, the 0x299 length signal + descriptor, and encrypts every data page.
    private static void ApplyEncryption(byte[] file, int dbKey, byte[] descriptor, IPageCodec codec)
    {
        WriteDatabaseKey(file, dbKey);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(LengthOffset, 2), (ushort)descriptor.Length);
        descriptor.CopyTo(file, DescriptorOffset);

        int pages = file.Length / PageSize;
        for (int p = 1; p < pages; p++)
            codec.EncryptPage(p, file.AsSpan(p * PageSize, PageSize));
    }

    private static HashAlgorithmName ToHashName(StandardHash hash) => hash switch
    {
        StandardHash.Md5 => HashAlgorithmName.MD5,
        StandardHash.Sha1 => HashAlgorithmName.SHA1,
        StandardHash.Sha256 => HashAlgorithmName.SHA256,
        StandardHash.Sha384 => HashAlgorithmName.SHA384,
        StandardHash.Sha512 => HashAlgorithmName.SHA512,
        _ => throw new ArgumentOutOfRangeException(nameof(hash)),
    };

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

    /// <summary>Detects a file's format without loading it: <see cref="JetFormatBase.Detect"/> reads only the
    /// header, so this is a header read rather than a full copy of a database that may be hundreds of MB.</summary>
    private static JetFormatBase DetectFormatOf(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return JetFormatBase.Detect(stream);
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
