using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private const string RootIntakeFolder = ".";
    private readonly IntakeFolderPreference? _intakeFolderPreference;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomIntakeFolder))]
    [NotifyPropertyChangedFor(nameof(IntakeFolderLabel))]
    private string _intakeFolder = RootIntakeFolder;

    public bool HasCustomIntakeFolder => IntakeFolder != RootIntakeFolder;
    public string IntakeFolderLabel => HasCustomIntakeFolder ? $"PDFs/{IntakeFolder}" : "PDFs";

    private string? ManualIntakeDestination(InboxLibrary library) =>
        HasCustomIntakeFolder ? Path.Combine(library.PdfDirectory, IntakeFolder) : null;

    [RelayCommand]
    private Task ChooseIntakeFolderAsync() => RunOperationAsync("Choose an intake folder…", async token =>
    {
        var library = RequireLibrary();
        string? folder = await _dialogs.PickFolderAsync("Choose a library folder for files and scans", library.PdfDirectory);
        if (folder is null)
        {
            Status = "Intake folder selection cancelled.";
            return;
        }

        token.ThrowIfCancellationRequested();
        string relative = library.GetRelativeLibraryFolderPath(folder);
        _intakeFolderPreference?.Save(library.RootPath, relative);
        IntakeFolder = relative;
        Status = $"Intake folder set to {IntakeFolderLabel}.";
    });

    private string? RestoreIntakeFolder(string libraryRoot)
    {
        IntakeFolder = RootIntakeFolder;
        try
        {
            IntakeFolder = _intakeFolderPreference?.Load(libraryRoot) ?? RootIntakeFolder;
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"Could not load the intake folder. {error.Message}";
        }
    }
}
