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
    public async Task TagRenameDialogRequiresAnExplicitMergeForAnExistingTag()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new TagRenameDialog("Property", ["Property", "Archive"]);
        var closed = dialog.ShowDialog<TagRenameRequest?>(owner);
        try
        {
            var name = Assert.IsType<TextBox>(dialog.FindControl<TextBox>("TagNameBox"));
            var notice = Assert.IsType<TextBlock>(dialog.FindControl<TextBlock>("TagRenameNotice"));
            var save = Assert.IsType<Button>(dialog.FindControl<Button>("SaveTagButton"));
            name.Text = " ";
            await WaitForAsync(() => !save.IsEnabled);
            name.Text = "PROPERTY";
            await WaitForAsync(() => save.IsEnabled && save.IsDefault);
            Assert.Equal("Rename tag", save.Content);

            name.Text = "archive";
            await WaitForAsync(() => Equals(save.Content, "Merge tags"));

            Assert.True(notice.IsVisible);
            Assert.Contains("“Archive”", notice.Text);
            Assert.False(save.IsDefault);
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var request = Assert.IsType<TagRenameRequest>(await closed.WaitAsync(SearchTestDeadline, TestContext.Current.CancellationToken));
            Assert.True(request.MergeIntoExisting);
            Assert.Equal("archive", request.Name);
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await closed;
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task RenamingTheActiveTagOpensItsNewSidebarFilter()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            var token = TestContext.Current.CancellationToken;
            BatchFixtures.AddTwoDocuments(library, token);
            foreach (var document in library.Find(new DocumentFilter(), token).Items) library.AddTag(document.Id, "Property");
        }
        var dialogs = new FixtureDialogs { TagRenameChoice = new("Housing", MergeIntoExisting: false) };
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var rename = Assert.IsType<Button>(window.FindControl<Button>("RenameActiveTagButton"));
            Assert.False(rename.IsVisible);
            model.FilterByTag("Property");
            await WaitForPreviewAsync(model);
            long? selectedId = model.SelectedDocument?.Id;
            Assert.True(rename.IsVisible);
            Assert.True(rename.IsEnabled);

            await Assert.IsAssignableFrom<IAsyncRelayCommand>(rename.Command).ExecuteAsync(rename.CommandParameter);

            Assert.Equal("Housing", model.Heading);
            Assert.Equal("Housing", Assert.Single(model.Tags));
            Assert.Equal(2, model.Documents.Count);
            Assert.Equal(selectedId, model.SelectedDocument?.Id);
            Assert.All(model.Documents, document => Assert.Equal("Housing", Assert.Single(document.Tags)));
        }
        finally
        {
            window.Close();
        }
    }
}
