using System;
using System.Collections.Generic;
using System.Reflection;
using AdvancedRoadNaming.Domain;
using AdvancedRoadNaming.Services;
using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;

internal static class GeometryTests
{
    private static int _assertions;
    private static Bezier4x3 Line(float3 a, float3 b) => new Bezier4x3(a, math.lerp(a, b, 1f / 3f), math.lerp(a, b, 2f / 3f), b);
    private static void Check(bool value, string message)
    {
        _assertions++;
        if (!value) throw new Exception(message);
    }
    private static void Near(float actual, float expected, float tolerance, string message) => Check(math.abs(actual - expected) <= tolerance, message + $": {actual} != {expected}");

    private static List<Bezier4x3> Build(Bezier4x3[] source, float start = 0f, float end = 1f)
    {
        // Exercise the actual pure assembly phase without creating a Unity ECS world.
        var builder = typeof(RouteOverlayGeometryBuilder);
        var infoType = builder.GetNestedType("SegmentInfo", BindingFlags.NonPublic);
        var infos = Array.CreateInstance(infoType, source.Length);
        var segments = new List<Entity>();
        for (var i = 0; i < source.Length; i++)
        {
            var entity = new Entity { Index = i + 1, Version = 1 };
            segments.Add(entity);
            infos.SetValue(Activator.CreateInstance(infoType, entity, source[i], RouteOverlayMath.ApproximateLength(source[i])), i);
        }
        var waypoints = new List<RoadRouteWaypoint>
        {
            new RoadRouteWaypoint(segments[0], MathUtils.Position(source[0], start), start),
            new RoadRouteWaypoint(segments[segments.Count - 1], MathUtils.Position(source[source.Length - 1], end), end)
        };
        var curves = new List<Bezier4x3>();
        builder.GetMethod("BuildTrimmedRouteGeometry", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { segments, waypoints, curves, infos });
        return curves;
    }

