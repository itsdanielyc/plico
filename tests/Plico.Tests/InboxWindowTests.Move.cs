using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
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
