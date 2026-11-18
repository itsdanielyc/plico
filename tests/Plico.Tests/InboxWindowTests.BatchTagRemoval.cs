using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task BatchTagRemovalUsesTheSelectedPdfsAndRetainsTheSelection()
    {
        const string sharedTag = "Records";
        const string otherTag = "Keep";
        const string taggedTitle = "April lease";
        const string otherTitle = "May lease";
        const string outsideTitle = "June lease";
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, token);
            File.Copy(Path.Combine(library.PdfDirectory, $"{taggedTitle}.pdf"),
                Path.Combine(library.PdfDirectory, $"{outsideTitle}.pdf"));
            library.Refresh(token);
            var ids = library.Find(new DocumentFilter(), token).Items.ToDictionary(document => document.Title, document => document.Id);
            library.AddTagToDocuments([ids[taggedTitle], ids[outsideTitle]], sharedTag);
            library.AddTag(ids[otherTitle], otherTag);
        }

        var window = new MainWindow { Width = 900, Height = 590 };
        using var model = new InboxViewModel(new WorkspaceDialogs(window));
        model.InitializeLibrary(() => temporary.Path);
        window.DataContext = model;
        window.Show();
        try
        {
            var list = Assert.IsType<ListBox>(window.FindControl<ListBox>("DocumentList"));
            list.SelectedItems = model.Documents.Where(document => document.Title != outsideTitle).ToList();
            Assert.True(model.HasBatchSelection);
            var remove = Assert.IsType<Button>(window.FindControl<Button>("RemoveBatchTagButton"));
            Assert.True(remove.IsEffectivelyVisible);
            Assert.True(remove.IsEnabled);

            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => window.OwnedWindows.Count > 0);
            var dialog = Assert.Single(window.OwnedWindows);
            var confirm = Assert.IsType<Button>(dialog.FindControl<Button>("ConfirmTagRemovalButton"));
            Assert.False(confirm.IsEnabled);
            Assert.IsType<ListBox>(dialog.FindControl<ListBox>("BatchTagChoices")).SelectedIndex = 0;
            Assert.True(confirm.IsEnabled);
            Assert.IsType<Button>(dialog.FindControl<Button>("CancelTagRemovalButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => !model.IsBusy);
            Assert.Contains(sharedTag, model.Documents.Single(document => document.Title == taggedTitle).Tags);

            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => window.OwnedWindows.Count > 0);
            dialog = Assert.Single(window.OwnedWindows);
            var choices = Assert.IsType<ListBox>(dialog.FindControl<ListBox>("BatchTagChoices"));
            var options = choices.Items.Cast<BatchTagOption>().ToArray();
            Assert.Equal([new BatchTagOption(otherTag, 1), new BatchTagOption(sharedTag, 1)], options);
            choices.SelectedItem = options[1];
            Assert.IsType<Button>(dialog.FindControl<Button>("ConfirmTagRemovalButton"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => model.Status == "Tag removed from 1 document." && model.HasBatchSelection);
            Assert.Contains(otherTag, model.Documents.Single(document => document.Title == otherTitle).Tags);
            Assert.DoesNotContain(sharedTag, model.Documents.Single(document => document.Title == taggedTitle).Tags);
            Assert.Contains(sharedTag, model.Documents.Single(document => document.Title == outsideTitle).Tags);
            Assert.Equal([taggedTitle, otherTitle], list.Selection.SelectedItems.OfType<DocumentSummary>()
                .Select(document => document.Title).Order(StringComparer.Ordinal));
        }
        finally
        {
            foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
            await WaitForAsync(() => !model.IsBusy);
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }
}
