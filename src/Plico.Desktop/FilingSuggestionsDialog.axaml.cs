using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

internal sealed class FilingTagChoice(FilingTag suggestion)
{
    public string Tag => suggestion.Tag;
    public string Label => suggestion.Source == FilingTagSource.Rule ? $"{Tag} · Rule" : $"{Tag} · Reviewed PDFs";
    public bool Selected { get; set; }
}

internal sealed partial class FilingSuggestionsDialog : Window
{
    private sealed record DateChoice(DateOnly? Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed class FilingDraft(FilingSuggestion suggestion)
    {
        internal FilingSuggestion Suggestion { get; } = suggestion;
        internal FilingTagChoice[] Tags { get; } = suggestion.Tags.Select(tag => new FilingTagChoice(tag)).ToArray();
        internal DateOnly? Date { get; set; }
        public override string ToString() => Suggestion.Title;
    }

    private readonly FilingDraft[] _drafts;
    private bool _updatingFields;

    internal FilingSuggestionsDialog(IReadOnlyList<FilingSuggestion> suggestions)
    {
        InitializeComponent();
        _drafts = suggestions.Select(suggestion => new FilingDraft(suggestion)).ToArray();
        SuggestionDocumentBox.ItemsSource = _drafts;
        SuggestionDocumentBox.IsVisible = _drafts.Length > 1;
        SuggestionDocumentBox.SelectedIndex = 0;
    }

    private void Document_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (SuggestionDocumentBox.SelectedItem is not FilingDraft draft) return;
        _updatingFields = true;
        try
        {
            var item = draft.Suggestion;
            var fields = item.Fields;
            SuggestionTitle.Text = item.Title;
            SuggestionTitle.IsVisible = _drafts.Length <= 1;
            var sameDate = new DateChoice(null, item.CurrentDate is { } current ? $"Keep {DisplayDates.Day(current)}" : "Leave document date empty");
            var alternatives = fields.DocumentDates.Where(date => date != item.CurrentDate)
                .Select(date => new DateChoice(date, date.ToString("D", CultureInfo.CurrentCulture))).ToArray();
            SuggestedDateBox.ItemsSource = new[] { sameDate }.Concat(alternatives).ToArray();
            SuggestedDateBox.SelectedItem = SuggestedDateBox.Items.OfType<DateChoice>().First(choice => choice.Value == draft.Date);
            SuggestedDates.IsVisible = alternatives.Length > 0;
            SuggestedTagList.ItemsSource = draft.Tags;
            SuggestedTags.IsVisible = draft.Tags.Length > 0;
            var details = new List<string>();
            if (fields.DueDates.Count > 0)
                details.Add($"Possible due dates  {string.Join(" · ", fields.DueDates.Select(date => DisplayDates.Day(date)))}");
            if (fields.Amount is { } amount) details.Add($"Possible amount  {amount.Value:N2} {amount.Currency}".TrimEnd());
            if (fields.Iban is { } iban) details.Add($"IBAN  {iban}");
            RecognizedFields.Text = string.Join(Environment.NewLine, details);
            RecognizedFields.IsVisible = details.Count > 0;
            NoFilingSuggestions.IsVisible = !SuggestChanges(draft) && details.Count == 0;
            ApplyFilingSuggestionsButton.IsVisible = _drafts.Any(SuggestChanges);
        }
        finally
        {
            _updatingFields = false;
        }
    }

    private static bool SuggestChanges(FilingDraft draft) => draft.Tags.Length > 0 ||
        draft.Suggestion.Fields.DocumentDates.Any(date => date != draft.Suggestion.CurrentDate);

    private void Date_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_updatingFields && SuggestionDocumentBox.SelectedItem is FilingDraft draft && SuggestedDateBox.SelectedItem is DateChoice choice)
            draft.Date = choice.Value;
    }

    private void Apply_Click(object? sender, RoutedEventArgs e) => Close(_drafts
        .Select(item => new FilingSelection(item.Suggestion.DocumentId, item.Date,
            item.Tags.Where(tag => tag.Selected).Select(tag => tag.Tag).ToArray()))
        .Where(selection => selection.DocumentDate.HasValue || selection.Tags.Count > 0).ToArray());

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
