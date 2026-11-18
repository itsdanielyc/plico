using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Plico.Core;
using static Plico.Desktop.DocumentMetadataDialog;

namespace Plico.Desktop;

internal sealed class LocalModelSettingsDialog : Window
{
    internal LocalModelSettingsDialog(LocalModelSettings current)
    {
        Title = DesktopText.Translate("Local models");
        Width = 530;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var enabled = new CheckBox { Content = DesktopText.Translate("Enable local model requests"), IsChecked = current.Enabled };
        var address = CreateTextBox(DesktopText.Translate("Server address"), current.Endpoint);
        var chat = CreateTextBox(DesktopText.Translate("Chat model"), current.ChatModel, DesktopText.Translate("Installed model name"));
        var embeddings = CreateTextBox(DesktopText.Translate("Embedding model"), current.EmbeddingModel, DesktopText.Translate("Installed model name"));
        var prefixes = new CheckBox { Content = DesktopText.Translate("Embedding model uses query and passage prefixes"), IsChecked = current.UseEmbeddingPrefixes };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var body = new StackPanel { Spacing = 8, Children = { enabled } };
        AddField(body, DesktopText.Translate("Loopback server address"), address);
        AddField(body, DesktopText.Translate("Chat model"), chat);
        AddField(body, DesktopText.Translate("Embedding model"), embeddings);
        body.Children.Add(prefixes);
        body.Children.Add(status);
        var cancel = new Button { Content = DesktopText.Translate("Cancel"), IsCancel = true };
        cancel.Click += (_, _) => Close();
        var save = new Button { Content = DesktopText.Translate("Save"), IsDefault = true };
        save.Click += (_, _) =>
        {
            try
            {
                Close(new LocalModelSettings(enabled.IsChecked == true, address.Text ?? "", chat.Text ?? "", embeddings.Text ?? "",
                    prefixes.IsChecked == true).Validate());
            }
            catch (ArgumentException error) { status.Text = error.Message; }
        };
        var panel = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(20), RowSpacing = 12 };
        AddRow(panel, 0, new ScrollViewer { Content = body });
        AddRow(panel, 1, new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right, Orientation = Orientation.Horizontal,
            Spacing = 8, Children = { cancel, save }
        });
        Content = panel;
    }
}

internal sealed class SemanticQueryDialog : Window
{
    internal SemanticQueryDialog(string initial)
    {
        Title = DesktopText.Translate("Semantic search");
        Width = 520;
        Height = 230;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var query = CreateTextBox(DesktopText.Translate("Describe what you want to find"), initial, DesktopText.Translate("Describe what you want to find"));
        query.AcceptsReturn = true;
        query.TextWrapping = TextWrapping.Wrap;
        query.MaxLength = 1_200;
        var submit = new Button { Content = DesktopText.Translate("Search"), IsDefault = true };
        submit.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(query.Text)) Close(query.Text.Trim());
        };
        var cancel = new Button { Content = DesktopText.Translate("Cancel"), IsCancel = true };
        cancel.Click += (_, _) => Close();
        var panel = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(20), RowSpacing = 12 };
        AddRow(panel, 0, query);
        AddRow(panel, 1, new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right, Orientation = Orientation.Horizontal,
            Spacing = 8, Children = { cancel, submit }
        });
        Content = panel;
    }
}

internal sealed class SimilarityResultsDialog : Window
{
    internal SimilarityResultsDialog(string title, IReadOnlyList<DocumentSimilarityMatch> results)
    {
        Title = title;
        Width = 590;
        Height = 410;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var entries = results.Select(match => new MetadataOption<DocumentSimilarityMatch>(
            $"{match.Document.Title} · {match.Document.RelativePath}", match)).ToArray();
        var list = new ListBox { Name = "SimilarityMatches", ItemsSource = entries, SelectedIndex = entries.Length == 0 ? -1 : 0 };
        var close = new Button { Content = DesktopText.Translate("Close"), IsCancel = true };
        close.Click += (_, _) => Close();
        var open = new Button { Name = "OpenSimilarityMatch", Content = DesktopText.Translate("Open PDF"), IsEnabled = entries.Length > 0, IsDefault = true };
        open.Click += (_, _) =>
        {
            if (list.SelectedItem is MetadataOption<DocumentSimilarityMatch> selected) Close(selected.Value);
        };
        list.SelectionChanged += (_, _) => open.IsEnabled = list.SelectedIndex >= 0;
        var panel = new Grid { RowDefinitions = new("*,Auto"), RowSpacing = 12, Margin = new Thickness(20) };
        AddRow(panel, 0, results.Count == 0 ? new TextBlock { Text = DesktopText.Translate("No matching documents.") } : list);
        AddRow(panel, 1, new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right, Orientation = Orientation.Horizontal,
            Spacing = 8, Children = { close, open }
        });
        Content = panel;
    }
}

