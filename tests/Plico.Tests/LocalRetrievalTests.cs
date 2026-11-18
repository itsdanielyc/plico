using Plico.Core;
using System.Net;
using Xunit;

namespace Plico.Tests;

public sealed class LocalRetrievalTests
{
    [Fact]
    public async Task SemanticSearchReusesVectorsAndReembedsAChangedDocument()
    {
        using var directory = new TemporaryLibrary();
        using var library = InboxLibrary.Open(directory.Path);
        var token = TestContext.Current.CancellationToken;
        var lease = await library.ImportAsync(Fixture("Lease Renewal.pdf"), token);
        var ledger = await library.ImportAsync(Fixture("Match Ledger.pdf"), token);
        var fake = new CountingModel();
        var scored = await library.SearchSemanticallyAsync("Northstar agreement", fake, cancellationToken: token);
        Assert.Equal(lease.Id, scored[0].Document.Id);
        Assert.True(scored[0].Score > scored[^1].Score);
        int first = fake.Documents;
        Assert.True(first >= 2);
        await library.SearchSemanticallyAsync("Another query", fake, cancellationToken: token);
        Assert.Equal(first, fake.Documents);
        library.SetTitle(ledger.Id, "Updated Northstar ledger");
        await library.SearchSemanticallyAsync("Northstar ledger", fake, cancellationToken: token);
        Assert.True(fake.Documents > first);
        Assert.True(fake.Documents < first * 2);
    }

    [Fact]
    public void RelatedDocumentsCanRankWithNoLocalModel()
    {
        using var directory = new TemporaryLibrary();
        using (InboxLibrary.Open(directory.Path)) { }
        var catalog = new LibraryCatalog(Path.Combine(directory.Path, ".plico", "catalog.sqlite3"));
        long source = Add("Lease", "northstar lease renewal landlord property tenancy deposit agreement");
        long relevant = Add("Property renewal", "northstar lease renewal property landlord deposit agreement");
        Add("Unrelated", "bread flour grocer quantity receipt payment");
        using var library = InboxLibrary.Open(directory.Path);
        var result = Assert.Single(library.FindRelatedDocuments(source, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(relevant, result.Document.Id);
        long Add(string title, string body) =>
            catalog.Upsert(title + ".pdf", title, new PdfText(body, 1, true), new FileVersion(1, 1024, true), new byte[32]);
    }

    [Theory]
    [InlineData("https://ollama.com/v1")]
    [InlineData("http://localhost.example:11434")]
    [InlineData("http://localhost:11434/redirect")]
    [InlineData("http://localhost:11434?host=other")]
    [InlineData("http://10.0.0.8:11434")]
    public void LocalModelEndpointRejectsNonLoopbackOrAmbiguousAddresses(string endpoint) =>
        Assert.Throws<ArgumentException>(() => LocalModelSettings.ValidateEndpoint(endpoint));

    [Fact]
    public async Task BoundedProviderUsesOnlyTheConfiguredLoopbackRouteAndOrdersEmbeddings()
    {
        var capture = new ModelHandler();
        using var client = new LocalModelClient(new(true, "http://localhost:11434/v1", "local-chat", "local-embed", true), capture);
        var vectors = await client.EmbedAsync(["first", "second"], EmbeddingInput.Document, TestContext.Current.CancellationToken);
        Assert.Equal(new float[] { 1, 0 }, vectors[0]);
        Assert.Equal(new float[] { 0, 1 }, vectors[1]);
        Assert.Equal("http://127.0.0.1:11434/v1/embeddings", capture.Target);
        Assert.Contains("passage: first", capture.Payload, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => new LocalModelClient(LocalModelSettings.Default));
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    internal sealed class CountingModel : ILocalEmbeddingModel
    {
        public string CacheKey => "fixture-model";
        internal int Documents { get; private set; }

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> text, EmbeddingInput input, CancellationToken cancellationToken)
        {
            if (input == EmbeddingInput.Document) Documents += text.Count;
            return Task.FromResult<IReadOnlyList<float[]>>(text.Select(item => input == EmbeddingInput.Query ||
                item.Contains("northstar", StringComparison.OrdinalIgnoreCase) ? new float[] { 3, 0 } : new float[] { 0, 2 }).ToArray());
        }
    }

    private sealed class ModelHandler : HttpMessageHandler
    {
        internal string Target { get; private set; } = "";
        internal string Payload { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Target = request.RequestUri!.AbsoluteUri;
            Payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[{"index":1,"embedding":[0,1]},{"index":0,"embedding":[1,0]}]}""")
            };
        }
    }
}
