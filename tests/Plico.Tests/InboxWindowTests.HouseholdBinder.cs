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
    public async Task HouseholdBinderExportsTheReorderedSelectionWithoutAddingItToTheLibrary()
    {
        using var temporary = new TemporaryLibrary();
        string libraryPath = Directory.CreateDirectory(Path.Combine(temporary.Path, "Library")).FullName;
        string destination = Directory.CreateDirectory(Path.Combine(temporary.Path, "Export")).FullName;
        using (var library = InboxLibrary.Open(libraryPath))
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);

        var dialogs = new FixtureDialogs();
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => libraryPath);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var action = Assert.IsType<Button>(window.FindControl<Button>("ExportHouseholdBinderButton"));
            Assert.False(action.IsEffectivelyVisible);
            var select = Assert.IsType<Button>(window.FindControl<Button>("SelectShownButton"));
            select.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(action.IsEffectivelyVisible);
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(action.Command);
            await command.ExecuteAsync(action.CommandParameter);
            Assert.Empty(Directory.EnumerateFileSystemEntries(destination));

            var shownOrder = model.Documents.Select(document => document.Id).ToArray();
            dialogs.BinderChoice = selected =>
            {
                Assert.Equal(shownOrder, selected.Select(document => document.Id));
                return Task.FromResult<CombinePdfsRequest?>(new("Household documents", shownOrder.Reverse().ToArray()));
            };
            dialogs.FolderPath = destination;
            await command.ExecuteAsync(action.CommandParameter);

            Assert.True(File.Exists(Path.Combine(destination, "Household documents.pdf")), model.Status);
            Assert.Equal(destination, model.LatestExportLocation);
            Assert.True(model.HasBatchSelection);
            Assert.Equal(shownOrder, model.Documents.Select(document => document.Id));
        }
        finally
        {
            window.Close();
        }
    }
}
