using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Funbit.Ets.Telemetry.Server.Controllers;

namespace Funbit.Ets.Telemetry.Server
{
    /// <summary>
    /// Minimal HTTP server implementation using raw TCP sockets.
    /// Bypasses HTTP.SYS completely to work around Windows KB5066835/KB5065789 bugs.
    /// </summary>
    public class MinimalHttpServer : IDisposable
    {
        static readonly log4net.ILog Log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly int _port;
        private TcpListener _listener;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _listenerTask;
        private bool _isRunning;
        private readonly object _lock = new object();

        // Keep-alive: reuse TCP connections to avoid TIME_WAIT socket accumulation.
        // At 10 polls/sec with Connection: close, each poll opens a new TCP connection
        // that lingers in TIME_WAIT for 240s, eventually exhausting ephemeral ports.
        const int KeepAliveTimeoutMs = 30000;

        public MinimalHttpServer(int port)
        {
            _port = port;
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_isRunning)
                {
                    Log.Warn("Server is already running");
                    return;
                }

                try
                {
                    Log.Info("=== Minimal HTTP Server Starting ===");
                    Log.InfoFormat("Port: {0}", _port);
                    Log.Info("Mode: HTTP.SYS Bypass (workaround for KB5066835/KB5065789)");

                    _listener = new TcpListener(IPAddress.Any, _port);
                    _listener.Start();
                    _cancellationTokenSource = new CancellationTokenSource();
                    _isRunning = true;

                    // Start accepting connections in background
                    _listenerTask = Task.Run(() => AcceptConnectionsAsync(_cancellationTokenSource.Token));

                    Log.InfoFormat("Minimal HTTP Server started successfully on port {0}", _port);
                    Log.Info("Endpoints available:");
                    Log.Info("  - GET  /              (Status page)");
                    Log.Info("  - GET  /api/ets2/telemetry  (Telemetry JSON)");
                    Log.Info("  - POST /api/ets2/telemetry  (Telemetry JSON)");
                }
                catch (Exception ex)
                {
                    _isRunning = false;
                    Log.Error("Failed to start Minimal HTTP Server", ex);
                    throw;
                }
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRunning)
                    return;

