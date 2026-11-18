namespace Plico.Core;

/// <summary>How words in the title or indexed text trigger a tag suggestion.</summary>
public enum FilingRuleMatch { Phrase, AnyWord, AllWords }

/// <summary>A library-wide tag suggestion rule. A null ID creates a rule.</summary>
public sealed record FilingRule(long? Id, string Tag, string Pattern, FilingRuleMatch Match);

/// <summary>The evidence behind a suggested tag.</summary>
public enum FilingTagSource { Rule, ReviewedDocuments }

/// <summary>A tag and the reason it was suggested.</summary>
public sealed record FilingTag(string Tag, FilingTagSource Source);

/// <summary>A labelled amount recognized in the document and its currency when printed.</summary>
public sealed record FilingAmount(decimal Value, string? Currency);

/// <summary>Possible document dates, deadlines, and payment details recognized from indexed text.</summary>
public sealed record FilingFields(IReadOnlyList<DateOnly> DocumentDates, IReadOnlyList<DateOnly> DueDates,
    FilingAmount? Amount, string? Iban);

/// <summary>Filing candidates for one catalog record. Current values stay in place until confirmed.</summary>
public sealed record FilingSuggestion(long DocumentId, string Title, DateOnly? CurrentDate, FilingFields Fields,
    IReadOnlyList<FilingTag> Tags);

/// <summary>Fields a reviewer confirmed. A null date keeps the current date.</summary>
public sealed record FilingSelection(long DocumentId, DateOnly? DocumentDate, IReadOnlyList<string> Tags);
