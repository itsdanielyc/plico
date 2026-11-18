using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Plico.Core;

internal static partial class FilingFieldExtractor
{
    internal const int MaximumTextCharacters = 196_608;
    private const int MaximumDateChoices = 6;
    private const int DateLabelContext = 48;
    private const int LabelBonus = 20;
    private const int IbanMinimumLength = 15;
    private const int IbanMaximumLength = 34;
    private const int IbanModulus = 97;
    private const int IbanPrefixLength = 4;
    private const RegexOptions PatternOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
    private const string Day = @"(?<day>[0-9]{1,2})";
    private const string Month = @"(?<name>\p{L}{3,12})";
    private const string Year = @"(?<year>[0-9]{4})";
    private const string Currency = @"(?<currency>EUR|GBP|USD|CAD|CHF|AUD|[€£$])";

    private enum DateRole { Document, Due, Omit }
    private readonly record struct DateCandidate(DateOnly Date, DateRole Role, int Score, int Position);

    // O(n + d log d) time and O(d) scratch for n bounded text characters and d date matches.
    internal static FilingFields Read(string text, CultureInfo culture)
    {
        text = text.Length <= MaximumTextCharacters ? text : text[..MaximumTextCharacters];
        var dates = FindDates(text, culture);
        return new(
            BestDates(dates, DateRole.Document),
            BestDates(dates, DateRole.Due),
            FindAmount(text, culture),
            FindIban(text));
    }

    internal static CultureInfo DateCulture(OcrLanguage language, CultureInfo locale)
    {
        string fallback = language switch
        {
            OcrLanguage.French => "fr-FR",
            OcrLanguage.German => "de-DE",
            OcrLanguage.Spanish => "es-ES",
            _ => "en-GB"
        };
        return locale.TwoLetterISOLanguageName.Equals(fallback[..2], StringComparison.OrdinalIgnoreCase)
            ? locale : CultureInfo.GetCultureInfo(fallback);
    }

