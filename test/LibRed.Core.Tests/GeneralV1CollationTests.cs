using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// The Access-2010+ "General" (v1) text collation. Its weights are the Windows NLS weights verbatim — the
// two-byte (Script Member, Alphabetic Weight) primary and the Diacritic Weight secondary — taken from the
// Windows Server 2008 sorting weight table, which is the one ACE froze.
//
// The expected keys below were measured from a database Access itself created with the General sort order,
// by reading the bytes ACE stored in its own index. They are ground truth, not this encoder's output.
public class GeneralV1CollationTests
{
    private static byte[] Encode(string value, Collation collation, bool ascending = true)
    {
        var column = new ColumnDef { Name = "t", Type = JetDataType.Text, Index = 0, Collation = collation };
        return IndexKeyEncoder.Encode([(column, ascending)], [value]);
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

    [Theory]
    // Plain letters: two bytes per character, and case folds (the Case Weight is the section ACE truncates).
    [InlineData("apple", "7F0E020E7E0E7E0E480E210100")]
    [InlineData("Apple", "7F0E020E7E0E7E0E480E210100")]
    [InlineData("cafe", "7F0E0A0E020E230E210100")]
    // Accent: the secondary section carries the Diacritic Weight (acute = 0x0E).
    [InlineData("café", "7F0E0A0E020E230E21010202020E00")]
    // Space has a real primary weight (07 02) inside a string; trailing spaces are trimmed.
    [InlineData("a b", "7F0E0207020E090100")]
    [InlineData("a1", "7F0E020D190100")]
    [InlineData("Ä", "7F0E02011300")]
    // Expansions come from the table's EXPANSION section: sharp s -> s,s and AE -> A,E.
    [InlineData("ß", "7F0E910E910100")]
    [InlineData("Æ", "7F0E020E210100")]
    [InlineData("Ø", "7F0E7C012100")]
    // Ordinal indicator: base letter primary with a distinguishing secondary.
    [InlineData("ª", "7F0E02010300")]
    // Superscript one shares Digit One's primary — 0D19, the weight that identifies the Server 2008 table.
    [InlineData("¹", "7F0D190100")]
    [InlineData("£", "7F07980100")]
    [InlineData("©", "7F0A070100")]
    [InlineData("«", "7F08180100")]
    [InlineData("½", "7F0D1801D600")]
    // Scripts beyond Latin, which the v0 table cannot encode at all.
    [InlineData("Α", "7F0F020100")]
    [InlineData("α", "7F0F020100")]
    [InlineData("А", "7F10020100")]
    // Width folds, because width lives in the discarded Case Weight.
    [InlineData("Ａ", "7F0E020100")]
    [InlineData("　", "7F07020100")]
    // Ligature expands to f + i.
    [InlineData("ﬁ", "7F0E230E320100")]
    // Soft hyphen is wholly ignorable in this table (v0 records it inline as 0x83 instead).
    [InlineData("­", "7F0100")]
    [InlineData("coop", "7F0E0A0E7C0E7C0E7E0100")]
    // Word-sort ignorables: no primary weight, an inline record instead. The position counts primary
    // *weights*, not bytes — 0x0B = 0x07 + 4x1 after one character, even though two bytes were emitted.
    [InlineData("co-op", "7F0E0A0E7C0E7C0E7E01010101800F068200")]
    [InlineData("O'Brien", "7F0E7C0E090E8A0E320E210E7001010101800B068000")]
    [InlineData("Anne-Marie", "7F0E020E700E700E210E510E020E8A0E320E21010101018017068200")]
    public void Encodes_the_bytes_ace_stores(string value, string expected) =>
        Assert.Equal(expected, Hex(Encode(value, Collation.General)));

    [Fact]
    public void The_two_general_orders_encode_differently()
    {
        Assert.NotEqual(
            Hex(Encode("apple", Collation.GeneralLegacy)),
            Hex(Encode("apple", Collation.General)));
    }

    // The whole point of a key: memcmp order must be value order.
    [Fact]
    public void Keys_sort_in_value_order()
    {
        string[] values = ["", "a", "ab", "apple", "b", "cafe", "café", "z", "Α", "А"];
        var keys = values.Select(v => Encode(v, Collation.General)).ToList();

        for (int i = 0; i + 1 < values.Length; i++)
            Assert.True(Compare(keys[i], keys[i + 1]) < 0,
                $"'{values[i]}' should sort before '{values[i + 1]}'");

        static int Compare(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return a.Length.CompareTo(b.Length);
        }
    }

    [Fact]
    public void Descending_inverts_every_byte_and_appends_a_terminator()
    {
        byte[] ascending = Encode("apple", Collation.General);
        byte[] descending = Encode("apple", Collation.General, ascending: false);

        Assert.Equal(ascending.Length + 1, descending.Length);
        for (int i = 0; i < ascending.Length; i++) Assert.Equal((byte)~ascending[i], descending[i]);
        Assert.Equal(0x00, descending[^1]);
    }

    // There is no longer a BMP character to refuse: the measured sweep covers all 63,422 ACE stores, so the
    // test that used to assert refusal (on U+0378) now asserts what ACE actually does with it, below.
    // Refusal is still tested for the case that keeps it — a non-English locale, further down.
    //
    // Astral characters are NOT covered by that claim: the sweep measured the BMP only, and a surrogate pair
    // arrives as two chars that the table happens to weigh individually, so it encodes rather than refusing.
    // Whether ACE agrees is unmeasured.

    // U+0378 is unassigned in Unicode, and refusing it looks like the safe answer — but ACE stores an empty
    // key for it, so refusing would reject a value ACE accepts, and weighing it would sort it wrongly.
    // Matching the engine beats both. Measured, not assumed: it is one of the 5,082 characters the override
    // resource records as ignorable. Private-use characters (U+E000) do have weights and are not affected.
    [Fact]
    public void An_unassigned_character_is_ignorable_as_ACE_stores_it()
    {
        Assert.Equal(Hex(Encode("ab", Collation.General)), Hex(Encode("a͸b", Collation.General)));
    }

    [Fact]
    public void An_empty_string_encodes_to_an_empty_key()
    {
        Assert.Equal("7F0100", Hex(Encode("", Collation.General)));
        Assert.Equal("7F0100", Hex(Encode("   ", Collation.General)));   // trailing spaces are trimmed
    }

    // Non-English locales are still refused: the embedded table is the English (1033) one.
    [Fact]
    public void A_non_english_locale_is_still_refused()
    {
        var error = Assert.Throws<NotSupportedException>(
            () => Encode("a", new Collation(CollatingOrder.Cyrillic, 1)));
        Assert.Contains("not implemented", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
