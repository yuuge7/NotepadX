using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using NotepadX.Interop;

namespace NotepadX.Services;

/// <summary>
/// Swaps the Fluent control theme plus the app brush set. Reads the system preference
/// straight from the registry, so it also works on Windows 10 where WinRT UISettings
/// is not guaranteed to be reachable from an unpackaged app.
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static ResourceDictionary? _fluentDict;
    private static ResourceDictionary? _brushDict;

    public static bool IsDark { get; private set; }
    public static event Action? ThemeChanged;

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key?.GetValue("AppsUseLightTheme") is int light) return light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException)
        {
        }
        return false;
    }

    public static void Apply(AppTheme theme)
    {
        bool dark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => SystemUsesDarkTheme()
        };

        IsDark = dark;
        var app = Application.Current;
        if (app is null) return;

        var fluent = LoadFluent(dark);
        var brushes = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute)
        };

        var dicts = app.Resources.MergedDictionaries;

        // Appended, never inserted: in a merged dictionary the last entry wins, so both
        // must sit after everything App.xaml declares.
        if (_fluentDict is not null) dicts.Remove(_fluentDict);
        if (_brushDict is not null) dicts.Remove(_brushDict);

        if (fluent is not null)
        {
            dicts.Add(fluent);
            _fluentDict = fluent;
        }

        dicts.Add(brushes);
        _brushDict = brushes;

        ApplySystemAccent(app, dark);

        foreach (Window w in app.Windows)
            NativeMethods.SetDarkTitleBar(w, dark);

        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// Adopts the colour the user picked in Windows personalisation, nudged until it has
    /// enough contrast against the current theme. Resources set directly on the
    /// application beat anything in its merged dictionaries, so this wins over the palette.
    /// </summary>
    private static void ApplySystemAccent(Application app, bool dark)
    {
        var accent = ReadSystemAccent();
        if (accent is not Color color) return;

        color = EnsureReadable(color, dark);

        app.Resources["App.Accent"] = new SolidColorBrush(color);
        app.Resources["App.AccentText"] = new SolidColorBrush(Luminance(color) > 0.55 ? Colors.Black : Colors.White);
        app.Resources["App.Selection"] = new SolidColorBrush(dark ? Darken(color, 0.45) : color);
    }

    private static Color? ReadSystemAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int raw)
            {
                // Stored as 0xAABBGGRR.
                byte r = (byte)(raw & 0xFF);
                byte g = (byte)((raw >> 8) & 0xFF);
                byte b = (byte)((raw >> 16) & 0xFF);
                return Color.FromRgb(r, g, b);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException)
        {
        }
        return null;
    }

    private static Color EnsureReadable(Color c, bool dark)
    {
        double l = Luminance(c);
        if (dark && l < 0.45) return Lighten(c, 0.45 - l + 0.15);
        if (!dark && l > 0.55) return Darken(c, l - 0.55 + 0.12);
        return c;
    }

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    private static Color Lighten(Color c, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)(c.R + (255 - c.R) * amount),
            (byte)(c.G + (255 - c.G) * amount),
            (byte)(c.B + (255 - c.B) * amount));
    }

    private static Color Darken(Color c, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)(c.R * (1 - amount)),
            (byte)(c.G * (1 - amount)),
            (byte)(c.B * (1 - amount)));
    }

    private static ResourceDictionary? LoadFluent(bool dark)
    {
        // Ships with the .NET 9+ Windows Desktop runtime. If it is ever missing the app
        // still runs, it just falls back to the built-in control theme.
        var uri = new Uri(
            $"pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{(dark ? "Dark" : "Light")}.xaml",
            UriKind.Absolute);
        try { return new ResourceDictionary { Source = uri }; }
        catch (Exception) { return null; }
    }
}
