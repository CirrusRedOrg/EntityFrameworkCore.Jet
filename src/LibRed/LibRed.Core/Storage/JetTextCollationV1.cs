using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace LibRed.Storage;

/// <summary>
/// Index-key weights for the Access-2010+ <b>"General"</b> text collation (sort-order version 1).
///
/// <para>Unlike General-Legacy (<see cref="JetTextCollation"/>), whose weights are a Jet-era compaction into
/// one byte per character, v1 uses the Windows NLS weights <b>verbatim</b>: the primary is the two-byte
/// <c>(Script Member, Alphabetic Weight)</c> pair and the secondary is the <c>Diacritic Weight</c>, exactly as
/// published in Microsoft's sorting weight tables. The <c>Case Weight</c> is dropped, which is why case and
/// character width both fold (a full-width <c>Ａ</c> and <c>A</c> differ only in that discarded weight).</para>
///
/// <para>The table is the <b>Windows Server 2008</b> one, frozen: reconstructing measured ACE v1 keys scores
/// 25/25 against it and lower against every other published version. See <c>tools/sortkey-table/generate.ps1</c>
/// for provenance and how the embedded resource is built.</para>
/// </summary>
internal static class JetTextCollationV1
{
    private const byte EndPrimary = 0x01;
    private const byte EndKey = 0x00;
    private const byte InlineStart = 0x80;
    private const byte DefaultSecondary = 0x02;

    // The script members below were derived by measuring ACE, and [MS-UCODEREF] "GetWindowsSortKey
    // Pseudocode" names every one of them. Its constants are UNSORTABLE 0, NONSPACE_MARK 1, EXPANSION 2,
    // EASTASIA_SPECIAL 3, JAMO_SPECIAL 4, EXTENSION_A 5, PUNCTUATION 6, SYMBOL_1..6 7-12, DIGIT 13, LATIN 14.
    // Everything at or below MAX_SPECIAL_CASE (11 or 12, by Windows version) goes to its SpecialCaseHandler
    // rather than being weighed the ordinary way — which is exactly the set of classes needing bespoke
    // handling here, arrived at one measurement at a time.

    /// <summary>[MS-UCODEREF] <c>PUNCTUATION</c>. Characters that carry no primary weight but are recorded
    /// positionally so <c>co-op</c> stays beside <c>coop</c>. The apostrophe and hyphen live here (their
    /// <c>0x80</c>/<c>0x82</c> inline codes are simply their Alphabetic Weights), which is why exactly those
    /// two are special — it is the platform's rule, not an Access one.</summary>
    private const byte WordSortScriptMember = 6;

    /// <summary>[MS-UCODEREF] <c>EXTENSION_A</c>. The CJK ideographs, their extensions, the compatibility
    /// forms and the Kangxi radicals. ACE gives every one of them a four-byte primary <c>FD FF AW DW</c> and
    /// no secondary, rather than the ordinary <c>(SM, AW)</c> primary with <c>DW</c> as a secondary. The
    /// specification's own <c>SCRIPT_MEMBER_EXT_A</c> is 254 and Extension B measures as <c>FE</c>, so the
    /// <c>FD</c> here is a third range and stays as measured.</summary>
    private const byte HanScriptMember = 5;

    /// <summary>[MS-UCODEREF] <c>JAMO_SPECIAL</c>. Like Han they put their weights straight into the
    /// primary — <c>(AW, DW)</c>, no secondary — but with no <c>FD FF</c> marker ahead of them. The composed
    /// Hangul syllables are a different class and were always correct.</summary>
    private const byte HangulJamoScriptMember = 4;

    /// <summary>
    /// [MS-UCODEREF] <c>EASTASIA_SPECIAL</c> — not "kana", although kana is what reaches it here.
    /// </summary>
    /// <remarks>
    /// The class holds the East Asian characters needing special handling, and the specification gives it two
    /// reserved primary weights: <c>PW_REPEAT</c> 0 and <c>PW_CHO_ON</c> 1, up to <c>MAX_SPECIAL_PW</c>. That
    /// names something measured the hard way here — the seven characters ACE gives the unweighted
    /// <c>FF FF</c> primary are exactly those two. The iteration marks (<c>U+3005</c>, <c>U+309D</c>,
    /// <c>U+309E</c>, <c>U+3031</c>, <c>U+3032</c>, <c>U+A015</c>) carry <c>PW_REPEAT</c>, and the lone
    /// prolonged sound mark <c>U+FF70</c> carries <c>PW_CHO_ON</c>. They were treated as an unexplained list
    /// of exceptions before this; they are one rule.
    /// </remarks>
    private const byte EastAsiaSpecialScriptMember = 3;

