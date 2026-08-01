using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace NotepadX.Services;

/// <summary>
/// Local named pipe used to hand file arguments to the already running instance,
/// the way Windows 11 Notepad opens a second file as a new tab. Purely machine-local.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly string _pipeName;
    private readonly Mutex _mutex;
    private CancellationTokenSource? _cts;

    public bool IsFirstInstance { get; }
    public event Action<string[]>? FilesRequested;

    public SingleInstance()
    {
        string user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        string suffix = Convert.ToHexString(
            System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(user)))[..16];

        _pipeName = "NotepadX_pipe_" + suffix;
        _mutex = new Mutex(true, @"Local\NotepadX_mutex_" + suffix, out bool createdNew);
        IsFirstInstance = createdNew;
    }

    public void StartServer()
    {
        if (!IsFirstInstance) return;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ServerLoopAsync(_cts.Token));
    }

    private async Task ServerLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var payload = await reader.ReadToEndAsync(token).ConfigureAwait(false);

                var files = payload
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();

                FilesRequested?.Invoke(files);
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) { /* client vanished, keep listening */ }
        }
    }

    /// <summary>Sends paths to the running instance. Returns false if nobody answered.</summary>
    public bool SendToExisting(string[] files)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.Write(string.Join('\n', files));
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { if (IsFirstInstance) _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
    }
}
