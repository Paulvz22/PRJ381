using System.Globalization;
using BCOpendayApp.AR.Geo;
using BCOpendayApp.AR.Models;
using BCOpendayApp.AR.Nav;

/// <summary>Tests for the pure-C# nav graph loader and Dijkstra router.</summary>
static class NavRouterTests
{
    static readonly GeoPoint Campus = new(-25.7300, 28.1900);

    static GeoPoint At(double east, double north) => GeoMath.FromLocal(Campus, new GeoMath.Vec2(east, north));

    /// <summary>Inline JSON graph: "a"-"b"-"d" is shorter than the "a"-"c"-"d" detour; "x" is isolated.</summary>
    static string SampleGraph() => $$"""
        {
          "nodes": [
            { "id": "a", "lat": {{Num(At(0, 0).Lat)}}, "lng": {{Num(At(0, 0).Lng)}} },
            { "id": "b", "lat": {{Num(At(10, 500).Lat)}}, "lng": {{Num(At(10, 500).Lng)}}, "buildingId": "library" },
            { "id": "c", "lat": {{Num(At(500, 500).Lat)}}, "lng": {{Num(At(500, 500).Lng)}} },
            { "id": "d", "lat": {{Num(At(0, 1000).Lat)}}, "lng": {{Num(At(0, 1000).Lng)}} },
            { "id": "x", "lat": {{Num(At(900, 900).Lat)}}, "lng": {{Num(At(900, 900).Lng)}} }
          ],
          "edges": [
            { "from": "a", "to": "b" },
            { "from": "b", "to": "d" },
            { "from": "a", "to": "c" },
            { "from": "c", "to": "d" }
          ]
        }
        """;

    static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    public static void Register(Action<string, Func<Task>> add)
    {
        add("NAVROUTER: shortest path is chosen over the longer detour", () =>
        {
            var router = new NavRouter(NavGraph.Load(SampleGraph()));
            var path = router.ShortestPath("a", "d")!;
            Check(path is { Count: 3 });
            Check(path[0] == At(0, 0) && path[^1] == At(0, 1000));
            Check(path[1] == At(10, 500));
            return Task.CompletedTask;
        });
        add("NAVROUTER: unreachable and unknown nodes give null", () =>
        {
            var router = new NavRouter(NavGraph.Load(SampleGraph()));
            Check(router.ShortestPath("a", "x") is null);
            Check(router.ShortestPath("x", "a") is null);
            Check(router.ShortestPath("a", "nope") is null);
            Check(router.ShortestPath("nope", "a") is null);
            return Task.CompletedTask;
        });
        add("NAVROUTER: a node routes to itself as a single-point path", () =>
        {
            var router = new NavRouter(NavGraph.Load(SampleGraph()));
            var path = router.ShortestPath("b", "b")!;
            Check(path is { Count: 1 } && path[0] == At(10, 500));
            return Task.CompletedTask;
        });
        add("NAVROUTER: nearest node snaps to the closest graph node", () =>
        {
            var graph = NavGraph.Load(SampleGraph());
            var router = new NavRouter(graph);
            Check(router.NearestNode(At(5, 480))!.Id == "b");
            Check(router.NearestNode(At(490, 510))!.Id == "c");
            Check(graph.NodesById["b"].BuildingId == "library");
            Check(graph.NodesById["a"].BuildingId is null);
            return Task.CompletedTask;
        });
        add("NAVROUTER: a graph with no nodes has no nearest node", () =>
        {
            var router = new NavRouter(NavGraph.Load("""{ "nodes": [], "edges": [] }"""));
            Check(router.NearestNode(Campus) is null);
            Check(router.ShortestPath("a", "b") is null);
            return Task.CompletedTask;
        });
        add("NAVROUTER: edges are undirected in both directions", () =>
        {
            var router = new NavRouter(NavGraph.Load(SampleGraph()));
            var backwards = router.ShortestPath("d", "a")!;
            Check(backwards is { Count: 3 } && backwards[1] == At(10, 500));
            return Task.CompletedTask;
        });
        add("NAVROUTER: duplicate node ids are rejected", () =>
        {
            var json = """
                {
                  "nodes": [
                    { "id": "a", "lat": -25.73, "lng": 28.19 },
                    { "id": "a", "lat": -25.74, "lng": 28.20 }
                  ],
                  "edges": []
                }
                """;
            CheckRejected(json);
            return Task.CompletedTask;
        });
        add("NAVROUTER: edges referencing unknown nodes are rejected", () =>
        {
            var json = """
                {
                  "nodes": [ { "id": "a", "lat": -25.73, "lng": 28.19 } ],
                  "edges": [ { "from": "a", "to": "ghost" } ]
                }
                """;
            CheckRejected(json);
            return Task.CompletedTask;
        });
        add("NAVROUTER: self-loops are rejected", () =>
        {
            var json = """
                {
                  "nodes": [ { "id": "a", "lat": -25.73, "lng": 28.19 } ],
                  "edges": [ { "from": "a", "to": "a" } ]
                }
                """;
            CheckRejected(json);
            return Task.CompletedTask;
        });
        add("NAVROUTER: malformed JSON is rejected", () =>
        {
            CheckRejected("{ not json at all");
            CheckRejected("[]");
            return Task.CompletedTask;
        });
    }

    static void CheckRejected(string json)
    {
        try
        {
            NavGraph.Load(json);
        }
        catch (ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("Expected the graph to be rejected.");
    }

    static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed."); }
}