    private static readonly Lazy<WeightTable> Table = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Appends the collation key body for <paramref name="value"/> — everything after the start flag. Returns
    /// false if any character has no weight in the table (the caller reports it rather than emitting a key
    /// that would sort wrongly).
    /// </summary>
    public static bool TryEncode(string value, List<byte> output) => TryEncode(value, output, out _);

    /// <param name="hasWordSortRecord">
    /// Whether the key carries an inline word-sort section. The caller needs this to decide whether an
    /// over-long entry may be truncated: the checksum that replaces the dropped bytes is unverified when
    /// those bytes hold such a record, because the record is precisely what cannot be observed.
    /// </param>
    public static bool TryEncode(string value, List<byte> output, out bool hasWordSortRecord)
    {
        hasWordSortRecord = false;
        WeightTable table = Table.Value;
        ReadOnlySpan<char> text = value.AsSpan().TrimEnd(' ');

        var primaries = new List<byte>();
        var secondaries = new List<byte>();
        // Position is counted in primary *weights*, not bytes. In v0 the two coincide (one byte per weight);
        // here a weight is two bytes, and ACE still counts weights — verified against ACE (`O'Brien` puts the
        // apostrophe at 0x0B = 0x07 + 4x1 in both orders, though v1 has emitted twice as many bytes by then).
        var inline = new List<(int Position, byte ScriptMember, byte AlphabeticWeight)>();

        // The kana small/normal and prolonged-mark flags, and the running state the prolonged mark needs.
        var kana = new List<bool>();
        var prolonged = new List<bool>();
        int kanaWeight = -1;
        byte kanaVowel = 0;
        bool kanaSmall = false;

        foreach (char character in text)
        {
            // A surrogate the table has no weight for is IGNORABLE, not an error — which is the whole of what
            // astral support needs here, because both halves are otherwise weighed like any other character.
            //
            // ACE weighs an astral character by BOTH halves where it has weights for both: U+10000 is
            // 7F B002 B4F8 01 3F 3F 00, the high surrogate D800 weighing B002 and the low DC00 weighing B4F8.
            // Only the high surrogates up to U+D87F carry weights, so from plane 3 upward the high half drops
            // out and the low one stands alone — U+30000 is 7F B4F8 01 3F 00. Planes 1 and 2 are therefore
            // fully distinguished, while planes 3 to 16 collapse onto 1,024 keys and U+30000, U+31000,
            // U+34000 and U+40000 all share one. That is ACE's behaviour to reproduce, not to improve.
            //
            // Written as "skip every high surrogate" first, generalising from plane-3 samples where the high
            // half is unweighted. That broke all 131,068 characters of planes 1 and 2, where it is not.
            if (char.IsSurrogate(character) && !table.TryGetWeight(character, out _, out _, out _)) continue;

            // Kana are weighed and sectioned exactly as in v0 — same sound weights, same section, verified
            // byte-for-byte against ACE under both sort orders. Handled here rather than in Append because a
            // single kana changes the shape of the WHOLE key.
            //
            // The prolonged sound mark lengthens the preceding kana's VOWEL, so it takes that vowel's primary
            // and inherits its small flag, while marking itself in a second packed section. With no kana ahead
            // of it there is nothing to lengthen, and it falls through to the ordinary table weight.
            if (character is (char)0x30FC or (char)0xFF70 && kanaVowel != 0 &&
                kanaWeight == secondaries.Count - 1)
            {
                primaries.Add(JetKanaSection.KanaPage);
                primaries.Add(kanaVowel);
                secondaries.Add(DefaultSecondary);
                kana.Add(kanaSmall);
                prolonged.Add(true);
                kanaWeight = secondaries.Count - 1;
                continue;
            }

            // The halfwidth voicing marks are COMBINING: ACE folds them into the preceding kana's secondary
            // rather than weighing them. Measured alone they look ignorable, which is what hides this — so a
            // single-character sweep cannot catch it, and it has to be carried over from v0 deliberately.
            // Both halves of the guard matter: for a lone mark kanaWeight and Count-1 are each -1, which
            // would otherwise pass and index the list at -1.
            if (character is (char)0xFF9E or (char)0xFF9F && kanaWeight >= 0 &&
                kanaWeight == secondaries.Count - 1)
            {
                secondaries[kanaWeight] = character == (char)0xFF9E ? (byte)0x03 : (byte)0x04;
                continue;
            }

            if (JetTextCollationTableV0.TryGetKana(
                    character, out byte sound, out byte voicing, out bool small, out byte vowel))
            {
                // Where the two versions part company: v1 weighs a compatibility form by the sound of the
                // kana it decomposes to, while v0 gives it one of its own. The circled katakana are the
                // case — ACE v1 weighs ㋐ as ア (02), where the v0 table holds 03 for it, and 46 and 2A
                // where v1 wants 03 and 04. Everything else about the section is shared, which is why this
                // is a substitution here rather than a second kana path.
                string baseForm = character.ToString().Normalize(NormalizationForm.FormKD);
                if (baseForm.Length == 1 && baseForm[0] != character &&
                    JetTextCollationTableV0.TryGetKana(baseForm[0], out byte baseSound, out _, out _, out _))
                    sound = baseSound;

                primaries.Add(JetKanaSection.KanaPage);
                primaries.Add(sound);
                secondaries.Add(voicing);
                kana.Add(small);
                prolonged.Add(false);
                kanaWeight = secondaries.Count - 1;
                kanaVowel = vowel;
                kanaSmall = small;
                continue;
            }

            // A measured override wins over everything below it, because it came from ACE itself. Its bytes
            // are appended verbatim — it states the finished bytes, not a table entry to interpret through
            // the Han, Hangul and zero-AW rules below.
            //
            // Ahead of the kana fallback deliberately: script member 03 also collects characters that are not
            // kana letters at all — the iteration marks, the lone prolonged mark, the double hyphen — which
            // ACE gives the unweighted FF FF primary and no kana section. Those are measured, so they are
            // simply recorded, and only what no measurement covers reaches the inference below.
            if (JetTextCollationV1Overrides.IsIgnorable(character))
            {
                // ACE contributes nothing for this character — it disappears from the key entirely.
                continue;
            }

            if (JetTextCollationV1Overrides.TryGet(
                    character, out ReadOnlySpan<byte> measuredPrimaries,
                    out ReadOnlySpan<byte> measuredSecondaries))
            {
                foreach (byte b in measuredPrimaries) primaries.Add(b);
                foreach (byte b in measuredSecondaries) secondaries.Add(b);
                continue;
            }

            // The kana the measured v0 table does not carry: the small hiragana ka and ke, the katakana
            // phonetic extensions, and the enclosed (circled) katakana. v1's own table classifies them under
            // script member 03, and its DW is the voicing — the enclosure rides along there, so a circled
            // katakana is simply its kana with secondary EE.
            //
            // Its AW is the sound and its DW the voicing. Only characters absent from the measured v0 kana
            // table reach here, and every one of them is its own base, so there is no decomposition to
            // follow — the branch above owns the compatibility forms.
            //
            // The small flag cannot be assumed from reaching this path — the phonetic extensions are small
            // and the circled forms are not, and marking all of them small put a spurious A0 in 45 keys.
            // ACE's flag agrees exactly with the Unicode names: SMALL KA and SMALL KE, and the SMALL
            // KU..SMALL RO run. The vowel is left at zero, because nothing measured covers a prolonged mark
            // following one of these.
            if (table.TryGetWeight(character, out byte member, out byte tableSound, out byte tableVoicing) &&
                member == EastAsiaSpecialScriptMember)
            {
                bool isSmall = character is (char)0x3095 or (char)0x3096
                               or >= (char)0x31F0 and <= (char)0x31FF;
                primaries.Add(JetKanaSection.KanaPage);
                primaries.Add(tableSound);
                secondaries.Add(tableVoicing);
                kana.Add(isSmall);
                prolonged.Add(false);
                kanaWeight = secondaries.Count - 1;
                kanaVowel = 0;
                kanaSmall = isSmall;
                continue;
            }

            if (table.TryExpand(character, out char[]? sequence))
            {
                foreach (char expanded in sequence)
                    if (!Append(expanded)) return false;
            }
            else if (!Append(character))
            {
                return false;
            }
        }

        bool Append(char character)
        {
            if (!table.TryGetWeight(character, out byte scriptMember, out byte alphabetic, out byte diacritic))
                return false;

            // A wholly zero entry is an ignorable with no record at all (e.g. the soft hyphen, which v0
            // instead records inline as 0x83 — a real difference between the two weight tables).
            if (scriptMember == 0 && alphabetic == 0) return true;

            if (scriptMember == WordSortScriptMember)
            {
                // secondaries.Count is the weight count: every weight contributes exactly one secondary slot,
                // whereas primaries.Count/2 assumed each weight is two bytes — which the four-byte Han
                // primary above breaks.
                inline.Add((secondaries.Count, scriptMember, alphabetic));
                return true;
            }

            // A Han character takes a FOUR-byte primary and no secondary at all: the fixed marker FD FF,
            // then its own alphabetic and diacritic weights. Splitting it the ordinary way — (SM, AW) as the
            // primary and DW as a secondary — is what made every CJK key wrong, some 28,200 of them.
            //   U+4E00  ACE 7F FD FF 3C 6A 01 00, where the NLS entry is SM 05, AW 3C, DW 6A.
            if (scriptMember == HanScriptMember)
            {
                primaries.Add(0xFD);
                primaries.Add(0xFF);
                primaries.Add(alphabetic);
                primaries.Add(diacritic);
                secondaries.Add(DefaultSecondary);
                return true;
            }

            // Hangul jamo take a two-byte primary of (AW, DW) with no secondary — the same "weights straight
            // into the primary" shape as Han above, but without the FD FF marker. U+1100 is C0 02, where the
            // NLS entry is SM 04, AW C0, DW 02.
            if (scriptMember == HangulJamoScriptMember)
            {
                primaries.Add(alphabetic);
                primaries.Add(diacritic);
                secondaries.Add(DefaultSecondary);
                return true;
            }

            // A zero alphabetic weight means the character carries NO primary — only its secondary. Emitting
            // (SM, 0) as a primary put an extra weight into every such key, which is where the last of the
            // Greek, Cyrillic, Hebrew and Indic differences came from.
            //   U+0483  ACE 7F 01 94 00, not 7F 01 00 01 94 00.
            //
            // And where something precedes it, it does not take a slot of its own either: it FOLDS into the
            // preceding weight, adding its diacritic. That is the Hebrew and Arabic presentation forms, whose
            // expansions are a letter followed by a point —
            //   U+FB30  ACE 7F 28 02 01 30 00, where the parts weigh 0x02 and 0x2E and 0x02 + 0x2E = 0x30.
            if (alphabetic == 0)
            {
                if (secondaries.Count == 0) secondaries.Add(diacritic);
                else secondaries[^1] = (byte)(secondaries[^1] + diacritic);
                return true;
            }

            primaries.Add(scriptMember);
            primaries.Add(alphabetic);
            secondaries.Add(diacritic);
            return true;
        }

        output.AddRange(primaries);
        output.Add(EndPrimary);

        // Secondary section: emitted up to and including the last character carrying a non-default accent.
        int lastAccent = secondaries.FindLastIndex(weight => weight != DefaultSecondary);
        for (int i = 0; i <= lastAccent; i++) output.Add(secondaries[i]);

        hasWordSortRecord = inline.Count > 0;

        if (kana.Count > 0) JetKanaSection.Append(output, kana, prolonged);

        // Those three 0x01s are not an "introducer" but three SECTION SEPARATORS. [MS-UCODEREF] gives the key
        // as primaries SEP diacritics SEP case SEP extra SEP specials TERM, and Access emits the same frame
        // while leaving the case section EMPTY — which is why case and width fold, since the Case Weight is
        // where width lives. So the run is: end of diacritics, an empty case section, an empty extra section.
        // A kana section fills that extra section, and the run shortens accordingly.
        if (inline.Count > 0)
        {
            if (kana.Count > 0)
            {
                output.Add(0xFF);
                output.Add(EndPrimary);
            }
            else
            {
                output.Add(EndPrimary);
                output.Add(EndPrimary);
                output.Add(EndPrimary);
            }
            foreach ((int position, byte scriptMember, byte alphabetic) in inline)
            {
                // [MS-UCODEREF] SpecialWeightType is (Position: 16-bit, ScriptMember, PrimaryWeight), and its
                // Position is emitted big-endian — "Byte1 = Position >> 8, Byte2 = Position & 0xff" — so this
                // is ONE sixteen-bit field with bit 15 set, not a 0x80 marker followed by a byte. Both
                // readings give the same bytes below 0x100 and only the field reading survives past it, which
                // is why treating 0x80 as a marker looked right for every short value and silently produced a
                // wrong key for anything longer. Measured against ACE: a hyphen at character 250 is 83 EF.
                int position16 = InlineStart << 8 | (0x07 + 4 * position);
                output.Add((byte)(position16 >> 8));
                output.Add((byte)position16);
                output.Add(scriptMember);
                output.Add(alphabetic);
            }
        }

        output.Add(EndKey);
        return true;
    }

