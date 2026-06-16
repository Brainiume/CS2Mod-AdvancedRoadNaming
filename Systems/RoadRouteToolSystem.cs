using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using AdvancedRoadNaming.Domain;
using AdvancedRoadNaming.Services;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.InputSystem;

namespace AdvancedRoadNaming.Systems
{
    public sealed partial class RoadRouteToolSystem : ToolBaseSystem
    {
        public const string ToolIdentifier = "AdvancedRoadNaming.RouteBasedRoadNamingTool";

        private SegmentMetadataSystem _metadataSystem;
        private RouteSelectionController _selectionController;
        private RoadRouteToolMode _mode;
        private string _inputText;
        private RouteNumberPlacement _routeNumberPlacement;
        private string _statusMessage;
        private bool _isRunning;
        private bool _routeMenuActive;
        private bool _savedRoutesViewActive;
        private bool _undergroundMode;
        private long _roadNameEditRouteId;
        private long _selectedSavedRouteId;
        private int _manageOverlayVersion;
        private bool _savedRoutesJsonDirty = true;
        private string _savedRoutesJsonCache = "[]";
        private SavedRouteReviewSession _savedRouteReview;
        private readonly System.Collections.Generic.List<Entity> _savedRoutePreviewSegments = new System.Collections.Generic.List<Entity>();
        private readonly System.Collections.Generic.List<RoadRouteWaypoint> _savedRoutePreviewWaypoints = new System.Collections.Generic.List<RoadRouteWaypoint>();

        public override string toolID => ToolIdentifier;

        public override bool allowUnderground => true;

        public RoadRouteToolMode Mode => _mode;

        public string InputText => _inputText ?? string.Empty;

        public RouteNumberPlacement RouteNumberPlacement => _routeNumberPlacement;

        public string StatusMessage => !string.IsNullOrWhiteSpace(_statusMessage) ? _statusMessage : _selectionController?.BuildRouteInstruction() ?? string.Empty;

        public bool IsRunning => _isRunning;

        public bool RouteMenuActive => _routeMenuActive;

        public bool UndergroundMode => _undergroundMode;

        public Entity HoveredSegment => _selectionController?.HoveredSegment ?? Entity.Null;

        public RoadRouteWaypoint? HoveredWaypoint => _selectionController?.HoveredWaypoint;

        public System.Collections.Generic.IReadOnlyList<Entity> SelectedSegments => _selectionController.SelectedSegments;

        public System.Collections.Generic.IReadOnlyList<RoadRouteWaypoint> Waypoints => _selectionController.Waypoints;

        public System.Collections.Generic.IReadOnlyList<Entity> PreviewSegments => _selectionController.PreviewSegments;

        public System.Collections.Generic.IReadOnlyList<RoadRouteWaypoint> PreviewWaypoints => _selectionController.PreviewWaypoints;

        public bool HasActiveMoveEdit => _selectionController?.HasActiveMoveEdit ?? false;

        public bool HasHoveredRouteWaypoint => _selectionController?.HasHoveredRouteWaypoint ?? false;

        public int ActiveEditIndex => _selectionController?.ActiveEditIndex ?? -1;

        public System.Collections.Generic.IReadOnlyList<Entity> SavedRoutePreviewSegments => _savedRoutePreviewSegments;

        public System.Collections.Generic.IReadOnlyList<RoadRouteWaypoint> SavedRoutePreviewWaypoints => _savedRoutePreviewWaypoints;

        public int WaypointCount => _selectionController.WaypointCount;

        public SavedRouteReviewSession SavedRouteReview => _savedRouteReview;

        public long RoadNameEditRouteId => _roadNameEditRouteId;

        public bool SavedRoutesViewActive => _savedRoutesViewActive;

        public long SelectedSavedRouteId => _selectedSavedRouteId;

        public bool SavedRouteManipulateMode => _savedRouteReview != null && _savedRouteReview.Mode == SavedRouteReviewMode.Modify;

        public int ManageOverlayVersion => _manageOverlayVersion;

        public int ReviewSegmentCount => _savedRouteReview == null
            ? 0
            : _savedRouteReview.Mode == SavedRouteReviewMode.Modify
                ? _selectionController.SelectedSegments.Count
                : _savedRouteReview.CandidateSegments.Count;

        public int ReviewWaypointCount => _savedRouteReview == null
            ? 0
            : _savedRouteReview.Mode == SavedRouteReviewMode.Modify
                ? _selectionController.WaypointCount
                : _savedRouteReview.CandidateWaypoints.Count;

