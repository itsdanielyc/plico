using System.Globalization;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>
/// Words that follow the person's platform: "this PC" and "Match Windows" on Windows, "this computer" and "Match
/// the system" elsewhere. Each variant has its own catalog row, so French follows the same rule.
/// </summary>
internal static class PlatformText
{
    private static bool OnWindows => OperatingSystem.IsWindows();

    public static string MatchSystemTheme => DesktopText.ByKey(OnWindows ? "MatchWindows" : "MatchSystem");
    public static string WelcomeLede => DesktopText.ByKey(OnWindows ? "WelcomeHeroLedePc" : "WelcomeHeroLedeComputer");
    public static string PrivacyLine => DesktopText.ByKey(OnWindows ? "PrivacyShortPc" : "PrivacyShortComputer");
    public static string LocalModelsHelp => DesktopText.ByKey(OnWindows ? "LocalModelsHelpPc" : "LocalModelsHelpComputer");

    /// <summary>
    /// The empty date field's help while it holds the suggested date: the date, and the key that files it, named as the
    /// keyboard names it ("Enter", "Return" on a Mac).
    /// </summary>
    internal static string SuggestedDateHelp(string date) => string.Format(CultureInfo.InvariantCulture,
        DesktopText.ByKey(OperatingSystem.IsMacOS() ? "SuggestedDateFieldHelpReturn" : "SuggestedDateFieldHelpEnter"), date);

    /// <summary>"English · read on this PC": the recognition language and where recognition runs.</summary>
    internal static string RecognitionPlace(OcrLanguage language) =>
        $"{LanguageName(language)} · {DesktopText.Translate(OnWindows ? "read on this\u00a0PC" : "read on this\u00a0computer")}";

    /// <summary>A recognition language's name in the interface's language: "German", "Allemand".</summary>
    internal static string LanguageName(OcrLanguage language) => DesktopText.Translate(language.ToString());
}

/// <summary>Names a recognition language in the interface's language, in Settings' list as on the status card.</summary>
internal sealed class LanguageNameConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly LanguageNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is OcrLanguage language ? PlatformText.LanguageName(language) : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
