using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

/// <summary>
/// Month names typed into the card's date field with the keyboard, whatever the person's own culture is: the usual
/// short forms of September ("Sep", "Sept") and any start of a month's name that only one month has are read, in
/// every culture, the invariant one Linux's C locale gives included; a start two months share is not.
/// </summary>
public sealed partial class InboxWindowTests
{
    [AvaloniaTheory]
    [InlineData("en-GB")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("")]
    public async Task TheUsualWaysToWriteAMonthAreReadInEveryCulture(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = cultureName.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(cultureName);
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var dateField = Assert.IsType<TextBox>(window.FindControl<TextBox>("DocumentDateField"));
            PressKey(window, Key.D, PhysicalKey.D, "d");
            Assert.True(dateField.IsFocused);

            // Each is typed over what the field holds, which Ctrl+A selects.
            (string Typed, DateOnly Date)[] dates =
            [
                ("26 Sep 2026", new(2026, 9, 26)), ("27 Sept 2026", new(2026, 9, 27)), ("28 sept. 2026", new(2026, 9, 28)),
                ("29 SEP 2026", new(2026, 9, 29)), ("1 Febr 2027", new(2027, 2, 1)), ("2 Augu 2027", new(2027, 8, 2)),
                ("3 November 2027", new(2027, 11, 3))
            ];
            foreach (var (typed, date) in dates)
            {
                SelectAllInField(window);
                TypeText(window, typed);
                PressKey(window, Key.Enter, PhysicalKey.Enter);
                Assert.True(model.SelectedDocument!.DocumentDate == date,
                    $"“{typed}” was not read as {date:yyyy-MM-dd} under “{CultureInfo.CurrentCulture.Name}”: {model.DocumentDateError}");
            }

            // Without a culture of its own, or in one that is not English, the English interface writes September in
            // three letters, as it writes every other month.
            SelectAllInField(window);
            TypeText(window, "30 Sep 2026");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            if (cultureName is "" or "de-DE") Assert.Equal("30 Sep 2026", dateField.Text);

            // A start that two months share, or one too short to tell, is not read as either.
            foreach (string typed in new[] { "4 Ju 2026", "4 Ma 2026", "4 S 2026" })
            {
                SelectAllInField(window);
                TypeText(window, typed);
                PressKey(window, Key.Enter, PhysicalKey.Enter);
                Assert.True(model.HasDocumentDateError, $"“{typed}” was read as {model.SelectedDocument!.DocumentDate:yyyy-MM-dd}.");
                Assert.Equal(new DateOnly(2026, 9, 30), model.SelectedDocument!.DocumentDate);
            }
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>Ctrl+A, or ⌘A on macOS, in the field that has the keyboard.</summary>
    private static void SelectAllInField(Window window) => PressKey(window, Key.A, PhysicalKey.A, "a",
        OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);

    [AvaloniaFact]
    public async Task TheFrenchInterfaceReadsMonthsByAnyStartOnlyOneMonthHas()
    {
        DesktopText.SetLanguage(InterfaceLanguage.French);
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            PressKey(window, Key.D, PhysicalKey.D, "d");
            (string Typed, DateOnly Date)[] dates =
            [
                ("5 sep 2026", new(2026, 9, 5)), ("6 sept. 2026", new(2026, 9, 6)), ("7 juil 2026", new(2026, 7, 7)),
                ("8 fev 2026", new(2026, 2, 8)), ("9 déc. 2026", new(2026, 12, 9))
            ];
            foreach (var (typed, date) in dates)
            {
                SelectAllInField(window);
                TypeText(window, typed);
                PressKey(window, Key.Enter, PhysicalKey.Enter);
                Assert.True(model.SelectedDocument!.DocumentDate == date, $"« {typed} » was not read as {date:yyyy-MM-dd}: {model.DocumentDateError}");
            }
            // "jui" begins both juin and juillet.
            SelectAllInField(window);
            TypeText(window, "1 jui 2026");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.True(model.HasDocumentDateError);
            Assert.Equal(new DateOnly(2026, 12, 9), model.SelectedDocument!.DocumentDate);
        }
        finally
        {
            window.Close();
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }
}
