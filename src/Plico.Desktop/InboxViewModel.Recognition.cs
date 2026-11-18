using CommunityToolkit.Mvvm.ComponentModel;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private static readonly IReadOnlyList<OcrLanguage> AvailableRecognitionLanguages = Enum.GetValues<OcrLanguage>();
    private Action<OcrLanguage>? _rememberRecognitionLanguage;

    [ObservableProperty] private OcrLanguage _recognitionLanguage = OcrLanguage.English;

    public IReadOnlyList<OcrLanguage> RecognitionLanguages => AvailableRecognitionLanguages;

    internal void InitializeRecognitionLanguage(Func<OcrLanguage> load, Action<OcrLanguage> save)
    {
        try
        {
            RecognitionLanguage = load();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status = error.Message;
        }
        _rememberRecognitionLanguage = save;
    }

    partial void OnRecognitionLanguageChanged(OcrLanguage value)
    {
        try
        {
            _rememberRecognitionLanguage?.Invoke(value);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status = $"Using {value} OCR for this session. {error.Message}";
        }
    }
}
