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
    public async Task BatchMoveActionFilesTheSelectionThroughOnePicker()
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
            var move = Assert.IsType<Button>(window.FindControl<Button>("MoveSelectedPdfsButton"));
            Assert.False(move.IsEffectivelyVisible);
            var select = Assert.IsType<Button>(window.FindControl<Button>("SelectShownButton"));
            select.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(move.IsEffectivelyVisible);
            Assert.True(move.IsEnabled);
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(move.Command);
            long? selectedId = model.SelectedDocument?.Id;
            await command.ExecuteAsync(move.CommandParameter);
            Assert.Equal("Move folder selection closed.", model.Status);
            Assert.True(model.HasBatchSelection);
            Assert.Equal(1, dialogs.FolderPickerCalls);

            string pdfs = Path.Combine(temporary.Path, "PDFs");
            dialogs.FolderPath = Directory.CreateDirectory(Path.Combine(pdfs, "Filed")).FullName;
            await command.ExecuteAsync(move.CommandParameter);
            await WaitForPreviewAsync(model);
            Assert.Equal(2, dialogs.FolderPickerCalls);
            Assert.Equal(pdfs, dialogs.LastFolderStart);
            Assert.Equal("Moved 2 documents.", model.Status);
            Assert.Equal(selectedId, model.SelectedDocument?.Id);
            Assert.NotNull(model.Preview);
            Assert.All(model.Documents, document => Assert.StartsWith("Filed/", document.RelativePath));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MoveActionKeepsThePdfOpenAtItsNewLocation()
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
            var move = Assert.IsType<Button>(window.FindControl<Button>("MovePdfButton"));
            Assert.True(move.IsEffectivelyVisible);
            Assert.True(move.IsEnabled);
            var document = Assert.IsType<DocumentSummary>(model.SelectedDocument);
            string pdfDirectory = Path.Combine(temporary.Path, "PDFs");
            string folder = Directory.CreateDirectory(Path.Combine(pdfDirectory, "Filed")).FullName;
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(move.Command);
            await command.ExecuteAsync(move.CommandParameter);
            Assert.Equal("Move folder selection closed.", model.Status);
            Assert.Equal(document.RelativePath, model.SelectedDocument?.RelativePath);
            dialogs.FolderPath = folder;
            await command.ExecuteAsync(move.CommandParameter);

            Assert.Equal(pdfDirectory, dialogs.LastFolderStart);
            Assert.Equal(document.Id, model.SelectedDocument?.Id);
            Assert.Equal("Filed/" + document.RelativePath, model.SelectedDocument?.RelativePath);
            Assert.NotNull(model.Preview);
            Assert.StartsWith("Moved to Filed/", model.Status);
        }
        finally
        {
            window.Close();
        }
    }
}
