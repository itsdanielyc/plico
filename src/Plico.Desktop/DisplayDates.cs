using System.Globalization;
using Avalonia.Data.Converters;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>
/// One way to write dates everywhere in the interface, in the person's locale: "22 Nov 2026, 12:13" in English and
/// "22 nov. 2026, 12:13" in French, and in a queue row the day as a person says it ("Today", "Yesterday", "Mon").
/// </summary>
internal static class DisplayDates
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

    /// <summary>"22 Nov 2026, 12:13" in local time.</summary>
    internal static string DayAndTime(DateTimeOffset moment) => moment.LocalDateTime.ToString("d MMM yyyy, HH:mm", Culture);

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
