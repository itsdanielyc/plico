using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Plico.Desktop;

/// <summary>Library, intake, recognition and filing preferences, bound to the same view model as the main window.</summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>A section is current once its heading is within this distance of where the first heading rests.</summary>
    private const double CurrentSectionSlack = 8;

    public SettingsWindow()
    {
        InitializeComponent();
        SettingsScroll.ScrollChanged += (_, _) => MarkCurrentSection();
        SettingsScroll.SizeChanged += (_, _) => FitScrollTail();
        ModelsGroup.SizeChanged += (_, _) => FitScrollTail();
        Opened += (_, _) => MarkCurrentSection();
    }

    /// <summary>The section names a caller can open: GeneralGroup, RecognitionGroup, IntakeGroup and the rest.</summary>
    internal void ShowSection(string section)
    {
        if (this.FindControl<Control>(section) is not { } target) return;
        // Layout may not have run yet when the window has just been created.
        Dispatcher.UIThread.Post(() =>
        {
            if (target.TranslatePoint(default, SettingsPage) is not { } position) return;
            SettingsScroll.Offset = new Vector(0, Math.Max(0, position.Y));
            MarkCurrentSection(section);
        }, DispatcherPriority.Loaded);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void Jump_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string target }) ShowSection(target);
    }

    private void ShowShortcuts_Click(object? sender, RoutedEventArgs e)
    {
        if (Owner is MainWindow main)
        {
            Close();
            main.ShowShortcutSheet();
        }
    }

    /// <summary>The white pill follows the page: the last section whose heading has reached the top is current.</summary>
    private void MarkCurrentSection(string? forced = null)
    {
        string? current = forced;
        if (current is null)
        {
            foreach (var section in SettingsPage.Children.OfType<StackPanel>())
            {
                if (section.TranslatePoint(default, SettingsScroll) is not { } top) continue;
                if (top.Y <= SettingsPage.Margin.Top + CurrentSectionSlack || current is null) current = section.Name;
            }
        }
        foreach (var row in SectionList.Children.OfType<Button>())
            row.Classes.Set("current", Equals(row.Tag, current));
    }

    /// <summary>Leaves enough room under the last section for its heading to reach the top of the page.</summary>
    private void FitScrollTail()
    {
        double room = SettingsScroll.Viewport.Height - ModelsGroup.Bounds.Height - SettingsPage.Margin.Top - SettingsPage.Spacing;
        ScrollTail.Height = Math.Max(56, room);
    }
}
