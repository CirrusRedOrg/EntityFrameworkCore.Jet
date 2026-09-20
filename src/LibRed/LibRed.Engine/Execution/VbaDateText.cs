using System.Globalization;

namespace LibRed.Engine.Execution;

/// <summary>
/// Text read as a date and time the way <c>CDate</c> reads it — OLE Automation's own parser rather than .NET's
/// (verified vs ACE; documented at
/// https://learn.microsoft.com/en-us/office/vba/Language/Concepts/Getting-Started/type-conversion-functions).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The text is numbers and words split by separators. <c>/</c> <c>-</c> <c>,</c>, spaces and the regional date
/// separator split a date; <c>:</c> <c>.</c> and the regional time separator split a time, so <c>2.5</c> is 02:05.
/// A regional date separator of <c>.</c> makes it a date separator instead.</item>
/// <item>A time is two or three numbers split by time separators, or one number with AM or PM; hours run to 23 and
/// minutes and seconds to 59. AM or PM moves an hour up to 12 (12 AM is midnight) and leaves a later one alone.
/// A time may come before or after the date.</item>
/// <item>A date of two numbers takes the regional order of day and month, then the other order, then month and year
/// (<c>1,2020</c> is January 2020); with no year it is in the current year. Three numbers are year, month and day
/// when the first cannot be a day, otherwise in the regional order, then with day and month swapped.</item>
/// <item>A month name, full or abbreviated, takes one number as its day, or as its year when it cannot be one
/// (<c>Feb 30</c> is 2030-02-01), or two as day and year. A day-of-week name is not recognised.</item>
/// <item>A year under 100 is placed by the calendar's two-digit window (<c>0</c> is 2000, <c>99</c> 1999); a year past
/// 9999 is invalid.</item>
/// <item>Anything else — one bare number, a stray separator, an unknown word — is not a date, and the caller reads the
/// text as a number instead.</item>
/// </list>
/// </remarks>
internal static class VbaDateText
{
    private enum Kind { Number, Month, Meridiem, DateSeparator, TimeSeparator }

    /// <summary>A part of the text. A number keeps how many digits it was written with; a time separator is
    /// <see cref="Dot"/> when it was a period.</summary>
    private readonly record struct Token(Kind Kind, int Value, int Digits = 0);

    private const int Dot = 1;

    public static bool TryParse(string text, CultureInfo culture, out DateTime value)
    {
        value = default;
        if (Tokenize(text, culture) is not { } tokens)
            return false;

        TimeSpan? time = null;
        var dateNumbers = new List<int>();
        int? month = null;
        Token? separator = null;   // the separator just read, if the last token was one

        for (int i = 0; i < tokens.Count; i++)
        {
            Token token = tokens[i];
            switch (token.Kind)
            {
                case Kind.Number when IsTimeStart(tokens, i):
                    // A time follows spacing or starts the text; an explicit date separator cannot lead into one.
                    if (time is not null || separator is { Value: not Spacing } || ReadTime(tokens, ref i) is not { } read)
                        return false;
                    time = read;
                    separator = null;
                    break;
                case Kind.Number:
                    dateNumbers.Add(token.Value);
                    separator = null;
                    break;
                case Kind.Month:
                    if (month is not null)
                        return false;
                    month = token.Value;
                    separator = null;
                    break;
                case Kind.DateSeparator:
                    // Between two parts only: not first, not doubled, not last.
                    if (separator is not null || i == 0 || i == tokens.Count - 1)
                        return false;
                    separator = token;
                    break;
                default:
                    return false;
            }
        }

        DateTime date;
        if (month is null && dateNumbers.Count == 0)
        {
            if (time is null)
                return false;
            date = new DateTime(1899, 12, 30);
        }
        else if (DateOf(dateNumbers, month, culture) is { } parsed)
        {
            date = parsed;
        }
        else
        {
            return false;
        }

        value = date + (time ?? TimeSpan.Zero);
        return true;
    }

