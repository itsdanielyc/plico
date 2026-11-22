using System.Text.Json;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private OcrProcessingOptions _ocrProcessing = OcrProcessingOptions.Default;
    private Action<OcrProcessingOptions>? _saveOcrProcessing;

    public bool CanUseRapidOcr => OcrProcessingOptions.RapidOcrAvailable;

    /// <summary>The engine is chosen while Plico is free, as the language and orientation are: work under way keeps its engine.</summary>
    public bool CanChangeRapidOcr => CanUseRapidOcr && !IsBusy;

    public bool UseRapidOcr
    {
        get => _ocrProcessing.Engine == OcrEngineKind.RapidOcr;
        set
        {
            if (value && !CanUseRapidOcr) return;
            ChangeOcrProcessing(_ocrProcessing with { Engine = value ? OcrEngineKind.RapidOcr : OcrEngineKind.Tesseract });
        }
    }

    public bool AutoOrientPages
    {
        get => _ocrProcessing.AutoOrient;
        set => ChangeOcrProcessing(_ocrProcessing with { AutoOrient = value });
    }

    internal void InitializeOcrProcessing(OcrProcessingPreference preference)
    {
        try
        {
            ChangeOcrProcessing(preference.Load());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            AddStartupNotice(DesktopText.Format($"Couldn't load the recognition settings. {error.Message}"));
        }
        _saveOcrProcessing = preference.Save;
    }

    private void ChangeOcrProcessing(OcrProcessingOptions options)
    {
        if (options == _ocrProcessing) return;
        if (IsBusy)
        {
            // Refused while work runs: a switch that was flipped anyway goes back to what Plico uses.
            OnPropertyChanged(nameof(UseRapidOcr));
            OnPropertyChanged(nameof(AutoOrientPages));
            return;
        }
        _library?.ConfigureOcr(options);
        _ocrProcessing = options;
        OnPropertyChanged(nameof(UseRapidOcr));
        OnPropertyChanged(nameof(AutoOrientPages));
        try
        {
            _saveOcrProcessing?.Invoke(options);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Fail($"Using the OCR settings for this session. {error.Message}");
        }
    }
}
