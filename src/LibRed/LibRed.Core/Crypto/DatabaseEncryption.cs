using LibRed.Formats;
using LibRed.IO;
using LibRed.Storage;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace LibRed.Crypto;

/// <summary>
/// Sets, removes, and changes the password/encryption of an Access database. Implements every <c>.accdb</c>
/// scheme — Office "Standard"/CryptoAPI (RC4-40, AES-256) and Agile (AES-256-CBC / SHA-512) — plus the legacy
/// Jet 4 (<c>.mdb</c>) database password (password-only obfuscation) and RC4 page encoding.
/// </summary>
/// <remarks>
/// <para><b>Every operation works on a database the caller has already opened exclusively</b>, and refuses a
/// shared one — the rule ACE applies to itself, rejecting <c>ALTER DATABASE PASSWORD</c> on a shared connection
/// even when nobody else is attached. Nothing here opens a file: the exclusive handle is the caller's, and the
/// old password is the one the database was opened under, so neither is passed again.</para>
/// <code>
/// using var db = JetDatabase.Open(path, readOnly: false, password: "old", exclusive: true);
/// DatabaseEncryption.ChangePassword(db, "new", AccessEncryption.Agile);
/// // db is closed; reopen under the new password
/// </code>
/// <para><b>Re-encoding the pages writes a sibling copy and replaces the database with it</b>, rather than
/// transforming the original. A kill at any moment therefore leaves either the untouched original or the
/// finished replacement, where writing in place left a truncated file. The pages stream through one page-sized
/// buffer, so the memory cost is a page rather than the database; and because the channel hands them over
/// decrypted, a password change re-encrypts in that same pass and the plaintext database never reaches disk.
/// The replacement cannot be moved into place under an open handle, so these operations <b>close the
/// database</b> — page for page it is a different file, and the caller reopens it under the new password.
/// On an <c>.accdb</c> page 0's 40-byte <c>0x42</c> field carries the low byte of the database key, as Access
/// writes it, and the stored SIDs are re-masked to the keystream that field now gives (page-00 §2.3).</para>
/// <para>The legacy Jet password instead changes page 0's 40-byte field and re-masks the stored SIDs, which the
/// field's keystream covers, through the open channel in one transaction, and leaves the database open: copying
/// a whole database to change those is the greater risk.</para>
/// </remarks>
public static class DatabaseEncryption
{
    private const int KeyOffset = 0x3E;          // 4-byte database (encoding) key, XOR-masked by the page-0 header mask
    private const int LengthOffset = 0x299;      // 2-byte EncryptionInfo blob length (Access's "is encrypted" signal)
    private const int DescriptorOffset = 0x29B;  // the EncryptionInfo blob itself

    // The descriptor shares page 0 with the user commit-byte table at 0xE00, which a fresh file seeds with the
    // neutral 00 01 pairs — an all-zero table reads to Access as "every user is mid-write", i.e. corrupt. So a
    // descriptor may only occupy the zero padding that ends at 0xDFF, not merely fit within the page.
    private const int DescriptorPaddingEnd = 0xE00;

    private const int JetPasswordOffset = 0x42;  // 40-byte legacy Jet database-password field (header-masked)
    private const int JetPasswordSize = 40;      // 20 UTF-16LE chars
    private const int HeaderDateOffset = 0x72;   // 8-byte creation-date OLE double (header-masked)

    /// <summary>How a page is re-encoded on its way into the replacement file. The page arrives decrypted,
    /// whatever the database was stored under, so this is the new encoding alone.</summary>
    private delegate void PageTransform(int page, Span<byte> bytes);

