using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Plico.Core;

namespace Plico.Desktop;

internal sealed record TagNavigationItem(string Label, int Depth, IBrush Color)
{
    private const int IndentPerLevel = 9;
    public Thickness Indent => new(Math.Min(Depth, 6) * IndentPerLevel, 0, 0, 0);
}

internal sealed partial class InboxViewModel
{
    private static readonly IBrush DefaultTagColor = Brush.Parse("#C5DDA6");
    [ObservableProperty] private IReadOnlyList<TagNavigationItem> _tagNavigation = [];

    // O(t + s log s) for t configured tags and s shown entries, with O(t) metadata and traversal space.
    private void RefreshTagNavigation()
    {
        if (_library is not { } library || Tags.Count == 0)
        {
            TagNavigation = [];
            return;
        }
        var appearances = library.GetTagAppearances();
        var byName = appearances.ToDictionary(tag => InboxLibrary.GetTagKey(tag.Label), StringComparer.Ordinal);
        var visible = new HashSet<string>(StringComparer.Ordinal);
        foreach (string label in Tags)
        {
            string key = InboxLibrary.GetTagKey(label);
            while (visible.Add(key) && byName.TryGetValue(key, out var tag) && tag.ParentLabel is { } parent)
                key = InboxLibrary.GetTagKey(parent);
        }

        var groups = appearances.Where(tag => visible.Contains(InboxLibrary.GetTagKey(tag.Label)))
            .OrderBy(tag => tag.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToLookup(tag => tag.ParentLabel is null ? "" : InboxLibrary.GetTagKey(tag.ParentLabel));
        var stack = new Stack<(TagAppearance Tag, int Depth)>();
        foreach (var root in groups[""].Reverse()) stack.Push((root, 0));
        var entries = new List<TagNavigationItem>(visible.Count);
        while (stack.TryPop(out var item))
        {
            entries.Add(new(item.Tag.Label, item.Depth, item.Tag.ColorHex is { } color ? Brush.Parse(color) : DefaultTagColor));
            foreach (var child in groups[InboxLibrary.GetTagKey(item.Tag.Label)].Reverse())
                stack.Push((child, item.Depth + 1));
        }
        TagNavigation = entries;
    }
}
