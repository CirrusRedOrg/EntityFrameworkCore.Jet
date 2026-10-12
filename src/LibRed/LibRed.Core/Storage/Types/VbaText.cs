using System.Globalization;
using System.Text;

namespace LibRed.Storage.Types;

/// <summary>A value as VBA's <c>CStr</c> writes it, in the current culture — as ACE writes a non-text value into
/// text, and as the engine's evaluator writes its values — and text as VBA reads it as a number.</summary>
public static class VbaText
{
    /// <summary>
    /// Text as VBA reads it as a number, in the regional separators and currency symbol (verified vs ACE: by
    /// <c>+</c>, and by INSERT and ALTER COLUMN into a number column). Surrounding whitespace is skipped. One sign
    /// may come before or after the number, spaced from it; brackets make it negative but cannot be combined with a
    /// sign; one currency symbol may come on either side. Group separators are ignored once a digit has been read,
    /// even after the decimal point. An <c>e</c> or <c>d</c> exponent needs at least one digit. <c>&amp;H</c>/<c>&amp;O</c>
    /// text is a whole number, read as a Long when it fits 32 bits (<c>&amp;HFFFFFFFF</c> is -1). Anything else is a
    /// type mismatch, and a value past a Double is an overflow.
    /// </summary>
    public static double TextAsNumber(string text)
    {
        double value = double.Parse(NumberText(text), NumberStyles.Float, CultureInfo.InvariantCulture);
        if (double.IsInfinity(value))
            throw new OverflowException($"Overflow: '{text}' is too large for a number.");
        return value;
    }

    /// <summary>Text read as <see cref="TextAsNumber"/> reads it, or null when it is not a number or is past a Double.</summary>
    public static double? TryTextAsNumber(string text)
    {
        try
        {
            return TextAsNumber(text);
        }
        catch (Exception e) when (e is InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>Text read as <see cref="TextAsNumber"/> reads it, but exactly; null when it is past a Decimal.</summary>
    public static decimal? TextAsDecimal(string text) =>
        decimal.TryParse(NumberText(text), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) ? value : null;

    /// <summary>The number <see cref="TextAsNumber"/> reads, written out in invariant form.</summary>
    private static string NumberText(string text)
    {
        NumberFormatInfo format = CultureInfo.CurrentCulture.NumberFormat;
        string s = text.Trim();
        if (s.Length > 1 && s[0] == '&' && s[1] is 'H' or 'h' or 'O' or 'o')
            return RadixNumber(text, s[2..], s[1] is 'H' or 'h' ? 16 : 8).ToString(CultureInfo.InvariantCulture);

        int i = 0;
        bool negative = false, signed = false, currency = false, open = false, closed = false;
        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s[i])) i++;
            else if (s[i] is '+' or '-' && !signed) { signed = true; negative = s[i] == '-'; i++; }
            else if (s[i] == '(' && !open) { open = true; i++; }
            else if (!currency && At(s, i, format.CurrencySymbol)) { currency = true; i += format.CurrencySymbol.Length; }
            else break;
        }

        var number = new StringBuilder();
        bool digits = false, point = false;
        while (i < s.Length)
        {
            if (s[i] is >= '0' and <= '9') { number.Append(s[i]); digits = true; i++; }
            else if (!point && At(s, i, format.NumberDecimalSeparator)) { number.Append('.'); point = true; i += format.NumberDecimalSeparator.Length; }
            else if (digits && At(s, i, format.NumberGroupSeparator)) i += format.NumberGroupSeparator.Length;
            else break;
        }
        if (!digits) throw NotANumber(text);
        if (point && number[^1] == '.') number.Append('0');

        if (i < s.Length && s[i] is 'e' or 'E' or 'd' or 'D')
        {
            number.Append('E');
            i++;
            if (i < s.Length && s[i] is '+' or '-') number.Append(s[i++]);
            int start = i;
            while (i < s.Length && s[i] is >= '0' and <= '9') number.Append(s[i++]);
            if (i == start) throw NotANumber(text);
        }

        while (i < s.Length)
        {
            if (char.IsWhiteSpace(s[i])) i++;
            else if (s[i] is '+' or '-' && !signed) { signed = true; negative = s[i] == '-'; i++; }
            else if (s[i] == ')' && open && !closed) { closed = true; i++; }
            else if (!currency && At(s, i, format.CurrencySymbol)) { currency = true; i += format.CurrencySymbol.Length; }
            else throw NotANumber(text);
        }
        if (open != closed || (open && signed)) throw NotANumber(text);

