using System.Globalization;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed class FilingSuggestionsTests
{
    [Fact]
    public void ExtractsLocaleOrderedDatesAndValidatesTheIbanChecksum()
    {
        const string text = """
            Invoice date: 03/04/2026
            Due: 31 May 2026
            Total due: £1,248.19
            IBAN GB81 WEST 1234 5698 7654 32
            IBAN GB82 WEST 1234 5698 7654 32
            """;
        var fields = FilingFieldExtractor.Read(text, CultureInfo.GetCultureInfo("en-GB"));
        Assert.Equal([new DateOnly(2026, 4, 3), new DateOnly(2026, 3, 4)], fields.DocumentDates);
        Assert.Equal(new DateOnly(2026, 5, 31), Assert.Single(fields.DueDates));
        Assert.Equal(new FilingAmount(1248.19m, "GBP"), fields.Amount);
        Assert.Equal("GB82WEST12345698765432", fields.Iban);
    }

    [Theory]
    [InlineData("fr-FR", "Date de facture : 28 février 2026\nÉchéance : 15 mars 2026\nTotal TTC : 1 237,00 EUR")]
    [InlineData("de-DE", "Rechnungsdatum: 28. Februar 2026\nFällig: 15. März 2026\nGesamtbetrag: 1.237,00 €")]
    [InlineData("es-ES", "Fecha de factura: 28 de febrero de 2026\nVencimiento: 15 de marzo de 2026\nImporte total: 1.237,00 €")]
    public void DetectsDatesAndAmountsInTheBundledEuropeanLanguages(string locale, string text)
    {
        var fields = FilingFieldExtractor.Read(text, CultureInfo.GetCultureInfo(locale));
        Assert.Equal(new DateOnly(2026, 2, 28), Assert.Single(fields.DocumentDates));
        Assert.Equal(new DateOnly(2026, 3, 15), Assert.Single(fields.DueDates));
        Assert.Equal(new FilingAmount(1237.00m, "EUR"), fields.Amount);
    }

    [Theory]
    [InlineData("Total -125.50 EUR", "EUR")]
    [InlineData("Total -£125.50", "GBP")]
    [InlineData("Total: £-125.50", "GBP")]
    public void KeepsTheSignOfACreditWhenItsMinusFollowsTheTotalLabel(string text, string currency)
    {
        var fields = FilingFieldExtractor.Read(text, CultureInfo.GetCultureInfo("en-GB"));
        Assert.Equal(new FilingAmount(-125.50m, currency), fields.Amount);
    }

    [Theory]
    [InlineData(OcrLanguage.Italian, "28 febbraio 2026", "Scadenza: 15 marzo 2026")]
    [InlineData(OcrLanguage.Portuguese, "28 de fevereiro de 2026", "Vencimento: 15 de março de 2026")]
    [InlineData(OcrLanguage.Dutch, "28 februari 2026", "Vervaldatum: 15 maart 2026")]
    [InlineData(OcrLanguage.Polish, "28 lutego 2026", "Termin płatności: 15 marca 2026")]
    public void FilingDatesUseTheRecognizedLanguageOnAnEnglishDesktop(OcrLanguage language, string issued, string due)
    {
        using var directory = new TemporaryLibrary();
        using var library = InboxLibrary.Open(directory.Path);
        var catalog = new LibraryCatalog(Path.Combine(directory.Path, ".plico", "catalog.sqlite3"));
        long id = catalog.Upsert("Invoice.pdf", "Invoice", new PdfText(issued + "\n" + due, 1, true),
            new FileVersion(1, 100, true), new byte[32], recognizedLanguage: language);
        var locale = CultureInfo.GetCultureInfo("en-US");
        var filing = Assert.Single(library.SuggestFiling([id], OcrLanguage.English, locale, TestContext.Current.CancellationToken));
        Assert.Equal(new DateOnly(2026, 2, 28), Assert.Single(filing.Fields.DocumentDates));
        Assert.Equal(new DateOnly(2026, 3, 15), Assert.Single(filing.Fields.DueDates));
        Assert.Equal(filing.Fields.DueDates, library.SuggestDueDates(id, OcrLanguage.English, locale));
    }

    [Fact]
    public void RulesAndReviewedExamplesSuggestTagsWithoutFilingUntilConfirmed()
    {
        var token = TestContext.Current.CancellationToken;
        using var directory = new TemporaryLibrary();
        var catalog = new LibraryCatalog(Path.Combine(directory.Path, ".plico", CreateCatalog(directory.Path)));
        const string learned = "Electrical";
        for (int i = 0; i < 4; i++)
        {
            long invoice = Add(catalog, $"Supplier {i}", "electrical breaker cable circuit");
            catalog.AddTag(invoice, learned);
            catalog.SetReviewed(invoice, true);
            long receipt = Add(catalog, $"Shop {i}", "coffee coffee book newspaper");
            catalog.SetReviewed(receipt, true);
        }
        long target = Add(catalog, "Current invoice", "Invoice date: 2026-04-03\n electrical breaker cable circuit quote");
        for (int i = 0; i < 4; i++)
        {
            long unreviewed = Add(catalog, $"Unreviewed example {i}", "electrical breaker cable circuit");
            catalog.AddTag(unreviewed, "Incorrect");
        }
        using var library = InboxLibrary.Open(directory.Path);
        library.SaveFilingRule(new(null, "Quotes", "quote", FilingRuleMatch.AnyWord));

        var suggestions = Assert.Single(library.SuggestFiling([target], OcrLanguage.English, CultureInfo.InvariantCulture, token));
        Assert.Contains(suggestions.Tags, suggestion => suggestion == new FilingTag("Quotes", FilingTagSource.Rule));
        Assert.Contains(suggestions.Tags, suggestion => suggestion == new FilingTag(learned, FilingTagSource.ReviewedDocuments));
        Assert.DoesNotContain(suggestions.Tags, suggestion => suggestion.Tag == "Incorrect");
        Assert.Empty(catalog.Get(target).Tags);
        library.ApplyFilingSuggestions([new(target, suggestions.Fields.DocumentDates[0], [learned])], token);
        Assert.Equal(learned, Assert.Single(catalog.Get(target).Tags));
        Assert.Equal(new DateOnly(2026, 4, 3), catalog.Get(target).DocumentDate);
        library.AddTag(target, "Quotes");
        library.RenameTag("Quotes", "Estimates", cancellationToken: token);
        Assert.Equal("Estimates", Assert.Single(library.GetFilingRules()).Tag);
    }

    private static string CreateCatalog(string directory)
    {
        Directory.CreateDirectory(Path.Combine(directory, ".plico"));
        return "catalog.sqlite3";
    }

    private static long Add(LibraryCatalog catalog, string title, string body) =>
        catalog.Upsert(title + ".pdf", title, new PdfText(body, 1, true), new FileVersion(1, 100, true), new byte[32]);
}
