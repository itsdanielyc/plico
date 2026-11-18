using System.Globalization;
using System.Text;

namespace Plico.Core;

internal static class CalendarFile
{
    private const int MaximumLineOctets = 75;
    private const string CalendarDateFormat = "yyyyMMdd";
    private const string TimestampFormat = "yyyyMMdd'T'HHmmss'Z'";

    // O(total encoded text) time and O(one event) additional space.
    internal static void Write(string path, IEnumerable<CalendarDeadline> deadlines, DateTimeOffset exported, CancellationToken token)
    {
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false)) { NewLine = "\r\n" };
        writer.WriteLine("BEGIN:VCALENDAR");
        writer.WriteLine("VERSION:2.0");
        writer.WriteLine("PRODID:-//Plico//Document reminders//EN");
        string timestamp = exported.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
        foreach (var item in deadlines)
        {
            token.ThrowIfCancellationRequested();
            string title = Escape($"{item.Title} ({Label(item.Deadline.Kind)})");
            writer.WriteLine("BEGIN:VEVENT");
            writer.WriteLine($"UID:{item.Uid}@plico.local");
            writer.WriteLine($"DTSTAMP:{timestamp}");
            writer.WriteLine($"DTSTART;VALUE=DATE:{item.Deadline.Date.ToString(CalendarDateFormat, CultureInfo.InvariantCulture)}");
            WriteFolded(writer, $"SUMMARY:{title}");
            if (item.Deadline.LeadDays is { } days)
            {
                writer.WriteLine("BEGIN:VALARM");
                writer.WriteLine("ACTION:DISPLAY");
                writer.WriteLine(days == 0 ? "TRIGGER:PT0S" : $"TRIGGER:-P{days.ToString(CultureInfo.InvariantCulture)}D");
                WriteFolded(writer, $"DESCRIPTION:{title}");
                writer.WriteLine("END:VALARM");
            }
            writer.WriteLine("END:VEVENT");
        }
        writer.WriteLine("END:VCALENDAR");
    }

    private static string Label(DeadlineKind kind) => kind switch
    {
        DeadlineKind.KeepUntil => "Keep until",
        _ => kind.ToString()
    };

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\r\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\n", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal)
        .Replace(";", "\\;", StringComparison.Ordinal);

    private static void WriteFolded(TextWriter writer, string line)
    {
        int octets = 0;
        Span<char> buffer = stackalloc char[2];
        foreach (var rune in line.EnumerateRunes())
        {
            if (octets + rune.Utf8SequenceLength > MaximumLineOctets)
            {
                writer.WriteLine();
                writer.Write(' ');
                octets = 1;
            }
            int written = rune.EncodeToUtf16(buffer);
            writer.Write(buffer[..written]);
            octets += rune.Utf8SequenceLength;
        }
        writer.WriteLine();
    }
}
