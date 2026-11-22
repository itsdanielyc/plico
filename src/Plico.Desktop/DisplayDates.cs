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
        : CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "en" ? CultureInfo.CurrentCulture : EnglishFallback.Value;

    /// <summary>
    /// British English with every month's short name in three letters. ICU writes September "Sept" in en-GB, so without
    /// this the dates of a person whose culture is not English (Linux's C locale gives the invariant culture) would
    /// read "3 Sept 2026" beside "3 Aug 2026", unlike the interface's other places and other platforms.
    /// </summary>
    private static readonly Lazy<CultureInfo> EnglishFallback = new(() =>
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("en-GB").Clone();
        var format = culture.DateTimeFormat;
        string[] shortNames = format.MonthNames.Select(name => name.Length > 3 ? name[..3] : name).ToArray();
        format.AbbreviatedMonthNames = shortNames;
        format.AbbreviatedMonthGenitiveNames = shortNames;
        return CultureInfo.ReadOnly(culture);
    });

    /// <summary>The clock the queue's days are read from: the system's, or a test's.</summary>
    internal static TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>Today in the person's time zone, by <see cref="Clock"/>.</summary>
    internal static DateOnly Today => DateOnly.FromDateTime(Clock.GetLocalNow().DateTime);

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
    /// It also takes "2026-11-21", as search writes dates, and the person's short date written in full ("21/11/2026").
    /// Every form needs the day, the month and a four-digit year, so a date still being typed ("1 Jan 202", "21 Nov",
    /// "5 6") is never read as a whole one; a day that does not exist, such as 31 Feb, is refused too. Fields save as
    /// they lose the keyboard, so this is what keeps a half-typed date from being saved. O(n) in the length of the text.
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
            return month > 0 && TryCreateDay(Number(written.Groups["year"].Value), month, Number(written.Groups["day"].Value), out day);
        }
        var numeric = NumericDay().Match(value);
        if (!numeric.Success) return false;
        int first = Number(numeric.Groups["first"].Value), second = Number(numeric.Groups["second"].Value);
        if (numeric.Groups["isoYear"].Success)
            return numeric.Groups["separator"].Value == "-" && TryCreateDay(Number(numeric.Groups["isoYear"].Value), first, second, out day);
        // The person's short date: day and month in the order their own short date puts them.
        bool monthFirst = culture.DateTimeFormat.ShortDatePattern.TrimStart().StartsWith('M');
        return TryCreateDay(Number(numeric.Groups["year"].Value), monthFirst ? first : second, monthFirst ? second : first, out day);
    }

    private static int Number(string digits) => int.Parse(digits, CultureInfo.InvariantCulture);

    // "21 Nov 2026", "21 nov. 2026", "1 février 2024": a day, a month's name and a four-digit year.
    [GeneratedRegex(@"^(?<day>\d{1,2})\s+(?<month>\p{L}+)\.?,?\s+(?<year>[1-9]\d{3})$", RegexOptions.CultureInvariant)]
    private static partial Regex WrittenDay();

    // "2026-11-21" as search writes it, or a short date with its year in full: "21/11/2026", "21.11.2026", "11-21-2026".
    [GeneratedRegex(@"^(?:(?<isoYear>[1-9]\d{3})(?<separator>-)(?<first>\d{1,2})-(?<second>\d{1,2})|(?<first>\d{1,2})(?<separator>[/.-])(?<second>\d{1,2})\k<separator>(?<year>[1-9]\d{3}))$",
        RegexOptions.CultureInvariant)]
    private static partial Regex NumericDay();

    /// <summary>The shortest start of a month's name that is read as that month, when no other month's name starts so.</summary>
    private const int ShortestMonthStart = 3;

    /// <summary>
    /// The month a name stands for in the interface's language; 0 when none does. It takes the name written out or as
    /// the culture abbreviates it, and any start of at least three letters that only one month's name has, so
    /// September is "Sep" or "Sept" whichever way the culture abbreviates it ("sept." and "juil" in French too), while
    /// "Ju" or the French "jui" (juin, juillet) is read as neither. O(1): twelve months, four names each.
    /// </summary>
    private static int MonthNumber(string name, CultureInfo culture)
    {
        var format = culture.DateTimeFormat;
        const CompareOptions loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        bool Names(string candidate) => candidate.Length > 0 && culture.CompareInfo.Compare(name, candidate.TrimEnd('.'), loose) == 0;
        bool Starts(string candidate) => candidate.Length > 0 && culture.CompareInfo.IsPrefix(candidate, name, loose);
        int started = 0;
        for (int month = 1; month <= 12; month++)
        {
            if (Names(format.GetMonthName(month)) || Names(format.GetAbbreviatedMonthName(month)) ||
                Names(format.MonthGenitiveNames[month - 1]) || Names(format.AbbreviatedMonthGenitiveNames[month - 1]))
                return month;
            if (name.Length >= ShortestMonthStart && (Starts(format.GetMonthName(month)) || Starts(format.MonthGenitiveNames[month - 1])))
                started = started == 0 ? month : -1;
        }
        return Math.Max(started, 0);
    }

    private static bool TryCreateDay(int year, int month, int dayOfMonth, out DateOnly day)
    {
        bool exists = year is >= 1 and <= 9999 && month is >= 1 and <= 12 && dayOfMonth >= 1 && dayOfMonth <= DateTime.DaysInMonth(year, month);
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
        value is DocumentSummary document ? DisplayDates.QueueMeta(document, DisplayDates.Today) : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
