using System.Collections.Generic;
using AdvancedRoadNaming.Domain;
using Unity.Entities;
using Unity.Mathematics;

namespace AdvancedRoadNaming.Services
{
    public sealed class RouteSelectionController
    {
        private const int AutoPathMaxDepth = 512;
        private const float ExistingWaypointSnapDistance = 16f;

        private readonly SegmentValidationService _validation;
        private readonly RoadNetworkPathingService _pathing;
        private readonly List<RoadRouteWaypoint> _waypoints = new List<RoadRouteWaypoint>();
        private readonly List<Entity> _selectedSegments = new List<Entity>();
        private readonly List<Entity> _previewSegments = new List<Entity>();
        private readonly List<RoadRouteWaypoint> _previewWaypoints = new List<RoadRouteWaypoint>();

        private WaypointEditMode _activeEditMode;
        private int _activeEditIndex = -1;

        public RouteSelectionController(SegmentValidationService validation, RoadNetworkPathingService pathing)
        {
            _validation = validation;
            _pathing = pathing;
        }

        public Entity HoveredSegment { get; private set; }

        public RoadRouteWaypoint? HoveredWaypoint { get; private set; }

        public string Warning { get; private set; }

        public IReadOnlyList<Entity> SelectedSegments => _selectedSegments;

        public IReadOnlyList<Entity> PreviewSegments => _previewSegments;

        public IReadOnlyList<RoadRouteWaypoint> PreviewWaypoints => _previewWaypoints;

        public IReadOnlyList<RoadRouteWaypoint> Waypoints => _waypoints;

        public int WaypointCount => _waypoints.Count;

        public bool HasActiveWaypointEdit => _activeEditMode != WaypointEditMode.None && _activeEditIndex >= 0;

        public bool HasActiveMoveEdit => _activeEditMode == WaypointEditMode.Move && _activeEditIndex >= 0;

        public bool HasActiveInsertEdit => _activeEditMode == WaypointEditMode.Insert && _activeEditIndex >= 0;

        public int ActiveEditIndex => _activeEditIndex;

        public bool HasHoveredRouteWaypoint => HoveredWaypointIndex >= 0;

        public bool HasHoveredRouteInsertion => HoveredInsertionIndex >= 0;

        public int HoveredWaypointIndex { get; private set; } = -1;

        public int HoveredInsertionIndex { get; private set; } = -1;

        public void SetHovered(Entity entity)
        {
            HoveredSegment = entity;
            HoveredWaypoint = null;
            HoveredWaypointIndex = -1;
            HoveredInsertionIndex = -1;
            RebuildPreviewState();
        }

        public void SetHovered(RoadRouteWaypoint waypoint)
        {
            SetHovered(waypoint, true);
        }

        public void SetHovered(RoadRouteWaypoint waypoint, bool allowRouteEditSnap)
        {
            HoveredSegment = waypoint.Segment;
            HoveredWaypoint = waypoint;
            HoveredWaypointIndex = allowRouteEditSnap ? FindHoveredWaypointIndex(waypoint) : -1;
            HoveredInsertionIndex = allowRouteEditSnap && HoveredWaypointIndex < 0 ? FindHoveredInsertionIndex(waypoint) : -1;
            RebuildPreviewState();
        }

        public bool TryBeginEditFromHover()
        {
            Warning = null;
            if (!HoveredWaypoint.HasValue)
                return false;

            if (HoveredWaypointIndex >= 0)
            {
                _activeEditMode = WaypointEditMode.Move;
                _activeEditIndex = HoveredWaypointIndex;
                RebuildPreviewState();
                return true;
            }

            if (HoveredInsertionIndex >= 0)
            {
                _activeEditMode = WaypointEditMode.Insert;
                _activeEditIndex = HoveredInsertionIndex;
                RebuildPreviewState();
                return true;
            }

            return false;
        }

