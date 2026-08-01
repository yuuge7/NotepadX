using Microsoft.Win32;

namespace NotepadX.Services;

/// <summary>
/// Registers NotepadX with the shell so it appears in "Open with" and in Settings ›
/// Default apps.
///
/// Windows deliberately does not let an application make itself the default handler for
/// a file type — that has required an explicit user choice since Windows 8, and any code
/// claiming otherwise is fighting the shell. All this does is register properly and then
/// open the page where the user makes the choice.
///
/// Everything is written under HKEY_CURRENT_USER, so no administrator rights are needed
/// and nothing is changed for other accounts on the machine.
/// </summary>
public static class FileAssociation
{
    private const string ProgId = "NotepadX.Document";
    private const string AppName = "NotepadX";
    private const string CapabilitiesPath = @"Software\NotepadX\Capabilities";

    private static readonly string[] Extensions =
        [".txt", ".log", ".ini", ".md", ".csv", ".json", ".xml", ".yml", ".yaml", ".cfg", ".conf"];

    public static string ExecutablePath
    {
        get
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path)) return path;
            return Path.Combine(AppContext.BaseDirectory, "NotepadX.exe");
        }
    }

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
            return key?.GetValue(null) is string command && command.Contains("NotepadX", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool Register(out string? error)
    {
        error = null;
        string exe = ExecutablePath;

        try
        {
            using (var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
            {
                progId.SetValue(null, "Text Document");
                progId.SetValue("FriendlyTypeName", "Text Document");

                using (var icon = progId.CreateSubKey("DefaultIcon"))
                    icon.SetValue(null, $"\"{exe}\",0");

                using (var command = progId.CreateSubKey(@"shell\open\command"))
                    command.SetValue(null, $"\"{exe}\" \"%1\"");

                using (var print = progId.CreateSubKey(@"shell\print\command"))
                    print.SetValue(null, $"\"{exe}\" /p \"%1\"");
            }

            // Makes the app show up in the "Open with" list for these types.
            using (var app = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\NotepadX.exe"))
            {
                app.SetValue("FriendlyAppName", AppName);

                using (var command = app.CreateSubKey(@"shell\open\command"))
                    command.SetValue(null, $"\"{exe}\" \"%1\"");

                using var supported = app.CreateSubKey("SupportedTypes");
                foreach (var ext in Extensions) supported.SetValue(ext, "");
            }

            foreach (var ext in Extensions)
            {
                using var open = Registry.CurrentUser.CreateSubKey(
                    $@"Software\Classes\{ext}\OpenWithProgids");
                open.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            }

            // Capabilities are what put the app in Settings › Default apps.
            using (var capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesPath))
            {
                capabilities.SetValue("ApplicationName", AppName);
                capabilities.SetValue("ApplicationDescription", "Offline-first text editor with tabs");

                using var associations = capabilities.CreateSubKey("FileAssociations");
                foreach (var ext in Extensions) associations.SetValue(ext, ProgId);
            }

            using (var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
                registered.SetValue(AppName, CapabilitiesPath);

            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool Unregister(out string? error)
    {
        error = null;
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Applications\NotepadX.exe", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\NotepadX", throwOnMissingSubKey: false);

            using (var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            {
                if (registered?.GetValue(AppName) is not null) registered.DeleteValue(AppName, throwOnMissingValue: false);
            }

            foreach (var ext in Extensions)
            {
                using var open = Registry.CurrentUser.OpenSubKey(
                    $@"Software\Classes\{ext}\OpenWithProgids", writable: true);
                if (open?.GetValue(ProgId) is not null) open.DeleteValue(ProgId, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Opens the Windows page where the user picks the default app for a type.</summary>
    public static void OpenDefaultAppsSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ms-settings:defaultapps",
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}
