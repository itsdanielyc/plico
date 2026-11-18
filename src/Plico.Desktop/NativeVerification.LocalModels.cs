using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyLocalModelsAsync(Window window, InboxViewModel model, FixtureDialogs dialogs, string evidence)
    {
        Require(!model.HasLocalChat && !model.HasSemanticSearch, "Local model features were active before opt-in.");
        var previous = model.SelectedDocument is { HasText: true } selected ? selected :
            model.Documents.First(document => document.HasText);
        model.SelectedDocument = previous;
        var preferences = new LocalModelPreference(Path.Combine(model.LibraryPath, "Settings"));
        var fixture = new NativeModelFixture();
        model.InitializeLocalModels(preferences, _ => new(fixture, fixture));
        dialogs.LocalModelChoice = _ => Task.FromResult<LocalModelSettings?>(new(true, "http://127.0.0.1:11434", "fixture", "fixture"));
        dialogs.SemanticQueryChoice = _ => Task.FromResult<string?>("Lease renewal");
        dialogs.SimilarityChoice = async (title, matches) =>
        {
            Require(matches.Count > 0, "The native semantic query returned no matches.");
            var results = new SimilarityResultsDialog(title, matches);
            var choice = results.ShowDialog<DocumentSimilarityMatch?>(window);
            try
            {
                await CaptureAsync(results, evidence, "semantic-results.png");
                results.Close();
                return await choice;
            }
            finally { if (results.IsVisible) results.Close(); }
        };
        dialogs.LocalFilingChoice = async (proposal, definitions) =>
        {
            Require(proposal.Edit.Title == NativeModelFixture.FilingTitle, "The model proposal did not reach the review dialog.");
            Require(model.SelectedDocument?.Title == previous.Title, "The proposal changed the document before confirmation.");
            var review = new LocalFilingDialog(proposal, definitions);
            var result = review.ShowDialog<LocalFilingEdit?>(window);
            try
            {
                await CaptureAsync(review, evidence, "local-filing.png");
                review.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ApplyLocalFilingButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return await result;
            }
            finally { if (review.IsVisible) review.Close(); }
        };
        dialogs.LocalChatChoice = async input =>
        {
            var answer = await input.Ask(new("What is this paperwork?", false, []), CancellationToken.None);
            Require(answer.Text == NativeModelFixture.Answer && answer.Sources.Single().Id == previous.Id,
                "Local chat did not return an answer with the chosen document as context.");
        };
        try
        {
            await model.ConfigureLocalModelsCommand.ExecuteAsync(null);
            await model.SearchSemanticallyCommand.ExecuteAsync(null);
            var filing = (IAsyncRelayCommand)Control<Button>(window, "SuggestLocalFilingButton").Command!;
            await filing.ExecuteAsync(null);
            Require(model.Status == "Local filing saved.", model.Status);
            await model.AskLocalModelCommand.ExecuteAsync(null);
            Require(model.Status == "Local assistant closed.", model.Status);
            await model.UndoLocalFilingCommand.ExecuteAsync(null);
            Require(model.SelectedDocument?.Title == previous.Title, "Undo did not restore the original title.");
        }
        finally
        {
            dialogs.LocalModelChoice = null;
            dialogs.SemanticQueryChoice = null;
            dialogs.SimilarityChoice = null;
            dialogs.LocalFilingChoice = null;
            dialogs.LocalChatChoice = null;
        }
    }

    private sealed class NativeModelFixture : ILocalLanguageModel, ILocalEmbeddingModel
    {
        internal const string FilingTitle = "Reviewed lease";
        internal const string Answer = "A document about the tenancy [1].";

        public string CacheKey => "native-model-fixture";
        public Task<string> CompleteAsync(IReadOnlyList<LocalModelMessage> messages, bool jsonObject, CancellationToken cancellationToken) =>
            Task.FromResult(jsonObject ? """{"title":"Reviewed lease","tags":["agreements"]}""" : Answer);

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> text, EmbeddingInput input, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<float[]>>(text.Select(item => new float[] { 1, 0 }).ToArray());
    }
}
