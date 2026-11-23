using System.Collections.Frozen;
using System.Globalization;
using Avalonia.Markup.Xaml;

namespace Plico.Desktop;

/// <summary>Resolves interface labels and message templates from the installed desktop text catalog.</summary>
internal static class DesktopText
{
    private const string CatalogResource = "Plico.Desktop.InterfaceText";
    private const string FrenchCultureName = "fr";
    /// <summary>
    /// A key ending in this names a place's label in the sidebar, where a row has one line ("Inbox" as "Réception").
    /// It may repeat the place's English, so it stays out of the English index that code lookups use.
    /// </summary>
    private const string PlaceFormSuffix = "Place";
    private static readonly Lazy<Catalog> Strings = new(Load);
    private static bool _useFrench;

    internal static void SetLanguage(InterfaceLanguage language) => _useFrench = language switch
    {
        InterfaceLanguage.French => true,
        InterfaceLanguage.System => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == FrenchCultureName,
        _ => false
    };

    /// <summary>Whether the interface speaks French, so dates and numbers follow it.</summary>
    internal static bool IsFrench => _useFrench;

    internal static string ByKey(string key) => ByKey(key, _useFrench ? InterfaceLanguage.French : InterfaceLanguage.English);

    internal static string ByKey(string key, InterfaceLanguage language) =>
        Strings.Value.ByKey.TryGetValue(key, out var entry) ? entry.Get(language == InterfaceLanguage.French)
            : throw new InvalidDataException($"Missing desktop text key {key}.");

    internal static string Translate(string english) =>
        _useFrench ? Strings.Value.ByEnglish.GetValueOrDefault(english)?.French ?? english : english;

    /// <summary>
    /// A name the person gave (a tag, a title) in the interface's quotation marks: “Tax” in English, « Impôts » in French,
    /// with the no-break space every French row of the catalog uses inside them, so they never part from the name.
    /// </summary>
    internal static string Quote(string name) => _useFrench ? $"«\u00A0{name}\u00A0»" : $"“{name}”";

    internal static string Format(FormattableString message) =>
        string.Format(_useFrench ? CultureInfo.GetCultureInfo(FrenchCultureName) : CultureInfo.CurrentCulture,
            Translate(message.Format), message.GetArguments());

    private static Catalog Load()
    {
        using var stream = typeof(DesktopText).Assembly.GetManifestResourceStream(CatalogResource)
            ?? throw new InvalidDataException("The desktop text catalog is unavailable.");
        using var reader = new StreamReader(stream);
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        var byEnglish = new Dictionary<string, Entry>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            string[] fields = line.Split('\t');
            if (fields.Length != 3) throw new InvalidDataException("The desktop text catalog contains an invalid row.");
            var entry = new Entry(fields[1], fields[2]);
            entries.Add(fields[0], entry);
            // Every other English label is unique, so a code lookup by English finds exactly one translation.
            if (!fields[0].EndsWith(PlaceFormSuffix, StringComparison.Ordinal)) byEnglish.Add(fields[1], entry);
        }
        return new Catalog(entries.ToFrozenDictionary(StringComparer.Ordinal), byEnglish.ToFrozenDictionary(StringComparer.Ordinal));
    }

    private sealed record Entry(string English, string French)
    {
        internal string Get(bool french) => french ? French : English;
    }

    private sealed record Catalog(FrozenDictionary<string, Entry> ByKey, FrozenDictionary<string, Entry> ByEnglish);
}

/// <summary>Supplies a translated desktop string to an Avalonia view.</summary>
public sealed class TrExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => DesktopText.ByKey(key);
}
