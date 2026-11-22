using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>A tag Plico would add, shown dashed on the filing card until accepted.</summary>
internal sealed record SuggestedTag(string Label, FilingTagSource Source)
{
    public string Reason => DesktopText.Translate(Source == FilingTagSource.Rule
        ? "From a tag rule" : "Like documents you have reviewed");
}

internal sealed partial class InboxViewModel
{
    private static readonly TimeSpan SuggestionDelay = TimeSpan.FromMilliseconds(180);
    private readonly DispatcherTimer _suggestionTimer = new() { Interval = SuggestionDelay };
    private CancellationTokenSource? _suggestionRequest;
    private DocumentSummary? _suggestionDocument;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestions), nameof(HasSuggestedTags), nameof(SuggestionCount), nameof(SuggestionSummary), nameof(SuggestionReason))]
    private IReadOnlyList<SuggestedTag> _suggestedTags = [];

    public bool HasSuggestedTags => SuggestedTags.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestedDate), nameof(SuggestedDateLabel), nameof(HasSuggestions),
        nameof(SuggestionCount), nameof(SuggestionSummary), nameof(SuggestionReason))]
    private DateOnly? _suggestedDate;

    /// <summary>The document date read from the page, offered only while the document has no date.</summary>
    public bool HasSuggestedDate => SuggestedDate is not null && SelectedDocument is { DocumentDate: null };
    public string SuggestedDateLabel => SuggestedDate is { } date ? DisplayDates.Day(date) : "";
    public bool HasSuggestions => SuggestionCount > 0;
    public int SuggestionCount => SuggestedTags.Count + (HasSuggestedDate ? 1 : 0);
    public string SuggestionSummary => SuggestionCount == 1
        ? DesktopText.Translate("1 suggestion") : DesktopText.Format($"{SuggestionCount} suggestions");

    /// <summary>Where the suggestions came from, in one sentence.</summary>
    public string SuggestionReason
    {
        get
        {
            var sources = new List<string>(3);
            if (SuggestedTags.Any(tag => tag.Source == FilingTagSource.Rule)) sources.Add(DesktopText.Translate("your tag rules"));
            if (SuggestedTags.Any(tag => tag.Source == FilingTagSource.ReviewedDocuments)) sources.Add(DesktopText.Translate("documents you have reviewed"));
            if (HasSuggestedDate) sources.Add(DesktopText.Translate("the date printed on the page"));
            return sources.Count switch
            {
                0 => "",
                1 => DesktopText.Format($"From {sources[0]}."),
                2 => DesktopText.Format($"From {sources[0]} and {sources[1]}."),
                _ => DesktopText.Format($"From {sources[0]}, {sources[1]} and {sources[2]}.")
            };
        }
    }

    /// <summary>Accepts every suggestion on the open document in one catalog transaction.</summary>
    [RelayCommand]
    private void AcceptSuggestions() => ApplySuggestions(SuggestedTags.Select(tag => tag.Label).ToArray(), HasSuggestedDate);

    [RelayCommand]
    private void AcceptSuggestedTag(SuggestedTag? tag)
    {
        if (tag is not null) ApplySuggestions([tag.Label], includeDate: false);
    }

    [RelayCommand]
    private void AcceptSuggestedDate() => ApplySuggestions([], HasSuggestedDate);

    private void ApplySuggestions(IReadOnlyList<string> tags, bool includeDate)
    {
        if (!CanWork || SelectedDocument is not { } document || (tags.Count == 0 && !includeDate)) return;
        try
        {
            var library = RequireLibrary();
            // Filing selections add tags and leave existing assignments in place.
            var selection = new FilingSelection(document.Id, includeDate ? SuggestedDate : null, tags);
            library.ApplyFilingSuggestions([selection]);
            int accepted = tags.Count + (includeDate ? 1 : 0);
            Tags = library.GetTags();
            LoadResults(document.Id, retainLoadedResults: true);
            Status = accepted == 1 ? DesktopText.Translate("Suggestion accepted.") : DesktopText.Format($"{accepted} suggestions accepted.");
        }
        catch (Exception error)
        {
            Status = error.Message;
        }
    }

    private void RequestSuggestions(DocumentSummary? document, bool refresh = false)
    {
        if (document is null || document.Reviewed || _library is null)
        {
            ClearSuggestions();
            return;
        }
        if (!refresh && _suggestionDocument is { } previous && previous.Id == document.Id &&
            previous.ContentRevision == document.ContentRevision && previous.Title == document.Title &&
            previous.DocumentDate == document.DocumentDate && previous.Tags.SequenceEqual(document.Tags))
            return;
        ClearSuggestions();
        _suggestionDocument = document;
        _suggestionTimer.Start();
    }

    private void StartSuggestionRequest()
    {
        _suggestionTimer.Stop();
        if (_library is not { } library || SelectedDocument is not { Reviewed: false } document || document.Id != _suggestionDocument?.Id) return;
        var request = new CancellationTokenSource();
        _suggestionRequest = request;
        _ = LoadSuggestionsAsync(library, document, RecognitionLanguage, request);
    }

    // One catalog read per settled selection; earlier requests are cancelled before they apply.
    private async Task LoadSuggestionsAsync(InboxLibrary library, DocumentSummary document, OcrLanguage language,
        CancellationTokenSource request)
    {
        try
        {
            var locale = CultureInfo.CurrentCulture;
            var token = request.Token;
            var found = await TrackBackgroundRead(() => library.SuggestFiling([document.Id], language, locale, token));
            if (request.IsCancellationRequested || SelectedDocument?.Id != document.Id || found.Count == 0) return;
            var suggestion = found[0];
            var applied = SelectedDocument.Tags.Select(InboxLibrary.GetTagKey).ToHashSet(StringComparer.Ordinal);
            SuggestedTags = suggestion.Tags.Where(tag => applied.Add(InboxLibrary.GetTagKey(tag.Tag)))
                .Select(tag => new SuggestedTag(tag.Tag, tag.Source)).ToArray();
            SuggestedDate = suggestion.Fields.DocumentDates.Count > 0 ? suggestion.Fields.DocumentDates[0] : null;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Suggestions are optional; the fields stay editable when the catalog cannot be read.
            if (_suggestionRequest == request) ClearSuggestions();
        }
        finally
        {
            if (_suggestionRequest == request) _suggestionRequest = null;
            request.Dispose();
        }
    }

    private void ClearSuggestions()
    {
        _suggestionTimer.Stop();
        _suggestionRequest?.Cancel();
        _suggestionRequest = null;
        _suggestionDocument = null;
        SuggestedTags = [];
        SuggestedDate = null;
    }
}
