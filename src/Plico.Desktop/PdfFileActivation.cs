using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;

namespace Plico.Desktop;

/// <summary>Routes operating system PDF open requests to a Plico window owned by the current user.</summary>
internal sealed class PdfFileActivation : IDisposable
{
    private const int MaximumRequestBytes = 512 * 1024;
    private const byte Received = 1;
    private static readonly TimeSpan RelayTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(280);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _listener;

    internal PdfFileActivation(string pipeName, Action<string[]> accept) => _listener = ListenAsync(pipeName, accept);

    internal static string UserPipeName =>
        "plico-pdf-" + ApplicationSettings.LibraryKey(ApplicationSettings.DirectoryPath)[..24];

    internal static string[] ReadPaths(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || (arguments[0].StartsWith("--", StringComparison.Ordinal) && arguments[0] != "--")) return [];
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var paths = new List<string>();
        foreach (string argument in arguments)
        {
            try
            {
                if (!Path.GetExtension(argument).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                string fullPath = Path.GetFullPath(argument);
                if (seen.Add(fullPath)) paths.Add(fullPath);
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }
        return paths.ToArray();
    }

    internal static async Task<bool> ForwardAsync(string pipeName, string[] paths)
    {
        if (paths.Length == 0) return false;
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(paths);
        if (payload.Length > MaximumRequestBytes) return false;
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var connect = new CancellationTokenSource(ConnectTimeout);
        try
        {
            await client.ConnectAsync(connect.Token).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(RelayTimeout);
            byte[] header = new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            await client.WriteAsync(header, timeout.Token).ConfigureAwait(false);
            await client.WriteAsync(payload, timeout.Token).ConfigureAwait(false);
            await client.ReadExactlyAsync(header.AsMemory(0, 1), timeout.Token).ConfigureAwait(false);
            return header[0] == Received;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task ListenAsync(string name, Action<string[]> receive)
    {
        var token = _shutdown.Token;
        try
        {
            await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            while (!token.IsCancellationRequested)
            {
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                try
                {
                    using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
                    connection.CancelAfter(RelayTimeout);
                    await ReceiveAsync(server, receive, connection.Token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or OperationCanceledException or JsonException)
                {
                }
                finally
                {
                    server.Disconnect();
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
        }
    }

    private static async Task ReceiveAsync(Stream connection, Action<string[]> receive, CancellationToken token)
    {
        byte[] header = new byte[sizeof(int)];
        await connection.ReadExactlyAsync(header, token).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaximumRequestBytes) return;
        byte[] payload = new byte[length];
        await connection.ReadExactlyAsync(payload, token).ConfigureAwait(false);
        var requested = JsonSerializer.Deserialize<string?[]>(payload) ?? [];
        var paths = ReadPaths(requested.OfType<string>().ToArray());
        if (paths.Length == 0) return;
        receive(paths);
        header[0] = Received;
        await connection.WriteAsync(header.AsMemory(0, 1), token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _ = _listener.ContinueWith(_ => _shutdown.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
