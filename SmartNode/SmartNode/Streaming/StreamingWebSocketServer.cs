using Logic.Mapek;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SmartNode.Streaming;

public sealed class StreamingWebSocketServer : BackgroundService
{
    private readonly IStreamingTelemetryHub _telemetryHub;
    private readonly IMapekManager _mapekManager;
    private readonly StreamingWebSocketOptions _options;
    private readonly ILogger<StreamingWebSocketServer> _logger;

    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<Guid, ClientConnection> _clients = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public StreamingWebSocketServer(
        IStreamingTelemetryHub telemetryHub,
        IMapekManager mapekManager,
        StreamingWebSocketOptions options,
        ILogger<StreamingWebSocketServer> logger)
    {
        _telemetryHub = telemetryHub;
        _mapekManager = mapekManager;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("WebSocket streaming server disabled.");
            return;
        }

        var prefix = NormalizePrefix(_options.HttpListenerPrefix);
        _listener.Prefixes.Add(prefix);
        _listener.Start();

        _logger.LogInformation("WebSocket streaming server listening at {prefix}", prefix);

        var acceptLoopTask = AcceptLoop(stoppingToken);
        var broadcastLoopTask = BroadcastLoop(stoppingToken);

        await Task.WhenAll(acceptLoopTask, broadcastLoopTask);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        foreach (var client in _clients.Values)
        {
            try
            {
                client.Socket.Abort();
                client.Socket.Dispose();
            }
            catch
            {
                // Nothing to do if the socket has already been cleaned up.
            }
        }

        _clients.Clear();