        protected override void OnCreate()
        {
            base.OnCreate();
            _metadataSystem = World.GetOrCreateSystemManaged<SegmentMetadataSystem>();
            _selectionController = new RouteSelectionController(_metadataSystem.Validation, _metadataSystem.Pathing);
            _mode = RoadRouteToolMode.AssignMajorRouteNumber;
            _inputText = string.Empty;
            _routeNumberPlacement = RouteNumberPlacement.AfterBaseName;
            _statusMessage = "Place first waypoint on a road.";
            requireNet = Game.Net.Layer.Road;
            Mod.log.Info("RoadRouteToolSystem created");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            _isRunning = true;
            EnableToolActions(true);
            _statusMessage = _routeMenuActive
                ? "Advanced Road Routes menu active."
                : _savedRoutesViewActive
                ? "Saved routes view active."
                : "Route creation active. Place first waypoint on a road.";
        }

        protected override void OnStopRunning()
        {
            base.OnStopRunning();
            EnableToolActions(false);
            _isRunning = false;
            SetUnderground(false);
            _selectionController?.SetHovered(Entity.Null);
        }

        public override PrefabBase GetPrefab()
        {
            return null;
        }

        public override bool TrySetPrefab(PrefabBase prefab)
        {
            return false;
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.collisionMask = _undergroundMode
                ? CollisionMask.Underground
                : CollisionMask.OnGround | CollisionMask.Overground;
            m_ToolRaycastSystem.typeMask = TypeMask.Net;
            m_ToolRaycastSystem.netLayerMask = Game.Net.Layer.Road;
            m_ToolRaycastSystem.raycastFlags = RaycastFlags.Markers | RaycastFlags.ElevateOffset | RaycastFlags.SubElements | RaycastFlags.OutsideConnections;
        }

        public override void SetUnderground(bool isUnderground)
        {
            _undergroundMode = isUnderground;
            requireUnderground = isUnderground;
            if (m_ToolRaycastSystem != null)
            {
                m_ToolRaycastSystem.collisionMask = _undergroundMode
                    ? CollisionMask.Underground
                    : CollisionMask.OnGround | CollisionMask.Overground;
            }

            _statusMessage = _undergroundMode
                ? "Underground road selection active."
                : "Surface road selection active.";
        }

        public override void ElevationUp()
        {
            SetUnderground(false);
        }

        public override void ElevationDown()
        {
            SetUnderground(true);
        }

        public override void ElevationScroll()
        {
            SetUnderground(!_undergroundMode);
        }

        public void SetUndergroundMode(bool enabled)
        {
            SetUnderground(enabled);
        }

        public void SetMode(RoadRouteToolMode mode)
        {
            _routeMenuActive = false;
            _mode = mode;
            _statusMessage = $"Mode: {mode}. {_selectionController.BuildRouteInstruction()}";
        }

        public void SetInputText(string value)
        {
            _inputText = value ?? string.Empty;
        }

        public void SetRouteNumberPlacement(RouteNumberPlacement placement)
        {
            _routeNumberPlacement = placement;
        }

        public void SetRouteMenuActive(bool active)
        {
            _routeMenuActive = active;
            if (!active)
                return;

            _savedRoutesViewActive = false;
            _roadNameEditRouteId = 0;
            _selectedSavedRouteId = 0;
            _savedRouteReview = null;
            _selectionController.Clear();
            _selectionController.SetHovered(Entity.Null);
            _savedRoutePreviewSegments.Clear();
            _savedRoutePreviewWaypoints.Clear();
            _statusMessage = "Advanced Road Routes menu active.";
            MarkSavedRoutesJsonDirty();
            _manageOverlayVersion++;
            Mod.log.Info("Road Naming: Advanced Road Routes menu activated.");
        }

        public void SetSavedRoutesViewActive(bool active, bool resetSelection = true)
        {
            _routeMenuActive = false;
            _savedRoutesViewActive = active;
            if (resetSelection)
            {
                _roadNameEditRouteId = 0;
                if (!active)
                    _selectedSavedRouteId = 0;
                _savedRouteReview = null;
                _selectionController.Clear();
                _selectionController.SetHovered(Entity.Null);
                _savedRoutePreviewSegments.Clear();
                _savedRoutePreviewWaypoints.Clear();
            }
            else if (!active)
            {
                _roadNameEditRouteId = 0;
                _savedRoutePreviewSegments.Clear();
                _savedRoutePreviewWaypoints.Clear();
            }

            _statusMessage = active
                ? "Saved routes view active."
                : "Route creation active. Place first waypoint on a road.";
            if (active)
                MarkSavedRoutesJsonDirty();
            _manageOverlayVersion++;
            Mod.log.Info(active
                ? "Road Naming: route creation input suspended while viewing Saved Routes."
                : "Road Naming: route creation input re-enabled.");
        }

        public bool TryAddHoveredSegment()
        {
            return TryAddHoveredWaypoint();
        }

