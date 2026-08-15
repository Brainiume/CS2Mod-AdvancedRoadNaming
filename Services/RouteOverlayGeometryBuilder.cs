using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using AdvancedRoadNaming.Domain;
using Unity.Entities;
using Unity.Mathematics;

namespace AdvancedRoadNaming.Services
{
    public static class RouteOverlayGeometryBuilder
    {
        private const float RangeEpsilon = 0.001f;
        private const float JoinDistanceThreshold = 22f;
        private const float MinJoinTrimDistance = 4f;
        private const float MaxJoinTrimDistance = 20f;
        private const float MaxJoinHandleDistance = 18f;
        private const float StraightJoinThreshold = 0.996f;
        private const int DistanceSamples = 16;

        public static void BuildRouteGeometry(
            EntityManager entityManager,
            IReadOnlyList<Entity> segments,
            IReadOnlyList<RoadRouteWaypoint> waypoints,
            List<Bezier4x3> curves,
            List<float3> nodes)
        {
            curves.Clear();
            nodes.Clear();

            if (waypoints != null)
            {
                for (var i = 0; i < waypoints.Count; i++)
                    nodes.Add(waypoints[i].Position);
            }

            if (segments == null || segments.Count == 0)
                return;

            var segmentInfos = new SegmentInfo[segments.Count];
            for (var i = 0; i < segments.Count; i++)
                segmentInfos[i] = BuildSegmentInfo(entityManager, segments[i]);

            try
            {
                BuildTrimmedRouteGeometry(segments, waypoints, curves, segmentInfos);
            }
            catch
            {
                curves.Clear();
                AddFullValidSegmentCurves(segmentInfos, curves);
            }

            if (curves.Count == 0)
                AddFullValidSegmentCurves(segmentInfos, curves);
        }

        private static void BuildTrimmedRouteGeometry(
            IReadOnlyList<Entity> segments,
            IReadOnlyList<RoadRouteWaypoint> waypoints,
            List<Bezier4x3> curves,
            SegmentInfo[] segmentInfos)
        {
            var connections = new ConnectionInfo?[math.max(segments.Count - 1, 0)];
            for (var i = 0; i < connections.Length; i++)
            {
                if (segmentInfos[i].IsValid && segmentInfos[i + 1].IsValid && TryBuildConnection(segmentInfos[i], segmentInfos[i + 1], out var connection))
                    connections[i] = connection;
            }

            var hasStartEndpoint = TryGetRouteEndpoint(segments, waypoints, true, out var startWaypoint);
            var hasEndEndpoint = TryGetRouteEndpoint(segments, waypoints, false, out var endWaypoint);

            for (var i = 0; i < segmentInfos.Length; i++)
            {
                if (!segmentInfos[i].IsValid)
                    continue;

                var previous = i > 0 ? connections[i - 1] : null;
                var next = i < connections.Length ? connections[i] : null;
                var startEndpoint = hasStartEndpoint ? (RoadRouteWaypoint?)startWaypoint : null;
                var endEndpoint = hasEndEndpoint ? (RoadRouteWaypoint?)endWaypoint : null;

                if (!TryBuildSegmentRange(segmentInfos[i], previous, next, startEndpoint, endEndpoint, i == 0, i == segmentInfos.Length - 1, out var range))
                {
                    curves.Add(segmentInfos[i].Curve);
                    continue;
                }

                if (range.x <= RangeEpsilon && range.y >= 1f - RangeEpsilon)
                {
                    curves.Add(segmentInfos[i].Curve);
                }
                else
                {
                    try
                    {
                        curves.Add(RouteOverlayMath.Cut(segmentInfos[i].Curve, range));
                    }
                    catch
                    {
                        curves.Add(segmentInfos[i].Curve);
                    }
                }
            }

            for (var i = 0; i < connections.Length; i++)
            {
                if (connections[i].HasValue && TryBuildJoinCurve(connections[i].Value, out var joinCurve))
                    curves.Add(joinCurve);
            }
        }

