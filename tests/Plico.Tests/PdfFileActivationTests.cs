using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed class PdfFileActivationTests
{
    [Fact]
    public async Task PdfActivationRelaysOrderedUniquePathsToTheExistingWindow()
    {
        using var temporary = new TemporaryLibrary();
        string first = Path.Combine(temporary.Path, "Household lease signé.pdf");
        string second = Path.Combine(temporary.Path, "Vehicle invoice.PDF");
        string other = Path.Combine(temporary.Path, "notes.txt");
        string pipe = $"plico-test-{Guid.NewGuid():N}";
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var relay = new PdfFileActivation(pipe, paths => received.TrySetResult(paths));

        Assert.True(await PdfFileActivation.ForwardAsync(pipe, [first, other, first, second]));
        Assert.Equal([first, second], await received.Task.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Empty(PdfFileActivation.ReadPaths(["--verify", other, first]));
    }
}