        public bool TryBeginInsertFromNearestWaypoint()
        {
            Warning = null;
            if (!HoveredWaypoint.HasValue)
                return false;

            if (_waypoints.Count == 0)
                return false;

            if (HoveredWaypointIndex >= 0 || HoveredInsertionIndex >= 0)
                return false;

            var insertionIndex = FindNearestWaypointInsertionIndex(HoveredWaypoint.Value);
            if (insertionIndex < 0)
                return false;

            _activeEditMode = WaypointEditMode.Insert;
            _activeEditIndex = insertionIndex;
            RebuildPreviewState();
            return true;
        }

        public bool CommitActiveEdit()
        {
            Warning = null;
            if (!HasActiveWaypointEdit)
                return false;

            if (!HoveredWaypoint.HasValue)
            {
                Warning = "Keep the waypoint near a valid road while dragging.";
                return false;
            }

            bool committed;
            switch (_activeEditMode)
            {
                case WaypointEditMode.Insert:
                    committed = TryInsertWaypoint(_activeEditIndex, HoveredWaypoint.Value);
                    break;
                case WaypointEditMode.Move:
                    committed = TryMoveWaypoint(_activeEditIndex, HoveredWaypoint.Value);
                    break;
                default:
                    committed = false;
                    break;
            }

            if (committed)
                ClearActiveEditState();

            RebuildPreviewState();
            return committed;
        }

        public bool TryRemoveHoveredOrActiveWaypoint()
        {
            Warning = null;

            if (HasActiveMoveEdit)
                return TryRemoveWaypoint(_activeEditIndex);

            if (HoveredWaypointIndex >= 0)
                return TryRemoveWaypoint(HoveredWaypointIndex);

            Warning = "Hover an existing waypoint to remove it.";
            return false;
        }

        public void CancelActiveEdit()
        {
            Warning = null;
            ClearActiveEditState();
            RebuildPreviewState();
        }

        public bool TryAddHoveredWaypoint()
        {
            if (!HoveredWaypoint.HasValue)
            {
                Warning = "Place the waypoint on a valid road segment.";
                return false;
            }

            return TryAddWaypoint(HoveredWaypoint.Value);
        }

        public bool TryAddWaypoint(RoadRouteWaypoint waypoint)
        {
            Warning = null;
            if (!_validation.IsValidRoadSegment(waypoint.Segment))
            {
                Warning = "Place the waypoint on a valid road segment.";
                return false;
            }

            if (_waypoints.Count == 0)
            {
                _waypoints.Add(waypoint);
                _selectedSegments.Clear();
                AppendSegmentIfMissing(_selectedSegments, waypoint.Segment);
                _previewSegments.Clear();
                _previewWaypoints.Clear();
                return true;
            }

            var previousWaypoint = _waypoints[_waypoints.Count - 1];
            if (previousWaypoint.Segment == waypoint.Segment && math.distance(previousWaypoint.Position, waypoint.Position) < 1f)
            {
                Warning = "That waypoint is already the current route end.";
                return false;
            }

            var candidateWaypoints = new List<RoadRouteWaypoint>(_waypoints) { waypoint };
            return TryCommitCandidateWaypoints(candidateWaypoints, out _);
        }

        public bool TryAddSegment(Entity entity, bool autoPathEnabled)
        {
            return TryAddWaypoint(new RoadRouteWaypoint(entity, float3.zero, 0.5f));
        }

        public void RemoveLastWaypoint()
        {
            Warning = null;
            if (_waypoints.Count == 0)
            {
                Warning = "No waypoints to undo.";
                return;
            }

            _waypoints.RemoveAt(_waypoints.Count - 1);
            RebuildSelectedSegmentsFromWaypoints();
            RebuildPreviewState();
        }

        public void RemoveLast()
        {
            RemoveLastWaypoint();
        }

        public void Clear()
        {
            Warning = null;
            HoveredSegment = Entity.Null;
            HoveredWaypoint = null;
            HoveredWaypointIndex = -1;
            HoveredInsertionIndex = -1;
            ClearActiveEditState();
            _waypoints.Clear();
            _selectedSegments.Clear();
            _previewSegments.Clear();
            _previewWaypoints.Clear();
        }

