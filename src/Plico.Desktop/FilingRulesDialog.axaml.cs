using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class FilingRulesDialog : Window
{
    private sealed record ModeChoice(FilingRuleMatch Value, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly ModeChoice[] Modes =
    [
        new(FilingRuleMatch.Phrase, "Exact phrase"),
        new(FilingRuleMatch.AnyWord, "Any word"),
        new(FilingRuleMatch.AllWords, "All words")
    ];

    private readonly List<FilingRule> _rules;
    private readonly Func<FilingRule, FilingRule> _save;
    private readonly Action<long> _remove;

    internal FilingRulesDialog(IReadOnlyList<FilingRule> rules, Func<FilingRule, FilingRule> save, Action<long> remove)
    {
        _rules = [.. rules];
        _save = save;
        _remove = remove;
        InitializeComponent();
        RuleMatchMode.ItemsSource = Modes;
        RuleMatchMode.SelectedIndex = 0;
        RefreshRules();
    }

    private void Rule_Changed(object? sender, SelectionChangedEventArgs e)
    {
        var rule = FilingRuleList.SelectedItem as FilingRule;
        RemoveRuleButton.IsEnabled = rule?.Id is not null;
        RuleTag.Text = rule?.Tag;
        RulePattern.Text = rule?.Pattern;
        RuleMatchMode.SelectedItem = Modes.First(choice => choice.Value == (rule?.Match ?? FilingRuleMatch.Phrase));
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (RuleMatchMode.SelectedItem is not ModeChoice mode) return;
        var existing = FilingRuleList.SelectedItem as FilingRule;
        ApplyChange(() =>
        {
            var saved = _save(new(existing?.Id, RuleTag.Text ?? "", RulePattern.Text ?? "", mode.Value));
            int index = _rules.FindIndex(rule => rule.Id == saved.Id);
            if (index < 0) _rules.Add(saved);
            else _rules[index] = saved;
            RefreshRules(saved);
            FilingRuleStatus.Text = "Rule saved.";
        });
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (FilingRuleList.SelectedItem is not FilingRule { Id: long id } rule) return;
        ApplyChange(() =>
        {
            _remove(id);
            _rules.Remove(rule);
            RefreshRules();
            FilingRuleStatus.Text = "Rule removed.";
        });
    }

    private void New_Click(object? sender, RoutedEventArgs e)
    {
        FilingRuleList.SelectedIndex = -1;
        RuleTag.Text = RulePattern.Text = "";
        RuleMatchMode.SelectedIndex = 0;
        FilingRuleStatus.Text = "";
        RuleTag.Focus();
    }

    private void RefreshRules(FilingRule? selected = null)
    {
        FilingRuleList.ItemsSource = _rules.ToArray();
        FilingRuleList.IsVisible = _rules.Count > 0;
        FilingRuleList.SelectedItem = selected;
    }

    private void ApplyChange(Action change)
    {
        try { change(); }
        catch (Exception error) { FilingRuleStatus.Text = error.Message; }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
