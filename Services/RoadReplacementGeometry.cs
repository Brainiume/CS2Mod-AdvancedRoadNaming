using System;
using System.Collections.Generic;
using AdvancedRoadNaming.Domain;
using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;

namespace AdvancedRoadNaming.Services
{
    // Uses only captured lineage and curve geometry; never searches nearby roads.
    internal sealed class RoadReplacementGeometry
    {
        internal readonly struct Piece
        {
            public readonly Entity Entity;
            public readonly Bezier4x3 Curve;
            public Piece(Entity entity, Bezier4x3 curve) { Entity = entity; Curve = curve; }
        }

        public readonly Bezier4x3 Original;
        public readonly List<Piece> Pieces = new List<Piece>();

        public RoadReplacementGeometry(Bezier4x3 original) { Original = original; }

        internal static bool IsSplitPiece(Bezier4x3 source, Bezier4x3 piece,
            uint sourceStart, uint sourceEnd, uint pieceStart, uint pieceEnd)
        {
            if (pieceStart < sourceStart || pieceEnd > sourceEnd || pieceEnd < pieceStart) return false;
            return MathUtils.Distance(source.xz, piece.a.xz, out var start) <= 0.25f
                && MathUtils.Distance(source.xz, piece.d.xz, out var end) <= 0.25f
                && end > start
                && MathUtils.Distance(source.xz, MathUtils.Position(piece, 0.5f).xz, out _) <= 0.25f;
        }

        public void Add(Entity entity, Bezier4x3 curve)
        {
            if (!Pieces.Exists(piece => piece.Entity == entity))
                Pieces.Add(new Piece(entity, curve));
        }

        public void Sort()
        {
            Pieces.Sort((a, b) =>
            {
                MathUtils.Distance(Original, MathUtils.Position(a.Curve, 0.5f), out var ta);
                MathUtils.Distance(Original, MathUtils.Position(b.Curve, 0.5f), out var tb);
                var order = ta.CompareTo(tb);
                return order != 0 ? order : a.Entity.Index.CompareTo(b.Entity.Index);
            });
        }

        public RoadRouteWaypoint Remap(RoadRouteWaypoint waypoint)
        {
            var best = float.MaxValue;
            var result = waypoint;
            foreach (var piece in Pieces)
            {
                var distance = MathUtils.Distance(piece.Curve, waypoint.Position, out var t);
                if (distance >= best) continue;
                best = distance;
                result = new RoadRouteWaypoint(piece.Entity, MathUtils.Position(piece.Curve, t), t);
            }
            return result;
        }

        public List<Entity> RoutePieces(bool reversed, Entity start = default, Entity end = default)
        {
            var pieces = new List<Entity>();
            for (var i = 0; i < Pieces.Count; i++)
                pieces.Add(Pieces[reversed ? Pieces.Count - 1 - i : i].Entity);
            var first = start == Entity.Null ? 0 : pieces.IndexOf(start);
            var last = end == Entity.Null ? pieces.Count - 1 : pieces.IndexOf(end);
            if (first < 0) first = 0;
            if (last < 0) last = pieces.Count - 1;
            return last >= first ? pieces.GetRange(first, last - first + 1) : new List<Entity>();
        }

        public bool IsReversed(Bezier4x3? previous, Bezier4x3? next, IReadOnlyList<RoadRouteWaypoint> waypoints, Entity original)
        {
            if (previous.HasValue)
                return EndpointDistance(previous.Value, Original.d) < EndpointDistance(previous.Value, Original.a);
            if (next.HasValue)
                return EndpointDistance(next.Value, Original.a) < EndpointDistance(next.Value, Original.d);
            float? first = null;
            foreach (var waypoint in waypoints)
            {
                if (waypoint.Segment != original) continue;
                if (!first.HasValue) first = waypoint.CurvePosition;
                else if (math.abs(waypoint.CurvePosition - first.Value) > 0.0001f)
                    return waypoint.CurvePosition < first.Value;
            }
            return false;
        }

        private static float EndpointDistance(Bezier4x3 curve, float3 point)
            => math.min(math.distancesq(curve.a, point), math.distancesq(curve.d, point));
    }
}
