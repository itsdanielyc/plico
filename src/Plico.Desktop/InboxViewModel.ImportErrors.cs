using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private readonly SortedDictionary<string, ImportFailure> _unresolvedImports = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    [ObservableProperty] private IReadOnlyList<ImportFailure> _importFailures = [];

    public bool HasImportFailures => ImportFailures.Count > 0;

    partial void OnImportFailuresChanged(IReadOnlyList<ImportFailure> value) =>
        OnPropertyChanged(nameof(HasImportFailures));

    [RelayCommand]
    private Task InspectImportErrorsAsync()
    {
        if (!HasImportFailures) return Task.CompletedTask;
        string previousStatus = Status;
        return RunOperationAsync("Reviewing import errors…", async token =>
        {
            var failures = ImportFailures;
            switch (await _dialogs.ShowImportErrorsAsync(failures))
            {
                case ImportErrorsAction.Retry:
                    await ImportFilesAsync(failures.Select(failure => failure.SourcePath).ToArray(), token);
                    break;
                case ImportErrorsAction.Clear:
                    ClearImportErrors();
                    Status = "Import errors cleared.";
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
}