        return negative || open ? "-" + number : number.ToString();
    }

    private static long RadixNumber(string text, string digits, int radix)
    {
        if (digits.Length == 0) throw NotANumber(text);
        ulong value = 0;
        foreach (char c in digits)
        {
            int digit = c is >= '0' and <= '9' ? c - '0'
                : c is >= 'A' and <= 'F' ? c - 'A' + 10
                : c is >= 'a' and <= 'f' ? c - 'a' + 10
                : int.MaxValue;
            if (digit >= radix) throw NotANumber(text);
            value = checked(value * (ulong)radix + (ulong)digit);
        }
        return value <= uint.MaxValue ? (int)(uint)value : checked((long)value);
    }

    private static bool At(string s, int index, string symbol) =>
        symbol.Length > 0 && string.CompareOrdinal(s, index, symbol, 0, symbol.Length) == 0;

    private static InvalidCastException NotANumber(string text) =>
        new($"Type mismatch: '{text}' cannot be read as a number.");

    /// <summary>
    /// A value as CStr writes it (verified vs ACE). A Boolean is its VARIANT_BOOL number; a Double has 15
    /// significant digits and a Single 7; a Decimal drops trailing zeros; a date is written in the regional short
    /// date and long time, without the time at midnight and without the date on 1899-12-30; a GUID is braced upper
    /// case; a binary value is read as UTF-16 text.
    /// </summary>
    public static string Of(object v) => v switch
    {
        string s => s,
        bool b => b ? "-1" : "0",
        double d => FloatingText(d, 15),
        float f => FloatingText(f, 7),
        decimal m => m.ToString("0.############################", CultureInfo.CurrentCulture),
        DateTime d => DateText(d),
        Guid g => g.ToString("B").ToUpperInvariant(),
        byte[] bytes => Encoding.Unicode.GetString(Utf16Layout(bytes)),
        _ => Convert.ToString(v, CultureInfo.CurrentCulture)!,
    };

    /// <summary>
    /// A number as Access <c>Str</c> writes it (verified vs ACE): as CStr writes it, but always with a period and no
    /// zero before it, and with a space where a positive number's sign would go (<c>Str(0.5)</c> is " .5",
    /// <c>Str(True)</c> is "-1").
    /// </summary>
    public static string Str(object number)
    {
        NumberFormatInfo invariant = NumberFormatInfo.InvariantInfo;
        string text = number switch
        {
            bool b => b ? "-1" : "0",
            double d => FloatingText(d, 15, invariant),
            float f => FloatingText(f, 7, invariant),
            decimal m => m.ToString("0.############################", invariant),
            var n => Convert.ToString(n, invariant)!,
        };
        if (text.StartsWith("0.", StringComparison.Ordinal))
            text = text[1..];
        else if (text.StartsWith("-0.", StringComparison.Ordinal))
            text = "-" + text[2..];
        return text[0] == '-' ? text : " " + text;
    }

    /// <summary>A binary value laid out as the UTF-16 text it is read as: an odd trailing byte zero-padded (matching
    /// ACE, which reinterprets a binary column as a UTF-16 string — LenB of a 3-byte value is 4).</summary>
    public static byte[] Utf16Layout(byte[] bytes)
    {
        if (bytes.Length % 2 == 0) return bytes;
        var padded = new byte[bytes.Length + 1];
        Array.Copy(bytes, padded, bytes.Length);
        return padded;
    }

    /// <summary>
    /// A floating value rounded to <paramref name="digits"/> significant digits, trailing zeros dropped. Written
    /// in E notation when its exponent is at least <paramref name="digits"/>, or when fixed notation would need
    /// more than <paramref name="digits"/> decimals (verified vs ACE: <c>1/3</c> is 0.333333333333333, a Single
    /// 1E7 is 1E+07, a Single 1E-5 is 0.00001, 1E300 is 1E+300).
    /// </summary>
    public static string FloatingText(double value, int digits, NumberFormatInfo? format = null)
    {
        if (value == 0) return "0";
        if (!double.IsFinite(value)) return value.ToString(CultureInfo.InvariantCulture);

        format ??= CultureInfo.CurrentCulture.NumberFormat;
        string scientific = value.ToString("E" + (digits - 1), CultureInfo.InvariantCulture);  // -d.dddE+ddd
        int mark = scientific.IndexOf('E');
        int exponent = int.Parse(scientific[(mark + 1)..], CultureInfo.InvariantCulture);
        bool negative = scientific[0] == '-';
        string significant = scientific[(negative ? 1 : 0)..mark].Replace(".", "").TrimEnd('0');
        string sign = negative ? format.NegativeSign : "";
        string point = format.NumberDecimalSeparator;

        int decimals = significant.Length - 1 - exponent;
        if (exponent >= digits || decimals > digits)
        {
            string mantissa = significant.Length == 1 ? significant : significant[0] + point + significant[1..];
            return $"{sign}{mantissa}E{(exponent < 0 ? "-" : "+")}{Math.Abs(exponent):00}";
        }
        if (exponent < 0)
            return sign + "0" + point + new string('0', -exponent - 1) + significant;
        if (significant.Length <= exponent + 1)
            return sign + significant + new string('0', exponent + 1 - significant.Length);
        return sign + significant[..(exponent + 1)] + point + significant[(exponent + 1)..];
    }

    /// <summary>The culture's long time pattern, with the narrow no-break space newer ICU data puts before AM/PM
    /// (U+202F, on Linux and macOS) written as the plain space of Windows' regional settings, and so of ACE. A date
    /// then writes the same text on every platform.</summary>
    public static string LongTimePattern(DateTimeFormatInfo format) => format.LongTimePattern.Replace(' ', ' ');

    /// <summary>A date as <see cref="Of"/> writes it; the year is not zero-padded (year 100 is "100") unless
    /// <paramref name="padYear"/> asks for it, as Format does.</summary>
    public static string DateText(DateTime d, bool padYear = false)
    {
        DateTimeFormatInfo format = CultureInfo.CurrentCulture.DateTimeFormat;
        string time = d.ToString(LongTimePattern(format), CultureInfo.CurrentCulture);
        if (d.Date == JetTypeCodec.OleEpoch) return time;
        string datePattern = padYear
            ? format.ShortDatePattern
            : format.ShortDatePattern.Replace("yyyy", "'" + d.Year.ToString(CultureInfo.InvariantCulture) + "'");
        string date = d.ToString(datePattern, CultureInfo.CurrentCulture);
        return d.TimeOfDay == TimeSpan.Zero ? date : date + " " + time;
    }
}
