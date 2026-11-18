using Plico.Core;
using Plico.Desktop;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace Plico.Tests;

public sealed class OcrLanguageTests
{
    [Theory]
    [InlineData("Facture française.png", OcrLanguage.French, "Règlement")]
    [InlineData("Deutsche Rechnung.png", OcrLanguage.German, "Überweisung")]
    [InlineData("Spanish Receipt.png", OcrLanguage.Spanish, "pequeño")]
    public async Task BundledLanguagesProduceSearchablePdfs(string filename, OcrLanguage language, string accentedWord)
    {
        using var directory = new TemporaryLibrary();
        using var library = InboxLibrary.Open(directory.Path);
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", filename);
        var imported = await library.ImportFileAsync(fixture, TestContext.Current.CancellationToken, language);
        using var pdf = PdfDocument.Open(library.ResolvePdf(imported.Document));

        Assert.Contains(accentedWord, ContentOrderTextExtractor.GetText(pdf.GetPage(1)), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(imported.Document.Id, Assert.Single(library.Find(new DocumentFilter(Query: accentedWord), TestContext.Current.CancellationToken).Items).Id);
    }

    [Theory]
    [InlineData(OcrLanguage.German)]
    [InlineData(OcrLanguage.Spanish)]
    public void OcrLanguageChoiceReopensFromLocalSettings(OcrLanguage language)
    {
        using var directory = new TemporaryLibrary();
        var preference = new OcrLanguagePreference(directory.Path);
        Assert.Equal(OcrLanguage.English, preference.Load());
        preference.Save(language);

        Assert.Equal(language, new OcrLanguagePreference(directory.Path).Load());
    }
}