        private static void AddFullValidSegmentCurves(SegmentInfo[] segmentInfos, List<Bezier4x3> curves)
        {
            if (segmentInfos == null)
                return;

            for (var i = 0; i < segmentInfos.Length; i++)
            {
                if (segmentInfos[i].IsValid)
                    curves.Add(segmentInfos[i].Curve);
            }
        }

        public static void BuildHoverGeometry(EntityManager entityManager, Entity segment, List<Bezier4x3> curves)
        {
            curves.Clear();
            var info = BuildSegmentInfo(entityManager, segment);
            if (info.IsValid)
                curves.Add(info.Curve);
        }

        private static SegmentInfo BuildSegmentInfo(EntityManager entityManager, Entity segment)
        {
            if (segment == Entity.Null || !entityManager.Exists(segment) || !entityManager.HasComponent<Curve>(segment))
                return default;

            var curve = entityManager.GetComponentData<Curve>(segment).m_Bezier;
            var length = math.max(RouteOverlayMath.ApproximateLength(curve), 0.01f);
            return new SegmentInfo(segment, curve, length);
        }

        private static bool TryBuildSegmentRange(
            SegmentInfo segment,
            ConnectionInfo? previous,
            ConnectionInfo? next,
            RoadRouteWaypoint? startEndpoint,
            RoadRouteWaypoint? endEndpoint,
            bool isFirst,
            bool isLast,
            out float2 range)
        {
            range = new float2(0f, 1f);

            float? startBound = null;
            float? endBound = null;

            if (previous.HasValue)
                startBound = previous.Value.NextTrimParameter;
            else if (isFirst && startEndpoint.HasValue && startEndpoint.Value.Segment == segment.Entity)
                startBound = math.clamp(startEndpoint.Value.CurvePosition, 0f, 1f);

            if (next.HasValue)
                endBound = next.Value.CurrentTrimParameter;
            else if (isLast && endEndpoint.HasValue && endEndpoint.Value.Segment == segment.Entity)
                endBound = math.clamp(endEndpoint.Value.CurvePosition, 0f, 1f);

            if (!startBound.HasValue && !endBound.HasValue)
            {
                range = new float2(0f, 1f);
                return true;
            }

            if (!startBound.HasValue)
            {
                startBound = next.HasValue
                    ? GetOppositeEndpointParameter(next.Value.CurrentUsesStart)
                    : endBound.Value;
            }

            if (!endBound.HasValue)
            {
                endBound = previous.HasValue
                    ? GetOppositeEndpointParameter(previous.Value.NextUsesStart)
                    : startBound.Value;
            }

            range = new float2(math.min(startBound.Value, endBound.Value), math.max(startBound.Value, endBound.Value));
            return range.y - range.x > RangeEpsilon;
        }

        private static float GetOppositeEndpointParameter(bool usesStart)
        {
            return usesStart ? 1f : 0f;
        }

        private static bool TryBuildConnection(SegmentInfo current, SegmentInfo next, out ConnectionInfo connection)
        {
            connection = default;
            var candidate = SelectClosestConnection(current, next);
            if (candidate.Distance > JoinDistanceThreshold)
                return false;

            var currentEndpointTangent = GetPlanarEndpointTangent(current.Curve, candidate.CurrentUsesStart);
            var nextEndpointTangent = GetPlanarEndpointTangent(next.Curve, candidate.NextUsesStart);
            if (math.lengthsq(currentEndpointTangent) < 0.0001f || math.lengthsq(nextEndpointTangent) < 0.0001f)
                return false;

            var routeIn = candidate.CurrentUsesStart ? -currentEndpointTangent : currentEndpointTangent;
            var routeOut = candidate.NextUsesStart ? nextEndpointTangent : -nextEndpointTangent;
            var alignment = math.clamp(math.dot(routeIn, routeOut), -1f, 1f);
            var turnAmount = math.saturate((1f - alignment) * 0.5f);

            var desiredTrimDistance = math.lerp(MinJoinTrimDistance, MaxJoinTrimDistance, turnAmount);
            var currentTrimDistance = math.min(desiredTrimDistance, current.Length * 0.35f);
            var nextTrimDistance = math.min(desiredTrimDistance, next.Length * 0.35f);

            var currentTrimParameter = candidate.CurrentUsesStart
                ? GetParameterAtDistanceFromStart(current.Curve, currentTrimDistance)
                : GetParameterAtDistanceFromEnd(current.Curve, currentTrimDistance);
            var nextTrimParameter = candidate.NextUsesStart
                ? GetParameterAtDistanceFromStart(next.Curve, nextTrimDistance)
                : GetParameterAtDistanceFromEnd(next.Curve, nextTrimDistance);

            connection = new ConnectionInfo(
                current,
                next,
                candidate.Anchor,
                candidate.CurrentUsesStart,
                candidate.NextUsesStart,
                currentTrimParameter,
                nextTrimParameter,
                alignment,
                candidate.Distance);
            return true;
        }