    private static void ReplacementTests()
    {
        var original = new Entity { Index = 100, Version = 1 };
        var a = new Entity { Index = 101, Version = 1 };
        var b = new Entity { Index = 102, Version = 1 };
        var c = new Entity { Index = 103, Version = 1 };
        var full = Line(new float3(0, 0, 0), new float3(120, 0, 0));
        Check(RoadReplacementGeometry.IsSplitPiece(full, Line(new float3(0,0,0),new float3(40,0,0)), 10, 30, 10, 16), "split recognizes inherited build-order range and curve");
        Check(!RoadReplacementGeometry.IsSplitPiece(full, Line(new float3(0,0,0),new float3(40,0,0)), 10, 30, 5, 16), "split rejects foreign build-order range");
        Check(!RoadReplacementGeometry.IsSplitPiece(full, Line(new float3(0,0,10),new float3(40,0,10)), 10, 30, 10, 16), "split rejects parallel nearby road");
        Check(!RoadReplacementGeometry.IsSplitPiece(full, Line(new float3(0,0,0),new float3(0,0,40)), 10, 30, 10, 16), "split rejects connected side street");
        Check(!RoadReplacementGeometry.IsSplitPiece(full, Line(new float3(0,0,0),new float3(160,0,0)), 10, 30, 10, 16), "split rejects extension outside old geometry");
        var batch = new RoadReplacementGeometry(full);
        batch.Add(c, Line(new float3(80, 0, 0), new float3(120, 0, 0)));
        batch.Add(a, Line(new float3(0, 0, 0), new float3(40, 0, 0)));
        batch.Add(b, Line(new float3(80, 0, 0), new float3(40, 0, 0)));
        batch.Add(a, full); // duplicate capture must not replace or duplicate a piece
        batch.Sort();
        Check(batch.Pieces.Count == 3, "split retains every distinct sibling");
        Check(batch.Pieces[0].Entity == a && batch.Pieces[1].Entity == b && batch.Pieces[2].Entity == c, "split order ignores capture order and native edge direction");
        var waypoint = batch.Remap(new RoadRouteWaypoint(original, new float3(50, 0, 0), 50f / 120));
        Check(waypoint.Segment == b, "waypoint selects correct split sibling");
        Near(waypoint.CurvePosition, 0.75f, 0.002f, "waypoint parameter is reprojected on reversed replacement");
        Near(waypoint.Position.x, 50f, 0.01f, "split preserves waypoint world position");
        var forward = batch.RoutePieces(false);
        Check(forward.Count == 3 && forward[0] == a && forward[2] == c, "forward route includes all split pieces");
        var reverse = batch.RoutePieces(true);
        Check(reverse.Count == 3 && reverse[0] == c && reverse[2] == a, "reverse route includes all split pieces in reverse order");
        var trimmed = batch.RoutePieces(false, b, c);
        Check(trimmed.Count == 2 && trimmed[0] == b, "start waypoint excludes pieces outside saved selection");
        trimmed = batch.RoutePieces(true, b, a);
        Check(trimmed.Count == 2 && trimmed[1] == a, "reverse selection clips correct endpoint");
        var points = new List<RoadRouteWaypoint> {
            new RoadRouteWaypoint(original, new float3(100,0,0), 100f/120),
            new RoadRouteWaypoint(original, new float3(20,0,0), 20f/120) };
        Check(batch.IsReversed(null, null, points, original), "single-edge reverse direction comes from saved waypoints");
        Check(batch.IsReversed(Line(new float3(120,0,0),new float3(160,0,0)),null,points,original), "previous route edge establishes reverse traversal");
        Check(!batch.IsReversed(null,Line(new float3(120,0,0),new float3(160,0,0)),points,original), "next route edge establishes forward traversal");
        var curved = new Bezier4x3(new float3(0,0,0),new float3(10,0,80),new float3(90,0,80),new float3(100,0,0));
        MathUtils.Divide(curved, out var left, out var right, 0.4f);
        var curvedBatch = new RoadReplacementGeometry(curved);
        curvedBatch.Add(b,right); curvedBatch.Add(a,left); curvedBatch.Sort();
        var onCurve = MathUtils.Position(curved,0.7f);
        var remapped = curvedBatch.Remap(new RoadRouteWaypoint(original,onCurve,0.7f));
        Check(remapped.Segment == b, "curved split chooses right sibling");
        Near(remapped.CurvePosition,0.5f,0.01f,"curved split recalculates local parameter");
        Check(math.distance(remapped.Position,onCurve)<0.1f,"curved split retains world position");
    }

