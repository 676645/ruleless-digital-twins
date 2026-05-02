using Logic.Models.MapekModels;

namespace SmartNode.Streaming;

public sealed class StreamingWebSocketOptions
{
    public bool Enabled { get; set; } = true;
    public string HttpListenerPrefix { get; set; } = "http://localhost:8765/ws/";
}

public sealed class StreamingSimulationEvent
{
    public required string EventType { get; init; }
    public required string SimulationId { get; init; }
    public string? ParentSimulationId { get; init; }
    public string RuntimeStatus { get; init; } = "queued";
    public int Index { get; init; } = -1;
    public required IReadOnlyDictionary<string, object?> Properties { get; init; }
    public required IReadOnlyDictionary<string, object?> EdgeSettings { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}

public sealed class RuntimeSimulationNode
{
    public required string SimulationId { get; init; }
    public string? ParentSimulationId { get; init; }
    public string RuntimeStatus { get; set; } = "queued";
    public int Index { get; set; } = -1;
    public IReadOnlyDictionary<string, object?> Properties { get; set; } = new Dictionary<string, object?>();
    public IReadOnlyDictionary<string, object?> EdgeSettings { get; set; } = new Dictionary<string, object?>();
}

public sealed class StreamingSnapshot
{
    public required IReadOnlyCollection<RuntimeSimulationNode> Nodes { get; init; }
}

public sealed class StreamingCommand
{
    public string Kind { get; init; } = "command";
    public string? RequestId { get; init; }
    public required string Command { get; init; }
    public IDictionary<string, object?>? Payload { get; init; }
}

public sealed class StreamingCommandResult
{
    public string Kind { get; init; } = "commandResult";
    public string? RequestId { get; init; }
    public required string Command { get; init; }
    public bool Ok { get; init; }
    public string Message { get; init; } = string.Empty;
    public object? Data { get; init; }
}

public interface IStreamingTelemetryHub
{
    IAsyncEnumerable<StreamingSimulationEvent> ReadEvents(CancellationToken cancellationToken);
    StreamingSnapshot GetSnapshot();
    void ResetSnapshot();
}

public static class StreamingSimulationProjection
{
    public static IReadOnlyDictionary<string, object?> BuildPropertyProjection(Simulation simulation)
    {
        return simulation.SerializableSimulation.PropertyCache.Properties
            .ToDictionary(property => property.Name, property => (object?)property.Value);
    }

    public static IReadOnlyDictionary<string, object?> BuildEdgeProjection(Simulation simulation)
    {
        return simulation.SerializableSimulation.Actions
            .ToDictionary(action => action.Actuator, action => (object?)action.Value);
    }
}