        public bool TryAddHoveredWaypoint()
        {
            var hoveredWaypoint = _selectionController.HoveredWaypoint;
            if (!hoveredWaypoint.HasValue)
            {
                _statusMessage = "Place the waypoint on a valid road.";
                Mod.log.Warn("Road Naming: click ignored because there is no committed hover waypoint.");
                return false;
            }

            var previousWaypointCount = _selectionController.WaypointCount;
            var previousSegmentCount = _selectionController.SelectedSegments.Count;
            var added = _selectionController.TryAddWaypoint(hoveredWaypoint.Value);
            _statusMessage = added ? _selectionController.BuildRouteInstruction() : _selectionController.Warning;
            if (added)
            {
                var appendedSegments = _selectionController.SelectedSegments.Count - previousSegmentCount;
                MarkModifyReviewDirty();
                Mod.log.Info(() => $"Road Naming: waypoint committed. FromWaypoint={previousWaypointCount}, ToWaypoint={_selectionController.WaypointCount}, AppendedSegments={appendedSegments}, TotalSegments={_selectionController.SelectedSegments.Count}");
            }
            else
            {
                Mod.log.Warn(() => $"Road Naming: waypoint rejected: {_selectionController.Warning}");
            }
            return added;
        }

        public bool TryAddSegment(Entity segment)
        {
            var waypoint = new RoadRouteWaypoint(segment, float3.zero, 0.5f);
            var added = _selectionController.TryAddWaypoint(waypoint);
            _statusMessage = added ? _selectionController.BuildRouteInstruction() : _selectionController.Warning;
            return added;
        }

        public void ClearSelection()
        {
            _selectionController.Clear();
            MarkModifyReviewDirty();
            _statusMessage = "Route cleared. Place first waypoint on a road.";
            Mod.log.Info("Road Naming: waypoint route cleared.");
        }

        public void RemoveLastSegment()
        {
            RemoveLastWaypoint();
        }

        public void RemoveLastWaypoint()
        {
            _selectionController.RemoveLastWaypoint();
            MarkModifyReviewDirty();
            _statusMessage = _selectionController.BuildRouteInstruction();
            Mod.log.Info(() => $"Road Naming: waypoint undo. Waypoints={_selectionController.WaypointCount}, Segments={_selectionController.SelectedSegments.Count}");
        }

        public bool Apply()
        {
            if (_selectionController.SelectedSegments.Count == 0)
            {
                _statusMessage = "Create a committed waypoint route before applying.";
                return false;
            }

            bool result;
            string message;
            switch (_mode)
            {
                case RoadRouteToolMode.RenameSelectedSegments:
                    result = _metadataSystem.ApplyRename(_selectionController.SelectedSegments, _inputText, out message);
                    break;
                default:
                    result = _metadataSystem.ApplyRouteNumber(_selectionController.SelectedSegments, _inputText, _routeNumberPlacement, out message);
                    break;
            }

            _statusMessage = message;
            if (result && _mode == RoadRouteToolMode.AssignMajorRouteNumber)
            {
                var savedRoute = _metadataSystem.SaveAppliedRoute(_selectionController.SelectedSegments, _selectionController.Waypoints, _mode, _inputText, _routeNumberPlacement);
                _selectedSavedRouteId = savedRoute.RouteId;
                MarkSavedRoutesJsonDirty();
                _manageOverlayVersion++;
                _statusMessage = message + $" Saved route #{savedRoute.RouteId}.";
            }

            Mod.log.Info(() => $"Road Naming: apply executed. Mode={_mode}, Segments={_selectionController.SelectedSegments.Count}, Result={result}");
            return result;
        }

        public string SavedRoutesJson
        {
            get
            {
                if (_metadataSystem == null)
                    return "[]";

                if (_savedRoutesJsonDirty)
                {
                    _savedRoutesJsonCache = _metadataSystem.BuildSavedRoutesJson() ?? "[]";
                    _savedRoutesJsonDirty = false;
                }

                return _savedRoutesJsonCache;
            }
        }

        public bool PreviewSavedRoute(long routeId)
        {
            return SelectSavedRoute(routeId);
        }

        public bool SelectSavedRoute(long routeId)
        {
            if (_savedRouteReview != null && _savedRouteReview.Mode == SavedRouteReviewMode.Modify && _savedRouteReview.RouteId != routeId)
                DiscardActiveSavedRouteEdit("Route selection changed; discarded unapplied manipulate edits.");

            _savedRoutePreviewSegments.Clear();
            _savedRoutePreviewWaypoints.Clear();
            if (_metadataSystem == null || !_metadataSystem.RouteDatabase.TryGet(routeId, out var route))
            {
                _statusMessage = $"Saved route {routeId} was not found.";
                if (_selectedSavedRouteId == routeId)
                    _selectedSavedRouteId = 0;
                _manageOverlayVersion++;
                return false;
            }

            for (var i = 0; i < route.OrderedSegmentIds.Count; i++)
            {
                var segment = route.OrderedSegmentIds[i];
                if (_metadataSystem.Validation.IsValidRoadSegment(segment) && !_savedRoutePreviewSegments.Contains(segment))
                    _savedRoutePreviewSegments.Add(segment);
            }

            for (var i = 0; i < route.Waypoints.Count; i++)
                _savedRoutePreviewWaypoints.Add(route.Waypoints[i]);

            var status = _metadataSystem.EvaluateSavedRouteStatus(route);
            _selectedSavedRouteId = route.RouteId;
            _manageOverlayVersion++;
            _statusMessage = $"Previewing saved route #{route.RouteId}: {route.DisplayTitle} ({status}, {_savedRoutePreviewSegments.Count}/{route.OrderedSegmentIds.Count} valid segments).";
            Mod.log.Info(() => $"Road Naming: saved route preview requested. RouteId={route.RouteId}, ValidSegments={_savedRoutePreviewSegments.Count}, StoredSegments={route.OrderedSegmentIds.Count}, Status={status}.");
            return true;
        }

