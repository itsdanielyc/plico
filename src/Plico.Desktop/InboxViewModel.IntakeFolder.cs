using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private const string RootIntakeFolder = ".";
    private readonly IntakeFolderPreference? _intakeFolderPreference;
    private bool _includeWatchedScans;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomIntakeFolder))]
    [NotifyPropertyChangedFor(nameof(IntakeFolderLabel))]
    private string _intakeFolder = RootIntakeFolder;

    public bool HasCustomIntakeFolder => IntakeFolder != RootIntakeFolder;
    public string IntakeFolderLabel => HasCustomIntakeFolder ? $"PDFs/{IntakeFolder}" : "PDFs";

    public bool IncludeWatchedScans
    {
        get => _includeWatchedScans;
        set
        {
            if (value == _includeWatchedScans) return;
            if (IsBusy || !HasCustomIntakeFolder || _library is not { } library)
            {
                OnPropertyChanged(nameof(IncludeWatchedScans));
                return;
            }

            try
            {
                _intakeFolderPreference?.SaveIncludeWatchedScans(library.RootPath, value);
                SetProperty(ref _includeWatchedScans, value);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Status = $"Could not save the watched-scan choice. {error.Message}";
                OnPropertyChanged(nameof(IncludeWatchedScans));
            }
        }
    }

    private string? IntakeDestination(InboxLibrary library) =>
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
        SetProperty(ref _includeWatchedScans, false, nameof(IncludeWatchedScans));
        if (_intakeFolderPreference is not { } preference) return null;

        try
        {
            IntakeFolder = preference.Load(libraryRoot) ?? RootIntakeFolder;
            SetProperty(ref _includeWatchedScans, preference.LoadIncludeWatchedScans(libraryRoot), nameof(IncludeWatchedScans));
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return $"Could not load the intake settings. {error.Message}";
        }
    }
}
