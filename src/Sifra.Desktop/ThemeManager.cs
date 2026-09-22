using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Sifra.Desktop;

public enum AppTheme
{
    Light,
    Dark,
    System,
}

/// <summary>
/// Swaps the Sifra.Theme.Light.xaml / Sifra.Theme.Dark.xaml resource
/// dictionary in Application.Resources.MergedDictionaries. Every themed
/// brush is a semantic key (Sifra.SurfaceBrush, Sifra.CardBrush, etc.)
/// defined identically in both dictionaries, so nothing else needs to
/// change when the active dictionary is swapped.
/// </summary>
public static class ThemeManager
{
    private const string LightThemeUri = "Assets/Sifra.Theme.Light.xaml";
    private const string DarkThemeUri = "Assets/Sifra.Theme.Dark.xaml";
    private const string BridgeUri = "Assets/Sifra.WpfUiBridge.xaml";

    public static AppTheme CurrentMode { get; private set; } = AppTheme.System;

    public static void Apply(AppTheme mode)
    {
        CurrentMode = mode;

        if (mode == AppTheme.System)
        {
            SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        }
        else
        {
            SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        }

        ApplyResolvedTheme(Resolve(mode));
    }

    private static void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && CurrentMode == AppTheme.System)
        {
            ApplyResolvedTheme(Resolve(AppTheme.System));
        }
    }

    private static AppTheme Resolve(AppTheme mode) => mode == AppTheme.System ? IsSystemInDarkMode() ? AppTheme.Dark : AppTheme.Light : mode;

    private static bool IsSystemInDarkMode()
    {
        // AppsUseLightTheme = 0 means dark mode. Missing key (older Windows) defaults to light.
        var value = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1);
        return value is int intValue && intValue == 0;
    }

    private static void ApplyResolvedTheme(AppTheme resolved)
    {
        // Keeps WPF-UI's OWN internal theme state (Mica tint, ui:TitleBar
        // text) in sync with ours — that part is native/DWM-driven, not
        // resource-dictionary-based, so no DynamicResource override can
        // reach it. updateAccent:false keeps our brand blue instead of
        // WPF-UI substituting the Windows accent color.
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
            resolved == AppTheme.Dark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light,
            updateAccent: false);

        var uri = resolved == AppTheme.Dark ? DarkThemeUri : LightThemeUri;
        var newDictionary = new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d =>
            d.Source is not null && (d.Source.OriginalString == LightThemeUri || d.Source.OriginalString == DarkThemeUri));

        if (existing is not null)
        {
            var index = dictionaries.IndexOf(existing);
            dictionaries[index] = newDictionary;
        }
        else
        {
            dictionaries.Add(newDictionary);
        }

        // ApplicationThemeManager.Apply re-merges WPF-UI's own theme
        // dictionary, which can land after ours in the merge order and
        // silently re-override the Sifra.WpfUiBridge.xaml keys. Re-add the
        // bridge last, every time, so it always wins regardless of what
        // WPF-UI just did internally.
        var bridge = dictionaries.FirstOrDefault(d => d.Source?.OriginalString == BridgeUri);
        if (bridge is not null)
        {
            dictionaries.Remove(bridge);
            dictionaries.Add(bridge);
        }

        RefreshWpfUiBridgeKeys();
    }

    /// <summary>
    /// Sifra.WpfUiBridge.xaml's brushes are declared as e.g.
    /// Color="{DynamicResource Sifra.Theme.Input}" — a DynamicResource
    /// nested INSIDE another dictionary's resource, referencing a key that
    /// lives in the dictionary just swapped above. That cross-dictionary
    /// nesting does not reliably re-propagate when the source dictionary
    /// is replaced wholesale (confirmed live: buttons, text/password box
    /// fields, and ui:Card all stayed on the old theme's colors after a
    /// swap, while Sifra.WindowBrush/Sifra.CardBrush — defined with
    /// StaticResource INSIDE the swapped dictionary itself — updated
    /// correctly). The fix is to stop relying on that propagation for the
    /// WPF-UI-specific keys: explicitly overwrite each one, every time the
    /// theme changes, with a fresh brush built from the NOW-current
    /// Sifra.Theme.* color — the same direct-resource-key-replacement
    /// mechanism already proven to work for Window/Card.
    /// </summary>
    private static void RefreshWpfUiBridgeKeys()
    {
        var resources = Application.Current.Resources;
        Brush BrushFor(string themeColorKey) => new SolidColorBrush((Color)resources[themeColorKey]);

        resources["TextControlBackground"] = BrushFor("Sifra.Theme.Input");
        resources["TextControlBackgroundFocused"] = BrushFor("Sifra.Theme.Input");
        resources["TextControlBackgroundPointerOver"] = BrushFor("Sifra.Theme.Card");
        resources["TextControlBackgroundDisabled"] = BrushFor("Sifra.Theme.Hover");
        resources["TextControlForeground"] = BrushFor("Sifra.Theme.TextPrimary");
        resources["TextControlForegroundDisabled"] = BrushFor("Sifra.Theme.TextSecondary");
        resources["TextControlPlaceholderForeground"] = BrushFor("Sifra.Theme.TextSecondary");
        resources["TextControlButtonForeground"] = BrushFor("Sifra.Theme.TextSecondary");
        resources["TextControlBorderBrushDisabled"] = BrushFor("Sifra.Theme.Border");
        resources["TextControlElevationBorderBrush"] = BrushFor("Sifra.Theme.Border");

        resources["CardBackground"] = BrushFor("Sifra.Theme.Card");
        resources["CardForeground"] = BrushFor("Sifra.Theme.TextPrimary");
        resources["CardBorderBrush"] = BrushFor("Sifra.Theme.Border");
        resources["CardFooterBackground"] = BrushFor("Sifra.Theme.Card");

        resources["ButtonBackground"] = BrushFor("Sifra.Theme.Card");
        resources["ButtonBackgroundPointerOver"] = BrushFor("Sifra.Theme.Hover");
        resources["ButtonBackgroundPressed"] = BrushFor("Sifra.Theme.Selected");
        resources["ButtonForeground"] = BrushFor("Sifra.Theme.TextPrimary");
        resources["ButtonForegroundPointerOver"] = BrushFor("Sifra.Theme.TextPrimary");
        resources["ButtonForegroundPressed"] = BrushFor("Sifra.Theme.TextPrimary");
        resources["ButtonBorderBrushDisabled"] = BrushFor("Sifra.Theme.Border");

        // Always white, in both themes — NOT Sifra.Theme.SelectedText: that
        // key means "text on a light SELECTED-ROW background" (blue in
        // Light theme, for contrast against a pale blue row highlight),
        // which is a different thing that happens to share a name. Reusing
        // it here produced blue text on the blue accent button in Light
        // theme — illegible. The brand's accent buttons always carry white
        // text regardless of theme, so this is a fixed color, not a
        // theme-reactive one.
        var white = new SolidColorBrush(Colors.White);
        resources["AccentButtonForeground"] = white;
        resources["AccentButtonForegroundPointerOver"] = white;
        resources["AccentButtonForegroundPressed"] = white;

        resources["TextFillColorPrimaryBrush"] = BrushFor("Sifra.Theme.TextPrimary");
        resources["TextFillColorSecondaryBrush"] = BrushFor("Sifra.Theme.TextSecondary");
        resources["TextFillColorTertiaryBrush"] = BrushFor("Sifra.Theme.TextSecondary");
    }
}