        public bool BeginRebuildSavedRoute(long routeId)
        {
            if (!_metadataSystem.TryCreateRebuildReview(routeId, out var review, out var message))
            {
                _statusMessage = message;
                return false;
            }

            _savedRouteReview = review;
            _selectedSavedRouteId = routeId;
            LoadSavedRoutePreview(review.CandidateSegments, review.CandidateWaypoints);
            _selectionController.Clear();
            _savedRoutesViewActive = true;
            _manageOverlayVersion++;
            _statusMessage = message;
            Mod.log.Info(() => $"Road Naming: rebuild review entered. RouteId={routeId}, CandidateSegments={review.CandidateSegments.Count}.");
            return true;
        }

        public bool BeginModifySavedRoute(long routeId)
        {
            SavedRouteReviewSession review;
            string message;
            if (_savedRouteReview != null && _savedRouteReview.RouteId == routeId && _savedRouteReview.Mode == SavedRouteReviewMode.RebuildPreview)
            {
                review = new SavedRouteReviewSession
                {
                    RouteId = _savedRouteReview.RouteId,
                    Mode = SavedRouteReviewMode.Modify,
                    RouteMode = _savedRouteReview.RouteMode,
                    InputValue = _savedRouteReview.InputValue,
                    RouteNumberPlacement = _savedRouteReview.RouteNumberPlacement,
                    Message = $"Modify mode active for route #{routeId}. Drag existing waypoints or the route line to edit, then commit or cancel.",
                    IsDirty = false
                };
                for (var i = 0; i < _savedRouteReview.CandidateWaypoints.Count; i++)
                    review.CandidateWaypoints.Add(_savedRouteReview.CandidateWaypoints[i]);
                for (var i = 0; i < _savedRouteReview.CandidateSegments.Count; i++)
                    review.CandidateSegments.Add(_savedRouteReview.CandidateSegments[i]);
                message = review.Message;
            }
            else if (!_metadataSystem.TryCreateModifyReview(routeId, out review, out message))
            {
                _statusMessage = message;
                return false;
            }

            _savedRouteReview = review;
            _mode = review.RouteMode;
            _inputText = review.InputValue ?? string.Empty;
            _routeNumberPlacement = review.RouteNumberPlacement;
            _selectionController.LoadRoute(review.CandidateWaypoints, review.CandidateSegments);
            _savedRoutePreviewSegments.Clear();
            _savedRoutePreviewWaypoints.Clear();
            _selectedSavedRouteId = routeId;
            _savedRoutesViewActive = false;
            _manageOverlayVersion++;
            _statusMessage = message;
            Mod.log.Info(() => $"Road Naming: modify review entered. RouteId={routeId}, Waypoints={review.CandidateWaypoints.Count}, Segments={review.CandidateSegments.Count}.");
            return true;
        }

        public bool SetSavedRouteManipulateMode(long routeId, bool enabled)
        {
            if (enabled)
            {
                if (routeId <= 0)
                    routeId = _selectedSavedRouteId;

                if (routeId <= 0)
                {
                    _statusMessage = "Select a saved route before enabling manipulate mode.";
                    return false;
                }

                if (!SelectSavedRoute(routeId))
                    return false;

                return BeginModifySavedRoute(routeId);
            }

            if (_savedRouteReview == null)
            {
                SetSavedRoutesViewActive(true, false);
                return true;
            }

            return CancelSavedRouteReview(_savedRouteReview.RouteId);
        }

        public bool AcceptSavedRouteReview(long routeId)
        {
            if (_savedRouteReview == null || _savedRouteReview.RouteId != routeId)
            {
                _statusMessage = $"Saved route {routeId} is not currently in rebuild or modify review.";
                return false;
            }

            var finalWaypoints = _savedRouteReview.Mode == SavedRouteReviewMode.Modify
                ? _selectionController.Waypoints
                : _savedRouteReview.CandidateWaypoints;
            var finalSegments = _savedRouteReview.Mode == SavedRouteReviewMode.Modify
                ? _selectionController.SelectedSegments
                : _savedRouteReview.CandidateSegments;

            var result = _metadataSystem.CommitSavedRouteReview(routeId, finalWaypoints, finalSegments, _routeNumberPlacement, out var message);
            _statusMessage = message;
            if (result)
            {
                _savedRouteReview = null;
                _selectedSavedRouteId = routeId;
                MarkSavedRoutesJsonDirty();
                SetSavedRoutesViewActive(true, false);
                PreviewSavedRoute(routeId);
            }

            return result;
        }

