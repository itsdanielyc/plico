using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task WithoutTextShortcutFindsSourcesForBatchOcr()
    {
        using var temporary = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            AddOcrBatchScans(library, ["A scan.pdf", "B scan.pdf"], token);
            const string letter = "Lease Renewal.pdf";
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", letter), Path.Combine(library.PdfDirectory, letter));
            Assert.Equal(1, library.Refresh(token).Indexed);
        }

        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 900, Height = 590 };
        window.Show();
        try
        {
            var shortcut = Assert.IsType<Button>(window.FindControl<Button>("WithoutTextButton"));
            shortcut.Command!.Execute(null);
            Assert.Equal("Without text", model.Heading);
            Assert.Equal("text:missing", model.Query);
            Assert.Contains("current", shortcut.Classes);
            Assert.Equal(2, model.Documents.Count);
            Assert.All(model.Documents, document => Assert.False(document.HasText));

            var select = Assert.IsType<Button>(window.FindControl<Button>("SelectShownButton"));
            select.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var batch = Assert.IsType<Button>(window.FindControl<Button>("CreateSelectedOcrCopiesButton"));
            Assert.True(batch.IsEffectivelyVisible && batch.IsEnabled);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(batch.Command).ExecuteAsync(null);
            await WaitForPreviewAsync(model);
            Assert.All(model.OcrCopyBatchResults, item => Assert.Equal(OcrCopyOutcome.Added, item.Outcome));
            Assert.Equal(2, model.OcrCopyBatchResults.Count);

            var search = Assert.IsType<TextBox>(window.FindControl<TextBox>("SearchBox"));
            search.SetCurrentValue(TextBox.TextProperty, "text:present");
            await WaitForAsync(() => model.Documents.Count == 3 && model.Documents.All(document => document.HasText));
            Assert.DoesNotContain("current", shortcut.Classes);

            search.SetCurrentValue(TextBox.TextProperty, "text:unknown");
            await WaitForAsync(() => model.HasSearchQueryHint);
            Assert.Equal(3, model.Documents.Count);
            var hint = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("SearchQueryHint"));
            Assert.True(hint.IsEffectivelyVisible);
            Assert.Equal("Use one text filter: text:missing or text:present.", hint.Text);
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }
}