    private static void SpurTests()
    {
        var builder = typeof(RouteOverlayGeometryBuilder);
        var infoType = builder.GetNestedType("SegmentInfo", BindingFlags.NonPublic);
        var roads = new[] {
            Line(new float3(-100, 0, 0), float3.zero),
            Line(float3.zero, new float3(100, 0, 0)),
            Line(new float3(100, 0, 0), new float3(100, 0, 20)),
            Line(float3.zero, new float3(0, 0, -100)) };
        var segments = new List<Entity>();
        var infos = Array.CreateInstance(infoType, roads.Length);
        for (var i = 0; i < roads.Length; i++)
        {
            var entity = new Entity { Index = i + 1, Version = 1 };
            segments.Add(entity);
            infos.SetValue(Activator.CreateInstance(infoType, entity, roads[i], RouteOverlayMath.ApproximateLength(roads[i])), i);
        }
        var points = new List<RoadRouteWaypoint> {
            new RoadRouteWaypoint(segments[0], roads[0].a, 0),
            new RoadRouteWaypoint(segments[2], new float3(100, 0, 12), 0.6f),
            new RoadRouteWaypoint(segments[3], roads[3].d, 1) };
        Func<Entity, Entity, IReadOnlyList<Entity>> findPath = (from, to) =>
            from == segments[0] ? new[] { segments[0], segments[1], segments[2] }
            : new[] { segments[2], segments[1], segments[3] };
        var curves = new List<Bezier4x3>();
        var method = builder.GetMethod("BuildWaypointRouteGeometry", BindingFlags.NonPublic | BindingFlags.Static);
        method.Invoke(null, new object[] { segments, points, curves, infos, findPath });
        Check(curves.Count == 10, "Spur preserves both traversals of the return road");
        Near(math.distance(curves[4].d, points[1].Position), 0, 0.001f, "Outbound leg reaches interior spur waypoint");
        Near(math.distance(curves[5].a, points[1].Position), 0, 0.001f, "Return leg starts at the exact turnaround");
        for (var i = 1; i < curves.Count; i++)
            Near(math.distance(curves[i - 1].d, curves[i].a), 0, 0.001f, "Spur traversal has no missing road or jumping join");
        foreach (var curve in curves)
            for (var i = 0; i <= 32; i++)
                Check(MathUtils.Position(curve, i / 32f).z <= 12.001f, "Spur does not extend past its waypoint");

        roads[2] = new Bezier4x3(roads[2].d, roads[2].c, roads[2].b, roads[2].a);
        infos.SetValue(Activator.CreateInstance(infoType, segments[2], roads[2], 20f), 2);
        points[1] = new RoadRouteWaypoint(segments[2], new float3(100, 0, 12), 0.4f);
        curves.Clear();
        method.Invoke(null, new object[] { segments, points, curves, infos, findPath });
        Check(curves.Count == 10, "Reversed native spur preserves both legs");
        Near(math.distance(curves[4].d, points[1].Position), 0, 0.001f, "Reversed native spur reaches waypoint");
        for (var i = 1; i < curves.Count; i++)
            Near(math.distance(curves[i - 1].d, curves[i].a), 0, 0.001f, "Reversed native spur remains continuous");

        // Multiple reversals on one road used to collapse to the first/last range.
        points.Clear();
        foreach (var t in new[] { 0.2f, 0.8f, 0.3f, 0.6f })
            points.Add(new RoadRouteWaypoint(segments[1], MathUtils.Position(roads[1], t), t));
        findPath = (from, to) => new[] { from };
        curves.Clear();
        method.Invoke(null, new object[] { segments, points, curves, infos, findPath });
        Check(curves.Count == 3, "Same-road reversals preserve all waypoint legs");
        for (var i = 0; i < curves.Count; i++)
        {
            Near(math.distance(curves[i].a, points[i].Position), 0, 0.001f, "Same-road leg start");
            Near(math.distance(curves[i].d, points[i + 1].Position), 0, 0.001f, "Same-road leg end");
        }
    }