        public void LoadRoute(IReadOnlyList<RoadRouteWaypoint> waypoints, IReadOnlyList<Entity> segments)
        {
            Warning = null;
            HoveredSegment = Entity.Null;
            HoveredWaypoint = null;
            HoveredWaypointIndex = -1;
            HoveredInsertionIndex = -1;
            ClearActiveEditState();
            _waypoints.Clear();
            _selectedSegments.Clear();
            _previewSegments.Clear();
            _previewWaypoints.Clear();

            if (waypoints != null)
            {
                for (var i = 0; i < waypoints.Count; i++)
                    _waypoints.Add(waypoints[i]);
            }

            if (segments != null)
            {
                for (var i = 0; i < segments.Count; i++)
                    AppendSegmentIfMissing(_selectedSegments, segments[i]);
            }
        }

        public string BuildRouteInstruction()
        {
            if (!string.IsNullOrWhiteSpace(Warning))
                return Warning;

            if (HasActiveWaypointEdit)
            {
                switch (_activeEditMode)
                {
                    case WaypointEditMode.Insert:
                        return $"Insert mode active. Drag near a road, then release to place the new waypoint. Right-click cancels. {_waypoints.Count} waypoints, {_selectedSegments.Count} computed segments.";
                    case WaypointEditMode.Move:
                        return $"Move mode active. Drag near a road, then release to move the waypoint. Right-click cancels. {_waypoints.Count} waypoints, {_selectedSegments.Count} computed segments.";
                }
            }

            if (_waypoints.Count == 0)
                return "Place first waypoint on a road.";

            if (_waypoints.Count == 1)
                return "Place next waypoint to compute a connected road path.";

            if (HoveredWaypointIndex >= 0)
                return $"{_waypoints.Count} waypoints, {_selectedSegments.Count} computed segments. Drag the snapped waypoint to move it, or right-click and release to remove it.";

            if (HoveredInsertionIndex >= 0)
                return $"{_waypoints.Count} waypoints, {_selectedSegments.Count} computed segments. Click and drag the route line to insert a waypoint.";

            return $"{_waypoints.Count} waypoints, {_selectedSegments.Count} computed segments. Place next waypoint, or drag an existing waypoint or route line to edit.";
        }

        private bool TryInsertWaypoint(int insertionIndex, RoadRouteWaypoint waypoint)
        {
            if (insertionIndex < 0 || insertionIndex > _waypoints.Count)
            {
                Warning = "Could not determine where to insert the waypoint.";
                return false;
            }

            var candidateWaypoints = new List<RoadRouteWaypoint>(_waypoints);
            candidateWaypoints.Insert(insertionIndex, waypoint);
            return TryCommitCandidateWaypoints(candidateWaypoints, out _);
        }

        private bool TryMoveWaypoint(int waypointIndex, RoadRouteWaypoint waypoint)
        {
            if (waypointIndex < 0 || waypointIndex >= _waypoints.Count)
            {
                Warning = "Could not determine which waypoint to move.";
                return false;
            }

            var candidateWaypoints = new List<RoadRouteWaypoint>(_waypoints);
            candidateWaypoints[waypointIndex] = waypoint;
            return TryCommitCandidateWaypoints(candidateWaypoints, out _);
        }

        public bool TryRemoveWaypoint(int waypointIndex)
        {
            if (waypointIndex < 0 || waypointIndex >= _waypoints.Count)
            {
                Warning = "Could not determine which waypoint to remove.";
                return false;
            }

            _waypoints.RemoveAt(waypointIndex);
            ClearActiveEditState();
            RebuildSelectedSegmentsFromWaypoints();
            RebuildPreviewState();
            return true;
        }

        private bool TryCommitCandidateWaypoints(List<RoadRouteWaypoint> candidateWaypoints, out int appendedSegmentDelta)
        {
            appendedSegmentDelta = 0;
            if (!TryBuildSegmentsFromWaypoints(candidateWaypoints, out var candidateSegments, out var warning))
            {
                Warning = warning;
                return false;
            }

            appendedSegmentDelta = candidateSegments.Count - _selectedSegments.Count;
            _waypoints.Clear();
            _waypoints.AddRange(candidateWaypoints);
            _selectedSegments.Clear();
            _selectedSegments.AddRange(candidateSegments);
            _previewSegments.Clear();
            _previewWaypoints.Clear();
            return true;
        }

