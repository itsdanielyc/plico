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
    public async Task SearchableCopyActionOpensTheNewDocumentInTheInbox()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Scanned Receipt.pdf");
            File.Copy(fixture, Path.Combine(library.PdfDirectory, "Scanner output.pdf"));
            Assert.Equal(1, library.Refresh(TestContext.Current.CancellationToken).Indexed);
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var source = Assert.IsType<DocumentSummary>(model.SelectedDocument);
            Assert.False(source.HasText);
            var action = Assert.IsType<Button>(window.FindControl<Button>("CreateSearchableCopyButton"));
            Assert.True(action.IsEffectivelyVisible);
            var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(action.Command);

            await command.ExecuteAsync(action.CommandParameter);
            await WaitForPreviewAsync(model);

            Assert.Equal("Searchable copy added to inbox.", model.Status);
            Assert.True(model.InboxSelected);
            Assert.Contains(model.Documents, document => document.Id == source.Id && !document.HasText);
            Assert.True(Assert.IsType<DocumentSummary>(model.SelectedDocument).HasText);
            Assert.True(action.IsEffectivelyVisible);
            Assert.Equal("Create OCR copy", action.Content);
        }
        finally
        {
            window.Close();
        }
    }
}