        public bool CancelSavedRouteReview(long routeId)
        {
            if (_savedRouteReview == null || _savedRouteReview.RouteId != routeId)
            {
                _statusMessage = $"Saved route {routeId} is not currently being reviewed.";
                return false;
            }

            _savedRouteReview = null;
            _selectedSavedRouteId = routeId;
            SetSavedRoutesViewActive(true, false);
            PreviewSavedRoute(routeId);
            _statusMessage = $"Canceled saved route review for route {routeId}.";
            Mod.log.Info(() => $"Road Naming: saved route review canceled. RouteId={routeId}.");
            return true;
        }

        public bool ReapplySavedRoute(long routeId)
        {
            if (_savedRouteReview != null && _savedRouteReview.Mode == SavedRouteReviewMode.Modify && _savedRouteReview.RouteId == routeId)
            {
                var finalWaypoints = _selectionController.Waypoints;
                var finalSegments = _selectionController.SelectedSegments;
                if (!_metadataSystem.CommitSavedRouteReview(routeId, finalWaypoints, finalSegments, _routeNumberPlacement, out var commitMessage))
                {
                    _statusMessage = commitMessage;
                    return false;
                }

                _savedRouteReview = null;
                _savedRoutesViewActive = true;
                _selectedSavedRouteId = routeId;
                MarkSavedRoutesJsonDirty();
                _manageOverlayVersion++;
            }

            var result = _metadataSystem.ReapplySavedRoute(routeId, out var message);
            _statusMessage = message;
            if (result)
            {
                _selectedSavedRouteId = routeId;
                MarkSavedRoutesJsonDirty();
                SetSavedRoutesViewActive(true, false);
                PreviewSavedRoute(routeId);
                _manageOverlayVersion++;
            }
            Mod.log.Info(() => $"Road Naming: saved route reapply requested. RouteId={routeId}, Result={result}, Message='{message}'.");
            return result;
        }

        private void DiscardActiveSavedRouteEdit(string message)
        {
            if (_savedRouteReview == null || _savedRouteReview.Mode != SavedRouteReviewMode.Modify)
                return;

            var routeId = _savedRouteReview.RouteId;
            _savedRouteReview = null;
            _selectionController.Clear();
            _savedRoutesViewActive = true;
            _selectedSavedRouteId = routeId;
            _manageOverlayVersion++;
            _statusMessage = message;
            Mod.log.Info(() => $"Road Naming: manipulate edits discarded. RouteId={routeId}, Reason='{message}'.");
        }

        public bool BeginSavedRouteRoadNameEdit(long routeId)
        {
            if (_metadataSystem == null || !_metadataSystem.RouteDatabase.TryGet(routeId, out var route))
            {
                _statusMessage = $"Saved route {routeId} was not found.";
                return false;
            }

            var candidateSegments = new System.Collections.Generic.List<Entity>();
            for (var i = 0; i < route.OrderedSegmentIds.Count; i++)
            {
                var segment = route.OrderedSegmentIds[i];
                if (_metadataSystem.Validation.IsValidRoadSegment(segment))
                    candidateSegments.Add(segment);
            }

            if (candidateSegments.Count == 0)
            {
                _statusMessage = $"Saved route '{route.DisplayTitle}' has no valid road segments available for road-name editing.";
                return false;
            }

            _roadNameEditRouteId = routeId;
            _savedRouteReview = null;
            _selectionController.Clear();
            _selectionController.SetHovered(Entity.Null);
            _savedRoutePreviewSegments.Clear();
            _savedRoutePreviewWaypoints.Clear();
            _savedRoutesViewActive = true;
            _statusMessage = $"Road-name edit mode active for '{route.DisplayTitle}'. Use the vanilla road UI, then return to Saved Routes to keep those name changes.";
            Mod.log.Info(() => $"Road Naming: saved route road-name edit started. RouteId={routeId}, Segments={candidateSegments.Count}.");
            return true;
        }

        public bool ReturnToSavedRoutesFromRoadNameEdit()
        {
            if (_roadNameEditRouteId <= 0)
            {
                _statusMessage = "Saved route road-name edit mode is not active.";
                return false;
            }

            var routeId = _roadNameEditRouteId;
            var result = _metadataSystem.CaptureSavedRouteRoadNames(routeId, out var message);
            _roadNameEditRouteId = 0;
            _savedRoutesViewActive = true;
            if (result)
                MarkSavedRoutesJsonDirty();
            _statusMessage = message;
            Mod.log.Info(() => $"Road Naming: saved route road-name edit finished. RouteId={routeId}, Result={result}, Message='{message}'.");
            return result;
        }

