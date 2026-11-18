using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal static class NativeVerification
{
    private const string ReceiptKeyword = "WRENCH470";
    private const string TestTag = "Workshop";
    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan StateCheckInterval = TimeSpan.FromMilliseconds(30);

    internal static async Task RunAsync(
        IClassicDesktopStyleApplicationLifetime desktop, MainWindow window, InboxViewModel model, string evidenceDirectory, string fixture)
    {
        try
        {
            Directory.CreateDirectory(evidenceDirectory);
            Require(window.IsVisible && Control<TextBlock>(window, "InboxHeading").Text == "Inbox", "The native inbox did not open.");
            var import = Control<Button>(window, "AddFilesButton");
            Require(import.IsEnabled && import.Command is IAsyncRelayCommand, "The import control was not available.");
            await ((IAsyncRelayCommand)import.Command!).ExecuteAsync(null);
            Require(model.Documents.Count == 1 && model.SelectedDocument is { HasText: true },
                $"The receipt did not import with embedded text. {model.Status}");
            await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);

            Control<TextBox>(window, "TagBox").SetCurrentValue(TextBox.TextProperty, TestTag);
            Control<Button>(window, "AddTagButton").Command!.Execute(null);
            Require(model.SelectedDocument?.Tags.Contains(TestTag) == true, "The tag did not attach to the imported PDF.");
            await CaptureAsync(window, evidenceDirectory, "inbox.png");
            Control<Button>(window, "ReviewButton").Command!.Execute(null);
            Require(model.Documents.Count == 0, "The reviewed PDF remained in the inbox.");

            Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, ReceiptKeyword);
            await UntilAsync(() => model.Documents.Count == 1 && model.Preview is not null, () => model.Status);
            Require(model.SelectedDocument is { Reviewed: true } document && document.Tags.Contains(TestTag) &&
                    document.Match.Contains(ReceiptKeyword, StringComparison.OrdinalIgnoreCase),
                "The reviewed and tagged PDF was not found by OCR text.");
            await CaptureAsync(window, evidenceDirectory, "search.png");
            await VerifyDropAsync(window, model, fixture, evidenceDirectory);

            File.WriteAllText(Path.Combine(evidenceDirectory, "passed.txt"),
                "Native window imported and previewed a searchable PDF, tagged and reviewed it, found it by text, and skipped a dropped duplicate.");
            desktop.Shutdown(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(evidenceDirectory, "error.txt"), error.ToString());
            desktop.Shutdown(1);
        }
    }

    private static async Task VerifyDropAsync(MainWindow window, InboxViewModel model, string fixture, string directory)
    {
        using var file = await window.StorageProvider.TryGetFileFromPathAsync(new Uri(Path.GetFullPath(fixture)));
        if (file is null) throw new InvalidOperationException("The native file provider could not open the drop fixture.");
        using var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateFile(file));
        var pointer = new Point(window.Bounds.Width / 2, window.Bounds.Height / 2);
        var over = new DragEventArgs(DragDrop.DragOverEvent, transfer, window, pointer, KeyModifiers.None)
        {
            DragEffects = DragDropEffects.Copy
        };
        window.RaiseEvent(over);
        Require(over.DragEffects == DragDropEffects.Copy && Control<Border>(window, "DropTarget").IsVisible,
            "The native drop target did not offer to copy the file.");
        await CaptureAsync(window, directory, "drop-target.png");
        var drop = new DragEventArgs(DragDrop.DropEvent, transfer, window, pointer, KeyModifiers.None)
        {
            DragEffects = DragDropEffects.Copy
        };
        window.RaiseEvent(drop);
        await UntilAsync(() => !model.IsBusy && model.Status.Contains("already in the library", StringComparison.Ordinal),
            () => model.Status);
        Require(model.SelectedDocument is { Reviewed: true } document && document.Tags.Contains(TestTag) &&
                !Control<Border>(window, "DropTarget").IsVisible && File.Exists(fixture),
            "Dropping a repeat altered the existing document or moved the source.");
    }

    private static T Control<T>(Window window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing control {name}.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task UntilAsync(Func<bool> ready, Func<string> details)
    {
        using var timeout = new CancellationTokenSource(StateTimeout);
        try
        {
            while (!ready()) await Task.Delay(StateCheckInterval, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException($"The native window did not reach the expected state. {details()}");
        }
    }

    private static async Task CaptureAsync(Window window, string directory, string filename)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.Bounds.Size, 1), new Vector(96, 96));
            bitmap.Render(window);
            bitmap.Save(Path.Combine(directory, filename), PngBitmapEncoderOptions.Default);
        }, DispatcherPriority.Background);
    }
}

/// <summary>A labelled fixture picker for verification with the same real library used by normal launches.</summary>
internal sealed class FixtureDialogs(params string[] documents) : IWorkspaceDialogs
{
    public Task<IReadOnlyList<string>> PickDocumentsAsync() => Task.FromResult<IReadOnlyList<string>>(documents);
    public Task<string?> PickLibraryAsync() => Task.FromResult<string?>(null);
    public Task<ScanRequest?> PickScannerAsync() => Task.FromResult<ScanRequest?>(null);
    public void OpenPath(string path) => throw new InvalidOperationException("The verification fixture does not launch other apps.");
}