    private static DateOnly[] BestDates(List<DateCandidate> dates, DateRole role) => dates
        .Where(candidate => candidate.Role == role)
        .OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Position)
        .Select(candidate => candidate.Date).Distinct().Take(MaximumDateChoices).ToArray();

    private static List<DateCandidate> FindDates(string text, CultureInfo culture)
    {
        var dates = new List<DateCandidate>();
        string pattern = culture.DateTimeFormat.ShortDatePattern;
        bool monthFirst = pattern.IndexOf('M') < pattern.IndexOf('d');
        foreach (Match match in NumericDates().Matches(text))
        {
            if (match.Groups["separator"].ValueSpan[0] != match.Groups["separator2"].ValueSpan[0]) continue;
            var first = match.Groups["first"].ValueSpan;
            var last = match.Groups["last"].ValueSpan;
            int a = Number(first), b = Number(match.Groups["middle"].ValueSpan), c = Number(last);
            if (first.Length == 4)
            {
                if (last.Length <= 2) Add(a, b, c, score: 5, match.Index);
            }
            else if (last.Length == 4)
            {
                int primaryMonth = monthFirst ? a : b, primaryDay = monthFirst ? b : a;
                Add(c, primaryMonth, primaryDay, score: 4, match.Index);
                if (primaryDay != primaryMonth) Add(c, primaryDay, primaryMonth, score: 0, match.Index);
            }
        }

        var months = new Dictionary<string, int>(StringComparer.Create(culture, ignoreCase: true));
        for (int month = 1; month <= 12; month++)
        {
            months.TryAdd(culture.DateTimeFormat.GetMonthName(month).TrimEnd('.'), month);
            months.TryAdd(culture.DateTimeFormat.GetAbbreviatedMonthName(month).TrimEnd('.'), month);
        }
        ReadNamedDates(DayFirstDates());
        ReadNamedDates(MonthFirstDates());
        return dates;

        void ReadNamedDates(Regex regex)
        {
            foreach (Match match in regex.Matches(text))
            {
                if (months.TryGetValue(match.Groups["name"].Value, out int month))
                    Add(Number(match.Groups["year"].ValueSpan), month, Number(match.Groups["day"].ValueSpan), 6, match.Index);
            }
        }

        void Add(int year, int month, int day, int score, int index)
        {
            if (year is < 1 or > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return;
            int beginning = Math.Max(0, index - DateLabelContext);
            int lineBreak = index == 0 ? -1 : text.LastIndexOf('\n', index - 1, index - beginning);
            beginning = Math.Max(beginning, lineBreak + 1);
            string context = text[beginning..index];
            var role = DateRole.Document;
            foreach (Match label in DateLabels().Matches(context))
            {
                role = label.Groups["due"].Success ? DateRole.Due
                    : label.Groups["omit"].Success ? DateRole.Omit : DateRole.Document;
                score += LabelBonus;
            }
            if (role != DateRole.Omit) dates.Add(new(new DateOnly(year, month, day), role, score, index));
        }
    }

    private static int Number(ReadOnlySpan<char> text) => int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);

    private static FilingAmount? FindAmount(string text, CultureInfo culture)
    {
        foreach (Match match in Amounts().Matches(text))
        {
            string raw = match.Groups["amount"].Value;
            int period = raw.LastIndexOf('.');
            int comma = raw.LastIndexOf(',');
            int lastSeparator = Math.Max(period, comma);
            // A final two-digit group is a decimal amount in either of the supported separator orders.
            bool decimalAmount = lastSeparator >= 0 && (raw.Length - lastSeparator == 3 ||
                (raw.Length - lastSeparator == 2 && raw[lastSeparator].ToString() == culture.NumberFormat.NumberDecimalSeparator));
            var normalized = new StringBuilder(raw.Length);
            for (int index = 0; index < raw.Length; index++)
            {
                char character = raw[index];
                if (char.IsAsciiDigit(character) || character == '-') normalized.Append(character);
                else if (decimalAmount && index == lastSeparator) normalized.Append('.');
            }
            if (!decimal.TryParse(normalized.ToString(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out decimal value)) continue;
            string? currency = match.Groups["currency"].Value.ToUpperInvariant() switch
            {
                "€" => "EUR",
                "£" => "GBP",
                "" => null,
                var code => code
            };
            return new(value, currency);
        }
        return null;
    }

    // O(n) time across the text. Each possible checksum examines at most 34 characters.
    private static string? FindIban(string text)
    {
        Span<char> account = stackalloc char[IbanMaximumLength];
        foreach (Match match in IbanStart().Matches(text))
        {
            int length = 0;
            for (int index = match.Index; index < text.Length && length < account.Length; index++)
            {
                char character = text[index];
                if (char.IsAsciiLetterOrDigit(character))
                {
                    account[length++] = char.ToUpperInvariant(character);
                    continue;
                }
                if (character is not (' ' or '\u00a0' or '\u202f')) break;
                if (length >= IbanPrefixLength && length % IbanPrefixLength != 0) break;
            }
            int expectedLength = account[..Math.Min(length, 2)] switch
            {
                "AT" or "BA" or "EE" or "KZ" or "LT" or "LU" or "XK" => 20,
                "BE" => 16,
                "CH" or "HR" or "LI" or "LV" => 21,
                "DE" or "GB" or "IE" or "BG" or "ME" or "RS" => 22,
                "ES" or "RO" or "SE" or "SK" or "CZ" or "MD" or "TN" or "SA" => 24,
                "FR" or "IT" or "MC" or "SM" or "GR" => 27,
                "CY" or "HU" or "PL" => 28,
                "PT" => 25,
                "NL" or "FI" or "DK" or "FO" or "GL" => 18,
                "NO" => 15,
                _ => length
            };
            if (expectedLength < IbanMinimumLength || expectedLength > length) continue;
            var candidate = account[..expectedLength];
            if (HasIbanChecksum(candidate)) return candidate.ToString();
        }
        return null;
    }

    private static bool HasIbanChecksum(ReadOnlySpan<char> account)
    {
        int remainder = 0;
        for (int index = 0; index < account.Length; index++)
        {
            char character = account[(index + IbanPrefixLength) % account.Length];
            remainder = (char.IsAsciiDigit(character) ? remainder * 10 + character - '0' : remainder * 100 + character - 'A' + 10)
                % IbanModulus;
        }
        return remainder == 1;
    }

    [GeneratedRegex(@"\b(?<first>[0-9]{1,4})(?<separator>[-./])(?<middle>[0-9]{1,2})(?<separator2>[-./])(?<last>[0-9]{1,4})\b", PatternOptions)]
    private static partial Regex NumericDates();

    [GeneratedRegex(@"\b" + Day + @"(?:st|nd|rd|th|er|\.)?[ \t]+(?:de[ \t]+)?" + Month +
        @"\.?[ \t]+(?:de[ \t]+)?" + Year + @"\b", PatternOptions)]
    private static partial Regex DayFirstDates();

    [GeneratedRegex(@"\b" + Month + @"\.?[ \t]+" + Day + @"(?:st|nd|rd|th)?,?[ \t]+" + Year + @"\b", PatternOptions)]
    private static partial Regex MonthFirstDates();

    [GeneratedRegex(@"\b(?:(?<due>due(?: date)?|payable by|échéance|echeance|fällig(?:keit)?|faellig|vencimiento|fecha límite)|(?<omit>born|date of birth|dob|naissance|geburtsdatum|nacimiento)|invoice date|issued|date de facture|rechnungsdatum|fecha de factura)\b", PatternOptions)]
    private static partial Regex DateLabels();

    [GeneratedRegex(@"\b(?:total(?:[ \t]+(?:due|ttc|amount|a pagar))?|amount due|montant(?: ttc)?|gesamtbetrag|endbetrag|importe total)\b[ \t]*[:=\-]?[ \t]*" +
        Currency + @"?[ \t]*(?<amount>-?[0-9](?:[0-9.,'\u00a0\u202f ]{0,20}[0-9])?)[ \t]*" + Currency + "?", PatternOptions)]
    private static partial Regex Amounts();

    [GeneratedRegex(@"\b[A-Z]{2}[0-9]{2}", PatternOptions)]
    private static partial Regex IbanStart();
}