        public bool DeleteSavedRoute(long routeId)
        {
            var result = _metadataSystem.DeleteSavedRoute(routeId, out var message);
            if (result)
            {
                if (_savedRouteReview != null && _savedRouteReview.RouteId == routeId)
                    _savedRouteReview = null;
                if (_selectedSavedRouteId == routeId)
                    _selectedSavedRouteId = 0;
                _savedRoutePreviewSegments.Clear();
                _savedRoutePreviewWaypoints.Clear();
                _selectionController.Clear();
                MarkSavedRoutesJsonDirty();
                _manageOverlayVersion++;
            }

            _statusMessage = message;
            return result;
        }

        public bool RenameSavedRoute(long routeId, string title)
        {
            var result = _metadataSystem.RenameSavedRoute(routeId, title, out var message);
            if (result)
            {
                MarkSavedRoutesJsonDirty();
                _manageOverlayVersion++;
            }
            _statusMessage = message;
            return result;
        }

        public bool UpdateSavedRouteInput(long routeId, string inputValue)
        {
            var result = _metadataSystem.UpdateSavedRouteInput(routeId, inputValue, out var message);
            if (result)
            {
                MarkSavedRoutesJsonDirty();
                _manageOverlayVersion++;
            }
            _statusMessage = message;
            return result;
        }

        public bool UpdateSavedRoutePlacement(long routeId, RouteNumberPlacement placement)
        {
            var result = _metadataSystem.UpdateSavedRoutePlacement(routeId, placement, out var message);
            if (result)
            {
                MarkSavedRoutesJsonDirty();
                _manageOverlayVersion++;
            }
            _statusMessage = message;
            return result;
        }

        private void MarkSavedRoutesJsonDirty()
        {
            _savedRoutesJsonDirty = true;
            _savedRoutesJsonCache = "[]";
        }

        public bool RebuildSavedRoute(long routeId)
        {
            return BeginRebuildSavedRoute(routeId);
        }

        public string BuildPreviewText()
        {
            if (_savedRouteReview != null)
            {
                if (_savedRouteReview.Mode == SavedRouteReviewMode.Modify)
                    return $"Editing saved route #{_savedRouteReview.RouteId} | Waypoints: {WaypointCount} | Computed segments: {SelectedSegments.Count}";

                return _savedRouteReview.Message ?? string.Empty;
            }

            if (_selectionController.WaypointCount == 0)
                return "Place first waypoint on a road. The computed route segments will appear here.";

            var routeSummary = $"Waypoints: {_selectionController.WaypointCount} | Computed segments: {_selectionController.SelectedSegments.Count}";
            if (_mode == RoadRouteToolMode.RenameSelectedSegments)
                return $"New segment name: {(_inputText ?? string.Empty).Trim()} | {routeSummary}";

            return $"Base names preserved | Route code to apply: {(_inputText ?? string.Empty).Trim()} | {routeSummary}";
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (_selectionController == null)
                return inputDeps;

            if (!_isRunning)
            {
                _selectionController.SetHovered(Entity.Null);
                return inputDeps;
            }

            var leftClickPressed = _isRunning && applyAction != null && applyAction.WasPressedThisFrame();
            var leftClickReleased = _isRunning && applyAction != null && applyAction.WasReleasedThisFrame();
            var rightClickPressed = _isRunning && Mouse.current?.rightButton.wasPressedThisFrame == true;

            if (_routeMenuActive)
            {
                _selectionController.SetHovered(Entity.Null);
                EnableToolActions(false);
                return inputDeps;
            }

            if (_savedRoutesViewActive)
            {
                _selectionController.SetHovered(Entity.Null);
                EnableToolActions(false);
                if (Mouse.current?.leftButton.wasPressedThisFrame == true)
                    TrySelectSavedRouteFromRaycast();

                return inputDeps;
            }

            UpdateHoveredWaypoint();

            EnableToolActions(true);

            if (_selectionController.HasActiveWaypointEdit)
            {
                if (rightClickPressed)
                {
                    if (_selectionController.TryRemoveHoveredOrActiveWaypoint())
                    {
                        MarkModifyReviewDirty();
                        _statusMessage = _selectionController.BuildRouteInstruction();
                        Mod.log.Info(() => $"Road Naming: waypoint removed during edit. Waypoints={_selectionController.WaypointCount}, Segments={_selectionController.SelectedSegments.Count}");
                    }
                    else
                    {
                        _selectionController.CancelActiveEdit();
                        _statusMessage = _selectionController.BuildRouteInstruction();
                        Mod.log.Info("Road Naming: waypoint edit canceled.");
                    }

                    return inputDeps;
                }

                if (leftClickReleased)
                {
                    var committed = _selectionController.CommitActiveEdit();
                    _statusMessage = committed ? _selectionController.BuildRouteInstruction() : _selectionController.Warning;
                    if (committed)
                    {
                        MarkModifyReviewDirty();
                        Mod.log.Info(() => $"Road Naming: waypoint edit committed. Waypoints={_selectionController.WaypointCount}, Segments={_selectionController.SelectedSegments.Count}");
                    }
                    else
                    {
                        Mod.log.Warn(() => $"Road Naming: waypoint edit rejected: {_selectionController.Warning}");
                    }
                }
                else
                {
                    _statusMessage = _selectionController.BuildRouteInstruction();
                }

                return inputDeps;
            }

            if (leftClickPressed)
            {
                Mod.log.Info(() => $"Road Naming: click received. Hovered={HoveredSegment.Index}, Waypoints={WaypointCount}");
                if (_selectionController.TryBeginEditFromHover())
                {
                    _statusMessage = _selectionController.BuildRouteInstruction();
                    Mod.log.Info(() => $"Road Naming: waypoint edit started. ExistingWaypoint={_selectionController.HasHoveredRouteWaypoint}, Insertion={_selectionController.HasHoveredRouteInsertion}, Waypoints={WaypointCount}");
                }
                else
                {
                    TryAddHoveredWaypoint();
                }
            }

            if (rightClickPressed)
            {
                if (_selectionController.TryRemoveHoveredOrActiveWaypoint())
                {
                    MarkModifyReviewDirty();
                    _statusMessage = _selectionController.BuildRouteInstruction();
                    Mod.log.Info(() => $"Road Naming: hovered waypoint removed. Waypoints={_selectionController.WaypointCount}, Segments={_selectionController.SelectedSegments.Count}");
                }
                else
                {
                    RemoveLastWaypoint();
                }
            }

            return inputDeps;
        }


