using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Plico.Core;
using System.Globalization;

namespace Plico.Desktop;

internal sealed record MetadataEditInput(DocumentSummary Document, DocumentMetadata Metadata, MetadataDefinitions Definitions,
    IReadOnlyList<Correspondent> Suggestions, IReadOnlyList<MetadataLinkTarget> LinkTargets);

internal sealed record MetadataOption<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

internal sealed class DocumentMetadataDialog : Window
{
    private readonly ComboBox _type;
    private readonly List<(long Id, CheckBox Box)> _contacts = [];
    private readonly TextBox _notes;
    private readonly List<(long Id, Func<CustomFieldValue?> Read)> _fields = [];
    private readonly TextBlock _error = DialogLayout.Message();

    internal DocumentMetadataDialog(MetadataEditInput input)
    {
        Title = DesktopText.Translate("Document metadata");
        // The dialog is as tall as its fields, up to a height that leaves the window around it visible.
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 680;
        MinWidth = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var body = new StackPanel { Spacing = 14 };
        var definitions = input.Definitions;
        _type = Options(DesktopText.Translate("Document type"), [new(DesktopText.Translate("Unclassified"), null), .. definitions.DocumentTypes.Select(type =>
            new MetadataOption<long?>(type.Name, type.Id))], input.Metadata.DocumentTypeId);
        AddField(body, DesktopText.Translate("Document type"), _type);
        if (definitions.Correspondents.Count > 0)
        {
            var people = new StackPanel { Classes = { "field" } };
            people.Children.Add(new TextBlock { Text = DesktopText.Translate("Correspondents"), Classes = { "form-label" } });
            body.Children.Add(people);
            var selected = input.Metadata.CorrespondentIds.ToHashSet();
            var suggestions = input.Suggestions.Select(contact => contact.Id).ToHashSet();
            foreach (var contact in definitions.Correspondents.OrderByDescending(contact => selected.Contains(contact.Id))
                         .ThenByDescending(contact => suggestions.Contains(contact.Id)))
            {
                var box = new CheckBox
                {
                    Content = suggestions.Contains(contact.Id) && !selected.Contains(contact.Id)
                        ? DesktopText.Format($"{contact.Name} · Suggested") : contact.Name,
                    IsChecked = selected.Contains(contact.Id)
                };
                _contacts.Add((contact.Id, box));
                people.Children.Add(box);
            }
        }
        _notes = CreateTextBox(DesktopText.Translate("Notes"), input.Metadata.Notes);
        _notes.Name = "MetadataNotes";
        _notes.AcceptsReturn = true;
        _notes.TextWrapping = TextWrapping.Wrap;
        _notes.MinHeight = 76;
        AddField(body, DesktopText.Translate("Notes"), _notes);

        var fields = input.Metadata.Fields.ToDictionary(field => field.FieldId, field => field.Value);
        foreach (var field in definitions.CustomFields)
        {
            var editor = CreateEditor(field, fields.GetValueOrDefault(field.Id), input.LinkTargets);
            AddField(body, field.Name, editor.Control);
            _fields.Add((field.Id, editor.Read));
        }

        var save = DialogLayout.Primary(DesktopText.Translate("Save"), isDefault: true);
        save.Name = "SaveDocumentMetadataButton";
        save.Click += (_, _) => Save();
        var cancel = DialogLayout.Secondary(DesktopText.Translate("Cancel"), isCancel: true);
        cancel.Click += (_, _) => Close();
        DialogLayout.Compose(this, DesktopText.Format($"Details for “{input.Document.Title}”"),
            DesktopText.Translate("What kind of document this is, who it is from, and anything else to keep with it."),
            new ScrollViewer { Content = body }, _error, cancel, save);
    }

    private void Save()
    {
        try
        {
            var fields = _fields.Select(item => (item.Id, Value: item.Read()))
                .Where(item => item.Value is not null).Select(item => new DocumentField(item.Id, item.Value!)).ToArray();
            Close(new DocumentMetadata(ReadOption<long?>(_type), _contacts.Where(item => item.Box.IsChecked == true)
                .Select(item => item.Id).ToArray(), _notes.Text ?? "", fields));
        }
        catch (ArgumentException error) { _error.Text = error.Message; }
    }

