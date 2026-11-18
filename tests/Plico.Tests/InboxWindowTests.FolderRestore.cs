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
    public async Task IndexReportRestoresFromAFolderAndShowsTheBlockedPath()
    {
        const string firstName = "A letter.pdf";
        const string secondName = "B letter.pdf";
        const string existing = "Existing file";
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        string backup = Directory.CreateDirectory(Path.Combine(temporary.Path, "Backup")).FullName;
        string first;
        long secondId;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
            first = Path.Combine(library.PdfDirectory, firstName);
            string second = Path.Combine(library.PdfDirectory, secondName);
            File.Copy(fixture, first);
            File.Copy(fixture, second);
            library.Refresh(token);
            secondId = Assert.Single(library.Find(new DocumentFilter(), token).Items, item => item.RelativePath == secondName).Id;
            library.AddTag(secondId, "Housing");
            File.Move(first, Path.Combine(backup, "Copy.pdf"));
            File.Delete(second);
            library.Refresh(token);
            File.WriteAllText(first, existing);
        }

        var dialogs = new FixtureDialogs { FolderPath = backup, IndexReportChoice = new IndexReportDecision.RestoreFolder() };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var show = Assert.IsType<Button>(window.FindControl<Button>("IndexReportButton"));
            Assert.True(show.IsVisible);
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(show.Command);
            await command.ExecuteAsync(null);
            Assert.Equal("1 document restored. 1 document unavailable. 1 restore notice.", model.Status);
            var restored = Assert.Single(model.Documents);
            Assert.Equal(secondId, restored.Id);
            Assert.Contains("Housing", restored.Tags);
            Assert.Equal(existing, File.ReadAllText(first));

            dialogs.IndexReportChoice = null;
            await command.ExecuteAsync(null);
            var report = Assert.IsType<IndexReport>(dialogs.InspectedIndexReport);
            Assert.Equal(firstName, Assert.Single(report.UnavailablePaths));
            string notice = Assert.Single(report.Notices);
            Assert.Contains(firstName, notice, StringComparison.Ordinal);
            Assert.Contains("already occupies", notice, StringComparison.Ordinal);
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }
}
