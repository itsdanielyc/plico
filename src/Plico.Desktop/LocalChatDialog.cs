using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Plico.Core;
using static Plico.Desktop.DocumentMetadataDialog;

namespace Plico.Desktop;

internal sealed record LocalChatRequest(string Question, bool SearchLibrary, IReadOnlyList<LocalModelMessage> History);
internal sealed record LocalChatInput(string? DocumentTitle,
    Func<LocalChatRequest, CancellationToken, Task<LocalAssistantAnswer>> Ask,
    Action<long> OpenSource);

internal sealed class LocalChatDialog : Window
{
    private const int ChatHistoryLimit = 8;
    private const int TranscriptLimit = 64 * 1024;
    private readonly LocalChatInput _input;
    private readonly List<LocalModelMessage> _history = [];
    private readonly TextBox _question = CreateTextBox(DesktopText.Translate("Question"), placeholder: DesktopText.Translate("Ask a question about your documents"));
    private readonly TextBox _transcript = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _searchLibrary = new() { Content = DesktopText.Translate("Use library search for context") };
    private readonly StackPanel _sources = new() { Spacing = 5 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _send = new() { Content = DesktopText.Translate("Ask"), IsDefault = true };
    private readonly Button _stop = new() { Content = DesktopText.Translate("Stop"), IsEnabled = false };
    private CancellationTokenSource? _request;

    internal LocalChatDialog(LocalChatInput input)
    {
        _input = input;
        Title = DesktopText.Translate("Ask a local model");
        Width = 680;
        Height = 590;
        MinWidth = 480;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _question.MaxLength = 1_200;
        _question.AcceptsReturn = true;
        _question.TextWrapping = TextWrapping.Wrap;
        _question.MinHeight = 68;
        _searchLibrary.IsChecked = input.DocumentTitle is null;
        _searchLibrary.IsEnabled = input.DocumentTitle is not null;
        _searchLibrary.PropertyChanged += (_, change) =>
        {
            // A new search scope should use only the history generated in that scope.
            if (change.Property == CheckBox.IsCheckedProperty) _history.Clear();
        };
        var heading = new StackPanel { Spacing = 4 };
        if (input.DocumentTitle is { } title)
            heading.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        heading.Children.Add(_searchLibrary);
        _send.Click += async (_, _) => await AskAsync();
        _stop.Click += (_, _) => _request?.Cancel();
        var close = new Button { Content = DesktopText.Translate("Close"), IsCancel = true };
        close.Click += (_, _) => Close();
        Closed += (_, _) => _request?.Cancel();
        var panel = new Grid { RowDefinitions = new("Auto,*,Auto,Auto,Auto"), RowSpacing = 8, Margin = new Thickness(20) };
        AddRow(panel, 0, heading);
        AddRow(panel, 1, _transcript);
        AddRow(panel, 2, new ScrollViewer { Content = _sources, MaxHeight = 104 });
        AddRow(panel, 3, _question);
        var footer = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8, Children = { _status } };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Children = { close, _stop, _send }
        };
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        AddRow(panel, 4, footer);
        Content = panel;
    }

    private async Task AskAsync()
    {
        if (_request is not null || string.IsNullOrWhiteSpace(_question.Text)) return;
        string question = _question.Text.Trim();
        using var request = new CancellationTokenSource();
        _request = request;
        _send.IsEnabled = false;
        _stop.IsEnabled = true;
        _searchLibrary.IsEnabled = false;
        _status.Text = DesktopText.Translate("Waiting for the local model…");
        try
        {
            var answer = await _input.Ask(new(question, _searchLibrary.IsChecked == true, _history.ToArray()), request.Token);
            request.Token.ThrowIfCancellationRequested();
            _history.Add(new("user", question));
            _history.Add(new("assistant", answer.Text));
            if (_history.Count > ChatHistoryLimit) _history.RemoveRange(0, _history.Count - ChatHistoryLimit);
            string transcript = $"{_transcript.Text}\n\nYou\n{question}\n\nModel\n{answer.Text}".TrimStart();
            _transcript.Text = transcript.Length > TranscriptLimit ? transcript[^TranscriptLimit..] : transcript;
            _question.Text = "";
            _sources.Children.Clear();
            foreach (var (source, index) in answer.Sources.Select((value, index) => (value, index)))
            {
                var open = new Button { Content = $"[{index + 1}] {source.Title}" };
                open.Click += (_, _) =>
                {
                    try { _input.OpenSource(source.Id); }
                    catch (Exception error) when (error is IOException or InvalidOperationException) { _status.Text = error.Message; }
                };
                _sources.Children.Add(open);
            }
            _status.Text = "";
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { _status.Text = DesktopText.Translate("Question cancelled."); }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
        {
            _status.Text = error.Message;
        }
        finally
        {
            _request = null;
            _send.IsEnabled = true;
            _stop.IsEnabled = false;
            _searchLibrary.IsEnabled = _input.DocumentTitle is not null;
        }
    }
}
