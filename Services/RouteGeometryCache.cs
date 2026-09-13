using System.Collections.Generic;
using AdvancedRoadNaming.Domain;
using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;

namespace AdvancedRoadNaming.Services
{
    // Exact input snapshots avoid hash collisions and catch same-count waypoint edits.
    internal sealed class RouteGeometryCache
    {
        private readonly List<Entity> _segments = new List<Entity>();
        private readonly List<RoadRouteWaypoint> _waypoints = new List<RoadRouteWaypoint>();
        private long _networkRevision = -1;
        public readonly List<Bezier4x3> Curves = new List<Bezier4x3>();
        public readonly List<float3> Nodes = new List<float3>();
        public long Version { get; private set; }

        public bool Update(EntityManager manager, IReadOnlyList<Entity> segments,
            IReadOnlyList<RoadRouteWaypoint> waypoints, long networkRevision)
        {
            if (_networkRevision == networkRevision && Matches(segments, waypoints))
                return false;
            _networkRevision = networkRevision;
            _segments.Clear();
            _waypoints.Clear();
            if (segments != null)
                for (var i = 0; i < segments.Count; i++) _segments.Add(segments[i]);
            if (waypoints != null)
                for (var i = 0; i < waypoints.Count; i++) _waypoints.Add(waypoints[i]);
            RouteOverlayGeometryBuilder.BuildRouteGeometry(manager, _segments, _waypoints, Curves, Nodes);
            Version++;
            return true;
        }

        private bool Matches(IReadOnlyList<Entity> segments, IReadOnlyList<RoadRouteWaypoint> waypoints)
        {
            if (_segments.Count != (segments?.Count ?? 0) || _waypoints.Count != (waypoints?.Count ?? 0))
                return false;
            for (var i = 0; i < _segments.Count; i++)
                if (_segments[i] != segments[i]) return false;
            for (var i = 0; i < _waypoints.Count; i++)
                if (!SameWaypoint(_waypoints[i], waypoints[i])) return false;
            return true;
        }

        internal static bool SameWaypoint(RoadRouteWaypoint a, RoadRouteWaypoint b) =>
            a.Segment == b.Segment && a.CurvePosition == b.CurvePosition && a.Position.Equals(b.Position);

        public void Clear()
        {
            _segments.Clear();
            _waypoints.Clear();
            Curves.Clear();
            Nodes.Clear();
            _networkRevision = -1;
        }
    }
}
