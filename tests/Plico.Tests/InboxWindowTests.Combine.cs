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
    public async Task CombiningTheSelectionOpensANewInboxDocument()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        }
        var dialogs = new FixtureDialogs();
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var combine = Assert.IsType<Button>(window.FindControl<Button>("CombinePdfsButton"));
            Assert.False(combine.IsEffectivelyVisible);

            var select = Assert.IsType<Button>(window.FindControl<Button>("SelectShownButton"));
            select.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(combine.IsEffectivelyVisible);
            Assert.True(combine.IsEnabled);
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(combine.Command);
            await command.ExecuteAsync(combine.CommandParameter);
            Assert.Equal("PDF combination closed.", model.Status);
            Assert.True(model.HasBatchSelection);
            Assert.Equal(2, model.Documents.Count);

            var shownOrder = model.Documents.Select(document => document.Id).ToArray();
            dialogs.CombineChoice = selected =>
            {
                Assert.Equal(shownOrder, selected.Select(document => document.Id));
                return Task.FromResult<CombinePdfsRequest?>(new("Combined lease", selected.Reverse().Select(document => document.Id).ToArray()));
            };
            await command.ExecuteAsync(combine.CommandParameter);
            await WaitForPreviewAsync(model);
            Assert.IsType<DocumentSummary>(model.SelectedDocument);
            Assert.Equal("Combined lease", model.SelectedDocument!.Title);
            Assert.Equal(2, model.SelectedDocument.PageCount);
            Assert.True(model.InboxSelected);
            Assert.Equal(3, model.Documents.Count);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CombineDialogReturnsTheReorderedSourcesAndValidatedTitle()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        BatchFixtures.AddTwoDocuments(library, token);
        var sources = library.Find(new DocumentFilter(), token).Items;
        var owner = new Window();
        owner.Show();
        var dialog = new CombinePdfsDialog(sources);
        var completion = dialog.ShowDialog<CombinePdfsRequest?>(owner);
        try
        {
            var up = Assert.IsType<Button>(dialog.FindControl<Button>("MoveUpButton"));
            var down = Assert.IsType<Button>(dialog.FindControl<Button>("MoveDownButton"));
            Assert.False(up.IsEnabled);
            Assert.True(down.IsEnabled);
            down.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(up.IsEnabled);
            Assert.False(down.IsEnabled);

            var title = Assert.IsType<TextBox>(dialog.FindControl<TextBox>("TitleBox"));
            var confirm = Assert.IsType<Button>(dialog.FindControl<Button>("ConfirmCombineButton"));
            title.Text = "  ";
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Assert.IsType<TextBlock>(dialog.FindControl<TextBlock>("ValidationMessage")).IsVisible);
            title.Text = "  Lease packet  ";
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var request = Assert.IsType<CombinePdfsRequest>(await completion.WaitAsync(token));
            Assert.Equal("Lease packet", request.Title);
            Assert.Equal(sources.Reverse().Select(document => document.Id), request.DocumentIds);
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await completion;
            owner.Close();
        }
    }
}
