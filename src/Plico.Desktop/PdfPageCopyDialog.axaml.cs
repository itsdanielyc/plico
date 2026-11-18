using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

internal enum PdfPageCopyAction { Extract, Rotate }

/// <summary>The validated pages, title, and optional turn confirmed for a PDF copy.</summary>
internal sealed record PdfPageCopyRequest(string Title, PdfPageSelection Selection, PdfPageRotation? Rotation = null);

internal sealed partial class PdfPageCopyDialog : Window
{
    private static readonly PdfRotationChoice[] RotationChoices =
    [
        new(PdfPageRotation.Clockwise, DesktopText.Translate("90° clockwise")),
        new(PdfPageRotation.Counterclockwise, DesktopText.Translate("90° counterclockwise")),
        new(PdfPageRotation.HalfTurn, "180°")
    ];
    private readonly int _documentPageCount;
    private readonly PdfPageCopyAction _action;

    internal PdfPageCopyDialog(int documentPageCount, int currentPage, PdfPageCopyAction action)
    {
        InitializeComponent();
        _documentPageCount = documentPageCount;
        _action = action;
        bool rotate = action == PdfPageCopyAction.Rotate;
        Title = rotate ? DesktopText.Translate("Rotate pages") : "Extract pages";
        ConfirmPageCopyButton.Content = Title;
        PagesBox.Text = currentPage.ToString(CultureInfo.InvariantCulture);
        PageHint.Text = $"Choose pages 1 to {documentPageCount}. Use commas between entries and a hyphen for a range.";
        TitleBox.MaxLength = InboxLibrary.MaximumTitleLength;
        TitleBox.Text = rotate ? "Rotated pages" : "Extracted pages";
        if (rotate)
        {
            RotationField.IsVisible = true;
            RotationBox.ItemsSource = RotationChoices;
            RotationBox.SelectedIndex = 0;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PagesBox.Focus();
        PagesBox.SelectAll();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        PdfPageSelection selection;
        try
        {
            selection = PdfPageSelection.Parse(PagesBox.Text ?? "", _documentPageCount);
        }
        catch (ArgumentException error)
        {
            ShowValidation(error.Message, PagesBox);
            return;
        }

        PdfPageRotation? rotation = null;
        if (_action == PdfPageCopyAction.Rotate)
        {
            if (RotationBox.SelectedItem is not PdfRotationChoice choice)
            {
                ShowValidation("Choose a rotation.", RotationBox);
                return;
            }
            rotation = choice.Rotation;
        }
        try
        {
            string title = InboxLibrary.NormalizeTitle(TitleBox.Text ?? "");
            Close(new PdfPageCopyRequest(title, selection, rotation));
        }
        catch (ArgumentException error)
        {
            ShowValidation(error.Message, TitleBox);
        }
    }

    private void ShowValidation(string message, Control input)
    {
        ValidationMessage.Text = message;
        ValidationMessage.IsVisible = true;
        input.Focus();
    }

    private sealed record PdfRotationChoice(PdfPageRotation Rotation, string Label)
    {
        public override string ToString() => Label;
    }
}
