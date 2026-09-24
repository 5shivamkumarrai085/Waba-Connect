using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EmailChannel.Tests;

/// <summary>
/// The smallest SMTP server MailKit will hold a conversation with, so a real send can be observed
/// without touching the internet.
///
/// <para>
/// Worth the ~100 lines: it is the only way to assert on what actually goes on the wire. Mocking
/// the provider would verify that the code calls the code, and would have missed both of the real
/// defects this suite found — an empty message body, and Bcc leaking into the MIME headers.
/// </para>
/// <para>
/// Deliberately does not advertise STARTTLS. Connections in the suite are configured for no TLS,
/// and offering it would make MailKit upgrade and then fail against a server with no certificate.
/// </para>
/// </summary>
public sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<string> _messages = [];
    private readonly Lock _gate = new();
    private readonly Task _acceptLoop;

    public int Port { get; }

    /// <summary>
    /// When set, the server rejects DATA with this SMTP code — used to exercise the retry and
    /// dead-letter paths with a failure the provider classifies for itself. 4xx is transient,
    /// 5xx permanent.
    /// </summary>
    public int? RejectWithCode { get; set; }

    public FakeSmtpServer()
    {
        Port = GetFreePort();
        _listener = new TcpListener(IPAddress.Loopback, Port);
        _listener.Start();
        _acceptLoop = AcceptAsync(_cts.Token);
    }

    /// <summary>Every message transmitted so far, as raw MIME.</summary>
    public IReadOnlyList<string> Messages
    {
        get { lock (_gate) return _messages.ToList(); }
    }

    public int MessageCount
    {
        get { lock (_gate) return _messages.Count; }
    }

    public void Clear()
    {
        lock (_gate) _messages.Clear();
    }

    private async Task AcceptAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }

                // Each conversation on its own task, so a client that stalls cannot block the
                // next connection — the dispatch worker opens several concurrently.
                _ = Task.Run(() => ConverseAsync(client, ct), CancellationToken.None);
            }
        }
        finally
        {
            _listener.Stop();
        }
    }

    private async Task ConverseAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false))
                {
                    AutoFlush = true,
                    // SMTP is CRLF-terminated; a bare LF makes MailKit wait for the rest of the line.
                    NewLine = "\r\n"
                };

                await writer.WriteLineAsync("220 localhost fake-smtp ready");

                string? line;
                while (!ct.IsCancellationRequested && (line = await reader.ReadLineAsync()) is not null)
                {
                    if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("250-localhost");
                        await writer.WriteLineAsync("250-SIZE 26214400");
                        await writer.WriteLineAsync("250 8BITMIME");
                    }
                    else if (line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("250 localhost");
                    }
                    else if (line.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase)
                          || line.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("250 2.1.0 OK");
                    }
                    else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        if (RejectWithCode is { } code)
                        {
                            await writer.WriteLineAsync($"{code} Rejected by the test server");
                            continue;
                        }

                        await writer.WriteLineAsync("354 Start mail input; end with <CRLF>.<CRLF>");

                        var body = new StringBuilder();
                        string? dataLine;
                        while ((dataLine = await reader.ReadLineAsync()) is not null && dataLine != ".")
                        {
                            // Undo SMTP dot-stuffing so assertions see the original content.
                            body.AppendLine(dataLine.StartsWith("..") ? dataLine[1..] : dataLine);
                        }

                        lock (_gate) _messages.Add(body.ToString());
                        await writer.WriteLineAsync("250 2.0.0 OK queued as faketest12345");
                    }
                    else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("221 2.0.0 Bye");
                        break;
                    }
                    else if (line.StartsWith("RSET", StringComparison.OrdinalIgnoreCase)
                          || line.StartsWith("NOOP", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("250 2.0.0 OK");
                    }
                    else
                    {
                        // Includes AUTH. Accepting it unconditionally is fine here: the suite is
                        // not testing authentication, and refusing would only add noise.
                        await writer.WriteLineAsync("235 2.7.0 Accepted");
                    }
                }
            }
            catch (IOException)
            {
                // A client that hung up mid-conversation. Not interesting to a test.
            }
        }
    }

    private static int GetFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        try
        {
            await _acceptLoop;
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        _cts.Dispose();
    }
}
