using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Plico.Core;
using static Plico.Desktop.DocumentMetadataDialog;

namespace Plico.Desktop;

internal sealed class MetadataDefinitionsDialog : Window
{
    private enum Section { Correspondents, Types, Fields, Tags }
    private readonly InboxLibrary _library;
    private readonly ComboBox _section = Options(DesktopText.Translate("Metadata category"),
        [new(DesktopText.Translate("Correspondents"), Section.Correspondents), new(DesktopText.Translate("Document types"), Section.Types),
            new(DesktopText.Translate("Custom fields"), Section.Fields), new(DesktopText.Translate("Tag appearance"), Section.Tags)], Section.Correspondents);
    private readonly ListBox _definitions = new() { MinHeight = 136, MaxHeight = 190, Classes = { "plain", "framed" } };
    private readonly TextBox _name = CreateTextBox(DesktopText.Translate("Name"));
    private readonly TextBox _options = CreateTextBox(DesktopText.Translate("Aliases or choices"));
    private readonly ComboBox _kind = Options(DesktopText.Translate("Field type"), Enum.GetValues<CustomFieldKind>()
        .Select(kind => new MetadataOption<CustomFieldKind>(FieldLabel(kind), kind)).ToArray(), CustomFieldKind.Text);
    private readonly ComboBox _parent = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _color = CreateTextBox(DesktopText.Translate("Tag colour"), placeholder: "#RRGGBB");
    private readonly TextBlock _kindLabel = new() { Text = DesktopText.Translate("Field type"), Classes = { "form-label" } };
    private readonly TextBlock _optionsLabel = new() { Classes = { "form-label" } };
    private readonly TextBlock _parentLabel = new() { Text = DesktopText.Translate("Parent tag"), Classes = { "form-label" } };
    private readonly TextBlock _colorLabel = new() { Text = DesktopText.Translate("Tag colour"), Classes = { "form-label" } };
    private readonly TextBlock _status = DialogLayout.Message("description");

    internal MetadataDefinitionsDialog(InboxLibrary library)
    {
        _library = library;
        Title = DesktopText.Translate("Metadata definitions");
        Width = 540;
        Height = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _options.AcceptsReturn = true;
        _options.MinHeight = 64;
        Avalonia.Automation.AutomationProperties.SetName(_parent, DesktopText.Translate("Parent tag"));
        var add = DialogLayout.Secondary(DesktopText.Translate("New"));
        add.Click += (_, _) => { _definitions.SelectedItem = null; RefreshEditor(); _name.Focus(); };
        var save = DialogLayout.Secondary(DesktopText.Translate("Save definition"));
        save.Click += (_, _) => Save();
        var close = DialogLayout.Secondary(DesktopText.Translate("Close"), isCancel: true);
        close.Click += (_, _) => Close();
        var fields = new StackPanel
        {
            Spacing = 6,
            Children = { new TextBlock { Text = DesktopText.Translate("Name"), Classes = { "form-label" } }, _name, _kindLabel, _kind,
                _optionsLabel, _options, _parentLabel, _parent, _colorLabel, _color }
        };
        var panel = new Grid { RowDefinitions = new("Auto,Auto,*"), RowSpacing = 12 };
        AddRow(panel, 0, _section);
        AddRow(panel, 1, _definitions);
        AddRow(panel, 2, new ScrollViewer { Content = fields });
        DialogLayout.Compose(this, Title, panel, _status, add, close, save);
        _section.SelectionChanged += (_, _) => RefreshList();
        _definitions.SelectionChanged += (_, _) => RefreshEditor();
        _kind.SelectionChanged += (_, _) => RefreshVisibility();
        RefreshList();
    }

    private void RefreshList(object? selected = null)
    {
        var values = _library.GetMetadataDefinitions();
        IEnumerable<object> entries = ReadOption<Section>(_section) switch
        {
            Section.Correspondents => values.Correspondents,
            Section.Types => values.DocumentTypes,
            Section.Fields => values.CustomFields,
            Section.Tags => _library.GetTagAppearances(),
            _ => []
        };
        var options = entries.Select(item => new MetadataOption<object>(NameOf(item), item)).ToArray();
        _definitions.ItemsSource = options;
        _definitions.SelectedItem = selected is null ? null : options.FirstOrDefault(item => NameOf(item.Value) == NameOf(selected));
        RefreshEditor();
    }

