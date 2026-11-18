using Plico.Core;

namespace Plico.Desktop;

/// <summary>Stores the language chosen for new recognition work on this computer.</summary>
internal sealed class OcrLanguagePreference(string directory)
{
    private readonly string _path = Path.Combine(Path.GetFullPath(directory), "ocr-language.txt");

    internal OcrLanguage Load()
    {
        if (!File.Exists(_path)) return OcrLanguage.English;
        var saved = File.ReadAllText(_path).Trim();
        return Enum.TryParse<OcrLanguage>(saved, out var language) && Enum.IsDefined(language)
            ? language
            : OcrLanguage.English;
    }

    internal void Save(OcrLanguage language)
    {
        if (!Enum.IsDefined(language)) throw new ArgumentOutOfRangeException(nameof(language));
        ApplicationSettings.WriteAtomically(_path, language.ToString());
    }
}