    /// <summary>Encrypts a currently-unencrypted database with a new <paramref name="password"/> using
    /// <paramref name="scheme"/>. Throws if the database is already encrypted (use <see cref="ChangePassword"/>)
    /// or the scheme is invalid for the file format.</summary>
    public static void SetPassword(JetDatabase database, string password, AccessEncryption scheme)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        Rewrite(database, (page0, format) =>
        {
            ValidateScheme(scheme, format);
            if (DecodeDatabaseKey(page0) != 0)
                throw new InvalidOperationException("Database is already encrypted; use ChangePassword.");
            return Encrypt(page0, password, scheme);
        });
    }

    /// <summary>Decrypts an encrypted database, removing its password. The password itself is the one
    /// <paramref name="database"/> was opened under, so it is not passed again. Throws if the database is not
    /// encrypted.</summary>
    public static void RemovePassword(JetDatabase database)
    {
        Rewrite(database, (page0, _) =>
        {
            if (DecodeDatabaseKey(page0) == 0)
                throw new InvalidOperationException("Database is not encrypted.");
            ClearEncryption(page0);
            return null; // the pages arrive decrypted, so writing them through unchanged is the removal
        });
    }

    /// <summary>Encrypts a currently-unencrypted <c>.accdb</c> with Office "Standard" RC4, letting the caller pick
    /// the <paramref name="keyBits"/> (40–128, multiple of 8) and <paramref name="hash"/>. RC4 is the only Standard
    /// cipher a stock/add-in Access reads back; key lengths above 56 bits or SHA-2 hashes require the "Enhanced"
    /// provider (the EncryptionEnhancer add-in) to open in Access, though LibRed reads them regardless.</summary>
    public static void SetPasswordRc4(JetDatabase database, string password, int keyBits = 40, StandardHash hash = StandardHash.Sha1)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ValidateRc4Options(keyBits, hash);
        Rewrite(database, (page0, format) =>
        {
            if (!format.IsAccdb)
                throw new ArgumentException("Office-Standard encryption requires an .accdb (ACE) database.", nameof(database));
            if (DecodeDatabaseKey(page0) != 0)
                throw new InvalidOperationException("Database is already encrypted; use ChangePassword.");
            return EncryptRc4(page0, password, keyBits, hash);
        });
    }

    /// <summary>Changes the password (and optionally the scheme) in a single pass: the pages arrive decrypted
    /// under the password <paramref name="database"/> was opened with and are written back under
    /// <paramref name="newPassword"/>.</summary>
    public static void ChangePassword(JetDatabase database, string newPassword, AccessEncryption scheme)
    {
        ArgumentException.ThrowIfNullOrEmpty(newPassword);
        Rewrite(database, (page0, format) =>
        {
            ValidateScheme(scheme, format);
            if (DecodeDatabaseKey(page0) == 0)
                throw new InvalidOperationException("Database is not encrypted; use SetPassword.");
            ClearEncryption(page0); // the old descriptor may be longer than the new one
            return Encrypt(page0, newPassword, scheme);
        });
    }

    /// <summary>Changes the password to a fresh Office-Standard RC4 encryption with the given key length and
    /// hash — <see cref="ChangePassword"/> with the options <see cref="SetPasswordRc4"/> takes.</summary>
    public static void ChangePasswordRc4(JetDatabase database, string newPassword, int keyBits = 40, StandardHash hash = StandardHash.Sha1)
    {
        ArgumentException.ThrowIfNullOrEmpty(newPassword);
        ValidateRc4Options(keyBits, hash);
        Rewrite(database, (page0, format) =>
        {
            if (!format.IsAccdb)
                throw new ArgumentException("Office-Standard encryption requires an .accdb (ACE) database.", nameof(database));
            if (DecodeDatabaseKey(page0) == 0)
                throw new InvalidOperationException("Database is not encrypted; use SetPasswordRc4.");
            ClearEncryption(page0);
            return EncryptRc4(page0, newPassword, keyBits, hash);
        });
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
    /// double at <c>0x72</c>, all within the header-masked region; the field's bytes are verified byte-identical
    /// to Access's own output. The field also feeds the keystream every stored SID is masked with, so the SIDs are
    /// re-masked with it, as Access's own password change does. The database stays open.</summary>
    public static void SetJetPassword(JetDatabase database, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (password.Length > JetPasswordSize / 2)
            throw new ArgumentException($"A Jet database password is at most {JetPasswordSize / 2} characters.", nameof(password));

        RewriteJetPassword(database, password,
            "The legacy Jet password applies to .mdb, not .accdb — use SetPassword.");
    }

    /// <summary>Removes a legacy Jet 4 (<c>.mdb</c>) database password by clearing the password field (equivalent to
    /// setting an empty password), re-masking the stored SIDs as <see cref="SetJetPassword"/> does. The database
    /// stays open.</summary>
    public static void RemoveJetPassword(JetDatabase database) =>
        // An empty password: the field encodes the mask alone, decoding back to "".
        RewriteJetPassword(database, "",
            "The legacy Jet password applies to .mdb, not .accdb — use RemovePassword.");

    /// <summary>
    /// Writes <paramref name="password"/> into the page-0 field and re-masks every stored SID to match, in one
    /// transaction. The field is half of the header region the SID keystream is folded from (page-00 §2.3), so a
    /// new password is a new keystream; each SID in <c>MSysObjects.Owner</c> and <c>MSysACEs.SID</c> becomes
    /// <c>stored XOR oldStream XOR newStream</c> over its whole length — exactly what Access's own password change
    /// writes, the 102-byte SIDs included (verified against DAO's <c>NewPassword</c>). Left as they were, they
    /// would belong to no account under the new key.
    /// </summary>
    private static void RewriteJetPassword(JetDatabase database, string password, string accdbMessage)
    {
        PageChannel channel = RequireExclusive(database);
        if (channel.Format.IsAccdb) throw new ArgumentException(accdbMessage, nameof(database));

        var page0 = new byte[channel.PageSize];
        channel.ReadPage(0, page0);
        byte[] before = (byte[])page0.Clone();
        WritePasswordField(page0, Encoding.Unicode.GetBytes(password));

        database.BeginTransaction();
        try
        {
            channel.WritePage(0, page0);
            RemaskSids(database, before, page0);
            database.Commit();
        }
        catch
        {
            database.Rollback();
            throw;
        }
        database.Catalog.Invalidate(); // it holds the SIDs it read from MSysObjects' own row
    }

    /// <summary>
    /// Re-masks every SID in <c>MSysObjects.Owner</c> and <c>MSysACEs.SID</c> from the keystream page 0
    /// <paramref name="before"/> folds to into the one <paramref name="after"/> does (page-00 §2.3), inside the
    /// caller's transaction: each becomes <c>stored XOR oldStream XOR newStream</c> over its whole length.
    /// </summary>
    private static void RemaskSids(JetDatabase database, byte[] before, byte[] after)
    {
        foreach ((string tableName, string columnName) in new[] { ("MSysObjects", "Owner"), ("MSysACEs", "SID") })
        {
            Table table = database.OpenTable(tableName);
            int column = table.Definition.RequireColumn(columnName).Index;
            var rows = table.RowsWhere([column], values => values[column] is byte[] { Length: > 0 }).ToList();
            if (rows.Count == 0) continue;

            int longest = rows.Max(r => ((byte[])r.Values[column]!).Length);
            byte[] oldStream = SidKeystream.For(before, longest), newStream = SidKeystream.For(after, longest);
            var changed = new HashSet<int> { column };
            foreach ((RowId id, object?[] values) in rows)
            {
                byte[] sid = (byte[])values[column]!;
                for (int i = 0; i < sid.Length; i++) sid[i] ^= (byte)(oldStream[i] ^ newStream[i]);
                table.Update(id, values, changed);
            }
        }
    }

    // Encodes the header-masked 0x42 field: plaintext = value zero-padded to 40 bytes, XORed with the 4-byte
    // little-endian (int)creationDateDouble mask (cycled); the on-disk bytes are that plaintext XORed with the page-0
    // header mask. Reading (jackcess/LibRed) is the exact inverse. An .mdb's value is its Jet password, UTF-16LE;
    // an .accdb's is the low byte of its database key, 40 times over (zero when unencrypted).
    private static void WritePasswordField(byte[] page0, ReadOnlySpan<byte> value)
    {
        ReadOnlySpan<byte> hmask = JetFormatBase.PageZeroHeaderMask;
        int start = JetFormatBase.PageZeroHeaderMaskStart;

        Span<byte> date = stackalloc byte[8];
        for (int i = 0; i < 8; i++) date[i] = (byte)(page0[HeaderDateOffset + i] ^ hmask[HeaderDateOffset - start + i]);
        Span<byte> dateMask = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(dateMask, (int)BitConverter.ToDouble(date));

        Span<byte> field = stackalloc byte[JetPasswordSize];
        field.Clear();
        value.CopyTo(field);
        for (int i = 0; i < JetPasswordSize; i++)
            page0[JetPasswordOffset + i] = (byte)(field[i] ^ dateMask[i % 4] ^ hmask[JetPasswordOffset - start + i]);
    }

    /// <summary>Applies legacy Jet 4 (<c>.mdb</c>) page encoding — the "Encode Database" feature — RC4-encrypting
    /// every data page with a fresh random database key stored at <c>0x3E</c>. This is <b>independent</b> of the
    /// database password (<see cref="SetJetPassword"/>): a file may carry both (encoding scrambles the pages, the
    /// password gates opening). Throws if the database is already encoded, is an <c>.accdb</c>, or is Jet 3.</summary>
    public static void SetJetEncoding(JetDatabase database) => SetJetEncoding(database, NewDatabaseKey());

    // Explicit-key overload (internal): the public API picks a fresh random key; tests use a specific key to
    // reproduce a real Access-encoded file byte-for-byte.
    internal static void SetJetEncoding(JetDatabase database, int dbKey)
    {
        Rewrite(database, (page0, format) =>
        {
            RequireJet4Mdb(page0, format);
            if (DecodeDatabaseKey(page0) != 0)
                throw new InvalidOperationException("Database is already encoded.");

            WriteDatabaseKey(page0, dbKey); // page 0 (the header) is never encoded
            return new JetLegacyEncryption(dbKey).EncryptPage;
        });
    }

    /// <summary>Removes legacy Jet 4 (<c>.mdb</c>) page encoding — the pages are written back plaintext and the
    /// <c>0x3E</c> key cleared. Leaves any database password (the <c>0x42</c> field) untouched.</summary>
    public static void RemoveJetEncoding(JetDatabase database)
    {
        Rewrite(database, (page0, format) =>
        {
            RequireJet4Mdb(page0, format);
            if (DecodeDatabaseKey(page0) == 0)
                throw new InvalidOperationException("Database is not encoded.");

            WriteDatabaseKey(page0, 0);
            return null; // the channel decoded them on the way in
        });
    }

    private static void RequireJet4Mdb(byte[] page0, JetFormatBase format)
    {
        if (format.IsAccdb)
            throw new ArgumentException("Legacy Jet encoding applies to .mdb, not .accdb.", nameof(format));
        if (page0[0x14] == 0) // version byte: 0 = Jet 3 (2048-byte pages), 1 = Jet 4
            throw new NotSupportedException("Jet 3 (Access 97) page encoding is not supported.");
    }

    /// <summary>
    /// Re-encodes the whole database: page 0 is handed to <paramref name="change"/> to check and edit, every
    /// later page is read (decrypted by the open channel), passed through the transform it returns and written
    /// to a sibling copy, which then replaces the database. The database closes with it — see the class remarks.
    /// </summary>
    private static void Rewrite(JetDatabase database, Func<byte[], JetFormatBase, PageTransform?> change)
    {
        PageChannel channel = RequireExclusive(database);

        // Settle what this session would otherwise write only as it closes — the pages it freed go back to the
        // global map then (page-05 §9.1). Doing it now puts them in the copy; left until the Dispose below, they
        // would land in the file the replacement is about to overwrite.
        new PageAllocator(channel).ReturnReleasedPages();

        var page0 = new byte[channel.PageSize];
        channel.ReadPage(0, page0);
        byte[] before = (byte[])page0.Clone();
        PageTransform? transform = change(page0, channel.Format);

        // An .accdb's 0x42 field carries the low byte of the database key, so a new key is a new SID keystream.
        // The SIDs are re-masked in a transaction the copy below reads through and that is then rolled back: they
        // reach only the replacement, and the original is left exactly as it was unless the replace happens.
        bool remask = channel.Format.IsAccdb;
        if (remask)
        {
            WritePasswordField(page0, Enumerable.Repeat((byte)DecodeDatabaseKey(page0), JetPasswordSize).ToArray());
            database.BeginTransaction();
        }

        string path = channel.Path;
        string temporary = path + $".libred-encoding-{Guid.NewGuid():N}";
        bool replaced = false;
        try
        {
            if (remask) RemaskSids(database, before, page0);
            using (var copy = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                copy.Write(page0);
                var buffer = new byte[channel.PageSize];
                for (int page = 1, pages = channel.PageCount; page < pages; page++)
                {
                    channel.ReadPage(page, buffer);
                    transform?.Invoke(page, buffer);
                    copy.Write(buffer);
                }
                copy.Flush(flushToDisk: true);
            }

            // The handle has to go before the swap — a file this process holds open cannot be replaced — and
            // the database is a different file afterwards anyway, stored under a key this channel has not got.
            if (remask) database.Rollback();
            database.Dispose();
            if (OperatingSystem.IsWindows())
                File.Replace(temporary, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(temporary, path, overwrite: true);
            replaced = true;
        }
        finally
        {
            // Anything short of the replace leaves the database as it was, so the half-built copy is just litter.
            if (!replaced && channel.InTransaction) database.Rollback();
            if (!replaced && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>The state every operation here demands of the database it is given: opened for this caller
    /// alone, writable, and not mid-transaction.</summary>
    private static PageChannel RequireExclusive(JetDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        PageChannel channel = database.Channel;
        if (!channel.IsExclusive)
            throw new InvalidOperationException(
                "Changing a database's password or encoding needs it open exclusively: reopen it with "
                + "JetDatabase.Open(path, readOnly: false, exclusive: true). ACE refuses the same statements on "
                + "a shared connection.");
        if (channel.IsReadOnly)
            throw new InvalidOperationException("The database is open read-only.");
        if (channel.InTransaction)
            throw new InvalidOperationException("A database's password or encoding cannot be changed inside a transaction.");
        return channel;
    }

    private static PageTransform Encrypt(byte[] page0, string password, AccessEncryption scheme)
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
        return ApplyEncryption(page0, dbKey, descriptor, codec);
    }

    private static PageTransform EncryptRc4(byte[] page0, string password, int keyBits, StandardHash hash)
    {
        int dbKey = NewDatabaseKey();
        var (descriptor, codec) = OfficeStandardEncryption.CreateRc4(password, keyBits, ToHashName(hash), dbKey);
        return ApplyEncryption(page0, dbKey, descriptor, codec);
    }

    private static int NewDatabaseKey()
    {
        int dbKey = BinaryPrimitives.ReadInt32LittleEndian(RandomBytes(4));
        return dbKey == 0 ? 1 : dbKey; // 0 would read back as "unencrypted"
    }

    // Writes the database key and the 0x299 length signal + descriptor onto page 0, and returns the transform
    // that encrypts every later page.
    private static PageTransform ApplyEncryption(byte[] page0, int dbKey, byte[] descriptor, IPageCodec codec)
    {
        WriteDatabaseKey(page0, dbKey);
        if (DescriptorOffset + descriptor.Length > DescriptorPaddingEnd)
            throw new NotSupportedException(
                $"The {descriptor.Length}-byte EncryptionInfo descriptor does not fit page 0's padding "
                + $"({DescriptorPaddingEnd - DescriptorOffset} bytes); writing it would overrun the user "
                + "commit-byte table at 0xE00, which Access reads as corruption.");
        BinaryPrimitives.WriteUInt16LittleEndian(page0.AsSpan(LengthOffset, 2), (ushort)descriptor.Length);
        descriptor.CopyTo(page0, DescriptorOffset);
        return codec.EncryptPage;
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

    private static void ClearEncryption(byte[] page0)
    {
        // Clamp to the padding window rather than trusting the stored length. On an .accdb the frame has been
        // through Agile's or Standard's bound check by now, but a legacy .mdb never uses this frame at all, so
        // those two bytes hold whatever they hold, and 2 + blobLen could reach 65537, wiping page-0 structures
        // past the padding (the commit-byte table at 0xE00 among them) or throwing outright.
        int blobLen = BinaryPrimitives.ReadUInt16LittleEndian(page0.AsSpan(LengthOffset, 2));
        int clearLength = Math.Min(2 + blobLen, DescriptorPaddingEnd - LengthOffset);
        Array.Clear(page0, LengthOffset, clearLength);  // the length signal + the descriptor
        WriteDatabaseKey(page0, 0);                     // decodes back to 0 = unencrypted
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
                // Not unimplemented — differently named. The legacy .mdb has two independent mechanisms and
                // this generic entry point cannot tell which one is meant, so it names both rather than
                // sending the caller off after a feature that already ships.
                throw new NotSupportedException(
                    "Legacy Jet has two separate mechanisms, so SetPassword cannot pick one: use "
                    + $"{nameof(SetJetPassword)} for the database password (obfuscation only) or "
                    + $"{nameof(SetJetEncoding)} for RC4 page encoding.");
            case AccessEncryption.None:
                throw new ArgumentException("Use RemovePassword to remove encryption.", nameof(scheme));
            default:
                throw new ArgumentOutOfRangeException(nameof(scheme));
        }
    }

    private static int DecodeDatabaseKey(byte[] page0)
    {
        ReadOnlySpan<byte> mask = JetFormatBase.PageZeroHeaderMask;
        int start = JetFormatBase.PageZeroHeaderMaskStart;
        Span<byte> key = stackalloc byte[4];
        for (int i = 0; i < 4; i++) key[i] = (byte)(page0[KeyOffset + i] ^ mask[KeyOffset - start + i]);
        return BinaryPrimitives.ReadInt32LittleEndian(key);
    }

    private static void WriteDatabaseKey(byte[] page0, int dbKey)
    {
        ReadOnlySpan<byte> mask = JetFormatBase.PageZeroHeaderMask;
        int start = JetFormatBase.PageZeroHeaderMaskStart;
        Span<byte> k = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(k, dbKey);
        for (int i = 0; i < 4; i++) page0[KeyOffset + i] = (byte)(k[i] ^ mask[KeyOffset - start + i]);
    }

    private static byte[] RandomBytes(int n) { byte[] b = new byte[n]; RandomNumberGenerator.Fill(b); return b; }
}
