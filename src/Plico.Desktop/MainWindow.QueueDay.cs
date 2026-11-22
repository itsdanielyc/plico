using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Plico.Desktop;

/// <summary>
/// The queue says when a document arrived as a person says it ("Today", "Yesterday", "Mon"), so those words change at
/// midnight. One timer waits for the next local midnight and then writes the rows' days again; nothing else runs while
/// the day stays the same.
/// </summary>
public partial class MainWindow
{
    private ITimer? _dayTimer;

    private void WatchTheDay()
    {
        _dayTimer = DisplayDates.Clock.CreateTimer(_ => Dispatcher.UIThread.Post(BeginNewDay), null, UntilNextDay(), Timeout.InfiniteTimeSpan);
        Closed += (_, _) =>
        {
            _dayTimer?.Dispose();
            _dayTimer = null;
        };
    }

    /// <summary>A little past the next local midnight by <see cref="DisplayDates.Clock"/>, so the day has surely turned.</summary>
    private static TimeSpan UntilNextDay()
    {
        var now = DisplayDates.Clock.GetLocalNow();
        var midnight = new DateTimeOffset(now.Date.AddDays(1), now.Offset);
        return midnight - now + TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Writes the days of the rows on screen again; rows made later write theirs as they are made. O(n) in the rows
    /// realised, which the list keeps to about a screenful.
    /// </summary>
    private void BeginNewDay()
    {
        if (_dayTimer is null) return;
        foreach (var meta in DocumentList.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Classes.Contains("queue-meta")))
            BindingOperations.GetBindingExpressionBase(meta, TextBlock.TextProperty)?.UpdateTarget();
        _dayTimer.Change(UntilNextDay(), Timeout.InfiniteTimeSpan);
    }
}