        private static bool TryBuildJoinCurve(ConnectionInfo connection, out Bezier4x3 joinCurve)
        {
            joinCurve = default;

            var start = GetPosition(connection.Current.Curve, connection.CurrentTrimParameter);
            var end = GetPosition(connection.Next.Curve, connection.NextTrimParameter);

            var currentTangent = GetPlanarTangent(connection.Current.Curve, connection.CurrentTrimParameter);
            var nextTangent = GetPlanarTangent(connection.Next.Curve, connection.NextTrimParameter);
            if (math.lengthsq(currentTangent) < 0.0001f || math.lengthsq(nextTangent) < 0.0001f)
                return false;

            var directionIntoJoin = connection.CurrentUsesStart ? -currentTangent : currentTangent;
            var directionOutOfJoin = connection.NextUsesStart ? nextTangent : -nextTangent;
            var span = math.distance(start, end);
            if (span <= 0.01f)
                return false;

            var handleDistance = math.min(MaxJoinHandleDistance, math.max(span * 0.6f, 6f));
            float3 control1;
            float3 control2;
            if (connection.Alignment > StraightJoinThreshold)
            {
                var straightDirection = math.normalizesafe(directionIntoJoin + directionOutOfJoin, directionIntoJoin);
                var straightHandle = math.min(handleDistance, span * 0.5f);
                control1 = start + straightDirection * straightHandle;
                control2 = end - straightDirection * straightHandle;
            }
            else
            {
                control1 = start + directionIntoJoin * handleDistance;
                control2 = end - directionOutOfJoin * handleDistance;
            }

            // Keep generated connector controls level so the overlay does not roll around its center.
            control1.y = start.y;
            control2.y = end.y;

            joinCurve = new Bezier4x3(start, control1, control2, end);
            return true;
        }

        private static ConnectionInfo SelectClosestConnection(SegmentInfo current, SegmentInfo next)
        {
            var currentStart = current.Curve.a;
            var currentEnd = current.Curve.d;
            var nextStart = next.Curve.a;
            var nextEnd = next.Curve.d;

            var match = new ConnectionInfo(current, next, currentStart, true, true, 0f, 0f, 0f, math.distance(currentStart, nextStart));
            match = ChooseCloser(match, new ConnectionInfo(current, next, (currentStart + nextEnd) * 0.5f, true, false, 0f, 0f, 0f, math.distance(currentStart, nextEnd)));
            match = ChooseCloser(match, new ConnectionInfo(current, next, (currentEnd + nextStart) * 0.5f, false, true, 0f, 0f, 0f, math.distance(currentEnd, nextStart)));
            match = ChooseCloser(match, new ConnectionInfo(current, next, currentEnd, false, false, 0f, 0f, 0f, math.distance(currentEnd, nextEnd)));
            return match;
        }

        private static ConnectionInfo ChooseCloser(ConnectionInfo current, ConnectionInfo candidate)
        {
            return candidate.Distance < current.Distance ? candidate : current;
        }

        private static bool TryGetRouteEndpoint(IReadOnlyList<Entity> segments, IReadOnlyList<RoadRouteWaypoint> waypoints, bool first, out RoadRouteWaypoint waypoint)
        {
            waypoint = default;
            if (segments == null || segments.Count == 0 || waypoints == null || waypoints.Count == 0)
                return false;

            waypoint = first ? waypoints[0] : waypoints[waypoints.Count - 1];
            var expectedSegment = first ? segments[0] : segments[segments.Count - 1];
            return waypoint.Segment == expectedSegment;
        }

