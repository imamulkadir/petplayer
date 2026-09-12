using System.IO;
using System.IO.Pipes;
using System.Text;

namespace PetPlayer.Services;

/// <summary>
/// Keeps Pet Player to a single running instance per user session using a
/// named mutex, and forwards file paths from later launches ("Open with" on
/// another video) to the first instance over a named pipe. Deliberately does
/// not attempt any process-killing - if acquiring the mutex fails we simply
/// hand off and exit.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = "Local\\PetPlayer.SingleInstance";
    private const string PipeName = "PetPlayer.IPC";
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(2);

    private Mutex? _mutex;
    private CancellationTokenSource? _listenerCts;

    /// <summary>Raised on a background thread when another launch forwards a file path (or null for a plain activation request).</summary>
    public event EventHandler<string?>? FileReceived;

    public bool TryAcquireOwnership()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        return createdNew;
    }

    public void StartListening()
    {
        _listenerCts = new CancellationTokenSource();
        _ = ListenLoopAsync(_listenerCts.Token);
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token);

                using var reader = new StreamReader(server, Encoding.UTF8);
                var message = await reader.ReadToEndAsync(token);

                FileReceived?.Invoke(this, string.IsNullOrWhiteSpace(message) ? null : message);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Single-instance pipe listener error.", ex);
                await Task.Delay(500, CancellationToken.None);
            }
        }
    }

    /// <summary>Sends a file path (or a plain activation ping) to an already-running instance.</summary>
    public static bool TrySendToRunningInstance(string? filePath)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect((int)SendTimeout.TotalMilliseconds);

            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.Write(filePath ?? string.Empty);
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.LogError("Failed to forward launch arguments to the running Pet Player instance.", ex);
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            _listenerCts?.Cancel();
            _listenerCts?.Dispose();

            if (_mutex is not null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogError("Error while disposing SingleInstanceService.", ex);
        }
    }
}