        private void EnableToolActions(bool enabled)
        {
            if (applyAction != null)
                applyAction.shouldBeEnabled = enabled;

            if (cancelAction != null)
                cancelAction.shouldBeEnabled = enabled;
        }
        private void UpdateHoveredWaypoint()
        {
            if (_selectionController == null)
                return;

            if (TryGetSnappedWaypoint(out var waypoint))
            {
                var previousHover = _selectionController.HoveredSegment;
                _selectionController.SetHovered(waypoint);
                if (previousHover != waypoint.Segment)
                {
                    var previewSegments = _selectionController.PreviewSegments;
                    var previewCount = previewSegments != null ? previewSegments.Count : 0;
                    Mod.log.Info(() => $"Road Naming: valid hover target detected. Segment={waypoint.Segment.Index}, PreviewSegments={previewCount}");
                }
                return;
            }

            _selectionController.SetHovered(Entity.Null);
        }

        private bool TryGetSnappedWaypoint(out RoadRouteWaypoint waypoint)
        {
            waypoint = default;
            Entity entity;
            RaycastHit hit;
            if (!GetRaycastResult(out entity, out hit))
                return false;

            if (_metadataSystem.Validation.IsValidRoadSegment(entity))
                return TryCreateWaypointOnSegment(entity, hit.m_Position, out waypoint);

            if (EntityManager.HasComponent<Node>(entity))
                return TryCreateWaypointFromNode(entity, hit.m_Position, out waypoint);

            return false;
        }

        private bool TryCreateWaypointFromNode(Entity node, float3 hitPosition, out RoadRouteWaypoint waypoint)
        {
            waypoint = default;
            if (!EntityManager.HasBuffer<ConnectedEdge>(node))
                return false;

            var bestSegment = Entity.Null;
            var bestPosition = hitPosition;
            var bestCurvePosition = 0.5f;
            var bestDistance = float.MaxValue;
            var connectedEdges = EntityManager.GetBuffer<ConnectedEdge>(node, true);
            for (var i = 0; i < connectedEdges.Length; i++)
            {
                var segment = connectedEdges[i].m_Edge;
                if (!_metadataSystem.Validation.IsValidRoadSegment(segment))
                    continue;

                var curvePosition = 0.5f;
                var snappedPosition = hitPosition;
                var distance = 0f;
                if (EntityManager.HasComponent<Curve>(segment))
                {
                    var curve = EntityManager.GetComponentData<Curve>(segment);
                    distance = MathUtils.Distance(curve.m_Bezier.xz, hitPosition.xz, out curvePosition);
                    snappedPosition = MathUtils.Position(curve.m_Bezier, curvePosition);
                }

                if (bestSegment == Entity.Null || distance < bestDistance)
                {
                    bestDistance = distance;
                    bestSegment = segment;
                    bestPosition = snappedPosition;
                    bestCurvePosition = curvePosition;
                }
            }

            if (bestSegment == Entity.Null)
                return false;

            waypoint = new RoadRouteWaypoint(bestSegment, bestPosition, bestCurvePosition);
            return true;
        }

