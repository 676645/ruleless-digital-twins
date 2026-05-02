using Logic.Mapek;
using Logic.Models.MapekModels;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace SmartNode.Streaming;

public sealed class WebSocketStreamingSimulationProvider : IStreamingSimulationProvider, IStreamingTelemetryHub
{
    private readonly Channel<StreamingSimulationEvent> _eventChannel = Channel.CreateUnbounded<StreamingSimulationEvent>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ConcurrentDictionary<Simulation, string> _simulationIds = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<string, RuntimeSimulationNode> _snapshotNodes = new();

    public IAsyncEnumerable<StreamingSimulationEvent> ReadEvents(CancellationToken cancellationToken)
    {
        return _eventChannel.Reader.ReadAllAsync(cancellationToken);
    }

    public StreamingSnapshot GetSnapshot()
    {
        return new StreamingSnapshot
        {
            Nodes = _snapshotNodes.Values
                .Select(node => new RuntimeSimulationNode
                {
                    SimulationId = node.SimulationId,
                    ParentSimulationId = node.ParentSimulationId,
                    RuntimeStatus = node.RuntimeStatus,
                    Index = node.Index,
                    Properties = new Dictionary<string, object?>(node.Properties),
                    EdgeSettings = new Dictionary<string, object?>(node.EdgeSettings)
                })
                .ToArray()
        };
    }

    public void ResetSnapshot()
    {
        _snapshotNodes.Clear();
    }

    public void Queue(Simulation parent, Simulation simulation)
    {
        var parentId = EnsureSimulationId(parent);
        var simulationId = EnsureSimulationId(simulation);

        var properties = StreamingSimulationProjection.BuildPropertyProjection(simulation);
        var edgeSettings = StreamingSimulationProjection.BuildEdgeProjection(simulation);

        _snapshotNodes[simulationId] = new RuntimeSimulationNode
        {
            SimulationId = simulationId,
            ParentSimulationId = parentId,
            RuntimeStatus = "queued",
            Index = simulation.Index,
            Properties = properties,
            EdgeSettings = edgeSettings
        };

        PublishEvent(new StreamingSimulationEvent
        {
            EventType = "queued",
            SimulationId = simulationId,
            ParentSimulationId = parentId,
            RuntimeStatus = "queued",
            Index = simulation.Index,
            Properties = properties,
            EdgeSettings = edgeSettings
        });
    }

    public void Starting(Simulation simulation)
    {
        var simulationId = EnsureSimulationId(simulation);
        var properties = StreamingSimulationProjection.BuildPropertyProjection(simulation);
        var edgeSettings = StreamingSimulationProjection.BuildEdgeProjection(simulation);

        _snapshotNodes.AddOrUpdate(simulationId,
            _ => new RuntimeSimulationNode
            {
                SimulationId = simulationId,
                ParentSimulationId = null,
                RuntimeStatus = "started",
                Index = simulation.Index,
                Properties = properties,
                EdgeSettings = edgeSettings
            },
            (_, existing) =>
            {
                existing.RuntimeStatus = "started";
                existing.Index = simulation.Index;
                existing.Properties = properties;
                existing.EdgeSettings = edgeSettings;
                return existing;
            });

        PublishEvent(new StreamingSimulationEvent
        {
            EventType = "started",
            SimulationId = simulationId,
            ParentSimulationId = _snapshotNodes[simulationId].ParentSimulationId,
            RuntimeStatus = "started",
            Index = simulation.Index,
            Properties = properties,
            EdgeSettings = edgeSettings
        });
    }

    public void Stopped(Simulation simulation)
    {
        var simulationId = EnsureSimulationId(simulation);
        var properties = StreamingSimulationProjection.BuildPropertyProjection(simulation);
        var edgeSettings = StreamingSimulationProjection.BuildEdgeProjection(simulation);

        _snapshotNodes.AddOrUpdate(simulationId,
            _ => new RuntimeSimulationNode
            {
                SimulationId = simulationId,
                ParentSimulationId = null,
                RuntimeStatus = "stopped",
                Index = simulation.Index,
                Properties = properties,
                EdgeSettings = edgeSettings
            },
            (_, existing) =>
            {
                existing.RuntimeStatus = "stopped";
                existing.Index = simulation.Index;
                existing.Properties = properties;
                existing.EdgeSettings = edgeSettings;
                return existing;
            });

        PublishEvent(new StreamingSimulationEvent
        {
            EventType = "stopped",
            SimulationId = simulationId,
            ParentSimulationId = _snapshotNodes[simulationId].ParentSimulationId,
            RuntimeStatus = "stopped",
            Index = simulation.Index,
            Properties = properties,
            EdgeSettings = edgeSettings
        });
    }

    public void Reset(Simulation parent)
    {
        var simulationId = EnsureSimulationId(parent);
        var properties = StreamingSimulationProjection.BuildPropertyProjection(parent);
        var edgeSettings = StreamingSimulationProjection.BuildEdgeProjection(parent);

        _snapshotNodes.AddOrUpdate(simulationId,
            _ => new RuntimeSimulationNode
            {
                SimulationId = simulationId,
                ParentSimulationId = null,
                RuntimeStatus = "restarted",
                Index = parent.Index,
                Properties = properties,
                EdgeSettings = edgeSettings
            },
            (_, existing) =>
            {
                existing.RuntimeStatus = "restarted";
                existing.Index = parent.Index;
                existing.Properties = properties;
                existing.EdgeSettings = edgeSettings;
                return existing;
            });

        PublishEvent(new StreamingSimulationEvent
        {
            EventType = "restarted",
            SimulationId = simulationId,
            ParentSimulationId = _snapshotNodes[simulationId].ParentSimulationId,
            RuntimeStatus = "restarted",
            Index = parent.Index,
            Properties = properties,
            EdgeSettings = edgeSettings
        });
    }

    private string EnsureSimulationId(Simulation simulation)
    {
        return _simulationIds.GetOrAdd(simulation, _ => $"sim-{Guid.NewGuid():N}");
    }

    private void PublishEvent(StreamingSimulationEvent simulationEvent)
    {
        _eventChannel.Writer.TryWrite(simulationEvent);
    }
}