internal sealed class LocalFilingDialog : Window
{
    internal LocalFilingDialog(LocalFilingProposal proposal, MetadataDefinitions definitions)
    {
        Title = DesktopText.Translate("Local filing suggestions");
        Width = 560;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var edit = proposal.Edit;
        var fields = new StackPanel { Spacing = 8 };
        var title = Suggested(edit.Title is not null ? DesktopText.Format($"Title · {edit.Title}") : null);
        var date = Suggested(edit.DocumentDate is { } day ? DesktopText.Format($"Document date · {day:d}") : null);
        var tags = Suggested(edit.Tags.Count > 0 ? DesktopText.Format($"Tags · {string.Join(", ", edit.Tags)}") : null);
        string[] selectedContacts = definitions.Correspondents.Where(contact => edit.CorrespondentIds.Contains(contact.Id))
            .Select(contact => contact.Name).ToArray();
        var contacts = Suggested(selectedContacts.Length > 0 ? DesktopText.Format($"Correspondents · {string.Join(", ", selectedContacts)}") : null);
        string? typeName = definitions.DocumentTypes.FirstOrDefault(type => type.Id == edit.DocumentTypeId)?.Name;
        var type = Suggested(typeName is not null ? DesktopText.Format($"Document type · {typeName}") : null);
        foreach (var box in new[] { title, date, tags, contacts, type }) if (box is not null) fields.Children.Add(box);
        if (fields.Children.Count == 0) fields.Children.Add(new TextBlock { Text = DesktopText.Translate("The model returned no filing suggestions.") });
        if (proposal.Evidence.DueDates.Count > 0)
            fields.Children.Add(new TextBlock { Text = DesktopText.Format($"Possible due date · {proposal.Evidence.DueDates[0]:d}") });
        if (proposal.Evidence.Amount is { } amount)
            fields.Children.Add(new TextBlock { Text = DesktopText.Format($"Amount · {amount.Value} {amount.Currency}") });
        if (proposal.Evidence.Iban is { } iban)
            fields.Children.Add(new SelectableTextBlock { Text = DesktopText.Format($"IBAN · {iban}") });

        var cancel = new Button { Content = DesktopText.Translate("Close"), IsCancel = true };
        cancel.Click += (_, _) => Close();
        var apply = new Button { Name = "ApplyLocalFilingButton", Content = DesktopText.Translate("Apply selected"), IsDefault = true,
            IsEnabled = new[] { title, date, tags, contacts, type }.Any(choice => choice is not null) };
        apply.Click += (_, _) => Close(edit with
        {
            Title = title?.IsChecked == true ? edit.Title : null,
            DocumentDate = date?.IsChecked == true ? edit.DocumentDate : null,
            Tags = tags?.IsChecked == true ? edit.Tags : [],
            CorrespondentIds = contacts?.IsChecked == true ? edit.CorrespondentIds : [],
            DocumentTypeId = type?.IsChecked == true ? edit.DocumentTypeId : null
        });
        var panel = new Grid { RowDefinitions = new("*,Auto"), Margin = new Thickness(20), RowSpacing = 12 };
        AddRow(panel, 0, new ScrollViewer { Content = fields });
        AddRow(panel, 1, new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right, Orientation = Orientation.Horizontal,
            Spacing = 8, Children = { cancel, apply }
        });
        Content = panel;
    }

    private static CheckBox? Suggested(string? label) => label is null ? null : new CheckBox
    {
        IsChecked = true, Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }
    };
}
