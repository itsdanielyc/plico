using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private readonly SortedDictionary<string, ImportFailure> _unresolvedImports = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly ImportFailureStore? _importFailureStore;

    [ObservableProperty] private IReadOnlyList<ImportFailure> _importFailures = [];

    public bool HasImportFailures => ImportFailures.Count > 0;

    partial void OnImportFailuresChanged(IReadOnlyList<ImportFailure> value) =>
        OnPropertyChanged(nameof(HasImportFailures));

    [RelayCommand]
    private Task InspectImportErrorsAsync()
    {
        if (!HasImportFailures) return Task.CompletedTask;
        string previousStatus = Status;
        return RunOperationAsync(DesktopText.Translate("Reviewing import errors…"), async token =>
        {
            var failures = ImportFailures;
            switch (await _dialogs.ShowImportErrorsAsync(failures))
            {
                case ImportErrorsAction.Retry:
                    await ImportFilesAsync(failures.Select(failure => (failure.SourcePath, failure.Origin)), token);
                    break;
                case ImportErrorsAction.Clear:
                    if (PersistImportFailures(RequireLibrary().RootPath, []) is { } warning)
                    {
                        Status = warning;
                        break;
                    }
                    ClearImportErrors();
                    Status = DesktopText.Translate("Import errors cleared.");
                    break;
                default:
                    Status = previousStatus;
                    break;
            }
        });
    }

    private void ClearImportErrors()
    {
        _unresolvedImports.Clear();
        ImportFailures = [];
    }

    private string? RestoreImportFailures(string libraryRoot)
    {
        ClearImportErrors();
        if (_importFailureStore is not { } store) return null;
        try
        {
            foreach (var failure in store.Load(libraryRoot))
            {
                _unresolvedImports[failure.SourcePath] = failure;
            }
            if (_unresolvedImports.Count > 0) ImportFailures = _unresolvedImports.Values.ToArray();
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return $"Could not load saved import errors. {error.Message}";
        }
    }

    private string? PersistImportFailures(string libraryRoot, IReadOnlyList<ImportFailure> failures)
    {
        try
        {
            _importFailureStore?.Save(libraryRoot, failures);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return $"Could not save import errors. {error.Message}";
        }
    }
}