        private void RebuildPreviewState()
        {
            _previewSegments.Clear();
            _previewWaypoints.Clear();

            if (!HoveredWaypoint.HasValue)
                return;

            if (HasActiveWaypointEdit)
            {
                var candidateWaypoints = BuildActiveEditCandidate(HoveredWaypoint.Value);
                if (TryBuildSegmentsFromWaypoints(candidateWaypoints, out var previewSegments, out _))
                {
                    _previewWaypoints.AddRange(candidateWaypoints);
                    _previewSegments.AddRange(previewSegments);
                }

                return;
            }

            if (HoveredInsertionIndex >= 0)
            {
                var candidateWaypoints = new List<RoadRouteWaypoint>(_waypoints);
                candidateWaypoints.Insert(HoveredInsertionIndex, HoveredWaypoint.Value);
                if (TryBuildSegmentsFromWaypoints(candidateWaypoints, out var previewSegments, out _))
                {
                    _previewWaypoints.AddRange(candidateWaypoints);
                    _previewSegments.AddRange(previewSegments);
                }

                return;
            }

            if (_waypoints.Count == 0 || !_validation.IsValidRoadSegment(HoveredWaypoint.Value.Segment))
                return;

            var lastWaypoint = _waypoints[_waypoints.Count - 1];
            if (lastWaypoint.Segment == HoveredWaypoint.Value.Segment)
            {
                AppendSegmentIfMissing(_previewSegments, HoveredWaypoint.Value.Segment);
                _previewWaypoints.Add(lastWaypoint);
                _previewWaypoints.Add(HoveredWaypoint.Value);
                return;
            }

            var previewPath = _pathing.FindPath(lastWaypoint.Segment, HoveredWaypoint.Value.Segment, AutoPathMaxDepth);
            for (var i = 0; i < previewPath.Count; i++)
                AppendSegmentIfMissing(_previewSegments, previewPath[i]);

            _previewWaypoints.Add(lastWaypoint);
            _previewWaypoints.Add(HoveredWaypoint.Value);
        }

        private List<RoadRouteWaypoint> BuildActiveEditCandidate(RoadRouteWaypoint hoverWaypoint)
        {
            var candidateWaypoints = new List<RoadRouteWaypoint>(_waypoints);
            switch (_activeEditMode)
            {
                case WaypointEditMode.Insert:
                    if (_activeEditIndex >= 0 && _activeEditIndex <= candidateWaypoints.Count)
                        candidateWaypoints.Insert(_activeEditIndex, hoverWaypoint);
                    break;
                case WaypointEditMode.Move:
                    if (_activeEditIndex >= 0 && _activeEditIndex < candidateWaypoints.Count)
                        candidateWaypoints[_activeEditIndex] = hoverWaypoint;
                    break;
            }

            return candidateWaypoints;
        }

        private void RebuildSelectedSegmentsFromWaypoints()
        {
            _selectedSegments.Clear();
            if (_waypoints.Count == 0)
                return;

            if (!TryBuildSegmentsFromWaypoints(_waypoints, out var rebuiltSegments, out var warning))
            {
                Warning = warning;
                return;
            }

            _selectedSegments.AddRange(rebuiltSegments);
        }

        private bool TryBuildSegmentsFromWaypoints(IReadOnlyList<RoadRouteWaypoint> waypoints, out List<Entity> segments, out string warning)
        {
            warning = null;
            segments = new List<Entity>();
            if (waypoints == null || waypoints.Count == 0)
                return true;

            if (!_validation.IsValidRoadSegment(waypoints[0].Segment))
            {
                warning = "Place the waypoint on a valid road segment.";
                return false;
            }

            AppendSegmentIfMissing(segments, waypoints[0].Segment);
            for (var i = 1; i < waypoints.Count; i++)
            {
                if (!_validation.IsValidRoadSegment(waypoints[i].Segment))
                {
                    warning = "Place the waypoint on a valid road segment.";
                    return false;
                }

                var path = _pathing.FindPath(waypoints[i - 1].Segment, waypoints[i].Segment, AutoPathMaxDepth);
                if (path.Count == 0)
                {
                    warning = "No connected road path found between adjacent waypoints.";
                    return false;
                }

                AppendPath(segments, path);
            }

            return true;
        }

