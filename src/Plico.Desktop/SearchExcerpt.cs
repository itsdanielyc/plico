using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Plico.Core;

namespace Plico.Desktop;

/// <summary>
/// A result's text excerpt with its search hits in Plico's amber (the HighlightSurface and HighlightInk tokens), redrawn
/// when the theme changes so dark mode uses the dark amber.
/// </summary>
internal sealed class SearchExcerpt : TextBlock
{
    private static readonly IBrush FallbackBackground = new SolidColorBrush(Color.Parse("#FBE3A0"));
    private static readonly IBrush FallbackForeground = new SolidColorBrush(Color.Parse("#3D2C00"));
    public static readonly StyledProperty<DocumentSummary?> DocumentProperty =
        AvaloniaProperty.Register<SearchExcerpt, DocumentSummary?>(nameof(Document));

    public SearchExcerpt()
    {
        ActualThemeVariantChanged += (_, _) => Rebuild();
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    public DocumentSummary? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty) Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Rebuild();
    }

    private void Rebuild()
    {
        if (Inlines is not { } inlines) return;
        inlines.Clear();
        if (Document is not { Match.Length: > 0 } document) return;
        var background = Brush("HighlightSurface") ?? FallbackBackground;
        var foreground = Brush("HighlightInk") ?? FallbackForeground;
        int cursor = 0;
        foreach (var hit in document.MatchHighlights)
        {
            if (hit.Start > cursor) inlines.Add(new Run(document.Match[cursor..hit.Start]));
            inlines.Add(new Run(document.Match.Substring(hit.Start, hit.Length))
            {
                Background = background, Foreground = foreground, FontWeight = FontWeight.SemiBold
            });
            cursor = hit.Start + hit.Length;
        }
        if (cursor < document.Match.Length) inlines.Add(new Run(document.Match[cursor..]));
    }

    private IBrush? Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;
}
