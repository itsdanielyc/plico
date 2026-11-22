using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Data.Converters;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>
/// One way to write dates everywhere in the interface, in the person's locale: "22 Nov 2026 at 12:13" in English and
/// "22 nov. 2026 à 12:13" in French, and in a queue row the day as a person says it ("Today", "Yesterday", "Mon").
/// </summary>
internal static partial class DisplayDates
{
    /// <summary>
    /// French for the French interface; otherwise the person's own culture when it is English, or British English, so
    /// month names always match the interface's language.
    /// </summary>
    internal static CultureInfo Culture => DesktopText.IsFrench ? CultureInfo.GetCultureInfo("fr")
        : CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "en" ? CultureInfo.CurrentCulture : CultureInfo.GetCultureInfo("en-GB");

    /// <summary>"22 Nov 2026".</summary>
    internal static string Day(DateOnly date) => date.ToString("d MMM yyyy", Culture);

    /// <summary>"22 Nov 2026" for the local day of a moment.</summary>
    internal static string Day(DateTimeOffset moment) => moment.LocalDateTime.ToString("d MMM yyyy", Culture);

    /// <summary>"22 Nov 2026 at 12:13" in local time, "22 nov. 2026 à 12:13" in French.</summary>
    internal static string DayAndTime(DateTimeOffset moment) =>
        DesktopText.Format($"{Day(moment)} at {moment.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture)}");

    /// <summary>"Today", "Yesterday", a weekday within the last week ("Mon"), then "22 Nov 2026".</summary>
    internal static string Relative(DateOnly day, DateOnly today)
    {
        int daysAgo = today.DayNumber - day.DayNumber;
        return daysAgo switch
        {
            0 => DesktopText.Translate("Today"),
            1 => DesktopText.Translate("Yesterday"),
            > 1 and < 7 => Culture.TextInfo.ToTitleCase(day.ToString("ddd", Culture).TrimEnd('.')),
            _ => Day(day)
        };
    }

    /// <summary>
    /// Reads a date typed into a field the way the interface writes it: "21 Nov 2026", or in French "21 nov. 2026",
    /// with the month written out or abbreviated, in any case, with or without accents or the abbreviation's full stop.
    /// It also takes "2026-11-21", as search writes dates, and the person's short date ("21/11/2026"). A day that does
    /// not exist, such as 31 Feb, is refused. O(n) in the length of the text.
    /// </summary>
    internal static bool TryParseDay(string? text, out DateOnly day)
    {
        day = default;
        string value = (text ?? "").Trim();
        if (value.Length == 0) return false;
        var culture = Culture;
        var written = WrittenDay().Match(value);
        if (written.Success)
        {
            int month = MonthNumber(written.Groups["month"].Value, culture);
            return month > 0 && TryCreateDay(int.Parse(written.Groups["year"].Value, CultureInfo.InvariantCulture), month,
                int.Parse(written.Groups["day"].Value, CultureInfo.InvariantCulture), out day);
        }
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)
            || DateOnly.TryParse(value, culture, DateTimeStyles.AllowWhiteSpaces, out day);
    }

    // "21 Nov 2026", "21 nov. 2026", "1 février 2024": a day, a month's name and a four-digit year.
    [GeneratedRegex(@"^(?<day>\d{1,2})\s+(?<month>\p{L}+)\.?,?\s+(?<year>\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex WrittenDay();

    /// <summary>The month a name stands for in the interface's language, written out or abbreviated; 0 when none does.</summary>
    private static int MonthNumber(string name, CultureInfo culture)
    {
        var format = culture.DateTimeFormat;
        const CompareOptions loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        bool Names(string candidate) => candidate.Length > 0 && culture.CompareInfo.Compare(name, candidate.TrimEnd('.'), loose) == 0;
        for (int month = 1; month <= 12; month++)
        {
            if (Names(format.GetMonthName(month)) || Names(format.GetAbbreviatedMonthName(month)) ||
                Names(format.MonthGenitiveNames[month - 1]) || Names(format.AbbreviatedMonthGenitiveNames[month - 1]))
                return month;
        }
        return 0;
    }

    private static bool TryCreateDay(int year, int month, int dayOfMonth, out DateOnly day)
    {
        bool exists = year is >= 1 and <= 9999 && dayOfMonth >= 1 && dayOfMonth <= DateTime.DaysInMonth(year, month);
        day = exists ? new DateOnly(year, month, dayOfMonth) : default;
        return exists;
    }

    /// <summary>"1 page", "6 pages".</summary>
    internal static string Pages(int count) => count == 1 ? DesktopText.Translate("1 page") : DesktopText.Format($"{count} pages");

    /// <summary>
    /// A queue row's meta line: the document's own date when it has one, otherwise the day it was added, then its pages
    /// ("Today · 1 page", "29 Feb 2024 · 6 pages").
    /// </summary>
    internal static string QueueMeta(DocumentSummary document, DateOnly today)
    {
        string day = document.DocumentDate is { } dated ? Day(dated) : Relative(DateOnly.FromDateTime(document.AddedAt.LocalDateTime), today);
        return document.PageCount > 0 ? $"{day} · {Pages(document.PageCount)}" : day;
    }
}

/// <summary>Writes a queue row's meta line from its document.</summary>
internal sealed class QueueMetaConverter : IValueConverter
{
    public static readonly QueueMetaConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DocumentSummary document ? DisplayDates.QueueMeta(document, DateOnly.FromDateTime(DateTime.Today)) : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
