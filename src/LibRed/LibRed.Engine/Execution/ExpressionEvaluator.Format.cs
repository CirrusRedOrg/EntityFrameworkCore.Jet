using LibRed.Sql.Ast;
using System.Globalization;
using System.Text;

namespace LibRed.Engine.Execution;

/// <summary>
/// Access <c>Format</c>, <c>FormatNumber</c>, <c>FormatCurrency</c>, <c>FormatPercent</c> and <c>FormatDateTime</c>
/// (verified vs ACE). Output follows the current culture's separators, names and patterns, as ACE follows the
/// system's regional settings.
/// </summary>
internal sealed partial class ExpressionEvaluator
{
    private enum FormatKind
    {
        Number,
        Date,
        Text,
    }

    private enum NumberStyle
    {
        Number,
        Currency,
        Percent,
    }

    /// <summary>
    /// Access <c>Format(value, [format], [firstdayofweek], [firstweekofyear])</c>. A named format is matched whole and
    /// without regard to case. Otherwise the format is a text format when it has <c>@ &amp; &lt; &gt; !</c>, a number
    /// format when it has a <c>0</c> or <c>#</c>, a date format when it has a date or time symbol, and otherwise a
    /// number format of literals only. Text that is neither a number nor a date is returned unchanged by number and
    /// date formats. A Null value is empty text, or the format's Null section. A Null format or setting gives Null
    /// where ACE raises an error.
    /// </summary>
    private string? FormatValue(FunctionCall f)
    {
        object? value = Evaluate(f.Arguments[0]);
        string format = "";
        if (f.Arguments.Count > 1)
        {
            if (Evaluate(f.Arguments[1]) is not { } formatValue)
                return null;
            format = ConcatText(formatValue);
        }
        if (FirstDayOfWeek(f, 2) is not { } first || FirstWeekOfYear(f, 3) is not { } rule)
            return null;
        return FormatText(value, format, first, rule);
    }

    /// <summary>The date formats of FormatDateTime's 0 to 4 — the general date, long date, short date, long time and
    /// short time — which the named formats of the same names share.</summary>
    private static readonly string[] DateTimeFormats = ["c", "dddddd", "ddddd", "ttttt", "hh:nn"];

    private static string FormatText(object? value, string format, DayOfWeek first, CalendarWeekRule rule)
    {
        if (format.Length == 0)
            return value is null ? "" : GeneralText(value);

        switch (format.ToLowerInvariant())
        {
            case "general number": return NamedNumber(value, number => ConcatText(number));
            case "currency": return NamedNumber(value, number => StyledNumber(DigitsOf(number), NumberStyle.Currency, -1, -2, -2, -2));
            case "fixed": return FormatNumberSections(value, ["0.00"]);
            case "standard": return FormatNumberSections(value, ["#,##0.00"]);
            case "percent": return FormatNumberSections(value, ["0.00%"]);
            case "scientific": return FormatNumberSections(value, ["0.00E+00"]);
            case "yes/no": return NamedNumber(value, number => DigitsOf(number).IsZero ? "No" : "Yes");
            case "true/false": return NamedNumber(value, number => DigitsOf(number).IsZero ? "False" : "True");
            case "on/off": return NamedNumber(value, number => DigitsOf(number).IsZero ? "Off" : "On");
            case "general date": return FormatDate(value, DateTimeFormats[0], first, rule);
            case "long date": return FormatDate(value, DateTimeFormats[1], first, rule);
            case "medium date": return FormatDate(value, "dd-mmm-yy", first, rule);
            case "short date": return FormatDate(value, DateTimeFormats[2], first, rule);
            case "long time": return FormatDate(value, DateTimeFormats[3], first, rule);
            case "medium time": return FormatDate(value, "hh:nn AM/PM", first, rule);
            case "short time": return FormatDate(value, DateTimeFormats[4], first, rule);
        }

        List<string> sections = FormatSections(format);
        return FormatKindOf(format) switch
        {
            FormatKind.Text => FormatTextSections(value, sections),
            FormatKind.Date => FormatDate(value, sections[0], first, rule),
            _ => FormatNumberSections(value, sections),
        };
    }

