using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task TagSuggestionsReuseTheCatalogLabelAndKeepKeyboardApplyExplicit()
    {
        const string suggestedTag = "Réparation";
        const string newTag = "Invoices";
        const string typedPrefix = "Home";
        using var temporary = new TemporaryLibrary();
        long selectedId;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            var token = TestContext.Current.CancellationToken;
            BatchFixtures.AddTwoDocuments(library, token);
            var documents = library.Find(new DocumentFilter(), token).Items;
            selectedId = documents[0].Id;
            library.AddTag(selectedId, "Réglé");
            library.AddTag(documents[1].Id, suggestedTag);
            library.AddTag(documents[1].Id, "Home records");
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var documents = Assert.IsType<ListBox>(window.FindControl<ListBox>("DocumentList"));
            documents.SetCurrentValue(ListBox.SelectedItemProperty, model.Documents.Single(document => document.Id == selectedId));
            await WaitForPreviewAsync(model);
            var field = Assert.IsType<AutoCompleteBox>(window.FindControl<Control>("TagBox"));
            var input = StartTypingTag(field, "re\u0301");
            await WaitForAsync(() => field.IsDropDownOpen);

            Assert.Equal(suggestedTag, Assert.Single(Assert.IsAssignableFrom<IEnumerable<string>>(field.ItemsSource)));
            PressTagKey(input, Key.Down);
            PressTagKey(input, Key.Enter);
            Assert.Equal(suggestedTag, model.TagInput);
            Assert.DoesNotContain(suggestedTag, model.SelectedDocument!.Tags);

            PressTagKey(input, Key.Enter);
            Assert.Contains(suggestedTag, model.SelectedDocument!.Tags);
            Assert.Empty(model.TagInput);

            StartTypingTag(field, newTag);
            await WaitForAsync(() => model.TagInput == newTag);
            Assert.False(field.IsDropDownOpen);
            PressTagKey(input, Key.Enter);
            Assert.Contains(newTag, model.SelectedDocument!.Tags);

            StartTypingTag(field, typedPrefix);
            await WaitForAsync(() => field.IsDropDownOpen);
            var add = Assert.IsType<Button>(window.FindControl<Button>("AddTagButton"));
            var center = new Point(add.Bounds.Width / 2, add.Bounds.Height / 2);
            var position = Assert.IsType<Point>(add.TranslatePoint(center, window));
            window.MouseDown(position, MouseButton.Left);
            window.MouseUp(position, MouseButton.Left);
            Assert.False(field.IsDropDownOpen);
            Assert.Contains(typedPrefix, model.SelectedDocument!.Tags);
            await WaitForPreviewAsync(model);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task BatchTagSuggestionsAreBoundedAndFollowTheOpenLibrary()
    {
        const int suggestionLimit = 8;
        const string alternativeTag = "Cases filed";
        var expected = Enumerable.Range(0, suggestionLimit).Select(number => $"Case {number:00}").ToArray();
        using var first = new TemporaryLibrary();
        using var second = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        using (var library = InboxLibrary.Open(first.Path))
        {
            BatchFixtures.AddTwoDocuments(library, token);
            long id = library.Find(new DocumentFilter(), token).Items[0].Id;
            foreach (string label in expected.Append("Case 08")) library.AddTag(id, label);
        }
        using (var library = InboxLibrary.Open(second.Path))
        {
            BatchFixtures.AddTwoDocuments(library, token);
            library.AddTag(library.Find(new DocumentFilter(), token).Items[0].Id, alternativeTag);
        }
        using var model = new InboxViewModel(new FixtureDialogs { FolderPath = second.Path });
        model.InitializeLibrary(() => first.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var select = Assert.IsType<Button>(window.FindControl<Button>("SelectShownButton"));
            var field = Assert.IsType<AutoCompleteBox>(window.FindControl<Control>("BatchTagBox"));
            select.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var input = StartTypingTag(field, "case");
            await WaitForAsync(() => field.IsDropDownOpen);
            Assert.Equal(expected, Assert.IsAssignableFrom<IEnumerable<string>>(field.ItemsSource));

            PressTagKey(input, Key.Down);
            PressTagKey(input, Key.Enter);
            Assert.Equal(expected[0], model.BatchTagInput);
            Assert.Single(model.Documents, document => document.Tags.Contains(expected[0]));
            PressTagKey(input, Key.Enter);
            await WaitForAsync(() => model.Status == "Tag applied to 2 documents." && model.HasBatchSelection);
            Assert.All(model.Documents, document => Assert.Contains(expected[0], document.Tags));

            StartTypingTag(field, "case");
            await WaitForAsync(() => field.IsDropDownOpen);
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Assert.Empty(model.BatchTagInput);
            Assert.False(field.IsDropDownOpen);
            select.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            StartTypingTag(field, "case");
            await WaitForAsync(() => field.IsDropDownOpen);
            Assert.Equal(alternativeTag, Assert.Single(Assert.IsAssignableFrom<IEnumerable<string>>(field.ItemsSource)));
            PressTagKey(input, Key.Escape);
            Assert.False(field.IsDropDownOpen);
            await WaitForPreviewAsync(model);
        }
        finally
        {
            window.Close();
        }
    }

    private static TextBox StartTypingTag(AutoCompleteBox field, string text)
    {
        field.ApplyTemplate();
        var input = Assert.Single(field.GetVisualDescendants().OfType<TextBox>());
        Assert.True(input.Focus());
        input.SetCurrentValue(TextBox.TextProperty, text);
        return input;
    }

    private static void PressTagKey(TextBox input, Key key) => input.RaiseEvent(new KeyEventArgs
    {
        RoutedEvent = InputElement.KeyDownEvent,
        Key = key
    });
}