    private static void Main()
    {
        ReplacementTests();
        SpurTests();
        // Alternating native edge directions: the route must still run continuously east.
        var forward = Build(new[] { Line(new float3(0, 0, 0), new float3(200, 0, 0)),
            Line(new float3(400, 0, 0), new float3(200, 0, 0)), Line(new float3(400, 0, 0), new float3(600, 0, 0)) });
        Check(forward.Count == 5, "Roads and connectors must be interleaved");
        for (var i = 0; i < forward.Count; i++)
        {
            Check(forward[i].d.x > forward[i].a.x, "Every curve must face along the route");
            if (i > 0) Near(math.distance(forward[i - 1].d, forward[i].a), 0, 0.001f, "Continuous join order");
        }
        var sampler = new RouteCurveSampler();
        var anchors = new List<float3>();
        sampler.Rebuild(forward);
        sampler.Place(150, anchors);
        Check(anchors.Count == 4, "600m route at 150m spacing");
        for (var i = 0; i < anchors.Count; i++) Near(anchors[i].x, 75 + 150 * i, 0.05f, "Even route spacing");

        var backwards = Build(new[] { Line(new float3(0, 0, 0), new float3(1000, 0, 0)) }, 0.8f, 0.2f);
        Near(backwards[0].a.x, 800, 0.01f, "Reverse trimmed start");
        Near(backwards[0].d.x, 200, 0.01f, "Reverse trimmed end");
        sampler.Rebuild(backwards);
        sampler.Place(150, anchors);
        for (var i = 0; i < anchors.Count; i++) Near(anchors[i].x, 725 - 150 * i, 0.1f, "Reverse spacing");

        var turn = Build(new[] { Line(float3.zero, new float3(200, 0, 0)),
            Line(new float3(200, 0, 200), new float3(200, 0, 0)) }, 0.25f, 0.25f);
        Check(turn.Count == 3, "Turn includes its connector in place");
        Near(math.distance(turn[0].d, turn[1].a), 0, 0.001f, "Turn entry continuity");
        Near(math.distance(turn[1].d, turn[2].a), 0, 0.001f, "Turn exit continuity");
        Near(turn[0].a.x, 50, 0.01f, "Turn route start trim");
        Near(turn[2].d.z, 150, 0.01f, "Reversed turn route end trim");
        var closeEndpoints = Build(new[] { Line(float3.zero, new float3(200, 0, 0)),
            Line(new float3(200, 0, 0), new float3(400, 0, 0)) }, 0.999f, 0.001f);
        Near(closeEndpoints[0].a.x, 199.8f, 0.01f, "Junction trim cannot extend before the first waypoint");
        Near(closeEndpoints[closeEndpoints.Count - 1].d.x, 200.2f, 0.01f, "Junction trim cannot extend after the last waypoint");
        Check(Build(new[] { Line(float3.zero, new float3(100, 0, 0)) }, 0.5f, 0.5f).Count == 0,
            "Coincident endpoint range stays empty");

        sampler.Rebuild(new[] { Line(float3.zero, new float3(500, 0, 0)) });
        sampler.Place(300, anchors);
        Check(anchors.Count == 2, "Non-multiple route uses balanced intervals");
        Near(anchors[0].x, 125, 0.01f, "First half-spacing margin");
        Near(anchors[1].x, 375, 0.01f, "Last half-spacing margin");
        sampler.Rebuild(new[] { Line(float3.zero, new float3(60, 0, 0)) });
        sampler.Place(300, anchors);
        Check(anchors.Count == 1, "Short route gets one shield");
        Near(anchors[0].x, 30, 0.01f, "Short route center");
        sampler.Rebuild(new[] { Line(float3.zero, float3.zero) });
        sampler.Place(150, anchors);
        Check(anchors.Count == 0, "Degenerate route does not get a full-road fallback shield");

        // A curved route compared with an independent high-resolution arc-length reference.
        var curve = new Bezier4x3(float3.zero, new float3(0, 0, 800), new float3(1000, 0, -400), new float3(1000, 0, 0));
        sampler.Rebuild(new[] { curve });
        sampler.Place(150, anchors);
        var reference = new List<float3>();
        var lengths = new List<float>();
        var total = 0f;
        var previous = curve.a;
        for (var i = 0; i <= 20000; i++)
        {
            var point = MathUtils.Position(curve, i / 20000f);
            total += math.distance(previous, point);
            reference.Add(point); lengths.Add(total); previous = point;
        }
        for (var i = 0; i < anchors.Count; i++)
        {
            var nearest = 0; var minDistance = float.MaxValue;
            for (var j = 0; j < reference.Count; j++)
            {
                var distance = math.distancesq(anchors[i], reference[j]);
                if (distance < minDistance) { minDistance = distance; nearest = j; }
            }
            Near(lengths[nearest], (i + 0.5f) * total / anchors.Count, 0.75f, "Curved-route arc distance");
        }
        Console.WriteLine($"PASS: {_assertions} geometry/spacing assertions against production source.");
    }
}