    private void RefreshEditor()
    {
        var current = (_definitions.SelectedItem as MetadataOption<object>)?.Value;
        _name.Text = current is null ? "" : NameOf(current);
        _name.IsReadOnly = current is TagAppearance;
        _options.Text = current switch
        {
            Correspondent contact => string.Join(Environment.NewLine, contact.Aliases),
            CustomFieldDefinition field => string.Join(Environment.NewLine, field.Choices),
            _ => ""
        };
        CustomFieldKind kind = (current as CustomFieldDefinition)?.Kind ?? CustomFieldKind.Text;
        _kind.SelectedItem = _kind.Items.OfType<MetadataOption<CustomFieldKind>>().Single(item => item.Value == kind);
        if (ReadOption<Section>(_section) == Section.Tags)
        {
            var parents = _library.GetTagAppearances().Where(tag => tag.Label != _name.Text).Select(tag =>
                new MetadataOption<string?>(tag.Label, tag.Label));
            MetadataOption<string?>[] options = [new(DesktopText.Translate("No parent"), null), .. parents];
            _parent.ItemsSource = options;
            _parent.SelectedItem = options.FirstOrDefault(item => item.Value == (current as TagAppearance)?.ParentLabel) ?? options[0];
        }
        _color.Text = (current as TagAppearance)?.ColorHex ?? "";
        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        Section section = ReadOption<Section>(_section);
        _kindLabel.IsVisible = _kind.IsVisible = section == Section.Fields;
        _optionsLabel.IsVisible = _options.IsVisible = section == Section.Correspondents ||
            (section == Section.Fields && ReadOption<CustomFieldKind>(_kind) == CustomFieldKind.Choice);
        _optionsLabel.Text = section == Section.Correspondents ? DesktopText.Translate("Aliases, one per line") : DesktopText.Translate("Choices, one per line");
        _parentLabel.IsVisible = _parent.IsVisible = _colorLabel.IsVisible = _color.IsVisible = section == Section.Tags;
    }

    private void Save()
    {
        var existing = (_definitions.SelectedItem as MetadataOption<object>)?.Value;
        string label = _name.Text ?? "";
        string[] options = (_options.Text ?? "").Split(["\r\n", "\r", "\n"],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        try
        {
            object saved = ReadOption<Section>(_section) switch
            {
                Section.Correspondents => _library.SaveCorrespondent((existing as Correspondent)?.Id, label, options),
                Section.Types => _library.SaveDocumentType((existing as CatalogDocumentType)?.Id, label),
                Section.Fields => _library.SaveCustomField((existing as CustomFieldDefinition)?.Id, label,
                    ReadOption<CustomFieldKind>(_kind), ReadOption<CustomFieldKind>(_kind) == CustomFieldKind.Choice ? options : []),
                Section.Tags => UpdateTag(label),
                _ => throw new InvalidOperationException("Choose a metadata category.")
            };
            RefreshList(saved);
            _status.Text = DesktopText.Translate("Saved.");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException)
        {
            _status.Text = error.Message;
        }
    }

    private TagAppearance UpdateTag(string name)
    {
        string? parent = ReadOption<string?>(_parent);
        string? color = string.IsNullOrWhiteSpace(_color.Text) ? null : _color.Text;
        _library.SaveTagAppearance(name, parent, color);
        return new(name.Trim(), parent, color);
    }

    private static string NameOf(object definition) => definition switch
    {
        Correspondent correspondent => correspondent.Name,
        CatalogDocumentType type => type.Name,
        CustomFieldDefinition field => field.Name,
        TagAppearance tag => tag.Label,
        _ => throw new ArgumentException("Choose a metadata definition.")
    };

    private static string FieldLabel(CustomFieldKind kind) => kind switch
    {
        CustomFieldKind.Boolean => DesktopText.Translate("Yes or no"),
        CustomFieldKind.Url => DesktopText.Translate("Web address"),
        CustomFieldKind.DocumentLink => DesktopText.Translate("Document link"),
        _ => DesktopText.Translate(kind.ToString())
    };
}
