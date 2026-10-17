using System.Text.Json;
using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Models;

namespace BCOpendayApp.AR.Nav;

/// <summary>A point on the routing graph. Optionally links to a scanned building for AR labels.</summary>
public sealed record NavNode(string Id, double Lat, double Lng, string? BuildingId = null)
{
    public GeoPoint Point => new(Lat, Lng);
}

/// <summary>An undirected connection between two nodes; the walking length comes from <see cref="GeoMath.DistanceMeters"/>.</summary>
public sealed record NavEdge(string From, string To);

/// <summary>One step of an adjacency list entry.</summary>
public sealed record NavLink(string NodeId, double LengthMeters);

/// <summary>JSON payload shape: {"nodes":[...],"edges":[...]}.</summary>
public sealed class NavGraphDocument
{
    public List<NavNode> Nodes { get; init; } = new();

    public List<NavEdge> Edges { get; init; } = new();
}

/// <summary>
/// An undirected campus walking graph loaded from JSON. Pure C# (no MAUI types), so it is unit-tested
/// in the console test project.
/// </summary>
public sealed class NavGraph
{
    private readonly Dictionary<string, NavNode> _nodesById;
    private readonly Dictionary<string, List<NavLink>> _adjacency;

    public NavGraph(IReadOnlyList<NavNode> nodes, IReadOnlyList<NavEdge> edges)
    {
        _nodesById = new Dictionary<string, NavNode>();
        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                throw new ArgumentException("Every nav node needs a non-empty id.");
            }

            if (!_nodesById.TryAdd(node.Id, node))
            {
                throw new ArgumentException($"Duplicate nav node id '{node.Id}'.");
            }
        }

        _adjacency = _nodesById.Keys.ToDictionary(id => id, _ => new List<NavLink>());
        foreach (var edge in edges)
        {
            if (!_nodesById.TryGetValue(edge.From, out var from))
            {
                throw new ArgumentException($"Edge references unknown node '{edge.From}'.");
            }

            if (!_nodesById.TryGetValue(edge.To, out var to))
            {
                throw new ArgumentException($"Edge references unknown node '{edge.To}'.");
            }

            if (edge.From == edge.To)
            {
                throw new ArgumentException($"Self-loop on node '{edge.From}' is not allowed.");
            }

            var length = GeoMath.DistanceMeters(from.Point, to.Point);
            _adjacency[edge.From].Add(new NavLink(edge.To, length));
            _adjacency[edge.To].Add(new NavLink(edge.From, length));
        }

        Nodes = nodes.ToList();
        Edges = edges.ToList();
    }

    public IReadOnlyList<NavNode> Nodes { get; }

    public IReadOnlyList<NavEdge> Edges { get; }

    public IReadOnlyDictionary<string, NavNode> NodesById => _nodesById;

    /// <summary>Undirected neighbours of a node (both edge directions).</summary>
    public IReadOnlyList<NavLink> Neighbors(string id) => _adjacency[id];

    /// <summary>Parses and validates a graph from JSON, rejecting malformed or inconsistent data.</summary>
    public static NavGraph Load(string json)
    {
        NavGraphDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, NavGraphJsonContext.Default.NavGraphDocument);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The nav graph JSON is malformed.", exception);
        }

        if (document is null)
        {
            throw new ArgumentException("The nav graph JSON is empty.");
        }

        return new NavGraph(
            document.Nodes ?? new List<NavNode>(),
            document.Edges ?? new List<NavEdge>());
    }
}
