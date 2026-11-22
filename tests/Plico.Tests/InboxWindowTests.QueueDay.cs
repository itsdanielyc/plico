using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

/// <summary>
/// The queue says when a document arrived as a person says it ("Today", "Yesterday", "Mon"), and that stays true when
/// Plico is left open past midnight.
/// </summary>
public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task TheQueueSaysYesterdayOnceMidnightHasPassed()
    {
        var clock = new SteppedClock(DateTimeOffset.UtcNow);
        var previous = DisplayDates.Clock;
        DisplayDates.Clock = clock;
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var list = Assert.IsType<ListBox>(window.FindControl<ListBox>("DocumentList"));
            string[] RowDays() => list.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Classes.Contains("caption") && text.IsEffectivelyVisible)
                .Select(text => text.Text?.Split(" · ")[0] ?? "").ToArray();
            Assert.Equal(["Today", "Today"], RowDays());

            // Plico stays open overnight: the clock passes midnight, and then one more day.
            clock.Advance(TimeSpan.FromDays(1));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(RowDays().All(day => day == "Yesterday"), $"After midnight the queue still says “{string.Join(" / ", RowDays())}”.");

            clock.Advance(TimeSpan.FromDays(1));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            string weekday = DisplayDates.Relative(DateOnly.FromDateTime(clock.GetLocalNow().DateTime).AddDays(-2), DisplayDates.Today);
            Assert.True(RowDays().All(day => day == weekday), $"Two days on the queue says “{string.Join(" / ", RowDays())}”.");
        }
        finally
        {
            window.Close();
            DisplayDates.Clock = previous;
        }
    }

    [AvaloniaFact]
    public async Task AMachineThatSleptThroughMidnightSaysYesterdayWhenThePersonReturns()
    {
        var clock = new SteppedClock(DateTimeOffset.UtcNow);
        var previous = DisplayDates.Clock;
        DisplayDates.Clock = clock;
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var list = Assert.IsType<ListBox>(window.FindControl<ListBox>("DocumentList"));
            string[] RowDays() => list.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Classes.Contains("caption") && text.IsEffectivelyVisible)
                .Select(text => text.Text?.Split(" · ")[0] ?? "").ToArray();
            Assert.Equal(["Today", "Today"], RowDays());

            // The lid closes in the evening and opens the next morning: the machine slept through midnight, so the
            // midnight timer, which counts only time awake, has not run yet.
            clock.Sleep(TimeSpan.FromDays(1));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            // The person moves the pointer over the queue, and the days are written again.
            var row = list.GetVisualDescendants().OfType<ListBoxItem>().First();
            window.MouseMove(Assert.IsType<Point>(row.TranslatePoint(new Point(20, 10), window)));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(RowDays().All(day => day == "Yesterday"), $"After a night asleep the queue still says “{string.Join(" / ", RowDays())}”.");

            // Another night asleep, and this time the person's first touch is a key.
            clock.Sleep(TimeSpan.FromDays(1));
            PressKey(window, Key.LeftShift, PhysicalKey.ShiftLeft);
            window.UpdateLayout();
            string weekday = DisplayDates.Relative(DateOnly.FromDateTime(clock.GetLocalNow().DateTime).AddDays(-2), DisplayDates.Today);
            Assert.True(RowDays().All(day => day == weekday), $"After a second night the queue says “{string.Join(" / ", RowDays())}”.");
        }
        finally
        {
            window.Close();
            DisplayDates.Clock = previous;
        }
    }

    /// <summary>
    /// A clock that moves only when told to, in the machine's own time zone, and runs the timers that fall due as it
    /// moves, as the system's would.
    /// </summary>
    private sealed class SteppedClock(DateTimeOffset start) : TimeProvider
    {
        private readonly List<SteppedTimer> _timers = [];
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new SteppedTimer(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        /// <summary>
        /// Moves the clock on while the machine sleeps: no timer runs, and each one falls due that much later, since a
        /// timer counts only time awake.
        /// </summary>
        public void Sleep(TimeSpan span)
        {
            _now += span;
            foreach (var timer in _timers) timer.Postpone(span);
        }

        /// <summary>Moves the clock on and runs each timer due by then, in the order they fall due.</summary>
        public void Advance(TimeSpan span)
        {
            var until = _now + span;
            while (_timers.Where(timer => timer.Due <= until).MinBy(timer => timer.Due) is { } next)
            {
                _now = next.Due!.Value;
                next.Fire();
            }
            _now = until;
        }

        private sealed class SteppedTimer(SteppedClock clock, TimerCallback callback, object? state) : ITimer
        {
            private TimeSpan _period = Timeout.InfiniteTimeSpan;
            public DateTimeOffset? Due { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                _period = period;
                return true;
            }

            public void Postpone(TimeSpan span) => Due += span;

            public void Fire()
            {
                Due = _period == Timeout.InfiniteTimeSpan || _period == TimeSpan.Zero ? null : Due + _period;
                callback(state);
            }

            public void Dispose()
            {
                Due = null;
                clock._timers.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