        private bool TryCreateWaypointOnSegment(Entity segment, float3 hitPosition, out RoadRouteWaypoint waypoint)
        {
            waypoint = default;
            if (!_metadataSystem.Validation.IsValidRoadSegment(segment))
                return false;

            var curvePosition = 0.5f;
            var snappedPosition = hitPosition;
            if (EntityManager.HasComponent<Curve>(segment))
            {
                var curve = EntityManager.GetComponentData<Curve>(segment);
                MathUtils.Distance(curve.m_Bezier.xz, hitPosition.xz, out curvePosition);
                snappedPosition = MathUtils.Position(curve.m_Bezier, curvePosition);
            }

            waypoint = new RoadRouteWaypoint(segment, snappedPosition, curvePosition);
            return true;
        }

        private bool TrySelectSavedRouteFromRaycast()
        {
            Entity entity;
            RaycastHit hit;
            if (!GetRaycastResult(out entity, out hit))
            {
                _statusMessage = "Click a saved route line or waypoint to select it.";
                return false;
            }

            RoadRouteWaypoint waypoint;
            if (_metadataSystem.Validation.IsValidRoadSegment(entity))
            {
                if (!TryCreateWaypointOnSegment(entity, hit.m_Position, out waypoint))
                    return false;
            }
            else if (EntityManager.HasComponent<Node>(entity))
            {
                if (!TryCreateWaypointFromNode(entity, hit.m_Position, out waypoint))
                    return false;
            }
            else
            {
                _statusMessage = "Click a saved route line or waypoint to select it.";
                return false;
            }

            if (!TryFindSavedRouteAtWaypoint(waypoint, out var routeId))
            {
                _statusMessage = "No saved route found at that road segment.";
                return false;
            }

            return SelectSavedRoute(routeId);
        }

        private bool TryFindSavedRouteAtWaypoint(RoadRouteWaypoint waypoint, out long routeId)
        {
            routeId = 0;
            var bestScore = float.MaxValue;
            foreach (var route in _metadataSystem.RouteDatabase.Routes)
            {
                if (route == null || route.IsDeleted)
                    continue;

                if (!RouteContainsSegment(route, waypoint.Segment))
                    continue;

                var score = ScoreRoutePick(route, waypoint);
                if (route.RouteId == _selectedSavedRouteId)
                    score -= 0.25f;

                if (score < bestScore || (math.abs(score - bestScore) < 0.001f && (routeId == 0 || route.RouteId < routeId)))
                {
                    bestScore = score;
                    routeId = route.RouteId;
                }
            }

            return routeId > 0;
        }

        private static bool RouteContainsSegment(SavedRouteRecord route, Entity segment)
        {
            if (route == null || segment == Entity.Null)
                return false;

            for (var i = 0; i < route.OrderedSegmentIds.Count; i++)
            {
                if (route.OrderedSegmentIds[i] == segment)
                    return true;
            }

            return false;
        }

        private static float ScoreRoutePick(SavedRouteRecord route, RoadRouteWaypoint waypoint)
        {
            var bestWaypointDistance = float.MaxValue;
            for (var i = 0; i < route.Waypoints.Count; i++)
            {
                var savedWaypoint = route.Waypoints[i];
                if (savedWaypoint.Segment != waypoint.Segment)
                    continue;

                bestWaypointDistance = math.min(bestWaypointDistance, math.distance(savedWaypoint.Position, waypoint.Position));
            }

            if (bestWaypointDistance <= 20f)
                return bestWaypointDistance;

            var firstSegmentIndex = 10000;
            for (var i = 0; i < route.OrderedSegmentIds.Count; i++)
            {
                if (route.OrderedSegmentIds[i] == waypoint.Segment)
                {
                    firstSegmentIndex = i;
                    break;
                }
            }

            return 1000f + firstSegmentIndex;
        }

        private void LoadSavedRoutePreview(System.Collections.Generic.IReadOnlyList<Entity> segments, System.Collections.Generic.IReadOnlyList<RoadRouteWaypoint> waypoints)
        {
            _savedRoutePreviewSegments.Clear();
            _savedRoutePreviewWaypoints.Clear();
            if (segments != null)
            {
                for (var i = 0; i < segments.Count; i++)
                {
                    if (!_savedRoutePreviewSegments.Contains(segments[i]))
                        _savedRoutePreviewSegments.Add(segments[i]);
                }
            }

            if (waypoints != null)
            {
                for (var i = 0; i < waypoints.Count; i++)
                    _savedRoutePreviewWaypoints.Add(waypoints[i]);
            }
        }

        private void MarkModifyReviewDirty()
        {
            if (_savedRouteReview == null || _savedRouteReview.Mode != SavedRouteReviewMode.Modify)
                return;

            _savedRouteReview.IsDirty = true;
            _savedRouteReview.Message = $"Modify mode active. Drag existing waypoints or the route line to edit. {WaypointCount} waypoint(s), {SelectedSegments.Count} segment(s) currently selected.";
        }

    }
}







