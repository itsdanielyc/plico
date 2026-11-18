using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIndexReport))]
    private RefreshResult? _latestIndexRefresh;

    public bool ShowIndexReport => !IsBusy &&
        LatestIndexRefresh is { UnavailablePaths.Count: > 0 } or { Warnings.Count: > 0 };

    [RelayCommand]
    private Task RefreshIndexAsync() => RunOperationAsync("Checking the PDF folder…", async token =>
    {
        var library = RequireLibrary();
        var result = await Task.Run(() => library.Refresh(token), token);
        Tags = library.GetTags();
        RefreshInboxCount();
        LoadResults(reloadPreview: result.Indexed > 0 || result.Relocated > 0);
        LatestIndexRefresh = result;
        Status = $"{DocumentCount(result.Indexed)} indexed.";
        if (result.Relocated > 0) Status += $" {DocumentCount(result.Relocated)} moved.";
        if (result.UnavailablePaths.Count > 0) Status += $" {DocumentCount(result.UnavailablePaths.Count)} unavailable.";
        int notices = result.Warnings.Count;
        if (notices > 0) Status += notices == 1 ? " 1 index notice." : $" {notices} index notices.";
    });

    [RelayCommand]
    private Task ShowIndexReportAsync() => CanWork && ShowIndexReport && LatestIndexRefresh is { } result
        ? _dialogs.ShowIndexRefreshReportAsync(result)
        : Task.CompletedTask;
}