        private int FindHoveredWaypointIndex(RoadRouteWaypoint hoverWaypoint)
        {
            var closestIndex = -1;
            var closestDistance = ExistingWaypointSnapDistance;
            for (var i = 0; i < _waypoints.Count; i++)
            {
                var existing = _waypoints[i];
                var distance = math.distance(existing.Position, hoverWaypoint.Position);
                if (distance <= closestDistance)
                {
                    closestIndex = i;
                    closestDistance = distance;
                }
            }

            return closestIndex;
        }

        private int FindHoveredInsertionIndex(RoadRouteWaypoint hoverWaypoint)
        {
            if (_waypoints.Count < 2)
                return -1;

            for (var i = 0; i < _waypoints.Count - 1; i++)
            {
                var legPath = _pathing.FindPath(_waypoints[i].Segment, _waypoints[i + 1].Segment, AutoPathMaxDepth);
                if (!PathContainsSegment(legPath, hoverWaypoint.Segment))
                    continue;

                return i + 1;
            }

            return -1;
        }

        private int FindNearestWaypointInsertionIndex(RoadRouteWaypoint hoverWaypoint)
        {
            var nearestIndex = -1;
            var nearestDistance = float.MaxValue;
            for (var i = 0; i < _waypoints.Count; i++)
            {
                var distance = math.distance(_waypoints[i].Position, hoverWaypoint.Position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = i;
                }
            }

            if (nearestIndex < 0)
                return -1;

            if (_waypoints.Count == 1 || nearestIndex == 0)
                return 0;

            if (nearestIndex == _waypoints.Count - 1)
                return _waypoints.Count;

            var beforeCost = EstimateInsertionCost(_waypoints[nearestIndex - 1], hoverWaypoint, _waypoints[nearestIndex]);
            var afterCost = EstimateInsertionCost(_waypoints[nearestIndex], hoverWaypoint, _waypoints[nearestIndex + 1]);

            if (beforeCost < 0f && afterCost < 0f)
                return -1;
            if (beforeCost < 0f)
                return nearestIndex + 1;
            if (afterCost < 0f)
                return nearestIndex;

            return beforeCost <= afterCost ? nearestIndex : nearestIndex + 1;
        }

        private float EstimateInsertionCost(RoadRouteWaypoint from, RoadRouteWaypoint waypoint, RoadRouteWaypoint to)
        {
            var first = EstimatePathCost(from, waypoint);
            var second = EstimatePathCost(waypoint, to);
            return first < 0f || second < 0f ? -1f : first + second;
        }

        private float EstimatePathCost(RoadRouteWaypoint from, RoadRouteWaypoint to)
        {
            if (!_validation.IsValidRoadSegment(from.Segment) || !_validation.IsValidRoadSegment(to.Segment))
                return -1f;

            if (from.Segment == to.Segment)
                return math.distance(from.Position, to.Position);

            var path = _pathing.FindPath(from.Segment, to.Segment, AutoPathMaxDepth);
            if (path == null || path.Count == 0)
                return -1f;

            return path.Count;
        }

        private static bool PathContainsSegment(IReadOnlyList<Entity> path, Entity segment)
        {
            if (path == null)
                return false;

            for (var i = 0; i < path.Count; i++)
            {
                if (path[i] == segment)
                    return true;
            }

            return false;
        }

        private static int AppendPath(List<Entity> target, IReadOnlyList<Entity> path)
        {
            var appended = 0;
            for (var i = 0; i < path.Count; i++)
            {
                if (AppendSegmentIfMissing(target, path[i]))
                    appended++;
            }

            return appended;
        }

        private static bool AppendSegmentIfMissing(List<Entity> target, Entity segment)
        {
            if (target.Contains(segment))
                return false;

            target.Add(segment);
            return true;
        }

        private void ClearActiveEditState()
        {
            _activeEditMode = WaypointEditMode.None;
            _activeEditIndex = -1;
        }

        private enum WaypointEditMode
        {
            None = 0,
            Insert = 1,
            Move = 2
        }
    }
}