                try
                {
                    Log.Info("Stopping Minimal HTTP Server...");

                    _cancellationTokenSource?.Cancel();
                    _listener?.Stop();

                    // Wait for listener task to complete (with timeout)
                    if (_listenerTask != null && !_listenerTask.Wait(TimeSpan.FromSeconds(5)))
                    {
                        Log.Warn("Listener task did not complete within timeout");
                    }

                    _isRunning = false;
                    Log.Info("Minimal HTTP Server stopped");
                }
                catch (Exception ex)
                {
                    Log.Error("Error stopping server", ex);
                }
            }
        }

        private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();

                    // Handle each client in a separate task (long-lived with keep-alive)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleClientAsync(client, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Error handling client", ex);
                        }
                    }, cancellationToken);
                }
                catch (ObjectDisposedException)
                {
                    // Listener was stopped, exit gracefully
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        Log.Error("Error accepting client connection", ex);
                    }
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                client.SendTimeout = 5000;

                try
                {
                    using (var stream = client.GetStream())
                    {
                        // Keep-alive loop: handle multiple requests on the same TCP connection
                        while (!cancellationToken.IsCancellationRequested)
                        {
                            var request = await ReadHttpRequestAsync(stream, KeepAliveTimeoutMs);

                            if (request == null)
                                break; // Client closed connection, idle timeout, or malformed request

                            bool clientWantsClose = request.Headers != null &&
                                request.Headers.Any(h =>
                                    h.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase) &&
                                    h.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0);

                            var response = RouteRequest(request);
                            await SendHttpResponseAsync(stream, response, closeConnection: clientWantsClose);

                            if (clientWantsClose)
                                break;
                        }
                    }
                }
                catch (IOException ex)
                {
                    Log.Debug($"IO exception (client likely disconnected): {ex.Message}");
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                        Log.Error("Error processing client request", ex);
                }
            }
        }

        private async Task<HttpRequest> ReadHttpRequestAsync(NetworkStream stream, int timeoutMs)
        {
            var buffer = new byte[8192];

            // Idle timeout: if no request arrives within timeoutMs, return null to close
            // the keep-alive connection. NetworkStream.ReadAsync doesn't respect
            // Socket.ReceiveTimeout, so we use Task.WhenAny with Task.Delay instead.
            var readTask = stream.ReadAsync(buffer, 0, buffer.Length);
            var completed = await Task.WhenAny(readTask, Task.Delay(timeoutMs));

            if (completed != readTask)
                return null; // Idle timeout — close connection

            var bytesRead = await readTask;

            if (bytesRead == 0)
                return null;

            var requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            // Parse HTTP request line (e.g., "GET /api/ets2/telemetry HTTP/1.1")
            var lines = requestText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length == 0)
                return null;

            var requestLine = lines[0].Split(' ');

            if (requestLine.Length < 3)
                return null;

            return new HttpRequest
            {
                Method = requestLine[0],
                Path = requestLine[1],
                Headers = lines.Skip(1).TakeWhile(l => !string.IsNullOrWhiteSpace(l)).ToArray()
            };
        }

        private HttpResponse RouteRequest(HttpRequest request)
        {
            Log.DebugFormat("Request: {0} {1}", request.Method, request.Path);

            // Normalize path
            var path = request.Path.TrimEnd('/');
            if (string.IsNullOrEmpty(path))
                path = "/";

            // Route to appropriate handler
            if (path == "/")
            {
                return HandleStatusPage(request);
            }
            else if (path == "/api/ets2/telemetry")
            {
                return HandleTelemetryRequest(request);
            }
            else
            {
                return new HttpResponse
                {
                    StatusCode = 404,
                    StatusText = "Not Found",
                    ContentType = "text/plain",
                    Body = "404 Not Found"
                };
            }
        }

        private HttpResponse HandleStatusPage(HttpRequest request)
        {
            if (request.Method != "GET")
            {
                return new HttpResponse
                {
                    StatusCode = 405,
                    StatusText = "Method Not Allowed",
                    ContentType = "text/plain",
                    Body = "Method Not Allowed"
                };
            }

            // Use the shared HTML template from Ets2AppController with bypass notice enabled
            var html = Ets2AppController.GetStatusPageHtml(showBypassNotice: true);

            return new HttpResponse
            {
                StatusCode = 200,
                StatusText = "OK",
                ContentType = "text/html; charset=utf-8",
                Body = html,
                CacheControl = "no-cache"
            };
        }

        private HttpResponse HandleTelemetryRequest(HttpRequest request)
        {
            if (request.Method != "GET" && request.Method != "POST")
            {
                return new HttpResponse
                {
                    StatusCode = 405,
                    StatusText = "Method Not Allowed",
                    ContentType = "text/plain",
                    Body = "Method Not Allowed"
                };
            }

            try
            {
                var telemetryJson = Ets2TelemetryController.GetEts2TelemetryJson();

                return new HttpResponse
                {
                    StatusCode = 200,
                    StatusText = "OK",
                    ContentType = "application/json; charset=utf-8",
                    Body = telemetryJson,
                    CacheControl = "no-cache",
                    EnableCors = true
                };
            }
            catch (Exception ex)
            {
                Log.Error("Error getting telemetry data", ex);

                return new HttpResponse
                {
                    StatusCode = 500,
                    StatusText = "Internal Server Error",
                    ContentType = "text/plain",
                    Body = "Internal Server Error"
                };
            }
        }

        private async Task SendHttpResponseAsync(NetworkStream stream, HttpResponse response, bool closeConnection = false)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(response.Body ?? "");

            var responseBuilder = new StringBuilder();
            responseBuilder.AppendFormat("HTTP/1.1 {0} {1}\r\n", response.StatusCode, response.StatusText);
            responseBuilder.AppendFormat("Content-Type: {0}\r\n", response.ContentType);
            responseBuilder.AppendFormat("Content-Length: {0}\r\n", bodyBytes.Length);

            if (closeConnection)
            {
                responseBuilder.Append("Connection: close\r\n");
            }
            else
            {
                responseBuilder.Append("Connection: keep-alive\r\n");
                responseBuilder.AppendFormat("Keep-Alive: timeout={0}\r\n", KeepAliveTimeoutMs / 1000);
            }

            if (!string.IsNullOrEmpty(response.CacheControl))
            {
                responseBuilder.AppendFormat("Cache-Control: {0}\r\n", response.CacheControl);
            }

            if (response.EnableCors)
            {
                responseBuilder.Append("Access-Control-Allow-Origin: *\r\n");
                responseBuilder.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
                responseBuilder.Append("Access-Control-Allow-Headers: Content-Type\r\n");
            }

            responseBuilder.Append("\r\n");

            var headerBytes = Encoding.UTF8.GetBytes(responseBuilder.ToString());

            // Send headers
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);

            // Send body
            if (bodyBytes.Length > 0)
            {
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
            }

            await stream.FlushAsync();
        }

        public void Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
            _listener = null;
        }

        // Helper classes
        private class HttpRequest
        {
            public string Method { get; set; }
            public string Path { get; set; }
            public string[] Headers { get; set; }
        }

        private class HttpResponse
        {
            public int StatusCode { get; set; }
            public string StatusText { get; set; }
            public string ContentType { get; set; }
            public string Body { get; set; }
            public string CacheControl { get; set; }
            public bool EnableCors { get; set; }
        }
    }
}
