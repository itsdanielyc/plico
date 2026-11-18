using Plico.Core;
using UglyToad.PdfPig;
using Xunit;

namespace Plico.Tests;

public sealed class ExpandedOcrTests
{
    [Theory]
    [InlineData("Rotate: 90\nOrientation confidence: 19.25", 90)]
    [InlineData("Rotate: 270\r\nOrientation confidence: 15.02", 270)]
    [InlineData("Rotate: 180\nOrientation confidence: 3.5", 0)]
    [InlineData("Rotate: 45\nOrientation confidence: 99", 0)]
    public void OrientationResultRequiresAConfidentQuarterTurn(string result, int expectedRotation) =>
        Assert.Equal(expectedRotation, TesseractOrientation.ReadRotation(result));

    [Fact]
    public async Task AutoOrientationTurnsTheVisiblePageBeforeRecognition()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        string original = Path.Combine(AppContext.BaseDirectory, "fixtures", "Sideways orientation.png");
        library.ConfigureOcr(new(AutoOrient: true));
        var result = await library.ImportFileAsync(original, token);
        using var pdf = PdfDocument.Open(library.ResolvePdf(result.Document));
        var page = pdf.GetPage(1);
        Assert.True(page.Height > page.Width, $"Expected a portrait PDF, got {page.Width} × {page.Height}.");
        Assert.Contains("original certificate", library.ReadIndexedText(result.Document.Id), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OcrCopiesReuseLegacyKeysAndSeparateRecognitionSettings()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Scanned Receipt.pdf");
        var token = TestContext.Current.CancellationToken;
        var source = await library.ImportFileAsync(fixture, token);

        var legacy = await library.CreateSearchableCopyAsync(source.Document.Id, token);
        var repeat = await library.CreateSearchableCopyAsync(source.Document.Id, token);
        Assert.Equal(legacy.Document.Id, repeat.Document.Id);
        Assert.False(repeat.Added);

        library.ConfigureOcr(new(AutoOrient: true));
        var oriented = await library.CreateSearchableCopyAsync(source.Document.Id, token);
        Assert.True(oriented.Added);
        Assert.NotEqual(legacy.Document.Id, oriented.Document.Id);
        library.ConfigureOcr(OcrProcessingOptions.Default);
        Assert.Equal(legacy.Document.Id, (await library.CreateSearchableCopyAsync(source.Document.Id, token)).Document.Id);

        if (OcrProcessingOptions.RapidOcrAvailable)
        {
            library.ConfigureOcr(new(OcrEngineKind.RapidOcr));
            var rapid = await library.CreateSearchableCopyAsync(source.Document.Id, token);
            Assert.True(rapid.Added);
            Assert.NotEqual(legacy.Document.Id, rapid.Document.Id);
            Assert.Contains("WRENCH470", library.ReadIndexedText(rapid.Document.Id), StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Throws<PlatformNotSupportedException>(() => library.ConfigureOcr(new(OcrEngineKind.RapidOcr)));
        }
    }

    [Theory]
    [InlineData(OcrLanguage.Italian)]
    [InlineData(OcrLanguage.Portuguese)]
    [InlineData(OcrLanguage.Dutch)]
    [InlineData(OcrLanguage.Polish)]
    public async Task NewBundledLanguagesProduceSearchableCopies(OcrLanguage language)
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string original = Path.Combine(AppContext.BaseDirectory, "fixtures", "Hardware Store Receipt.png");
        var result = await library.ImportFileAsync(original, TestContext.Current.CancellationToken, language);
        Assert.True(result.Document.HasText);
        Assert.Contains("WRENCH470", library.ReadIndexedText(result.Document.Id), StringComparison.OrdinalIgnoreCase);
    }
}
