using System.Text.Json;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>Stores the desktop's offline engine and page orientation choices.</summary>
internal sealed class OcrProcessingPreference(string directory)
{
    private readonly string _path = Path.Combine(Path.GetFullPath(directory), "ocr-processing.json");

    internal OcrProcessingOptions Load()
    {
        if (!File.Exists(_path)) return OcrProcessingOptions.Default;
        var options = JsonSerializer.Deserialize<OcrProcessingOptions>(File.ReadAllText(_path)) ?? OcrProcessingOptions.Default;
        if (!Enum.IsDefined(options.Engine) ||
            (options.Engine == OcrEngineKind.RapidOcr && !OcrProcessingOptions.RapidOcrAvailable))
        {
            return options with { Engine = OcrEngineKind.Tesseract };
        }
        return options;
    }

    internal void Save(OcrProcessingOptions options) =>
        ApplicationSettings.WriteAtomically(_path, JsonSerializer.Serialize(options));
}
