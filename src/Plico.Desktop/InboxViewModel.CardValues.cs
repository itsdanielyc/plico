using System.Globalization;
using Avalonia.Media;

namespace Plico.Desktop;

/// <summary>A tag on the open document, drawn as a chip with the tag's colour dot (a quiet grey when it has none).</summary>
internal sealed record TagChip(string Label, IBrush Dot);

/// <summary>
/// Values the queue header and the filing card show instead of commands: "3 to review", "21 Nov 2026", the tag chips
/// and whether the PDF is searchable.
/// </summary>
internal sealed partial class InboxViewModel
{
    private static readonly IBrush UncolouredTagDot = new SolidColorBrush(Color.Parse("#8E8E96"));

    /// <summary>"3 to review" in the Inbox; elsewhere the result count ("12 documents", "Searching…").</summary>
    public string QueueCountLabel => InboxSelected && HasInboxCount ? DesktopText.Format($"{InboxCount} to review") : ResultCount;

    /// <summary>"21 Nov 2026", the date printed on the open document, or empty.</summary>
    public string SelectedDocumentDateLabel => SelectedDocument?.DocumentDate is { } date ? DisplayDates.Day(date) : "";
    public bool HasSelectedDocumentDate => SelectedDocument?.DocumentDate is not null;

    /// <summary>"Searchable", or "Not searchable yet" for a PDF without a text layer.</summary>
    public string SelectedTextState => SelectedDocument?.HasText == false
        ? DesktopText.Translate("Not searchable yet") : DesktopText.Translate("Searchable");

    /// <summary>The open document's tags with their colours from the tag list. O(t + n) for t tags and n chips.</summary>
    public IReadOnlyList<TagChip> SelectedTagChips
    {
        get
        {
            if (SelectedDocument is not { Tags.Count: > 0 } document) return [];
            var colours = new Dictionary<string, IBrush>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in TagNavigation)
                if (item.Color is { } colour) colours.TryAdd(item.Label, colour);
            return document.Tags.Select(tag => new TagChip(tag, colours.GetValueOrDefault(tag) ?? UncolouredTagDot)).ToArray();
        }
    }
    public bool HasSelectedTagChips => SelectedDocument is { Tags.Count: > 0 };

    private void NotifyCardValues(string? property)
    {
        switch (property)
        {
            case nameof(ResultCount) or nameof(InboxCount) or nameof(Query) or nameof(Heading):
                RaiseDerived(nameof(QueueCountLabel));
                break;
            case nameof(SelectedDocument):
                RaiseDerived(nameof(SelectedDocumentDateLabel));
                RaiseDerived(nameof(HasSelectedDocumentDate));
                RaiseDerived(nameof(SelectedTextState));
                RaiseDerived(nameof(SelectedTagChips));
                RaiseDerived(nameof(HasSelectedTagChips));
                break;
            case nameof(TagNavigation):
                RaiseDerived(nameof(SelectedTagChips));
                break;
        }
    }
}
