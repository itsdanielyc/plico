using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private static readonly IReadOnlyList<OcrLanguage> AvailableRecognitionLanguages = Enum.GetValues<OcrLanguage>();
    private Action<OcrLanguage>? _rememberRecognitionLanguage;

    [ObservableProperty] private OcrLanguage _recognitionLanguage = OcrLanguage.English;

    public IReadOnlyList<OcrLanguage> RecognitionLanguages => AvailableRecognitionLanguages;
    public string SearchableCopyAction => HasNoSearchableText ? "Create searchable copy" : "Create OCR copy";

    [RelayCommand]
    private Task CreateSearchableCopyAsync()
    {
        if (SelectedDocument is not { } scan) return Task.CompletedTask;
        var language = RecognitionLanguage;
        return RunOperationAsync("Creating a searchable copy…", async token =>
        {
            var library = RequireLibrary();
            var result = await Task.Run(() => library.CreateSearchableCopyAsync(scan.Id, token, language), token);
            Navigate(inboxOnly: !result.Document.Reviewed, preferredId: result.Document.Id);
            Status = result.Added ? "Searchable copy added to inbox." : "Searchable copy already in the library.";
        });
    }

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