    /// <summary>The words, numbers and separators of the text, or null when it holds anything else.</summary>
    private static List<Token>? Tokenize(string text, CultureInfo culture)
    {
        DateTimeFormatInfo format = culture.DateTimeFormat;
        bool dotIsDate = format.DateSeparator == ".";
        var tokens = new List<Token>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                // Spacing separates only two parts with nothing else between them.
                int end = i;
                while (end < text.Length && char.IsWhiteSpace(text[end])) end++;
                if (tokens.Count > 0 && tokens[^1].Kind is Kind.Number or Kind.Month or Kind.Meridiem
                    && end < text.Length && char.IsLetterOrDigit(text[end]))
                    tokens.Add(new Token(Kind.DateSeparator, Spacing));
                i = end;
            }
            else if (char.IsDigit(c))
            {
                int start = i;
                while (i < text.Length && char.IsDigit(text[i])) i++;
                if (i - start > 9)
                    return null;
                tokens.Add(new Token(Kind.Number, int.Parse(text.AsSpan(start, i - start), CultureInfo.InvariantCulture), i - start));
            }
            else if (char.IsLetter(c))
            {
                int start = i;
                while (i < text.Length && char.IsLetter(text[i])) i++;
                string word = text[start..i];
                if (Meridiem(word, format) is { } pm)
                    tokens.Add(new Token(Kind.Meridiem, pm ? 1 : 0));
                else if (MonthOf(word, format) is { } m)
                    tokens.Add(new Token(Kind.Month, m));
                else
                    return null;
            }
            else if (c == '.' && (!dotIsDate || AfterSeconds(tokens))
                || c == ':' || format.TimeSeparator.Length == 1 && c == format.TimeSeparator[0])
            {
                DropSpacing(tokens);
                tokens.Add(new Token(Kind.TimeSeparator, c == '.' ? Dot : 0));
                i++;
                SkipSpacing(text, ref i);
            }
            else if (c is '/' or '-' or ',' || format.DateSeparator.Length == 1 && c == format.DateSeparator[0])
            {
                DropSpacing(tokens);
                tokens.Add(new Token(Kind.DateSeparator, 0));
                i++;
                SkipSpacing(text, ref i);
            }
            else
            {
                return null;
            }
        }
        return tokens;
    }

    /// <summary>The value of a date separator that is only spacing, which a time may follow.</summary>
    private const int Spacing = 1;

    /// <summary>Whether the tokens so far end in hours, minutes and seconds written with colons, so a period can only
    /// start their fraction.</summary>
    private static bool AfterSeconds(List<Token> tokens) =>
        tokens.Count >= 5
        && tokens[^1].Kind == Kind.Number && tokens[^2] is { Kind: Kind.TimeSeparator, Value: not Dot }
        && tokens[^3].Kind == Kind.Number && tokens[^4] is { Kind: Kind.TimeSeparator, Value: not Dot }
        && tokens[^5].Kind == Kind.Number;

    /// <summary>An explicit separator absorbs the spacing before it.</summary>
    private static void DropSpacing(List<Token> tokens)
    {
        if (tokens.Count > 0 && tokens[^1] is { Kind: Kind.DateSeparator, Value: Spacing })
            tokens.RemoveAt(tokens.Count - 1);
    }

    private static void SkipSpacing(string text, ref int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
    }

    /// <summary>Whether the number at <paramref name="i"/> starts a time: a time separator or AM/PM follows it.</summary>
    private static bool IsTimeStart(List<Token> tokens, int i)
    {
        int next = i + 1;
        if (next < tokens.Count && tokens[next].Kind == Kind.DateSeparator && next + 1 < tokens.Count
            && tokens[next + 1].Kind == Kind.Meridiem)
            next++;   // "3 PM": the spacing before the designator
        return next < tokens.Count && tokens[next].Kind is Kind.TimeSeparator or Kind.Meridiem;
    }

    /// <summary>The time starting at <paramref name="i"/>, leaving <paramref name="i"/> on its last token; null when
    /// it is not a valid time.</summary>
    private static TimeSpan? ReadTime(List<Token> tokens, ref int i)
    {
        var parts = new List<int> { tokens[i].Value };
        long fraction = 0;
        bool dotted = false;
        while (i + 2 < tokens.Count && tokens[i + 1].Kind == Kind.TimeSeparator && tokens[i + 2].Kind == Kind.Number)
        {
            // A LibRed extension: after hours, minutes and seconds written with colons, a period and up to seven digits
            // are a fraction of a second, as LibRed's own SQL writes a time with milliseconds
            // (TIMEVALUE('12:30:45.123')). ACE refuses a fourth part.
            if (parts.Count == 3 && !dotted && tokens[i + 1].Value == Dot && tokens[i + 2].Digits <= 7)
            {
                fraction = tokens[i + 2].Value * (long)Math.Pow(10, 7 - tokens[i + 2].Digits);
                i += 2;
                break;
            }
            dotted |= tokens[i + 1].Value == Dot;
            parts.Add(tokens[i + 2].Value);
            i += 2;
        }
        if (i + 1 < tokens.Count && tokens[i + 1].Kind == Kind.TimeSeparator)
            return null;   // a separator with no number after it

        bool? pm = null;
        int next = i + 1;
        if (next < tokens.Count && tokens[next].Kind == Kind.DateSeparator && next + 1 < tokens.Count
            && tokens[next + 1].Kind == Kind.Meridiem)
            next++;
        if (next < tokens.Count && tokens[next].Kind == Kind.Meridiem)
        {
            pm = tokens[next].Value == 1;
            i = next;
        }

        if (parts.Count > 3 || parts.Count == 1 && pm is null)
            return null;
        int hour = parts[0], minute = parts.Count > 1 ? parts[1] : 0, second = parts.Count > 2 ? parts[2] : 0;
        if (hour > 23 || minute > 59 || second > 59)
            return null;
        if (pm is { } afternoon && hour <= 12)
            hour = afternoon ? (hour == 12 ? 12 : hour + 12) : (hour == 12 ? 0 : hour);
        return new TimeSpan(hour, minute, second) + TimeSpan.FromTicks(fraction);
    }

    private static DateTime? DateOf(List<int> numbers, int? month, CultureInfo culture)
    {
        int currentYear = DateTime.Today.Year;
        if (month is { } m)
        {
            return numbers.Count switch
            {
                1 => Make(currentYear, m, numbers[0]) ?? Make(Year(numbers[0], culture), m, 1),
                2 => Make(Year(numbers[1], culture), m, numbers[0]),
                _ => null,
            };
        }

        bool dayFirst = DayBeforeMonth(culture.DateTimeFormat.ShortDatePattern);
        switch (numbers.Count)
        {
            case 2:
            {
                (int day, int mon) = dayFirst ? (numbers[0], numbers[1]) : (numbers[1], numbers[0]);
                return Make(currentYear, mon, day)
                    ?? Make(currentYear, day, mon)
                    ?? Make(Year(numbers[1], culture), numbers[0], 1)
                    ?? Make(Year(numbers[0], culture), numbers[1], 1);
            }
            case 3:
            {
                if (numbers[0] > 31)
                    return Make(Year(numbers[0], culture), numbers[1], numbers[2]);
                (int day, int mon, int year) = YearFirst(culture.DateTimeFormat.ShortDatePattern)
                    ? (numbers[2], numbers[1], numbers[0])
                    : dayFirst ? (numbers[0], numbers[1], numbers[2]) : (numbers[1], numbers[0], numbers[2]);
                int y = Year(year, culture);
                return Make(y, mon, day) ?? Make(y, day, mon);
            }
            default:
                return null;
        }
    }

    private static int Year(int year, CultureInfo culture) =>
        year < 100 ? culture.Calendar.ToFourDigitYear(year) : year;

    private static DateTime? Make(int year, int month, int day) =>
        year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
            ? new DateTime(year, month, day)
            : null;

    private static bool DayBeforeMonth(string pattern)
    {
        int d = pattern.IndexOf('d'), m = pattern.IndexOf('M');
        return d >= 0 && m >= 0 && d < m;
    }

    private static bool YearFirst(string pattern)
    {
        int y = pattern.IndexOf('y'), m = pattern.IndexOf('M');
        return y >= 0 && m >= 0 && y < m;
    }

    private static bool? Meridiem(string word, DateTimeFormatInfo format) =>
        Is(word, format.AMDesignator) || Is(word, "AM") ? false
        : Is(word, format.PMDesignator) || Is(word, "PM") ? true
        : null;

    private static int? MonthOf(string word, DateTimeFormatInfo format)
    {
        for (int i = 0; i < 12; i++)
        {
            if (Is(word, format.MonthNames[i]) || Is(word, format.AbbreviatedMonthNames[i])
                || Is(word, format.MonthGenitiveNames[i]) || Is(word, format.AbbreviatedMonthGenitiveNames[i]))
                return i + 1;
        }
        return null;
    }

    private static bool Is(string word, string name) =>
        name.Length > 0 && string.Equals(word, name.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
}
