using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Tests.WebComponent;

public sealed class StaticFileServer(string rootDirectory) : IAsyncDisposable
{
    readonly string _root = Path.GetFullPath(rootDirectory);
    readonly string _rootPrefix = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource _cts = new();
    Task? _acceptLoop;

    public int Port { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public void Start()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    async Task HandleClientAsync(TcpClient client)
    {
        using var clientScope = client;
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        var requestLine = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(requestLine))
            return;

        // Drain headers
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
        {
        }

        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await WriteResponseAsync(stream, 400, "text/plain; charset=utf-8", "Bad Request");
            return;
        }

        var method = parts[0];
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(stream, 405, "text/plain; charset=utf-8", "Method Not Allowed");
            return;
        }

        var requestPath = parts[1].Split('?', '#')[0];
        requestPath = Uri.UnescapeDataString(requestPath);
        if (requestPath == "/")
            requestPath = "/index.html";

        var relative = requestPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var filePath = Path.GetFullPath(Path.Combine(_root, relative));

        // The WASM publish output includes pre-compressed .br/.gz variants.
        // Our test server intentionally serves the uncompressed sibling files.
        if (filePath.EndsWith(".br", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            var extensionLength = Path.GetExtension(filePath).Length;
            var uncompressedPath = filePath[..^extensionLength];
            if (File.Exists(uncompressedPath))
            {
                filePath = uncompressedPath;
            }
        }

        if (!filePath.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await WriteResponseAsync(stream, 403, "text/plain; charset=utf-8", "Forbidden");
            return;
        }

        if (!File.Exists(filePath))
        {
            await WriteResponseAsync(stream, 404, "text/plain; charset=utf-8", "Not Found");
            return;
        }

        var contentType = GetContentType(filePath);
        var bytes = await File.ReadAllBytesAsync(filePath);
        await WriteResponseAsync(stream, 200, contentType, bytes, string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase));
    }

    static async Task WriteResponseAsync(Stream stream, int statusCode, string contentType, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await WriteResponseAsync(stream, statusCode, contentType, bytes, false);
    }

    static async Task WriteResponseAsync(Stream stream, int statusCode, string contentType, byte[] body, bool skipBody)
    {
        var reason = statusCode switch
        {
            200 => "OK",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            _ => "Error"
        };

        var header =
            $"HTTP/1.1 {statusCode} {reason}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n" +
            "Cache-Control: no-store\r\n" +
            "\r\n";

        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes);
        if (!skipBody)
            await stream.WriteAsync(body);
    }

    static string GetContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".md" => "text/markdown; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".wasm" => "application/wasm",
            ".svg" => "image/svg+xml",
            ".br" => "application/octet-stream",
            ".gz" => "application/octet-stream",
            ".dat" => "application/octet-stream",
            _ => "application/octet-stream"
        };

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop;
            }
            catch
            {
                // Ignore shutdown failures.
            }
        }
        _cts.Dispose();
    }
}
