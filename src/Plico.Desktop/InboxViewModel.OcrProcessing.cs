using System.Text.Json;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private OcrProcessingOptions _ocrProcessing = OcrProcessingOptions.Default;
    private Action<OcrProcessingOptions>? _saveOcrProcessing;

    public bool CanUseRapidOcr => OcrProcessingOptions.RapidOcrAvailable;

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
            AddStartupNotice(error.Message);
        }
        _saveOcrProcessing = preference.Save;
    }

    private void ChangeOcrProcessing(OcrProcessingOptions options)
    {
        if (options == _ocrProcessing || IsBusy) return;
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
            Status = $"Using the OCR settings for this session. {error.Message}";
        }
    }
}
