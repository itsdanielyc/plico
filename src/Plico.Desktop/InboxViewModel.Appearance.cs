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
        NotifyThemeChoice();
    }

    /// <summary>Settings' three theme cards. Choosing one saves it at once and repaints every window.</summary>
    public bool UsesSystemTheme
    {
        get => _appearance.Theme == AppearanceTheme.System;
        set { if (value) ChooseTheme(AppearanceTheme.System); }
    }

    public bool UsesLightTheme
    {
        get => _appearance.Theme == AppearanceTheme.Light;
        set { if (value) ChooseTheme(AppearanceTheme.Light); }
    }

    public bool UsesDarkTheme
    {
        get => _appearance.Theme == AppearanceTheme.Dark;
        set { if (value) ChooseTheme(AppearanceTheme.Dark); }
    }

    /// <summary>
    /// Settings' interface language: 0 follows the system, 1 is English, 2 is French. Choosing one saves it at once; the
    /// words change when Plico next opens.
    /// </summary>
    public int InterfaceLanguageIndex
    {
        get => (int)_appearance.Language;
        set
        {
            if (value < 0 || !Enum.IsDefined((InterfaceLanguage)value) || (InterfaceLanguage)value == _appearance.Language) return;
            var chosen = _appearance with { Language = (InterfaceLanguage)value };
            try
            {
                _saveAppearance?.Invoke(chosen);
                _appearance = chosen;
                Status = DesktopText.Translate("Appearance saved. Reopen Plico to switch the interface language.");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Status = error.Message;
            }
            finally
            {
                OnPropertyChanged(nameof(InterfaceLanguageIndex));
            }
        }
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
        NotifyThemeChoice();
    });

    private void ChooseTheme(AppearanceTheme theme)
    {
        if (_appearance.Theme == theme) return;
        var chosen = _appearance with { Theme = theme };
        try
        {
            _saveAppearance?.Invoke(chosen);
            _appearance = chosen;
            Status = DesktopText.Translate("Appearance saved.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status = error.Message;
        }
        finally
        {
            NotifyThemeChoice();
        }
    }

    private void NotifyThemeChoice()
    {
        OnPropertyChanged(nameof(UsesSystemTheme));
        OnPropertyChanged(nameof(UsesLightTheme));
        OnPropertyChanged(nameof(UsesDarkTheme));
        OnPropertyChanged(nameof(InterfaceLanguageIndex));
    }
}