        private static float GetParameterAtDistanceFromStart(Bezier4x3 curve, float distance)
        {
            if (distance <= 0f)
                return 0f;

            var total = 0f;
            var previous = curve.a;
            for (var i = 1; i <= DistanceSamples; i++)
            {
                var t = i / (float)DistanceSamples;
                var point = GetPosition(curve, t);
                var step = math.distance(previous, point);
                if (total + step >= distance && step > 0.0001f)
                {
                    var alpha = (distance - total) / step;
                    var previousT = (i - 1) / (float)DistanceSamples;
                    return math.lerp(previousT, t, alpha);
                }

                total += step;
                previous = point;
            }

            return 1f;
        }

        private static float GetParameterAtDistanceFromEnd(Bezier4x3 curve, float distance)
        {
            if (distance <= 0f)
                return 1f;

            var total = 0f;
            var previous = curve.d;
            for (var i = 1; i <= DistanceSamples; i++)
            {
                var t = 1f - i / (float)DistanceSamples;
                var point = GetPosition(curve, t);
                var step = math.distance(previous, point);
                if (total + step >= distance && step > 0.0001f)
                {
                    var alpha = (distance - total) / step;
                    var previousT = 1f - (i - 1) / (float)DistanceSamples;
                    return math.lerp(previousT, t, alpha);
                }

                total += step;
                previous = point;
            }

            return 0f;
        }

        private static float3 GetPosition(Bezier4x3 curve, float t)
        {
            var q0 = math.lerp(curve.a, curve.b, t);
            var q1 = math.lerp(curve.b, curve.c, t);
            var q2 = math.lerp(curve.c, curve.d, t);
            var r0 = math.lerp(q0, q1, t);
            var r1 = math.lerp(q1, q2, t);
            return math.lerp(r0, r1, t);
        }

        private static float3 GetTangent(Bezier4x3 curve, float t)
        {
            var mt = 1f - t;
            return 3f * mt * mt * (curve.b - curve.a)
                + 6f * mt * t * (curve.c - curve.b)
                + 3f * t * t * (curve.d - curve.c);
        }

        private static float3 GetPlanarEndpointTangent(Bezier4x3 curve, bool usesStart)
        {
            return FlattenToPlanar(usesStart ? curve.b - curve.a : curve.d - curve.c);
        }

        private static float3 GetPlanarTangent(Bezier4x3 curve, float t)
        {
            return FlattenToPlanar(GetTangent(curve, t));
        }

        private static float3 FlattenToPlanar(float3 value)
        {
            var planar = new float3(value.x, 0f, value.z);
            return math.normalizesafe(planar);
        }

        private readonly struct SegmentInfo
        {
            public SegmentInfo(Entity entity, Bezier4x3 curve, float length)
            {
                Entity = entity;
                Curve = curve;
                Length = length;
            }

            public Entity Entity { get; }
            public Bezier4x3 Curve { get; }
            public float Length { get; }
            public bool IsValid => Entity != Entity.Null;
        }

        private readonly struct ConnectionInfo
        {
            public ConnectionInfo(
                SegmentInfo current,
                SegmentInfo next,
                float3 anchor,
                bool currentUsesStart,
                bool nextUsesStart,
                float currentTrimParameter,
                float nextTrimParameter,
                float alignment,
                float distance)
            {
                Current = current;
                Next = next;
                Anchor = anchor;
                CurrentUsesStart = currentUsesStart;
                NextUsesStart = nextUsesStart;
                CurrentTrimParameter = currentTrimParameter;
                NextTrimParameter = nextTrimParameter;
                Alignment = alignment;
                Distance = distance;
            }

            public SegmentInfo Current { get; }
            public SegmentInfo Next { get; }
            public float3 Anchor { get; }
            public bool CurrentUsesStart { get; }
            public bool NextUsesStart { get; }
            public float CurrentTrimParameter { get; }
            public float NextTrimParameter { get; }
            public float Alignment { get; }
            public float Distance { get; }
        }
    }
}