    private static WeightTable Load()
    {
        using Stream stream = typeof(JetTextCollationV1).Assembly
            .GetManifestResourceStream("LibRed.Resources.SortKeyTableV1.bin")
            ?? throw new InvalidOperationException("The v1 sorting weight table resource is missing from the assembly.");

        Span<byte> header = stackalloc byte[8];
        stream.ReadExactly(header);
        int weightCount = BinaryPrimitives.ReadInt32LittleEndian(header);
        int expansionCount = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);

        byte[] deltas = ReadSection(stream);
        byte[] scriptMembers = ReadSection(stream);
        byte[] alphabetics = ReadSection(stream);
        byte[] diacritics = ReadSection(stream);
        byte[] expansionBytes = ReadSection(stream);

        if (scriptMembers.Length != weightCount || alphabetics.Length != weightCount || diacritics.Length != weightCount)
            throw new InvalidDataException("The v1 sorting weight table resource is inconsistent with its header.");

        var codePoints = new ushort[weightCount];
        int offset = 0, current = 0;
        for (int i = 0; i < weightCount; i++)
        {
            current += ReadVarInt(deltas, ref offset);
            codePoints[i] = (ushort)current;
        }

        var expansions = new Dictionary<char, char[]>(expansionCount);
        offset = 0; current = 0;
        for (int i = 0; i < expansionCount; i++)
        {
            current += ReadVarInt(expansionBytes, ref offset);
            int length = expansionBytes[offset++];
            var sequence = new char[length];
            for (int j = 0; j < length; j++)
            {
                sequence[j] = (char)BinaryPrimitives.ReadUInt16LittleEndian(expansionBytes.AsSpan(offset));
                offset += 2;
            }
            expansions[(char)current] = sequence;
        }

