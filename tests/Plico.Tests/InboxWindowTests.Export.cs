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
    public async Task PreviewExportsOnePdfDirectlyToTheChosenFolder()
    {
        using var temporary = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        }
        var dialogs = new FixtureDialogs();
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 900, Height = 590 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var selected = Assert.IsType<DocumentSummary>(model.SelectedDocument);
            var preview = model.Preview;
            var export = Assert.IsType<Button>(window.FindControl<Button>("ExportPdfButton"));
            Assert.True(export.IsEffectivelyVisible);
            Assert.True(export.IsEnabled);
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(export.Command);

            await command.ExecuteAsync(export.CommandParameter);
            Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
            Assert.Null(model.LatestExportLocation);

            dialogs.FolderPath = destination.Path;
            string occupied = Path.Combine(destination.Path, Path.GetFileName(selected.RelativePath));
            File.WriteAllText(occupied, "An existing file");
            await command.ExecuteAsync(export.CommandParameter);
            string name = $"{Path.GetFileNameWithoutExtension(selected.RelativePath)} (2).pdf";
            string copy = Path.Combine(destination.Path, name);
            string original = Path.Combine(temporary.Path, "PDFs", selected.RelativePath);
            Assert.Equal(File.ReadAllBytes(original), File.ReadAllBytes(copy));
            Assert.Equal("An existing file", File.ReadAllText(occupied));
            Assert.Equal(2, Directory.EnumerateFileSystemEntries(destination.Path).Count());
            Assert.Equal(selected.Id, model.SelectedDocument?.Id);
            Assert.Same(preview, model.Preview);

            var open = Assert.IsType<Button>(window.FindControl<Button>("OpenExportFolderButton"));
            Assert.True(open.IsEffectivelyVisible);
            open.Command!.Execute(null);
            Assert.Equal(Path.GetFileName(destination.Path), Path.GetFileName(dialogs.LastOpenedPath));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task BatchExportOpensTheNewFolderAndKeepsTheSelection()
    {
        using var temporary = new TemporaryLibrary();
        using var anotherLibrary = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        }
        var dialogs = new FixtureDialogs { FolderPath = destination.Path };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var export = Assert.IsType<Button>(window.FindControl<Button>("ExportSelectedPdfsButton"));
            Assert.False(export.IsEffectivelyVisible);
            var select = Assert.IsType<Button>(window.FindControl<Button>("SelectShownButton"));
            select.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(export.IsEffectivelyVisible);
            Assert.True(export.IsEnabled);

            await Assert.IsAssignableFrom<IAsyncRelayCommand>(export.Command).ExecuteAsync(export.CommandParameter);

            Assert.True(model.HasBatchSelection);
            var open = Assert.IsType<Button>(window.FindControl<Button>("OpenExportFolderButton"));
            Assert.True(open.IsVisible);
            Assert.NotNull(open.Command);
            open.Command!.Execute(null);
            string exported = Assert.Single(Directory.EnumerateDirectories(destination.Path));
            Assert.Equal(Path.GetFileName(exported), Path.GetFileName(dialogs.LastOpenedPath));
            Assert.True(Directory.Exists(dialogs.LastOpenedPath));
            Assert.Equal(2, Directory.EnumerateFiles(exported, "*.pdf").Count());

            dialogs.FolderPath = anotherLibrary.Path;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Assert.False(open.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