    private static (Control Control, Func<CustomFieldValue?> Read) CreateEditor(CustomFieldDefinition field,
        CustomFieldValue? previous, IReadOnlyList<MetadataLinkTarget> targets)
    {
        switch (field.Kind)
        {
            case CustomFieldKind.Choice:
                var choices = Options<string?>(field.Name, [new(DesktopText.Translate("Unset"), null), .. field.Choices.Select(label =>
                    new MetadataOption<string?>(label, label))], (previous as CustomFieldValue.Choice)?.Label);
                return (choices, () => ReadOption<string?>(choices) is { } label ? new CustomFieldValue.Choice(label) : null);
            case CustomFieldKind.Boolean:
                var bools = Options<bool?>(field.Name, [new(DesktopText.Translate("Unset"), null), new(DesktopText.Translate("Yes"), true), new(DesktopText.Translate("No"), false)],
                    (previous as CustomFieldValue.Boolean)?.Value);
                return (bools, () => ReadOption<bool?>(bools) is { } value ? new CustomFieldValue.Boolean(value) : null);
            case CustomFieldKind.DocumentLink:
                var links = Options<long?>(field.Name, [new(DesktopText.Translate("Unset"), null), .. targets.Select(target =>
                    new MetadataOption<long?>(target.Title, target.Id))], (previous as CustomFieldValue.DocumentLink)?.DocumentId);
                return (links, () => ReadOption<long?>(links) is { } value ? new CustomFieldValue.DocumentLink(value) : null);
            case CustomFieldKind.Money:
                return MoneyEditor(field, previous as CustomFieldValue.Money);
            default:
                var input = CreateTextBox(field.Name, Format(previous),
                    field.Kind == CustomFieldKind.Date ? DisplayDates.Day(DateOnly.FromDateTime(DateTime.Today)) : null);
                return (input, () => ReadTextField(field, input.Text));
        }
    }

    private static (Control, Func<CustomFieldValue?>) MoneyEditor(CustomFieldDefinition field, CustomFieldValue.Money? previous)
    {
        var amount = CreateTextBox(field.Name, previous?.Amount.ToString(CultureInfo.CurrentCulture), DesktopText.Translate("Amount"));
        var currency = CreateTextBox(DesktopText.Format($"{field.Name} currency"), previous?.Currency, DesktopText.Translate("Currency code"));
        var panel = new Grid { ColumnDefinitions = new("*,120"), ColumnSpacing = 8, Children = { amount, currency } };
        Grid.SetColumn(currency, 1);
        return (panel, () =>
        {
            if (string.IsNullOrWhiteSpace(amount.Text)) return null;
            if (!decimal.TryParse(amount.Text, CultureInfo.CurrentCulture, out var number)) throw InvalidField(field);
            return new CustomFieldValue.Money(number, string.IsNullOrWhiteSpace(currency.Text) ? null : currency.Text.Trim());
        });
    }

    private static CustomFieldValue? ReadTextField(CustomFieldDefinition field, string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        string value = input.Trim();
        return field.Kind switch
        {
            CustomFieldKind.Text => new CustomFieldValue.Text(input),
            CustomFieldKind.Url when Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" =>
                new CustomFieldValue.Url(value),
            CustomFieldKind.Date when DisplayDates.TryParseDay(value, out var date) =>
                new CustomFieldValue.Date(date),
            CustomFieldKind.Integer when long.TryParse(value, CultureInfo.CurrentCulture, out var integer) => new CustomFieldValue.Integer(integer),
            CustomFieldKind.Decimal when decimal.TryParse(value, CultureInfo.CurrentCulture, out var number) => new CustomFieldValue.Decimal(number),
            _ => throw InvalidField(field)
        };
    }

    private static ArgumentException InvalidField(CustomFieldDefinition field) => new(DesktopText.Format($"Enter a valid value for “{field.Name}”."));

    private static string? Format(CustomFieldValue? value) => value switch
    {
        CustomFieldValue.Text text => text.Value,
        CustomFieldValue.Url url => url.Value,
        CustomFieldValue.Date date => DisplayDates.Day(date.Value),
        CustomFieldValue.Integer number => number.Value.ToString(CultureInfo.CurrentCulture),
        CustomFieldValue.Decimal number => number.Value.ToString(CultureInfo.CurrentCulture),
        _ => null
    };

    internal static ComboBox Options<T>(string label, IReadOnlyList<MetadataOption<T>> items, T selected)
    {
        var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = items, SelectedIndex = 0 };
        box.SelectedItem = items.FirstOrDefault(item => EqualityComparer<T>.Default.Equals(item.Value, selected)) ?? items[0];
        Avalonia.Automation.AutomationProperties.SetName(box, label);
        return box;
    }

    internal static T? ReadOption<T>(ComboBox box) => box.SelectedItem is MetadataOption<T> selected ? selected.Value : default;

    internal static TextBox CreateTextBox(string label, string? value = null, string? placeholder = null)
    {
        var box = new TextBox { Text = value, PlaceholderText = placeholder };
        Avalonia.Automation.AutomationProperties.SetName(box, label);
        return box;
    }

    /// <summary>Adds a labelled field: a 14 px label above its control.</summary>
    internal static void AddField(StackPanel body, string text, Control control) =>
        body.Children.Add(new StackPanel { Classes = { "field" }, Children = { new TextBlock { Text = text, Classes = { "form-label" } }, control } });

    internal static void AddRow(Grid panel, int row, Control content)
    {
        Grid.SetRow(content, row);
        panel.Children.Add(content);
    }
}
