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

        var files = e.Args.Where(a => !a.StartsWith('-') && !a.StartsWith('/')).ToArray();
        var settings = AppSettings.Current;

        _instance = new SingleInstance();

        // A second launch normally hands its files to the running window as new tabs,
        // matching Windows 11 Notepad. "Open in new window" opts out of that.
        if (!_instance.IsFirstInstance && settings.OpenFilesIn == OpenFilesIn.NewTab)
        {
            if (files.Length > 0 && _instance.SendToExisting(files))
            {
                Shutdown();
                return;
            }
            if (files.Length == 0 && _instance.SendToExisting(new[] { "focus" }))
            {
                Shutdown();
                return;
            }
        }

        _instance.FilesRequested += OnFilesRequested;
        _instance.StartServer();

        ThemeManager.Apply(settings.Theme);
        DispatcherUnhandledException += OnUnhandledException;

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

        if (files.Length > 0) window.OpenFiles(files);
        MainWindow = window;
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

            var real = files.Where(f => f != "focus").ToArray();
            if (real.Length > 0) target.OpenFiles(real);
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
