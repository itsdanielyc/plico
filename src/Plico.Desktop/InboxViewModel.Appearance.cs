using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private AppearanceOptions _appearance = AppearanceOptions.Default;
    private Action<AppearanceOptions>? _saveAppearance;

    internal void InitializeAppearance(AppearanceOptions options, Action<AppearanceOptions> save)
    {
        _appearance = options;
        _saveAppearance = save;
    }

    [RelayCommand]
    private Task ShowAppearanceAsync() => RunOperationAsync(DesktopText.Translate("Choose an appearance…"), async token =>
    {
        var chosen = await _dialogs.ChooseAppearanceAsync(_appearance);
        if (chosen is null)
        {
            Status = DesktopText.Translate("Appearance settings closed.");
            return;
        }
        token.ThrowIfCancellationRequested();
        _saveAppearance?.Invoke(chosen);
        Status = chosen.Language == _appearance.Language ? DesktopText.Translate("Appearance saved.") : DesktopText.Translate("Appearance saved. Reopen Plico to switch the interface language.");
        _appearance = chosen;
    });
}
