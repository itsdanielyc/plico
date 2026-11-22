using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Plico.Desktop;

/// <summary>
/// The queue says when a document arrived as a person says it ("Today", "Yesterday", "Mon"), so those words change at
/// midnight. One timer waits for the next local midnight and then writes the rows' days again; nothing else runs while
/// the day stays the same. A timer counts only time the machine is awake, so one that slept through midnight is late by
/// the length of the sleep: the person's return (the window coming to the front, a key, the pointer moving over it)
/// also writes the days again once midnight has passed, at the cost of one clock reading.
/// </summary>
public partial class MainWindow
{
    private ITimer? _dayTimer;

    // The moment, a little past the next local midnight, at which the days on the rows stop being true.
    private DateTimeOffset _dayEnds;

    private void WatchTheDay()
    {
        _dayEnds = NextDayBegins();
        _dayTimer = DisplayDates.Clock.CreateTimer(_ => Dispatcher.UIThread.Post(BeginNewDay), null, UntilNextDay(), Timeout.InfiniteTimeSpan);
        Activated += (_, _) => CatchUpWithTheDay();
        AddHandler(KeyDownEvent, (_, _) => CatchUpWithTheDay(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, (_, _) => CatchUpWithTheDay(), RoutingStrategies.Tunnel, handledEventsToo: true);
        Closed += (_, _) =>
        {
            _dayTimer?.Dispose();
            _dayTimer = null;
        };
    }

    /// <summary>A little past the next local midnight by <see cref="DisplayDates.Clock"/>, so the day has surely turned.</summary>
    private static DateTimeOffset NextDayBegins()
    {
        var now = DisplayDates.Clock.GetLocalNow();
        return new DateTimeOffset(now.Date.AddDays(1), now.Offset) + TimeSpan.FromSeconds(1);
    }

    private TimeSpan UntilNextDay() => _dayEnds - DisplayDates.Clock.GetLocalNow();

    /// <summary>Writes the days again if midnight has passed without the timer noticing (the machine slept). O(1) otherwise.</summary>
    private void CatchUpWithTheDay()
    {
        if (_dayTimer is not null && DisplayDates.Clock.GetUtcNow() >= _dayEnds) BeginNewDay();
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
        _dayEnds = NextDayBegins();
        _dayTimer.Change(UntilNextDay(), Timeout.InfiniteTimeSpan);
    }
}