        return base.StopAsync(cancellationToken);
    }

    private async Task AcceptLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                if (_listener.IsListening)
                {
                    _logger.LogWarning("HttpListener stopped unexpectedly while accepting connections.");
                }

                break;
            }

            if (context is null)
            {
                continue;
            }

            _ = Task.Run(() => HandleConnection(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleConnection(HttpListenerContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = 400;
            context.Response.Close();
            return;
        }

        WebSocketContext webSocketContext;
        try
        {
            webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to accept WebSocket connection.");
            context.Response.StatusCode = 500;
            context.Response.Close();
            return;
        }

        var id = Guid.NewGuid();
        var client = new ClientConnection(id, webSocketContext.WebSocket);
        _clients[id] = client;

        _logger.LogInformation("WebSocket client connected: {clientId}", id);

        try
        {
            await SendSnapshot(client, cancellationToken);
            await ReceiveLoop(client, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown path.
        }
        catch (WebSocketException)
        {
            // Client disconnected abruptly.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unexpected WebSocket client error for {clientId}", id);
        }
        finally
        {
            _clients.TryRemove(id, out _);
            try
            {
                if (client.Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
            }
            catch
            {
                // Ignore socket close failures.
            }

            client.Socket.Dispose();
            _logger.LogInformation("WebSocket client disconnected: {clientId}", id);
        }
    }

    private async Task ReceiveLoop(ClientConnection client, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        while (!cancellationToken.IsCancellationRequested && client.Socket.State == WebSocketState.Open)
        {
            var text = await ReceiveMessage(client.Socket, buffer, cancellationToken);
            if (text is null)
            {
                break;
            }

            StreamingCommand? command;
            try
            {
                command = JsonSerializer.Deserialize<StreamingCommand>(text, JsonOptions);
            }
            catch (JsonException)
            {
                await SendJson(client, new StreamingCommandResult
                {
                    RequestId = null,
                    Command = "unknown",
                    Ok = false,
                    Message = "Invalid JSON command payload."
                }, cancellationToken);
                continue;
            }

            if (command is null || string.IsNullOrWhiteSpace(command.Command))
            {
                await SendJson(client, new StreamingCommandResult
                {
                    RequestId = command?.RequestId,
                    Command = command?.Command ?? "unknown",
                    Ok = false,
                    Message = "Missing command name."
                }, cancellationToken);
                continue;
            }

            var result = HandleCommand(command);
            await SendJson(client, result, cancellationToken);

            if (string.Equals(command.Command, "getSnapshot", StringComparison.OrdinalIgnoreCase) && result.Ok)
            {
                await SendSnapshot(client, cancellationToken);
            }
        }
    }

    private StreamingCommandResult HandleCommand(StreamingCommand command)
    {
        var commandName = command.Command.Trim();

        if (string.Equals(commandName, "ping", StringComparison.OrdinalIgnoreCase))
        {
            return new StreamingCommandResult
            {
                RequestId = command.RequestId,
                Command = command.Command,
                Ok = true,
                Message = "pong",
                Data = new { utc = DateTime.UtcNow }
            };
        }

        if (string.Equals(commandName, "getSnapshot", StringComparison.OrdinalIgnoreCase))
        {
            return new StreamingCommandResult
            {
                RequestId = command.RequestId,
                Command = command.Command,
                Ok = true,
                Message = "Snapshot queued."
            };
        }

        if (string.Equals(commandName, "resetSnapshot", StringComparison.OrdinalIgnoreCase))
        {
            _telemetryHub.ResetSnapshot();
            return new StreamingCommandResult
            {
                RequestId = command.RequestId,
                Command = command.Command,
                Ok = true,
                Message = "Snapshot cleared."
            };
        }

        if (string.Equals(commandName, "stopLoop", StringComparison.OrdinalIgnoreCase))
        {
            _mapekManager.StopLoop();
            return new StreamingCommandResult
            {
                RequestId = command.RequestId,
                Command = command.Command,
                Ok = true,
                Message = "MAPE-K loop stop requested."
            };
        }

        return new StreamingCommandResult
        {
            RequestId = command.RequestId,
            Command = command.Command,
            Ok = false,
            Message = "Unknown command. Supported: ping, getSnapshot, resetSnapshot, stopLoop."
        };
    }

    private async Task BroadcastLoop(CancellationToken cancellationToken)
    {
        await foreach (var simulationEvent in _telemetryHub.ReadEvents(cancellationToken))
        {
            var payload = new
            {
                kind = "simulationEvent",
                simulationEvent
            };

            await Broadcast(payload, cancellationToken);
        }
    }

    private async Task Broadcast(object payload, CancellationToken cancellationToken)
    {
        var disconnected = new List<Guid>();

        foreach (var (clientId, client) in _clients)
        {
            try
            {
                await SendJson(client, payload, cancellationToken);
            }
            catch
            {
                disconnected.Add(clientId);
            }
        }

        foreach (var clientId in disconnected)
        {
            if (_clients.TryRemove(clientId, out var client))
            {
                try
                {
                    client.Socket.Abort();
                    client.Socket.Dispose();
                }
                catch
                {
                    // Ignore cleanup errors.
                }
            }
        }
    }

    private async Task SendSnapshot(ClientConnection client, CancellationToken cancellationToken)
    {
        var payload = new
        {
            kind = "snapshot",
            snapshot = _telemetryHub.GetSnapshot()
        };

        await SendJson(client, payload, cancellationToken);
    }

    private async Task SendJson(ClientConnection client, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);

        await client.SendLock.WaitAsync(cancellationToken);
        try
        {
            if (client.Socket.State == WebSocketState.Open)
            {
                await client.Socket.SendAsync(segment, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }
        }
        finally
        {
            client.SendLock.Release();
        }
    }

    private static async Task<string?> ReceiveMessage(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        WebSocketReceiveResult? result;

        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            output.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
        while (!result.EndOfMessage);

        return output.ToString();
    }

    private static string NormalizePrefix(string prefix)
    {
        var trimmed = string.IsNullOrWhiteSpace(prefix) ? "http://localhost:8765/ws/" : prefix.Trim();
        if (!trimmed.EndsWith('/'))
        {
            trimmed += '/';
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var parsedPrefix) &&
            (parsedPrefix.Host == "+" || parsedPrefix.Host == "*"))
        {
            var builder = new UriBuilder(parsedPrefix)
            {
                Host = "localhost"
            };
            trimmed = builder.Uri.ToString();
        }

        return trimmed;
    }

    private sealed class ClientConnection
    {
        public ClientConnection(Guid id, WebSocket socket)
        {
            Id = id;
            Socket = socket;
        }

        public Guid Id { get; }
        public WebSocket Socket { get; }
        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }
}
