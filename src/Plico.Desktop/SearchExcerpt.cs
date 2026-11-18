using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Plico.Core;

namespace Plico.Desktop;

internal sealed class SearchExcerpt : TextBlock
{
    private static readonly IBrush MatchBackground = new SolidColorBrush(Color.Parse("#FFE4A3"));
    private static readonly IBrush MatchForeground = new SolidColorBrush(Color.Parse("#27362A"));
    public static readonly StyledProperty<DocumentSummary?> DocumentProperty =
        AvaloniaProperty.Register<SearchExcerpt, DocumentSummary?>(nameof(Document));

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public DocumentSummary? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != DocumentProperty || Inlines is not { } inlines) return;
        inlines.Clear();
        if (Document is not { Match.Length: > 0 } document) return;
        int cursor = 0;
        foreach (var hit in document.MatchHighlights)
        {
            if (hit.Start > cursor) inlines.Add(new Run(document.Match[cursor..hit.Start]));
            inlines.Add(new Run(document.Match.Substring(hit.Start, hit.Length))
            {
                Background = MatchBackground, Foreground = MatchForeground, FontWeight = FontWeight.SemiBold
            });
            cursor = hit.Start + hit.Length;
        }
        if (cursor < document.Match.Length) inlines.Add(new Run(document.Match[cursor..]));
    }
}
