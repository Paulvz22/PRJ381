using System.Collections.Generic;
using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Models;

namespace BCOpendayApp.AR.Nav;

/// <summary>
/// Route finding over a <see cref="NavGraph"/>: snaps a GPS position to the closest node and runs
/// Dijkstra for the shortest walking path. Pure C#, no MAUI types.
/// </summary>
public sealed class NavRouter
{
    private readonly NavGraph _graph;

    public NavRouter(NavGraph graph)
    {
        _graph = graph;
    }

    /// <summary>The graph node closest to <paramref name="point"/>, or null when the graph is empty.</summary>
    public NavNode? NearestNode(GeoPoint point)
    {
        NavNode? nearest = null;
        var best = double.MaxValue;
        foreach (var node in _graph.Nodes)
        {
            var distance = GeoMath.DistanceMeters(point, node.Point);
            if (distance < best)
            {
                best = distance;
                nearest = node;
            }
        }

        return nearest;
    }

    /// <summary>
    /// The shortest walking path from <paramref name="fromId"/> to <paramref name="toId"/> as waypoints
    /// (both endpoints included), or null when either id is unknown or the nodes are disconnected.
    /// </summary>
    public IReadOnlyList<GeoPoint>? ShortestPath(string fromId, string toId)
    {
        if (!_graph.NodesById.ContainsKey(fromId) || !_graph.NodesById.ContainsKey(toId))
        {
            return null;
        }

        var best = new Dictionary<string, double> { [fromId] = 0 };
        var previous = new Dictionary<string, string>();
        var queue = new PriorityQueue<string, double>();
        queue.Enqueue(fromId, 0);

        while (queue.TryDequeue(out var current, out var distance))
        {
            if (distance > best[current])
            {
                continue; // stale queue entry
            }

            if (current == toId)
            {
                break;
            }

            foreach (var link in _graph.Neighbors(current))
            {
                var candidate = distance + link.LengthMeters;
                if (!best.TryGetValue(link.NodeId, out var known) || candidate < known)
                {
                    best[link.NodeId] = candidate;
                    previous[link.NodeId] = current;
                    queue.Enqueue(link.NodeId, candidate);
                }
            }
        }

        if (!best.ContainsKey(toId))
        {
            return null;
        }

        var ids = new List<string>();
        for (var at = toId; ; at = previous[at])
        {
            ids.Add(at);
            if (at == fromId)
            {
                break;
            }
        }

        ids.Reverse();
        return ids.Select(id => _graph.NodesById[id].Point).ToList();
    }
}
