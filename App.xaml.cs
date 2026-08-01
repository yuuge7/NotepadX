using System.Windows;
using System.Windows.Threading;
using NotepadX.Services;

namespace NotepadX;

public partial class App : Application
{
    private SingleInstance? _instance;

    public static SessionState Session { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var parsed = CommandLine.Parse(e.Args);
        var settings = AppSettings.Current;

        if (parsed.ShowHelp)
        {
            // A WinExe has no console attached, so the usage text goes in a dialog.
            MessageBox.Show(CommandLineArgs.Usage, "NotepadX", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        ThemeManager.Apply(settings.Theme);
        DispatcherUnhandledException += OnUnhandledException;

        if (parsed.Print && parsed.Files.Count > 0)
        {
            PrintAndExit(parsed);
            return;
        }

        // Passed on verbatim so any ":line" suffix survives the hand-off.
        var arguments = parsed.Files
            .Select(f => f.Line is int line ? $"{f.Path}:{line}" : f.Path)
            .ToArray();

        _instance = new SingleInstance();

        // A second launch normally hands its files to the running window as new tabs,
        // matching Windows 11 Notepad. "Open in new window" and -n opt out of that.
        bool handOff = !_instance.IsFirstInstance
                       && !parsed.NewWindow
                       && settings.OpenFilesIn == OpenFilesIn.NewTab;

        if (handOff)
        {
            if (arguments.Length > 0 && _instance.SendToExisting(arguments))
            {
                Shutdown();
                return;
            }
            if (arguments.Length == 0 && _instance.SendToExisting(new[] { "focus" }))
            {
                Shutdown();
                return;
            }
        }

        _instance.FilesRequested += OnFilesRequested;
        _instance.StartServer();

        Session = SessionStore.Load();

        MainWindow? window = null;

        if (settings.SessionMode == SessionMode.ContinuePrevious && Session.Windows.Count > 0)
        {
            foreach (var sw in Session.Windows)
            {
                var w = new MainWindow(sw);
                window ??= w;
                w.Show();
            }
        }
        else
        {
            SessionStore.PruneOrphanBuffers(new SessionState());
        }

        if (window is null)
        {
            window = new MainWindow(null);
            window.Show();
        }

        if (parsed.Files.Count > 0) window.OpenRequests(parsed.Files);
        MainWindow = window;
    }

    /// <summary>
    /// "/p file" prints to the default printer and exits, which is what the shell's
    /// Print verb on a text file expects. No window is ever shown.
    /// </summary>
    private void PrintAndExit(CommandLineArgs parsed)
    {
        var window = new MainWindow(null);
        try
        {
            foreach (var request in parsed.Files)
            {
                window.OpenFiles([request.Path]);
                window.PrintTab(window.ActiveTab, prompt: false);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Printing failed:\n\n" + ex.Message,
                "NotepadX", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SuspendPersist = true;   // a print run must not overwrite the saved session
            window.Close();
            Shutdown();
        }
    }

    private void OnFilesRequested(string[] files)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var target = Windows.OfType<MainWindow>().LastOrDefault();
            if (target is null)
            {
                target = new MainWindow(null);
                target.Show();
            }

            if (target.WindowState == WindowState.Minimized)
                target.WindowState = WindowState.Normal;
            target.Activate();

            var requests = files
                .Where(f => f != "focus")
                .Select(CommandLine.ParseFile)
                .ToList();

            if (requests.Count > 0) target.OpenRequests(requests);
        });
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "NotepadX hit an unexpected error:\n\n" + e.Exception.Message,
            "NotepadX", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Set right before a deliberate quit so the snapshot taken at that moment is not
    /// eroded as each window closes one after another.
    /// </summary>
    public static bool SuspendPersist { get; set; }

    /// <summary>Rebuilds session.json from every open window. Called on any structural change.</summary>
    public static void PersistSession()
    {
        if (Current is null || SuspendPersist) return;
        var state = new SessionState();
        foreach (var w in Current.Windows.OfType<MainWindow>())
            state.Windows.Add(w.CaptureSession());

        SessionStore.Save(state);
        SessionStore.PruneOrphanBuffers(state);
    }
}
