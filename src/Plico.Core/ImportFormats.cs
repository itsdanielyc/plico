namespace Plico.Core;

/// <summary>Registers additional input formats without extending the library's PDF storage contract.</summary>
internal static class ImportFormats
{
    internal const string PlainText = ".txt";
    internal const string Markdown = ".md";
    internal const string InternetMessage = ".eml";
    internal const string OutlookMessage = ".msg";
    internal const string PortableImage = ".png";

    private static readonly HashSet<string> OfficeFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".odt", ".rtf", ".xls", ".xlsx", ".ods", ".ppt", ".pptx", ".odp"
    };

    internal static bool IsOffice(string extension) => OfficeFormats.Contains(extension);
    internal static bool IsText(string extension) => extension is PlainText or Markdown or ".markdown";
    internal static bool IsEmail(string extension) => extension is InternetMessage or OutlookMessage;
    internal static bool IsHeif(string extension) => extension is ".heic" or ".heif";
}
