using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>The tag name and the choice to merge into a separate existing tag.</summary>
internal sealed record TagRenameRequest(string Name, bool MergeIntoExisting);

/// <summary>Edits a catalog tag and presents a separate action for a merge.</summary>
internal sealed partial class TagRenameDialog : Window
{
    private const string RenameAction = "Rename tag";
    private const string MergeAction = "Merge tags";
    private readonly string _sourceName;
    private readonly string _sourceKey;
    private readonly IReadOnlyDictionary<string, string> _catalogLabels;
    private string? _displayedMergeKey;

    internal TagRenameDialog(string sourceName, IReadOnlyList<string> catalogNames)
    {
        _sourceName = sourceName;
        _sourceKey = InboxLibrary.GetTagKey(sourceName);
        _catalogLabels = catalogNames.ToDictionary(InboxLibrary.GetTagKey, StringComparer.Ordinal);
        InitializeComponent();
        TagPrompt.Text = $"Rename “{sourceName}”";
        TagNameBox.MaxLength = InboxLibrary.MaximumTagNameLength;
        TagNameBox.Text = sourceName;
        TagNameBox.TextChanged += (_, _) => ReadForm();
        ReadForm();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        TagNameBox.Focus();
        TagNameBox.SelectAll();
    }

    private TagRenameRequest? ReadForm()
    {
        string name = TagNameBox.Text ?? "";
        try
        {
            string key = InboxLibrary.GetTagKey(name);
            string? target = key != _sourceKey && _catalogLabels.TryGetValue(key, out string? label) ? label : null;
            _displayedMergeKey = target is null ? null : key;
            SaveTagButton.Content = target is null ? RenameAction : MergeAction;
            SaveTagButton.IsEnabled = true;
            // A collision requires an explicit button activation rather than Enter from the name field.
            SaveTagButton.IsDefault = target is null;
            TagRenameNotice.IsVisible = target is not null;
            TagRenameNotice.Text = target is null ? "" : $"All documents tagged “{_sourceName}” will use “{target}”.";
            return new TagRenameRequest(name, MergeIntoExisting: target is not null);
        }
        catch (ArgumentException error)
        {
            _displayedMergeKey = null;
            SaveTagButton.Content = RenameAction;
            SaveTagButton.IsEnabled = false;
            SaveTagButton.IsDefault = false;
            TagRenameNotice.Text = error.Message;
            TagRenameNotice.IsVisible = true;
            return null;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        string? shownMergeKey = _displayedMergeKey;
        if (ReadForm() is { } request && shownMergeKey == _displayedMergeKey) Close(request);
    }
}