    /// <summary>A value as Format writes it without a format: as CStr does, but a date with its year padded and
    /// rounded to the second.</summary>
    private static string GeneralText(object value) =>
        value is DateTime date ? DateText(RoundToSecond(date), padYear: true) : ConcatText(value);

    private static string NamedNumber(object? value, Func<object, string> write) =>
        value is null or string { Length: 0 } ? ""
        : FormatNumberOperand(value) is { } number ? write(number)
        : ConcatText(value);

    /// <summary>The number a value stands for in a number format: a number, a Boolean, a date's serial, or text that
    /// reads as a number or else as a date. Null for other text, which the format leaves as it is.</summary>
    private static object? FormatNumberOperand(object value) => value switch
    {
        string or char => TryTextAsNumber(value.ToString()!) is { } number ? number
            : VbaDateText.TryParse(value.ToString()!, CultureInfo.CurrentCulture, out DateTime date) ? date.ToOADate()
            : null,
        DateTime date => date.ToOADate(),
        bool b => b ? -1 : 0,
        Guid or byte[] => null,
        _ => value,
    };

    /// <summary>The date a value stands for in a date format, or null for text that is neither a number nor a date.
    /// A number past the range of a date is an overflow.</summary>
    private static DateTime? FormatDateOperand(object value) =>
        value is DateTime date ? date
        : FormatNumberOperand(value) is { } number ? OaDate(Dbl(number))
        : null;

    /// <summary>A date rounded to the nearest second, as ACE writes it.</summary>
    private static DateTime RoundToSecond(DateTime date)
    {
        long rest = date.Ticks % TimeSpan.TicksPerSecond;
        long ticks = date.Ticks - rest + (rest >= TimeSpan.TicksPerSecond / 2 ? TimeSpan.TicksPerSecond : 0);
        return ticks <= DateTime.MaxValue.Ticks ? new DateTime(ticks, date.Kind) : date;
    }

