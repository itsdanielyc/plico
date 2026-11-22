using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

/// <summary>Copy in the family voice: one name per concept, ellipses only before a choice, one date format, British spelling.</summary>
public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task EachConceptHasOneNameAndOnlyChoicesEndInAnEllipsis()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            window.UpdateLayout();
            // Details is the card's name for the document's fields, as in "Add details".
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Classes.Contains("field-label") && text.Text == "Details" && text.IsEffectivelyVisible);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Classes.Contains("field-label") && text.Text == "Fields" && text.IsEffectivelyVisible);
            Assert.Equal("Reminder", AutomationProperties.GetName(Assert.IsType<Button>(window.FindControl<Button>("EditDeadlinesButton"))));
            Assert.Equal("Details", AutomationProperties.GetName(Assert.IsType<Button>(window.FindControl<Button>("EditMetadataButton"))));

            var titles = PaletteCommands.Build(window, model).Select(command => command.Title).ToArray();
            Assert.Contains("Reminder…", titles);
            Assert.Contains("Details…", titles);
            Assert.Contains("Upcoming", titles);
            Assert.Contains("Settings", titles);
            Assert.Contains("Trash", titles);
            Assert.Contains("Version history", titles);
            Assert.Contains("Related documents", titles);
            Assert.DoesNotContain(titles, title => title.Contains("deadlines", StringComparison.OrdinalIgnoreCase)
                || title.Contains("Custom fields", StringComparison.Ordinal) || title.StartsWith("Return", StringComparison.Ordinal));

            // The ⋯ menu is "More"; its items that open no further choice carry no ellipsis.
            var more = Assert.IsType<Button>(window.FindControl<Button>("LibraryToolsButton"));
            Assert.Equal("More", AutomationProperties.GetName(more));
            var menu = Assert.IsType<MenuFlyout>(more.Flyout);
            menu.ShowAt(more);
            var headers = menu.Items.OfType<MenuItem>().Select(item => item.Header as string).ToArray();
            menu.Hide();
            Assert.Contains("Check library integrity", headers);
            Assert.Contains("Upcoming", headers);
            Assert.Contains("Settings", headers);

            // A reviewed document goes "Back to Inbox", on the button and in the palette.
            model.ToggleReviewCommand.Execute(null);
            await WaitForPreviewAsync(model);
            model.ShowAllCommand.Execute(null);
            await WaitForAsync(() => model.Documents.Any(document => document.Reviewed));
            model.SelectedDocument = model.Documents.First(document => document.Reviewed);
            await WaitForPreviewAsync(model);
            Assert.Equal("Back to Inbox", model.ReviewAction);
            Assert.Contains("Back to Inbox", PaletteCommands.Build(window, model).Select(command => command.Title));
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DatesReadOneWayInBothLanguages()
    {
        var moment = new DateTimeOffset(new DateTime(2026, 11, 21, 13, 25, 0, DateTimeKind.Local));
        try
        {
            DesktopText.SetLanguage(InterfaceLanguage.English);
            Assert.Equal("21 Nov 2026 at 13:25", DisplayDates.DayAndTime(moment));
            DesktopText.SetLanguage(InterfaceLanguage.French);
            Assert.Equal("21 nov. 2026 à 13:25", DisplayDates.DayAndTime(moment));
        }
        finally
        {
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    [AvaloniaFact]
    public async Task DateFieldsInDialogsShowAndTakeTheInterfacesFormat()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new DocumentDateDialog(new DateOnly(2024, 2, 1), documentCount: 3);
        var result = dialog.ShowDialog<DocumentDateEdit?>(owner);
        try
        {
            var box = Assert.IsType<TextBox>(dialog.FindControl<TextBox>("DocumentDateBox"));
            Assert.Equal("1 Feb 2024", box.Text);
            Assert.DoesNotContain("YYYY", box.PlaceholderText ?? "", StringComparison.Ordinal);
            Assert.DoesNotContain("YYYY", dialog.FindControl<TextBlock>("DocumentDateHint")?.Text ?? "", StringComparison.Ordinal);
            Assert.Equal("Document date for 3 documents", dialog.FindControl<TextBlock>("DocumentDatePrompt")?.Text);
            box.Text = "29 feb 2024";
            Assert.IsType<Button>(dialog.FindControl<Button>("ConfirmDocumentDateButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new DateOnly(2024, 2, 29), Assert.IsType<DocumentDateEdit>(await result).Date);
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await result;
            owner.Close();
        }

        var lease = new DocumentSummary(1, "Lease", "Lease.pdf", DateTimeOffset.Now, false, 1, true, [], "");
        var editor = new DeadlineEditor(new DeadlineEditInput(lease, [new DocumentDeadline(DeadlineKind.Due, new DateOnly(2026, 12, 1), 7)], []));
        editor.Show();
        try
        {
            var due = editor.GetLogicalDescendants().OfType<TextBox>().Single(box => box.Name == "DeadlineDateDue");
            Assert.Equal("1 Dec 2026", due.Text);
            Assert.All(editor.GetLogicalDescendants().OfType<TextBox>(), box => Assert.DoesNotContain("YYYY", box.PlaceholderText ?? "", StringComparison.Ordinal));
            // An empty date can never pass for a set one: its placeholder is words, not a date, and the format is said once.
            foreach (var empty in editor.GetLogicalDescendants().OfType<TextBox>().Where(box => box.Name?.StartsWith("DeadlineDate", StringComparison.Ordinal) == true))
                Assert.False(DisplayDates.TryParseDay(empty.PlaceholderText, out _), $"{empty.Name} shows “{empty.PlaceholderText}” while empty.");
            var texts = editor.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text ?? "").ToArray();
            // The dialog uses the card's one name for a document's dates.
            Assert.Contains("Reminder for “Lease”", texts);
            Assert.Contains(texts, text => text.Contains($"like {DisplayDates.Day(DateOnly.FromDateTime(DateTime.Today))}", StringComparison.Ordinal));
        }
        finally
        {
            editor.Close();
        }
    }

    [AvaloniaFact]
    public void TheCatalogSpellsBritishAndNamesThisPcOrThisComputerByPlatform()
    {
        var rows = ReadCatalog();
        var american = new Regex(
            @"\b\w*(?:recogni|optimi|organi|summari|categori|customi|authori|minimi|maximi|prioriti|synchroni|normali|initiali|finali|speciali|visuali|apologi)z\w*|\bcolou?r\b(?<=\bcolor\b)|\bcenter\b|\bcanceled\b|\bfavorite|\bbehavior|\bgray\b|\banalyz",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.Empty(rows.Where(row => american.IsMatch(row.English)).Select(row => $"{row.Key}: {row.English}"));

        // Every sentence that names the machine comes as a pair, "this PC" for Windows and "this computer" elsewhere.
        var keys = rows.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows.Where(row => Regex.IsMatch(row.English, @"this[\s ](PC|computer)\b")))
        {
            bool pc = Regex.IsMatch(row.English, @"this[\s ]PC\b");
            string suffix = pc ? "Pc" : "Computer";
            Assert.True(row.Key.EndsWith(suffix, StringComparison.Ordinal), $"{row.Key} says “{row.English}” without a platform pair.");
            string partner = row.Key[..^suffix.Length] + (pc ? "Computer" : "Pc");
            Assert.True(keys.Contains(partner), $"{row.Key} has no {partner}.");
        }

        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var settings = new SettingsWindow { DataContext = model };
        settings.Show();
        try
        {
            string machine = OperatingSystem.IsWindows() ? "this PC" : "this computer";
            var help = settings.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? "")
                .Single(text => text.StartsWith("Optional. A model server on", StringComparison.Ordinal));
            Assert.Contains(machine, help, StringComparison.Ordinal);
        }
        finally { settings.Close(); }
    }

    private static IReadOnlyList<(string Key, string English, string French)> ReadCatalog()
    {
        using var stream = typeof(DesktopText).Assembly.GetManifestResourceStream("Plico.Desktop.InterfaceText")!;
        using var reader = new StreamReader(stream);
        var rows = new List<(string, string, string)>();
        while (reader.ReadLine() is { } line)
        {
            string[] fields = line.Split('\t');
            rows.Add((fields[0], fields[1], fields[2]));
        }
        return rows;
    }
}
