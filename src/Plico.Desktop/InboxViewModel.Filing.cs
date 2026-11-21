using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [RelayCommand]
    private Task SuggestFilingAsync() => SelectedDocument is { } document
        ? ReviewFilingAsync([document.Id]) : Task.CompletedTask;

    internal Task SuggestSelectedFilingAsync() => HasBatchSelection
        ? ReviewFilingAsync(_batchSelection.Select(document => document.Id).ToArray()) : Task.CompletedTask;

    [RelayCommand]
    private Task ManageFilingRulesAsync() => RunOperationAsync(DesktopText.Translate("Reading tag rules…"), async token =>
    {
        var library = RequireLibrary();
        var rules = await Task.Run(library.GetFilingRules, token);
        await _dialogs.ShowFilingRulesAsync(rules, library.SaveFilingRule, library.DeleteFilingRule);
        RequestSuggestions(SelectedDocument, refresh: true);
        Status = DesktopText.Translate("Tag rules are ready to use in Filing suggestions.");
    });

    private Task ReviewFilingAsync(IReadOnlyList<long> ids)
    {
        var language = RecognitionLanguage;
        var locale = CultureInfo.CurrentCulture;
        return RunOperationAsync(DesktopText.Translate("Reading filing suggestions…"), async token =>
        {
            var library = RequireLibrary();
            var suggestions = await Task.Run(() => library.SuggestFiling(ids, language, locale, token), token);
            var selections = await _dialogs.ShowFilingSuggestionsAsync(suggestions);
            if (selections is not { Count: > 0 })
            {
                Status = DesktopText.Translate("Closed filing suggestions.");
                return;
            }
            await Task.Run(() => library.ApplyFilingSuggestions(selections, token), token);
            Tags = library.GetTags();
            LoadResults(retainLoadedResults: true);
            Status = DesktopText.Format($"Applied filing suggestions to {DocumentCount(selections.Count)}.");
        });
    }
}