    /// <summary>The format's sections, split at semicolons outside quotes and escapes.</summary>
    private static List<string> FormatSections(string format)
    {
        var sections = new List<string>();
        int start = 0;
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] == '"')
                i = ClosingQuote(format, i);
            else if (format[i] == '\\')
                i++;
            else if (format[i] == ';')
            {
                sections.Add(format[start..i]);
                start = i + 1;
            }
        }
        sections.Add(format[start..]);
        return sections;
    }

    private static int ClosingQuote(string format, int open)
    {
        int close = format.IndexOf('"', open + 1);
        return close < 0 ? format.Length : close;
    }

    private static FormatKind FormatKindOf(string format)
    {
        bool digits = false, date = false;
        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"')
                i = ClosingQuote(format, i);
            else if (c == '\\')
                i++;
            else if (c is '@' or '&' or '<' or '>' or '!')
                return FormatKind.Text;
            else if (c is '0' or '#')
                digits = true;
            else if (c is '/' or ':' || DateSymbolLength(format, i) > 0 || Meridiem(format, i) > 0)
                date = true;
        }
        return digits || !date ? FormatKind.Number : FormatKind.Date;
    }

    // ---- Text formats ----

    /// <summary>
    /// A text format: <c>@</c> is a character or a space and <c>&amp;</c> a character or nothing, filled from the right
    /// unless <c>!</c> fills them from the left; <c>&lt;</c> and <c>&gt;</c> force lower and upper case (both together
    /// change nothing). Characters the placeholders do not take follow the format's output (left to right, the
    /// leftmost are dropped instead). Empty text and Null use the second section, or are empty.
    /// </summary>
    private static string FormatTextSections(object? value, List<string> sections)
    {
        string text = value is null ? "" : GeneralText(value);
        if (text.Length == 0)
            return sections.Count > 1 ? FormatTextSection(sections[1], "") : "";
        return FormatTextSection(sections[0], text);
    }

    private static string FormatTextSection(string section, string text)
    {
        var items = new List<(char Placeholder, string Literal)>();
        bool lower = false, upper = false, leftToRight = false;
        for (int i = 0; i < section.Length; i++)
        {
            char c = section[i];
            switch (c)
            {
                case '"':
                    int close = ClosingQuote(section, i);
                    items.Add(('\0', section[(i + 1)..close]));
                    i = close;
                    break;
                case '\\':
                    if (i + 1 < section.Length)
                        items.Add(('\0', section[++i].ToString()));
                    break;
                case '@' or '&':
                    items.Add((c, ""));
                    break;
                case '<': lower = true; break;
                case '>': upper = true; break;
                case '!': leftToRight = true; break;
                default:
                    items.Add(('\0', c.ToString()));
                    break;
            }
        }
        if (lower != upper)
            text = lower ? text.ToLowerInvariant() : text.ToUpperInvariant();

        int placeholders = items.Count(item => item.Placeholder != '\0');
        var output = new StringBuilder();
        if (placeholders == 0)
        {
            foreach (var item in items)
                output.Append(item.Literal);
            return output.Append(text).ToString();
        }

        if (leftToRight && text.Length > placeholders)
            text = text[^placeholders..];
        int blanks = leftToRight ? 0 : Math.Max(0, placeholders - text.Length);
        int slot = 0, next = 0;
        foreach (var item in items)
        {
            if (item.Placeholder == '\0')
            {
                output.Append(item.Literal);
                continue;
            }
            if (slot++ < blanks || next >= text.Length)
            {
                if (item.Placeholder == '@')
                    output.Append(' ');
            }
            else
                output.Append(text[next++]);
        }
        return output.Append(text[next..]).ToString();
    }

    // ---- Date formats ----

    private static string FormatDate(object? value, string format, DayOfWeek first, CalendarWeekRule rule)
    {
        if (value is null or string { Length: 0 })
            return "";
        return FormatDateOperand(value) is { } date
            ? DateSymbols(RoundToSecond(date), format, first, rule)
            : ConcatText(value);
    }

    /// <summary>
    /// A date written by a date format. Symbols are matched without regard to case, longest first, and a run longer
    /// than a symbol continues as the next (<c>yyy</c> is <c>yy</c> then <c>y</c>). <c>m</c> and <c>mm</c> are the minute
    /// straight after an hour symbol. AM/PM, A/P and AMPM make the hour a 12-hour one. Anything else is written as it is.
    /// </summary>
    private static string DateSymbols(DateTime date, string format, DayOfWeek first, CalendarWeekRule rule)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        DateTimeFormatInfo names = culture.DateTimeFormat;
        bool twelveHour = false;
        for (int i = 0; i < format.Length; i++)
            twelveHour |= Meridiem(format, i) > 0;

        var output = new StringBuilder();
        bool afterHour = false;
        for (int i = 0; i < format.Length;)
        {
            char c = format[i];
            if (c == '"')
            {
                int close = ClosingQuote(format, i);
                output.Append(format, i + 1, close - i - 1);
                i = close + 1;
                continue;
            }
            if (c == '\\')
            {
                if (i + 1 < format.Length)
                    output.Append(format[i + 1]);
                i += 2;
                continue;
            }
            if (Meridiem(format, i) is var meridiem and > 0)
            {
                // AM/PM and A/P take the case of their first letter; AMPM is the regional designator as it is defined.
                bool morning = date.Hour < 12;
                string text = meridiem switch
                {
                    4 => morning ? names.AMDesignator : names.PMDesignator,
                    5 => morning ? "AM" : "PM",
                    _ => morning ? "A" : "P",
                };
                output.Append(meridiem != 4 && char.IsLower(c) ? text.ToLowerInvariant() : text);
                i += meridiem;
                afterHour = false;
                continue;
            }
            int length = DateSymbolLength(format, i);
            if (length == 0)
            {
                output.Append(c switch
                {
                    '/' => names.DateSeparator,
                    ':' => names.TimeSeparator,
                    _ => c.ToString(),
                });
                i++;
                continue;
            }

            char symbol = char.ToLowerInvariant(c);
            int hour12 = date.Hour % 12 == 0 ? 12 : date.Hour % 12;
            output.Append((symbol, length) switch
            {
                ('d', 1) => date.Day.ToString(culture),
                ('d', 2) => date.Day.ToString("00", culture),
                ('d', 3) => names.GetAbbreviatedDayName(date.DayOfWeek),
                ('d', 4) => names.GetDayName(date.DayOfWeek),
                ('d', 5) => date.ToString(names.ShortDatePattern, culture),
                ('d', _) => date.ToString(names.LongDatePattern, culture),
                ('m', 1) when afterHour => date.Minute.ToString(culture),
                ('m', 2) when afterHour => date.Minute.ToString("00", culture),
                ('m', 1) => date.Month.ToString(culture),
                ('m', 2) => date.Month.ToString("00", culture),
                ('m', 3) => names.GetAbbreviatedMonthName(date.Month),
                ('m', _) => names.GetMonthName(date.Month),
                ('y', 1) => date.DayOfYear.ToString(culture),
                ('y', 2) => (date.Year % 100).ToString("00", culture),
                ('y', _) => date.Year.ToString("0000", culture),
                ('h', 1) => (twelveHour ? hour12 : date.Hour).ToString(culture),
                ('h', _) => (twelveHour ? hour12 : date.Hour).ToString("00", culture),
                ('n', 1) => date.Minute.ToString(culture),
                ('n', _) => date.Minute.ToString("00", culture),
                ('s', 1) => date.Second.ToString(culture),
                ('s', _) => date.Second.ToString("00", culture),
                ('w', 1) => (DaysIntoWeek(date, first) + 1).ToString(culture),
                ('w', _) => WeekOfYear(date, first, rule).ToString(culture),
                ('q', _) => ((date.Month + 2) / 3).ToString(culture),
                ('c', _) => DateText(date, padYear: true),
                _ => date.ToString(LongTimePattern(names), culture),
            });
            afterHour = symbol == 'h';
            i += length;
        }
        return output.ToString();
    }

    /// <summary>The length of the date symbol at <paramref name="index"/>, or 0 when there is none.</summary>
    private static int DateSymbolLength(string format, int index)
    {
        char symbol = char.ToLowerInvariant(format[index]);
        int run = 1;
        while (index + run < format.Length && char.ToLowerInvariant(format[index + run]) == symbol)
            run++;
        return symbol switch
        {
            'd' => Math.Min(run, 6),
            'm' => Math.Min(run, 4),
            'y' => run >= 4 ? 4 : run >= 2 ? 2 : 1,
            'h' or 'n' or 's' or 'w' => Math.Min(run, 2),
            'q' or 'c' => 1,
            't' => run >= 5 ? 5 : 0,
            _ => 0,
        };
    }

    /// <summary>The length of the AM/PM (5), AMPM (4) or A/P (3) symbol at <paramref name="index"/>, or 0.</summary>
    private static int Meridiem(string format, int index)
    {
        ReadOnlySpan<char> rest = format.AsSpan(index);
        return rest.StartsWith("am/pm", StringComparison.OrdinalIgnoreCase) ? 5
            : rest.StartsWith("ampm", StringComparison.OrdinalIgnoreCase) ? 4
            : rest.StartsWith("a/p", StringComparison.OrdinalIgnoreCase) ? 3
            : 0;
    }

    // ---- Number formats ----

    /// <summary>
    /// A number format of up to four sections: positive, negative, zero and Null. A negative value with no negative
    /// section uses the first with a minus sign. A value that the section rounds to zero is a zero, and a zero uses the
    /// third section, or the first when there is none or it is empty. An empty first section writes nothing.
    /// </summary>
    private static string FormatNumberSections(object? value, List<string> sections)
    {
        string Section(int i) => i < sections.Count ? sections[i] : "";
        if (value is null)
            return sections.Count > 3 ? new NumberSection(sections[3]).Write(default) : "";
        if (value is string { Length: 0 })
            return "";
        if (FormatNumberOperand(value) is not { } number)
            return ConcatText(value);

        FormatDigits digits = DigitsOf(number);
        if (!digits.IsZero)
        {
            bool negativeSection = digits.Negative && Section(1).Length > 0;
            var section = new NumberSection(negativeSection ? Section(1) : Section(0));
            if (section.Text.Length == 0)
                return "";
            FormatDigits magnitude = digits with { Negative = false };
            if (!section.RoundsToZero(magnitude))
                return (digits.Negative && !negativeSection ? "-" : "") + section.Write(magnitude);
        }
        string zero = Section(2).Length > 0 ? Section(2) : Section(0);
        return new NumberSection(zero).Write(default);
    }

    /// <summary>
    /// A number as its decimal digits: <c>0.Digits × 10^Point</c>, with no leading or trailing zeros (none at all for
    /// zero). A Double has 15 significant digits and a Single 7, as ACE writes them; other numbers are exact.
    /// </summary>
    private readonly record struct FormatDigits(bool Negative, string Digits, int Point)
    {
        private readonly string? digits = Digits;

        public string Digits
        {
            get => digits ?? "";
            init => digits = value;
        }

        public bool IsZero => Digits.Length == 0;

        public FormatDigits Scale(int powerOfTen) => IsZero ? this : this with { Point = Point + powerOfTen };

        /// <summary>Rounded to <paramref name="decimals"/> places, half away from zero.</summary>
        public FormatDigits Round(int decimals)
        {
            int keep = Point + decimals;
            if (keep >= Digits.Length)
                return this;
            if (keep < 0)
                return this with { Digits = "", Point = 0 };
            char[] kept = Digits[..keep].ToCharArray();
            int point = Point;
            if (Digits[keep] >= '5')
            {
                int i = kept.Length - 1;
                while (i >= 0 && kept[i] == '9')
                    kept[i--] = '0';
                if (i >= 0)
                    kept[i]++;
                else
                {
                    kept = ['1', .. kept];
                    point++;
                }
            }
            string rounded = new string(kept).TrimEnd('0');
            return this with { Digits = rounded, Point = rounded.Length == 0 ? 0 : point };
        }

        /// <summary>The digits before the point ("" below 1).</summary>
        public string Whole => Point <= 0 ? "" : Digits.Length >= Point ? Digits[..Point] : Digits.PadRight(Point, '0');

        /// <summary>The first <paramref name="count"/> digits after the point.</summary>
        public string Fraction(int count)
        {
            var fraction = new char[count];
            for (int i = 0; i < count; i++)
            {
                int index = Point + i;
                fraction[i] = index >= 0 && index < Digits.Length ? Digits[index] : '0';
            }
            return new string(fraction);
        }
    }

    private static FormatDigits DigitsOf(object number) => Numeric(number) switch
    {
        double d => ScientificDigits(d.ToString("E14", CultureInfo.InvariantCulture)),
        float f => ScientificDigits(((double)f).ToString("E6", CultureInfo.InvariantCulture)),
        var n => PlainDigits(Convert.ToString(n, CultureInfo.InvariantCulture)!),
    };

    private static FormatDigits ScientificDigits(string text)
    {
        bool negative = text[0] == '-';
        int mark = text.IndexOf('E');
        string mantissa = text[(negative ? 1 : 0)..mark].Replace(".", "").TrimEnd('0');
        int exponent = int.Parse(text[(mark + 1)..], CultureInfo.InvariantCulture);
        return mantissa.Length == 0 ? default : new(negative, mantissa, exponent + 1);
    }

    private static FormatDigits PlainDigits(string text)
    {
        bool negative = text[0] == '-';
        string unsigned = negative ? text[1..] : text;
        int dot = unsigned.IndexOf('.');
        string digits = unsigned.Replace(".", "");
        int point = dot < 0 ? unsigned.Length : dot;
        int leading = digits.Length - digits.TrimStart('0').Length;
        digits = digits.TrimStart('0').TrimEnd('0');
        return digits.Length == 0 ? default : new(negative, digits, point - leading);
    }

    /// <summary>
    /// One section of a number format. <c>0</c> is a digit or a zero and <c>#</c> a digit or nothing; the leftmost
    /// integer placeholder takes any further digits. The first <c>.</c> is the decimal point. A comma after an integer
    /// placeholder groups thousands when another integer placeholder follows it, and otherwise divides by 1000; a
    /// comma before any placeholder is written, one after the point is dropped. Each <c>%</c> multiplies by 100.
    /// <c>E+ E- e+ e-</c> after a placeholder give scientific notation with as many integer digits as there are integer
    /// placeholders; an <c>E</c> without a sign is dropped. <c>[…]</c> is dropped, and <c>*</c> drops itself and the
    /// next character. <c>:</c> and <c>/</c> are the regional separators; anything else is written as it is.
    /// </summary>
    private sealed class NumberSection
    {
        private enum Kind
        {
            Literal,
            Zero,
            Hash,
            Point,
            FractionZero,
            FractionHash,
            Exponent,
            ExponentDigit,
        }

        private readonly List<(Kind Kind, string Text)> items = [];
        private readonly int wholePlaceholders;
        private readonly int fractionPlaceholders;
        private readonly int minimumWhole;
        private readonly int scale;
        private readonly bool group;
        private readonly bool scientific;
        private readonly int exponentZeros;

        public string Text { get; }

        public NumberSection(string text)
        {
            Text = text;
            NumberFormatInfo format = CultureInfo.CurrentCulture.NumberFormat;
            DateTimeFormatInfo dates = CultureInfo.CurrentCulture.DateTimeFormat;
            bool fraction = false, exponent = false;
            int percent = 0, trailingCommas = 0, firstZero = -1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '"':
                        int close = ClosingQuote(text, i);
                        Literal(text[(i + 1)..close]);
                        i = close;
                        break;
                    case '\\':
                        if (i + 1 < text.Length)
                            Literal(text[++i].ToString());
                        break;
                    case '[':
                        int end = text.IndexOf(']', i);
                        i = end < 0 ? text.Length : end;
                        break;
                    case '*':
                        i++;
                        break;
                    case '0' or '#':
                        if (exponent)
                        {
                            items.Add((Kind.ExponentDigit, ""));
                            if (c == '0') exponentZeros++;
                        }
                        else if (fraction)
                        {
                            items.Add((c == '0' ? Kind.FractionZero : Kind.FractionHash, ""));
                            fractionPlaceholders++;
                        }
                        else
                        {
                            items.Add((c == '0' ? Kind.Zero : Kind.Hash, ""));
                            if (c == '0' && firstZero < 0) firstZero = wholePlaceholders;
                            wholePlaceholders++;
                            if (trailingCommas > 0) group = true;
                            trailingCommas = 0;
                        }
                        break;
                    case '.' when !fraction && !exponent:
                        items.Add((Kind.Point, format.NumberDecimalSeparator));
                        fraction = true;
                        break;
                    case ',':
                        if (!fraction && !exponent && wholePlaceholders == 0)
                            Literal(",");
                        else if (!fraction && !exponent)
                            trailingCommas++;
                        break;
                    case '%':
                        percent++;
                        Literal("%");
                        break;
                    case 'E' or 'e':
                        if (i + 1 < text.Length && text[i + 1] is '+' or '-' && !exponent && wholePlaceholders + fractionPlaceholders > 0)
                        {
                            items.Add((Kind.Exponent, $"{c}{text[i + 1]}"));
                            exponent = true;
                            scientific = true;
                            i++;
                        }
                        else if (i + 1 < text.Length && text[i + 1] is '+' or '-')
                            Literal(c.ToString());
                        break;
                    case ':':
                        Literal(dates.TimeSeparator);
                        break;
                    case '/':
                        Literal(dates.DateSeparator);
                        break;
                    default:
                        Literal(c.ToString());
                        break;
                }
            }
            scale = 2 * percent - 3 * trailingCommas;
            // A 0 placeholder shows a digit even for a zero, and so does every placeholder to its right.
            minimumWhole = firstZero < 0 ? 0 : wholePlaceholders - firstZero;
        }

        private void Literal(string text) => items.Add((Kind.Literal, text));

        private bool HasDigits => wholePlaceholders + fractionPlaceholders > 0;

        /// <summary>Whether the section writes the value as zero.</summary>
        public bool RoundsToZero(FormatDigits value) =>
            HasDigits && !scientific && value.Scale(scale).Round(fractionPlaceholders).IsZero;

        public string Write(FormatDigits value)
        {
            NumberFormatInfo format = CultureInfo.CurrentCulture.NumberFormat;
            value = value.Scale(scale);
            string whole, fractionDigits, exponentText = "";
            if (scientific)
            {
                int wholeCount = Math.Max(wholePlaceholders, 1);
                int exponent = 0;
                FormatDigits mantissa = value;
                if (!value.IsZero)
                {
                    exponent = value.Point - wholeCount;
                    mantissa = value.Scale(-exponent).Round(fractionPlaceholders);
                    if (mantissa.Point > wholeCount)
                    {
                        exponent++;
                        mantissa = value.Scale(-exponent).Round(fractionPlaceholders);
                    }
                }
                whole = mantissa.Whole.PadLeft(wholePlaceholders, '0');
                fractionDigits = mantissa.Fraction(fractionPlaceholders);
                exponentText = Math.Abs(exponent).ToString(CultureInfo.InvariantCulture).PadLeft(exponentZeros, '0');
                if (exponent < 0) exponentText = "-" + exponentText;
                else if (items.Find(item => item.Kind == Kind.Exponent).Text[1] == '+') exponentText = "+" + exponentText;
            }
            else
            {
                value = value.Round(fractionPlaceholders);
                whole = value.Whole.PadLeft(minimumWhole, '0');
                fractionDigits = value.Fraction(fractionPlaceholders);
            }

            int keepFraction = fractionPlaceholders;
            var fractionKinds = items.Where(item => item.Kind is Kind.FractionZero or Kind.FractionHash).Select(item => item.Kind).ToList();
            while (keepFraction > 0 && fractionKinds[keepFraction - 1] == Kind.FractionHash && fractionDigits[keepFraction - 1] == '0')
                keepFraction--;

            var output = new StringBuilder();
            int wholeSlot = 0, fractionSlot = 0;
            bool exponentWritten = false;
            foreach (var (kind, text) in items)
            {
                switch (kind)
                {
                    case Kind.Literal:
                        output.Append(text);
                        break;
                    case Kind.Zero or Kind.Hash:
                        // Digits are counted from the right; the leftmost placeholder also takes every digit past it.
                        int place = wholePlaceholders - 1 - wholeSlot++;
                        int highest = place == wholePlaceholders - 1 ? whole.Length - 1 : Math.Min(place, whole.Length - 1);
                        for (int digit = highest; digit >= place; digit--)
                        {
                            output.Append(whole[whole.Length - 1 - digit]);
                            if (group && digit > 0 && digit % 3 == 0)
                                output.Append(format.NumberGroupSeparator);
                        }
                        break;
                    case Kind.Point:
                        if (wholePlaceholders == 0)
                            output.Append(whole);
                        output.Append(text);
                        break;
                    case Kind.FractionZero or Kind.FractionHash:
                        if (wholePlaceholders == 0 && fractionSlot == 0 && !items.Exists(item => item.Kind == Kind.Point))
                            output.Append(whole);
                        if (fractionSlot < keepFraction)
                            output.Append(fractionDigits[fractionSlot]);
                        fractionSlot++;
                        break;
                    case Kind.Exponent:
                        output.Append(text[0]);
                        break;
                    case Kind.ExponentDigit:
                        if (!exponentWritten)
                            output.Append(exponentText);
                        exponentWritten = true;
                        break;
                }
            }
            if (scientific && !exponentWritten)
                output.Append(exponentText);
            return output.ToString();
        }
    }

    // ---- FormatNumber, FormatCurrency, FormatPercent ----

    /// <summary>
    /// Access <c>FormatNumber</c>, <c>FormatCurrency</c> and <c>FormatPercent(value, [digits], [leading zero],
    /// [parentheses], [group digits])</c> (verified vs ACE). The value is read as the conversion functions read it and
    /// rounded half away from zero; a value that rounds to zero has no sign. Digits of -1 take the regional default,
    /// and below that are an invalid procedure call. The other settings are -1 (yes), 0 (no) or -2 (the regional
    /// setting), and anything else is an invalid procedure call. A Null value is empty text; a Null setting gives Null
    /// where ACE raises a type mismatch.
    /// </summary>
    private string? FormatStyled(FunctionCall f, NumberStyle style)
    {
        int[] settings = [-1, -2, -2, -2];
        for (int i = 1; i < f.Arguments.Count; i++)
        {
            if (Evaluate(f.Arguments[i]) is not { } setting)
                return null;
            settings[i - 1] = i == 1 ? Setting(setting, -1, short.MaxValue) : Setting(setting, -2, 0);
        }
        if (Evaluate(f.Arguments[0]) is not { } value)
            return "";
        return StyledNumber(DigitsOf(ConversionNumber(value)), style, settings[0], settings[1], settings[2], settings[3]);
    }

    // The .NET pattern numbers of NumberFormatInfo: n is the number, $ and % the symbol, - the negative sign.
    private static readonly string[] CurrencyPositive = ["$n", "n$", "$ n", "n $"];
    private static readonly string[] CurrencyNegative =
        ["($n)", "-$n", "$-n", "$n-", "(n$)", "-n$", "n-$", "n$-", "-n $", "-$ n", "n $-", "$ n-", "$ -n", "n- $", "($ n)", "(n $)", "$- n"];
    private static readonly string[] PercentPositive = ["n %", "n%", "%n", "% n"];
    private static readonly string[] PercentNegative = ["-n %", "-n%", "-%n", "%-n", "%n-", "n-%", "n%-", "-% n", "n %-", "% n-", "% -n", "n- %"];
    private static readonly string[] NumberNegative = ["(n)", "-n", "- n", "n-", "n -"];

    private static string StyledNumber(FormatDigits value, NumberStyle style, int digits, int leadingZero, int parentheses, int groupDigits)
    {
        NumberFormatInfo format = CultureInfo.CurrentCulture.NumberFormat;
        // The default digits are the regional setting, which is 2 for numbers; .NET's ICU data says 3 for many cultures
        // (en-US included), so only the currency's own digits are taken from the culture.
        if (digits < 0)
            digits = style == NumberStyle.Currency ? format.CurrencyDecimalDigits : 2;
        if (style == NumberStyle.Percent)
            value = value.Scale(2);
        value = value.Round(digits);

        (string point, string separator) = style switch
        {
            NumberStyle.Currency => (format.CurrencyDecimalSeparator, format.CurrencyGroupSeparator),
            NumberStyle.Percent => (format.PercentDecimalSeparator, format.PercentGroupSeparator),
            _ => (format.NumberDecimalSeparator, format.NumberGroupSeparator),
        };
        string whole = value.Whole;
        if (whole.Length == 0 && leadingZero != 0)
            whole = "0";
        if (groupDigits != 0)
        {
            var grouped = new StringBuilder();
            for (int i = 0; i < whole.Length; i++)
            {
                if (i > 0 && (whole.Length - i) % 3 == 0)
                    grouped.Append(separator);
                grouped.Append(whole[i]);
            }
            whole = grouped.ToString();
        }
        string number = digits > 0 ? whole + point + value.Fraction(digits) : whole;

        (string positive, string negative) = style switch
        {
            NumberStyle.Currency => (CurrencyPositive[format.CurrencyPositivePattern], CurrencyNegative[format.CurrencyNegativePattern]),
            NumberStyle.Percent => (PercentPositive[format.PercentPositivePattern], PercentNegative[format.PercentNegativePattern]),
            _ => ("n", NumberNegative[format.NumberNegativePattern]),
        };
        string pattern = !value.Negative || value.IsZero ? positive
            : parentheses == -1 ? "(" + positive + ")"
            : parentheses == 0 && negative.Contains('(') ? "-" + positive
            : negative;

        string symbol = style switch
        {
            NumberStyle.Currency => format.CurrencySymbol,
            NumberStyle.Percent => format.PercentSymbol,
            _ => "",
        };
        var output = new StringBuilder();
        foreach (char c in pattern)
        {
            output.Append(c switch
            {
                'n' => number,
                '$' or '%' => symbol,
                '-' => format.NegativeSign,
                _ => c.ToString(),
            });
        }
        return output.ToString();
    }

    // ---- FormatDateTime ----

    /// <summary>
    /// Access <c>FormatDateTime(date, [format])</c> (verified vs ACE): 0 the general date, 1 the long date, 2 the short
    /// date, 3 the long time and 4 the 24-hour hh:mm time. A format outside 0-4 is an invalid procedure call even for
    /// a Null date, which is empty text. The date is read as CDate reads it.
    /// </summary>
    private string? FormatDateTime(FunctionCall f)
    {
        if (Optional(f, 1, 0, v => Setting(v, 0, 4)) is not { } format)
            return null;
        if (Evaluate(f.Arguments[0]) is not { } value)
            return "";
        return DateSymbols(RoundToSecond(ToDate(value)), DateTimeFormats[format], DayOfWeek.Sunday, CalendarWeekRule.FirstDay);
    }
}