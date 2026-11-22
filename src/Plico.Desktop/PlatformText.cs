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

    /// <summary>"English · read on this PC": the recognition language and where recognition runs.</summary>
    internal static string RecognitionPlace(OcrLanguage language) =>
        $"{DesktopText.Translate(language.ToString())} · {DesktopText.Translate(OnWindows ? "read on this\u00a0PC" : "read on this\u00a0computer")}";
}