        return new WeightTable(codePoints, scriptMembers, alphabetics, diacritics, expansions);
    }

    private static byte[] ReadSection(Stream stream)
    {
        Span<byte> length = stackalloc byte[4];
        stream.ReadExactly(length);
        var compressed = new byte[BinaryPrimitives.ReadInt32LittleEndian(length)];
        stream.ReadExactly(compressed);

        using var source = new MemoryStream(compressed);
        using var inflate = new ZLibStream(source, CompressionMode.Decompress);
        using var target = new MemoryStream();
        inflate.CopyTo(target);
        return target.ToArray();
    }

    private static int ReadVarInt(byte[] data, ref int offset)
    {
        int value = 0, shift = 0;
        while (true)
        {
            byte b = data[offset++];
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
        }
    }

    /// <summary>Code points sorted ascending with their weights in parallel arrays — a binary search over
    /// ~58k entries, rather than a dictionary, to keep the table near 300 KB resident instead of several MB.</summary>
    private sealed class WeightTable(
        ushort[] codePoints, byte[] scriptMembers, byte[] alphabetics, byte[] diacritics,
        Dictionary<char, char[]> expansions)
    {
        public bool TryGetWeight(char character, out byte scriptMember, out byte alphabetic, out byte diacritic)
        {
            int index = Array.BinarySearch(codePoints, (ushort)character);
            if (index < 0)
            {
                scriptMember = alphabetic = diacritic = 0;
                return false;
            }
            scriptMember = scriptMembers[index];
            alphabetic = alphabetics[index];
            diacritic = diacritics[index];
            return true;
        }

        /// <summary>The character's expansion (e.g. <c>ß</c> → <c>s</c>,<c>s</c>) when it has one. Deliberately
        /// not "return the character in a one-element buffer": a shared buffer would race between the
        /// concurrent readers the engine now allows, and allocating one per character would be worse.</summary>
        public bool TryExpand(char character, [NotNullWhen(true)] out char[]? sequence) =>
            expansions.TryGetValue(character, out sequence);
    }
}
