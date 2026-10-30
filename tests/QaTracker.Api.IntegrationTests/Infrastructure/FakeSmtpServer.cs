using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace QaTracker.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Minimal SMTP server on a free local port: accepts every mail and records the envelope (MAIL FROM / RCPT TO).
/// Enough for System.Net.Mail without TLS or sign-in.
/// </summary>
public sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public FakeSmtpServer()
    {
        _listener.Start();
        _loop = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public ConcurrentQueue<(string From, string To)> Received { get; } = new();

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
        }

        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            await HandleAsync(client, _stop.Token);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 fake.smtp ready");
        string? from = null;
        var to = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var command = line.Length >= 4 ? line[..4].ToUpperInvariant() : line.ToUpperInvariant();
            switch (command)
            {
                case "EHLO":
                case "HELO":
                    await writer.WriteLineAsync("250 fake.smtp");
                    break;
                case "MAIL":
                    from = Address(line);
                    await writer.WriteLineAsync("250 OK");
                    break;
                case "RCPT":
                    to.Add(Address(line));
                    await writer.WriteLineAsync("250 OK");
                    break;
                case "DATA":
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                    while (await reader.ReadLineAsync(cancellationToken) is { } data && data != ".")
                    {
                    }

                    foreach (var recipient in to)
                    {
                        Received.Enqueue((from ?? string.Empty, recipient));
                    }

                    await writer.WriteLineAsync("250 OK queued");
                    break;
                case "QUIT":
                    await writer.WriteLineAsync("221 Bye");
                    return;
                default:
                    await writer.WriteLineAsync("250 OK");
                    break;
            }
        }
    }

    private static string Address(string line)
    {
        var start = line.IndexOf('<', StringComparison.Ordinal);
        var end = line.IndexOf('>', StringComparison.Ordinal);
        return start >= 0 && end > start ? line[(start + 1)..end] : line;
    }
}
